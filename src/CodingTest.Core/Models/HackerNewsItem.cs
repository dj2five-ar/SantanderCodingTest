using System.Text.Json.Serialization;

namespace CodingTest.Core.Models;

public class HackerNewsItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("by")]
    public string By { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("descendants")]
    public int Descendants { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
