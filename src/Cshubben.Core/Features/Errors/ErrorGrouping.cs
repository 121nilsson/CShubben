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
        private readonly List<LogEntry> _samples = new();
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

            // Keep the newest few lines whole; the rest are only counted.
            _samples.Add(entry);
            if (_samples.Count > MaxSamples * 4)
                Trim();
        }

        private const int MaxSamples = 5;

        // The key uses placeholders (<n>, <id>) so repeats group; the title shows the newest real line, numbers intact.
        private string Title()
        {
            if (_fingerprint.Kind != ErrorKind.Message || _samples.Count == 0) return _fingerprint.Title;
            var newest = _samples.OrderByDescending(s => s.At).First();
            return ErrorFingerprints.DisplayMessage(newest.Message) ?? _fingerprint.Title;
        }

        private void Trim()
        {
            var keep = _samples.OrderByDescending(s => s.At).Take(MaxSamples).ToList();
            _samples.Clear();
            _samples.AddRange(keep);
        }

        public ErrorGroup Build() => new(
            _fingerprint.Key,
            Title(),
            _fingerprint.Detail,
            _fingerprint.Hint,
            _fingerprint.Kind,
            _count,
            _first,
            _last,
            _pods.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            _counts,
            _fingerprint.Method,
            _fingerprint.File,
            _fingerprint.Line,
            _samples.OrderByDescending(s => s.At).Take(MaxSamples).ToList());
    }
}
