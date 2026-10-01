using CodingTest.Core.Models;

namespace CodingTest.Core.Interfaces;

public interface IHackerNewsClient
{
    Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken ct = default);
    Task<HackerNewsItem?> GetStoryAsync(int id, CancellationToken ct = default);
}
