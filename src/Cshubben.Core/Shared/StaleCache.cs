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

    private string? _latestKey;

    /// <summary>The entry that was set last, with its time. Null when the cache is empty.</summary>
    public (string Key, T Value, DateTimeOffset At)? Latest() =>
        _latestKey is not null && _entries.TryGetValue(_latestKey, out var e) ? (_latestKey, e.Value, e.At) : null;

    /// <summary>Puts a value read from disk in the cache with the time it was originally loaded, so it counts as old and is shown at once while a fresh load runs.</summary>
    public void Seed(string key, T value, DateTimeOffset at)
    {
        _entries[key] = new Entry(at, value);
        _latestKey = key;
    }

    public T? Peek(string key) => _entries.TryGetValue(key, out var e) ? e.Value : null;

    public bool IsFresh(string key, TimeSpan ttl) => _entries.TryGetValue(key, out var e) && _clock() - e.At < ttl;

    public void Set(string key, T value)
    {
        _entries[key] = new Entry(_clock(), value);
        _latestKey = key;
    }

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();

    /// <summary>The cached value when it is younger than <paramref name="ttl"/>, otherwise the result of <paramref name="load"/> (stored on success).</summary>
    public async Task<T> GetAsync(string key, TimeSpan ttl, Func<Task<T>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (_entries.TryGetValue(key, out var e) && _clock() - e.At < ttl) return e.Value;

        // The first caller for a key does the load; others wait for its result. The entry is removed only after it was added,
        // so a load that finishes at once cannot leave a finished task behind.
        var gate = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = _loading.GetOrAdd(key, gate.Task);
        if (!ReferenceEquals(existing, gate.Task)) return await existing.ConfigureAwait(false);

        try
        {
            var value = await load().ConfigureAwait(false);
            Set(key, value);
            gate.SetResult(value);
            return value;
        }
        catch (Exception ex)
        {
            gate.SetException(ex);
            _ = gate.Task.Exception; // observed here; waiting callers still see the error
            throw;
        }
        finally
        {
            _loading.TryRemove(key, out _);
        }
    }
}
