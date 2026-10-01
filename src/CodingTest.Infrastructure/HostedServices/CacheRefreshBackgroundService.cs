using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CodingTest.Infrastructure.Configuration;
using CodingTest.Infrastructure.Services;

namespace CodingTest.Infrastructure.HostedServices;

/// <summary>
/// Runs on a configurable interval and proactively refreshes the in-memory story cache
/// before TTL expiry, so incoming API requests always hit a warm cache
/// </summary>
public class CacheRefreshBackgroundService : BackgroundService
{
    private readonly StoryService _storyService;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<CacheRefreshBackgroundService> _logger;

    public CacheRefreshBackgroundService(
        StoryService storyService,
        IOptions<CacheOptions> cacheOptions,
        ILogger<CacheRefreshBackgroundService> logger)
    {
        _storyService = storyService;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay so the app finishes starting up before the first refresh
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation(
                    "Background cache refresh starting for top {Count} stories",
                    _cacheOptions.WarmupCount);

                await _storyService.WarmCacheAsync(_cacheOptions.WarmupCount, stoppingToken);

                _logger.LogInformation("Background cache refresh completed at {Time}", DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background cache refresh failed - will retry after interval");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_cacheOptions.BackgroundRefreshIntervalSeconds),
                stoppingToken);
        }
    }
}
