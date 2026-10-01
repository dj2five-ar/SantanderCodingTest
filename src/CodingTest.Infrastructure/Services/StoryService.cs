using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CodingTest.Core.DTOs;
using CodingTest.Core.Interfaces;
using CodingTest.Core.Models;
using CodingTest.Infrastructure.Configuration;

namespace CodingTest.Infrastructure.Services;

/// <summary>
/// Retrieves best stories from Hacker News with a two-level caching strategy and
/// a stale-data fallback for HN API outages
///
/// Normal path (HN is reachable):
///   - Best-story ID list cached for <see cref="CacheOptions.StoryListTtlSeconds"/>
///   - Individual story details cached for <see cref="CacheOptions.StoryDetailTtlSeconds"/>
///   - Every successful full assembly updates a process-lifetime stale backup
///
/// Degraded path (HN unreachable / circuit open):
///   - If a stale backup exists, it is returned with <c>IsStale = true</c>
///   - The caller surfaces this via <c>X-Cache-Status: stale</c> so GUIs can
///     react without any JSON contract change
///   - If no stale backup exists (first-ever call), the exception propagates
///
/// Concurrency protections:
///   - <see cref="_idsFetchLock"/> (SemaphoreSlim 1,1) prevents cache stampedes
///     on the ID-list resource
///   - <see cref="_apiConcurrencyLimiter"/> limits parallel outbound HN calls
///     across all in-flight requests; Polly handles retries and circuit breaking
///     at the HTTP layer below this
/// </summary>
public class StoryService : IStoryService
{
    // Live caches use TTL entries that expire normally
    private const string BestStoryIdsCacheKey = "hn:best_story_ids";
    private static string StoryCacheKey(int id) => $"hn:story:{id}";

    // Stale backup - no expiry, NeverRemove priority, lives for the process lifetime
    private const string StaleStoriesCacheKey = "hn:best_stories:stale";

    /// <summary>
    /// Discriminates between a story that was legitimately absent from HN
    /// (soft failure - skip it) and a story whose fetch failed due to a network
    /// error or open circuit breaker (hard failure - abort the whole batch)
    /// </summary>
    private record StoryFetchResult(StoryResponse? Story, bool HardFailure = false);

    private readonly IHackerNewsClient _client;
    private readonly IMemoryCache _cache;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<StoryService> _logger;

    private readonly SemaphoreSlim _idsFetchLock = new(1, 1);
    private readonly SemaphoreSlim _apiConcurrencyLimiter;
    private readonly KeyedAsyncLock<int> _storyFetchLock = new();

    public StoryService(
        IHackerNewsClient client,
        IMemoryCache cache,
        IOptions<CacheOptions> cacheOptions,
        IOptions<HackerNewsOptions> hnOptions,
        ILogger<StoryService> logger)
    {
        _client = client;
        _cache = cache;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
        _apiConcurrencyLimiter = new SemaphoreSlim(
            hnOptions.Value.MaxParallelRequests,
            hnOptions.Value.MaxParallelRequests);
        _storyFetchLock = new KeyedAsyncLock<int>();
    }

