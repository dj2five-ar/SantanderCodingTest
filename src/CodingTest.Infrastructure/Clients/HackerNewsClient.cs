using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using CodingTest.Core.Interfaces;
using CodingTest.Core.Models;

namespace CodingTest.Infrastructure.Clients;

public class HackerNewsClient : IHackerNewsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HackerNewsClient> _logger;

    public HackerNewsClient(HttpClient httpClient, ILogger<HackerNewsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching best story ID list from Hacker News");
        var ids = await _httpClient.GetFromJsonAsync<int[]>("beststories.json", ct);
        return ids ?? Array.Empty<int>();
    }

    public async Task<HackerNewsItem?> GetStoryAsync(int id, CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching story {Id} from Hacker News", id);
        return await _httpClient.GetFromJsonAsync<HackerNewsItem>($"item/{id}.json", ct);
    }
}
