namespace Cshubben.Core.Features.Errors;

/// <summary>Puts log lines that are the same problem together and counts them per bucket.</summary>
public static class ErrorGrouping
{
    /// <summary>The groups, most frequent first (ties: most recent first), at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<ErrorGroup> Group(IEnumerable<LogEntry> entries, ErrorWindow window, int max = 50)
    {
        var builders = new Dictionary<string, Builder>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var fingerprint = ErrorFingerprints.Of(entry.Message);
            if (!builders.TryGetValue(fingerprint.Key, out var builder))
            {
                builder = new Builder(fingerprint, window.Buckets);
                builders[fingerprint.Key] = builder;
            }

            builder.Add(entry, window);
        }

        return builders.Values
            .Select(b => b.Build())
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.LastSeen)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    private sealed class Builder
    {
        private readonly ErrorFingerprint _fingerprint;
        private readonly int[] _counts;
        private readonly HashSet<string> _pods = new(StringComparer.Ordinal);
        private int _count;
        private DateTimeOffset _first = DateTimeOffset.MaxValue;
        private DateTimeOffset _last = DateTimeOffset.MinValue;

        public Builder(ErrorFingerprint fingerprint, int buckets)
        {
            _fingerprint = fingerprint;
            _counts = new int[buckets];
        }

        public void Add(LogEntry entry, ErrorWindow window)
        {
            _count++;
            if (entry.At < _first) _first = entry.At;
            if (entry.At > _last) _last = entry.At;
            if (entry.Pod.Length > 0) _pods.Add(entry.Pod);
            var index = window.IndexOf(entry.At);
            if (index >= 0) _counts[index]++;
        }

        public ErrorGroup Build() => new(
            _fingerprint.Key,
            _fingerprint.Title,
            _fingerprint.Detail,
            _fingerprint.Hint,
            _fingerprint.Kind,
            _count,
            _first,
            _last,
            _pods.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            _counts,
            _fingerprint.Method,
            _fingerprint.File);
    }
}
