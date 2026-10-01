namespace CodingTest.Core.DTOs;

/// <summary>
/// Wraps the story list with a freshness flag
/// The JSON body shape is unchanged regardless of staleness - the <see cref="IsStale"/>
/// flag is surfaced exclusively via the <c>X-Cache-Status: stale</c> HTTP response header
/// so GUI consumers can react (e.g. show a "data may not be current" banner) without
/// any API contract change
/// </summary>
public record StoryServiceResult(IReadOnlyList<StoryResponse> Stories, bool IsStale = false);