    public async Task<StoryServiceResult> GetBestStoriesAsync(
        int count, CancellationToken ct = default)
    {
        try
        {
            var stories = await FetchStoriesAsync(count, ct);
            return new StoryServiceResult(stories);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "HN API unavailable - checking stale cache");

            if (_cache.TryGetValue(StaleStoriesCacheKey, out IReadOnlyList<StoryResponse>? stale)
                && stale is not null)
            {
                var stalePage = stale.Take(count).ToList();
                _logger.LogInformation(
                    "Serving {Count} stale stories (data from before outage)", stalePage.Count);
                return new StoryServiceResult(stalePage, IsStale: true);
            }

            // No stale backup yet (service has never successfully fetched from HN)
            _logger.LogError("No stale data available - propagating exception");
            throw;
        }
    }

    // Called by the background service to proactively keep the cache warm
    internal async Task WarmCacheAsync(int count, CancellationToken ct)
    {
        var result = await GetBestStoriesAsync(count, ct);
        if (result.IsStale)
            _logger.LogWarning(
                "Background warm-up served stale data - HN API is likely unavailable");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<StoryResponse>> FetchStoriesAsync(
        int count, CancellationToken ct)
    {
        var ids = await GetBestStoryIdsAsync(ct);

        // Fan out: fetch story details in parallel, respecting the concurrency limit
        // Truncate only after getting results so we can confirm that the ordering by score is done correctly
        var tasks = ids.Select(id => GetCachedOrFetchStoryAsync(id, ct));
        var results = await Task.WhenAll(tasks);

        // Hard failures (circuit open / network error) mid-batch mean we cannot trust
        // the assembled list to be representative - abort and fall through to stale
        var hardFailures = results.Count(r => r.HardFailure);
        if (hardFailures > 0)
            throw new InvalidOperationException(
                $"{hardFailures}/{ids.Count} story detail fetches failed with a hard error " +
                "(circuit may be open). Aborting batch to avoid returning an incomplete list.");

        // Sort the full pool first, to ensure we return the highest-scoring stories
        var sorted = results
            .Select(r => r.Story)
            .OfType<StoryResponse>()
            .OrderByDescending(s => s.Score)
            .ToList();

        // Stale backup now always holds the full scored pool, not just the top n,
        // so that future requests can be served from it without a live HN round-trip,
        // as well as during an outage. The top n is truncated at the controller level before returning to the client
        var shouldUpdateStale = !_cache.TryGetValue(
            StaleStoriesCacheKey, out IReadOnlyList<StoryResponse>? existingStale)
            || existingStale is null
            || sorted.Count >= existingStale.Count;

        if (shouldUpdateStale)
        {
            _cache.Set(
                StaleStoriesCacheKey,
                (IReadOnlyList<StoryResponse>)sorted,
                new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
        }

        // Return the top n stories to the controller
        return sorted.Take(count).ToList();
    }

    private async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(BestStoryIdsCacheKey, out IReadOnlyList<int>? ids) && ids is not null)
            return ids;

        await _idsFetchLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(BestStoryIdsCacheKey, out ids) && ids is not null)
                return ids;

            var freshIds = await _client.GetBestStoryIdsAsync(ct);
            _cache.Set(BestStoryIdsCacheKey, freshIds,
                TimeSpan.FromSeconds(_cacheOptions.StoryListTtlSeconds));

            _logger.LogInformation("Refreshed best-story ID list ({Count} IDs)", freshIds.Count);
            return freshIds;
        }
        finally
        {
            _idsFetchLock.Release();
        }
    }

    private async Task<StoryFetchResult> GetCachedOrFetchStoryAsync(int id, CancellationToken ct)
    {
        if (_cache.TryGetValue(StoryCacheKey(id), out StoryResponse? story))
            return new StoryFetchResult(story);

        // lock per story ID to avoid stampedes when multiple requests for the same story arrive concurrently
        using (await _storyFetchLock.AcquireAsync(id, ct))   
        {
            // Check the cache again after acquiring the lock, in case another thread already fetched it
            if (_cache.TryGetValue(StoryCacheKey(id), out story))
                return new StoryFetchResult(story);

            // Cap the number of concurrent outbound requests to HN to avoid overwhelming the API and triggering rate limits
            await _apiConcurrencyLimiter.WaitAsync(ct);
            try
            {
                var item = await _client.GetStoryAsync(id, ct);

                // Soft failure: HN returned null for this ID (deleted / non-story item)
                // Skip it without aborting the rest of the batch
                if (item is null) return new StoryFetchResult(null);

                story = MapToResponse(item);
                _cache.Set(StoryCacheKey(id), story,
                    TimeSpan.FromSeconds(_cacheOptions.StoryDetailTtlSeconds));
                return new StoryFetchResult(story);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Hard failure: network error, timeout, or circuit breaker open
                // Flagged so FetchStoriesAsync can abort the whole batch rather than
                // returning and caching a misleadingly incomplete list
                _logger.LogWarning(ex, "Hard failure fetching story {Id}", id);
                return new StoryFetchResult(null, HardFailure: true);
            }
            finally
            {
                _apiConcurrencyLimiter.Release();
            }
        }
    }

    private static StoryResponse MapToResponse(HackerNewsItem item) => new()
    {
        Title = item.Title,
        Uri = item.Url,
        PostedBy = item.By,
        Time = DateTimeOffset.FromUnixTimeSeconds(item.Time),
        Score = item.Score,
        CommentCount = item.Descendants,
    };
}
