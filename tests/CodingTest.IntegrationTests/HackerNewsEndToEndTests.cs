using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using CodingTest.Core.DTOs;
using CodingTest.Core.Interfaces;
using CodingTest.Core.Models;
using CodingTest.Infrastructure.Clients;

namespace CodingTest.IntegrationTests;

/// <summary>
/// Exercises Controller -> StoryService -> HackerNewsClient -> HTTP end to end
/// Only the transport-level HttpMessageHandler is faked; everything above it
/// (resilience pipeline, caching, sorting, mapping) runs for real
/// </summary>
public class HackerNewsEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HackerNewsEndToEndTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task GetBestStories_EndToEnd_FetchesMapsAndSortsByScore()
    {
        var bestIds = new[] { 1, 2, 3 };
        var items = new Dictionary<int, HackerNewsItem>
        {
            [1] = new() { Id = 1, Title = "Low", Score = 10, By = "a", Time = 1570887781, Descendants = 1, Type = "story" },
            [2] = new() { Id = 2, Title = "High", Score = 999, By = "b", Time = 1570887781, Descendants = 2, Type = "story" },
            [3] = new() { Id = 3, Title = "Mid", Score = 100, By = "c", Time = 1570887781, Descendants = 3, Type = "story" },
        };

        var client = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IHackerNewsClient, HackerNewsClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeHackerNewsHandler(bestIds, items));
            }))
            .CreateClient();

        var response = await client.GetAsync("/api/stories/best?n=2");
        response.EnsureSuccessStatusCode();

        var stories = await response.Content.ReadFromJsonAsync<List<StoryResponse>>();

        Assert.NotNull(stories);
        Assert.Equal(2, stories!.Count);
        Assert.Equal("High", stories[0].Title);  // score 999 - correctly first
        Assert.Equal("Mid", stories[1].Title);   // score 100 - correctly second
    }

    /// <summary>
    /// Stubs HN's beststories.json and item/{id}.json endpoints
    /// </summary>
    private sealed class FakeHackerNewsHandler(
        int[] bestIds, IReadOnlyDictionary<int, HackerNewsItem> items) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("beststories.json"))
                return Task.FromResult(JsonResponse(bestIds));

            var match = Regex.Match(path, @"item/(\d+)\.json$");
            if (match.Success && items.TryGetValue(int.Parse(match.Groups[1].Value), out var item))
                return Task.FromResult(JsonResponse(item));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse<T>(T body) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(body),
        };
    }
}