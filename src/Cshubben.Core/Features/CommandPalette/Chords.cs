namespace Cshubben.Core.Features.CommandPalette;

/// <summary>One two-key navigation shortcut: the lead key (g) then <paramref name="Key"/>.</summary>
/// <param name="Key">Second key, lower case.</param>
/// <param name="Target">Page key the UI maps to a page ("overview", "mine", ...).</param>
/// <param name="Label">Human name for help text.</param>
public sealed record ChordBinding(char Key, string Target, string Label);

public enum ChordOutcome
{
    /// <summary>The key is not part of a chord.</summary>
    None,

    /// <summary>The lead key was pressed; waiting for the second key.</summary>
    Pending,

    /// <summary>A chord finished; see <see cref="ChordResult.Target"/>.</summary>
    Completed,

    /// <summary>The second key did not match; the chord is abandoned.</summary>
    Cancelled,
}

public readonly record struct ChordResult(ChordOutcome Outcome, string? Target);

/// <summary>"g then o" style shortcuts as a small state machine with a timeout. Pure: time is passed in.</summary>
public sealed class ChordMatcher
{
    public const char Lead = 'g';

    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    public static IReadOnlyList<ChordBinding> Default { get; } = new[]
    {
        new ChordBinding('o', "overview", "Overview"),
        new ChordBinding('f', "feed", "Feed"),
        new ChordBinding('m', "mine", "My PRs"),
        new ChordBinding('r', "reviews", "Review queue"),
        new ChordBinding('n', "notes", "Notes"),
        new ChordBinding('d', "drift", "Drift"),
        new ChordBinding('l', "linear", "Linear"),
        new ChordBinding('s', "stats", "Stats"),
    };

    private readonly Dictionary<char, string> _targets;
    private DateTimeOffset? _pendingSince;

    public ChordMatcher(IReadOnlyList<ChordBinding>? bindings = null)
    {
        _targets = new Dictionary<char, string>();
        foreach (var b in bindings ?? Default) _targets[char.ToLowerInvariant(b.Key)] = b.Target;
    }

    public bool IsPending(DateTimeOffset now) => _pendingSince is { } since && now - since <= Timeout;

    public void Reset() => _pendingSince = null;

    /// <summary>Feeds one typed letter (no modifiers held).</summary>
    public ChordResult Feed(char key, DateTimeOffset now)
    {
        var k = char.ToLowerInvariant(key);
        if (!IsPending(now))
        {
            _pendingSince = null;
            if (k != Lead) return new ChordResult(ChordOutcome.None, null);
            _pendingSince = now;
            return new ChordResult(ChordOutcome.Pending, null);
        }

        if (k == Lead)
        {
            _pendingSince = now;
            return new ChordResult(ChordOutcome.Pending, null);
        }

        _pendingSince = null;
        return _targets.TryGetValue(k, out var target)
            ? new ChordResult(ChordOutcome.Completed, target)
            : new ChordResult(ChordOutcome.Cancelled, null);
    }
}
