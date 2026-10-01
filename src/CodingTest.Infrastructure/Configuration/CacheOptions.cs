namespace CodingTest.Infrastructure.Configuration;

public class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>How long (seconds) the best-story ID list is considered fresh.</summary>
    public int StoryListTtlSeconds { get; set; } = 300;

    /// <summary>How long (seconds) individual story details are cached.</summary>
    public int StoryDetailTtlSeconds { get; set; } = 600;

    /// <summary>How often (seconds) the background service proactively warms the cache.</summary>
    public int BackgroundRefreshIntervalSeconds { get; set; } = 240;

    /// <summary>Number of top stories the background service pre-fetches to keep warm.</summary>
    public int WarmupCount { get; set; } = 200;
}
