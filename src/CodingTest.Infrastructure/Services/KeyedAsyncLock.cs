using System.Collections.Concurrent;

namespace CodingTest.Infrastructure.Services;

/// <summary>
/// Per-key async lock. Two calls with the same key are serialized; calls with
/// different keys proceed concurrently and don't block each other
/// </summary>
internal sealed class KeyedAsyncLock<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(TKey key, CancellationToken ct = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}