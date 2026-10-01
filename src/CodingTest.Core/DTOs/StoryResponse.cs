namespace CodingTest.Core.DTOs;

/// <summary>A Hacker News story as returned by the best-stories endpoint.</summary>
public class StoryResponse
{
    /// <summary>The story headline.</summary>
    /// <example>A uBlock Origin update was rejected from the Chrome Web Store</example>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Link to the external article, or <c>null</c> for text-only posts
    /// (Ask HN, Show HN, etc...)
    /// </summary>
    /// <example>https://github.com/uBlockOrigin/uBlock-issues/issues/745</example>
    public string? Uri { get; set; }

    /// <summary>Hacker News username of the submitter.</summary>
    /// <example>ismaildonmez</example>
    public string PostedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp of when the story was submitted, in ISO 8601 format.</summary>
    /// <example>2019-10-12T13:43:01+00:00</example>
    public DateTimeOffset Time { get; set; }

    /// <summary>The story's current score (community upvotes).</summary>
    /// <example>1716</example>
    public int Score { get; set; }

    /// <summary>Total number of comments posted on the story.</summary>
    /// <example>572</example>
    public int CommentCount { get; set; }
}
