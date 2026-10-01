using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using CodingTest.Core.Interfaces;
using CodingTest.Core.Models;
using CodingTest.Infrastructure.Configuration;
using CodingTest.Infrastructure.Services;

namespace CodingTest.UnitTests.Services;

public class StoryServiceTests : IDisposable
{
    private readonly Mock<IHackerNewsClient> _mockClient = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private StoryService CreateService(
        CacheOptions? cacheOptions = null,
        HackerNewsOptions? hnOptions = null)
    {
        return new StoryService(
            _mockClient.Object,
            _cache,
            Options.Create(cacheOptions ?? new CacheOptions()),
            Options.Create(hnOptions ?? new HackerNewsOptions { MaxParallelRequests = 5 }),
            NullLogger<StoryService>.Instance);
    }

    [Fact]
    public async Task GetBestStoriesAsync_ReturnsStoriesInDescendingScoreOrder()
    {
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1, 2, 3 });

        _mockClient.Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(1, "Low Score", score: 50));
        _mockClient.Setup(c => c.GetStoryAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(2, "High Score", score: 500));
        _mockClient.Setup(c => c.GetStoryAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(3, "Mid Score", score: 200));

        var service = CreateService();
        var result = await service.GetBestStoriesAsync(3);

        Assert.False(result.IsStale);
        Assert.Equal(3, result.Stories.Count);
        Assert.Equal(500, result.Stories[0].Score);
        Assert.Equal(200, result.Stories[1].Score);
        Assert.Equal(50, result.Stories[2].Score);
    }

    [Fact]
    public async Task GetBestStoriesAsync_ReturnsOnlyRequestedCount()
    {
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1, 2, 3 });

        for (int id = 1; id <= 3; id++)
        {
            var capturedId = id;
            _mockClient.Setup(c => c.GetStoryAsync(capturedId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StoryItem(capturedId, $"Story {capturedId}", score: capturedId * 100));
        }

        var service = CreateService();
        var result = await service.GetBestStoriesAsync(2);

        Assert.Equal(2, result.Stories.Count);
    }

    [Fact]
    public async Task GetBestStoriesAsync_CachesStoryDetails_DoesNotRefetchOnSecondCall()
    {
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1 });
        _mockClient.Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(1, "Cached Story", score: 100));

        var service = CreateService();

        await service.GetBestStoriesAsync(1);
        await service.GetBestStoriesAsync(1);

        // Story detail should only be fetched once; second call uses the cache
        _mockClient.Verify(
            c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBestStoriesAsync_CachesBestStoryIds_DoesNotRefetchIdListOnSecondCall()
    {
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1 });
        _mockClient.Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(1, "Story", score: 10));

        var service = CreateService();

        await service.GetBestStoriesAsync(1);
        await service.GetBestStoriesAsync(1);

        _mockClient.Verify(
            c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBestStoriesAsync_SkipsNullStories()
    {
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1, 2 });
        _mockClient.Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HackerNewsItem?)null);
        _mockClient.Setup(c => c.GetStoryAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(2, "Valid Story", score: 100));

        var service = CreateService();
        var result = await service.GetBestStoriesAsync(2);

        Assert.Single(result.Stories);
        Assert.Equal("Valid Story", result.Stories[0].Title);
    }

    [Fact]
    public async Task GetBestStoriesAsync_MapsFieldsCorrectly()
    {
        var unixTimestamp = 1570888981L;
        var item = new HackerNewsItem
        {
            Id = 99,
            Title = "A uBlock Origin update was rejected from the Chrome Web Store",
            Url = "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
            By = "ismaildonmez",
            Time = unixTimestamp,
            Score = 1716,
            Descendants = 572,
            Type = "story",
        };

        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 99 });
        _mockClient.Setup(c => c.GetStoryAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var service = CreateService();
        var result = await service.GetBestStoriesAsync(1);

        var story = Assert.Single(result.Stories);
        Assert.Equal(item.Title, story.Title);
        Assert.Equal(item.Url, story.Uri);
        Assert.Equal(item.By, story.PostedBy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(unixTimestamp), story.Time);
        Assert.Equal(1716, story.Score);
        Assert.Equal(572, story.CommentCount);
    }

    [Fact]
    public async Task GetBestStoriesAsync_WhenHnFails_ReturnsStaleDataIfAvailable()
    {
        // First call succeeds - populates the stale backup cache
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1 });
        _mockClient
            .Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(1, "Stale Story", score: 100));

        var service = CreateService();
        await service.GetBestStoriesAsync(1); // warms stale backup

        // Evict the live-cache entries to simulate TTL expiry
        // The stale backup (key "hn:best_stories:stale") is intentionally NOT removed
        _cache.Remove("hn:best_story_ids");
        _cache.Remove("hn:story:1");

        // Simulate HN going down
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HN is unreachable"));

        var result = await service.GetBestStoriesAsync(1);

        Assert.True(result.IsStale);
        Assert.Single(result.Stories);
        Assert.Equal("Stale Story", result.Stories[0].Title);
    }

    [Fact]
    public async Task GetBestStoriesAsync_WhenHnFailsAndNoStaleData_Throws()
    {
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HN is unreachable"));

        var service = CreateService();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetBestStoriesAsync(1));
    }

    [Fact]
    public async Task GetBestStoriesAsync_WhenIdListOrderDoesNotMatchScoreOrder_StillReturnsTrueTopN()
    {
        // beststories.json's own order is HN's ranking algorithm, not a score sort.
        // Put the highest-scoring story LAST in the ID list to prove it's not dropped.
        _mockClient
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1, 2, 3, 4, 5 });

        _mockClient.Setup(c => c.GetStoryAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(1, "Story A", score: 50));
        _mockClient.Setup(c => c.GetStoryAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(2, "Story B", score: 40));
        _mockClient.Setup(c => c.GetStoryAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(3, "Story C", score: 30));
        _mockClient.Setup(c => c.GetStoryAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(4, "Story D", score: 20));
        _mockClient.Setup(c => c.GetStoryAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoryItem(5, "Story E - Highest Score", score: 999));

        var service = CreateService();
        var result = await service.GetBestStoriesAsync(3);

        Assert.Equal(3, result.Stories.Count);
        Assert.Equal("Story E - Highest Score", result.Stories[0].Title);
        Assert.Equal(999, result.Stories[0].Score);
    }

    public void Dispose() => _cache.Dispose();

    private static HackerNewsItem StoryItem(int id, string title, int score) => new()
    {
        Id = id,
        Title = title,
        Score = score,
        By = "author",
        Time = 1570888981L,
        Type = "story",
    };
}
