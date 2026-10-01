using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using CodingTest.Core.DTOs;
using CodingTest.Core.Interfaces;

namespace CodingTest.IntegrationTests;

public class StoriesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StoriesApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBestStories_DefaultN_ReturnsOk()
    {
        var client = CreateClientWithMockedService(BuildStories(10));
        var response = await client.GetAsync("/api/stories/best");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetBestStories_ReturnsCorrectNumberOfStories()
    {
        var client = CreateClientWithMockedService(BuildStories(5));
        var response = await client.GetAsync("/api/stories/best?n=5");

        response.EnsureSuccessStatusCode();
        var stories = await response.Content.ReadFromJsonAsync<List<StoryResponse>>();

        Assert.NotNull(stories);
        Assert.Equal(5, stories.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetBestStories_WithNonPositiveN_ReturnsBadRequest(int n)
    {
        var client = CreateClientWithMockedService(BuildStories(0));
        var response = await client.GetAsync($"/api/stories/best?n={n}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBestStories_ResponseMatchesExpectedShape()
    {
        var expected = new StoryResponse
        {
            Title = "Test Story",
            Uri = "https://example.com",
            PostedBy = "tester",
            Time = new DateTimeOffset(2019, 10, 12, 13, 43, 1, TimeSpan.Zero),
            Score = 999,
            CommentCount = 42,
        };

        var client = CreateClientWithMockedService(new[] { expected });
        var response = await client.GetAsync("/api/stories/best?n=1");
        var stories = await response.Content.ReadFromJsonAsync<List<StoryResponse>>();

        Assert.NotNull(stories);
        var story = Assert.Single(stories);
        Assert.Equal(expected.Title, story.Title);
        Assert.Equal(expected.Uri, story.Uri);
        Assert.Equal(expected.PostedBy, story.PostedBy);
        Assert.Equal(expected.Score, story.Score);
        Assert.Equal(expected.CommentCount, story.CommentCount);
    }

    [Fact]
    public async Task GetBestStories_WhenDataIsStale_SetsXCacheStatusHeader()
    {
        var client = CreateClientWithMockedService(BuildStories(5), isStale: true);
        var response = await client.GetAsync("/api/stories/best?n=5");

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Cache-Status"));
        Assert.Equal("stale", response.Headers.GetValues("X-Cache-Status").Single());
    }

    [Fact]
    public async Task GetBestStories_WhenDataIsFresh_DoesNotSetXCacheStatusHeader()
    {
        var client = CreateClientWithMockedService(BuildStories(5), isStale: false);
        var response = await client.GetAsync("/api/stories/best?n=5");

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains("X-Cache-Status"));
    }

    // --- helpers ---

    private HttpClient CreateClientWithMockedService(
        IEnumerable<StoryResponse> stories, bool isStale = false)
    {
        var mockService = new Mock<IStoryService>();
        mockService
            .Setup(s => s.GetBestStoriesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryServiceResult(stories.ToList(), isStale));

        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // Replace the real IStoryService with the mock
                services.AddSingleton(mockService.Object);
            }))
            .CreateClient();
    }

    private static IEnumerable<StoryResponse> BuildStories(int count) =>
        Enumerable.Range(1, count).Select(i => new StoryResponse
        {
            Title = $"Story {i}",
            Uri = $"https://example.com/{i}",
            PostedBy = "author",
            Time = DateTimeOffset.UtcNow,
            Score = 1000 - i,
            CommentCount = i * 10,
        });
}
