using CodingTest.Core.DTOs;

namespace CodingTest.Core.Interfaces;

public interface IStoryService
{
    Task<StoryServiceResult> GetBestStoriesAsync(int count, CancellationToken ct = default);
}
