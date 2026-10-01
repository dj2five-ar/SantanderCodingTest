namespace CodingTest.Infrastructure.Configuration;

public class HackerNewsOptions
{
    public const string SectionName = "HackerNews";

    public string BaseUrl { get; set; } = "https://hacker-news.firebaseio.com/v0/";

    /// <summary>Per-request HTTP timeout in seconds.</summary>
    public int FetchTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of concurrent story-detail requests sent to HN at once.</summary>
    public int MaxParallelRequests { get; set; } = 20;
}
