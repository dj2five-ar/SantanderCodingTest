using Microsoft.AspNetCore.Mvc;
using CodingTest.Core.DTOs;
using CodingTest.Core.Interfaces;

namespace CodingTest.Api.Controllers;

/// <summary>Endpoints for retrieving top-ranked Hacker News stories.</summary>
[ApiController]
[Route("api/stories")]
public class StoriesController : ControllerBase
{
    private readonly IStoryService _storyService;

    /// <inheritdoc/>
    public StoriesController(IStoryService storyService)
    {
        _storyService = storyService;
    }

    /// <summary>Returns the best n Hacker News stories ranked by score descending.</summary>
    /// <param name="n">
    /// Number of stories to return. Must be between 1 and 200 inclusive
    /// Defaults to 10. The Hacker News beststories list contains at most 200 entries
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Stories are fetched from the Hacker News public API and cached in-memory
    /// A background service proactively refreshes the cache before TTL expiry so most
    /// requests are served without a live HN round-trip
    ///
    /// **Degraded mode** - when the HN API is unreachable and the live cache has expired,
    /// the last successfully fetched dataset is returned instead. In this case the response
    /// includes the header X-Cache-Status: stale so that GUI consumers can display an
    /// appropriate freshness warning without any change to the JSON body
    ///
    /// If no cached data exists at all (e.g. on first startup with HN down), a 503 is returned
    /// </remarks>
    [HttpGet("best")]
    [ProducesResponseType<IReadOnlyList<StoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBestStories(
        [FromQuery] int n = 10, CancellationToken ct = default)
    {
        if (n <= 0)
            return BadRequest("n must be greater than 0.");

        if (n > 200)
            return BadRequest("n must be less than or equal to 200.");

        try
        {
            var result = await _storyService.GetBestStoriesAsync(n, ct);

            if (result.IsStale)
            {
                // RFC 7234 section 5.5.1 Warning: 110 is deprecated in RFC 9110
                // X-Cache-Status is the modern convention (Nginx, Varnish, Fastly, CloudFront)
                // GUIs can read this header to display a data-may-not-be-current banner
                Response.Headers.Append("X-Cache-Status", "stale");
            }

            return Ok(result.Stories);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // StoryService already logged the root cause. Return 503 rather than
            // letting an unhandled exception produce a 500 with a stack trace
            _ = ex;
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "The Hacker News API is currently unavailable and no cached data exists. " +
                "Please retry later.");
        }
    }
}
