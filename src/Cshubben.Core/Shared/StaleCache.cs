using System.Collections.Concurrent;

namespace Cshubben.Core.Shared;

/// <summary>
/// A small in-memory cache for page data. <see cref="Peek"/> returns whatever is there (even if old) so a page can paint at once,
/// and <see cref="GetAsync"/> loads again only when the value is older than its time to live. Concurrent callers for the same key
/// share one load. The clock is injected so tests are deterministic.
/// </summary>
public sealed class StaleCache<T> where T : class
{
    private sealed record Entry(DateTimeOffset At, T Value);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<T>> _loading = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;

    public StaleCache(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public T? Peek(string key) => _entries.TryGetValue(key, out var e) ? e.Value : null;

    public bool IsFresh(string key, TimeSpan ttl) => _entries.TryGetValue(key, out var e) && _clock() - e.At < ttl;

    public void Set(string key, T value) => _entries[key] = new Entry(_clock(), value);

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();

    /// <summary>The cached value when it is younger than <paramref name="ttl"/>, otherwise the result of <paramref name="load"/> (stored on success).</summary>
    public async Task<T> GetAsync(string key, TimeSpan ttl, Func<Task<T>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (_entries.TryGetValue(key, out var e) && _clock() - e.At < ttl) return e.Value;

        var task = _loading.GetOrAdd(key, _ => RunAsync(key, load));
        return await task.ConfigureAwait(false);
    }

    private async Task<T> RunAsync(string key, Func<Task<T>> load)
    {
        try
        {
            var value = await load().ConfigureAwait(false);
            Set(key, value);
            return value;
        }
        finally
        {
            _loading.TryRemove(key, out _);
        }
    }
}
