namespace Cshubben.Core.Features.Review;

/// <summary>Find-in-file for the diff: which code lines contain a text, and how Enter / Shift+Enter step through them.</summary>
public static class DiffSearch
{
    /// <summary>Row keys of the code lines (in file order) that contain the query, ignoring case. An empty query matches nothing.</summary>
    public static IReadOnlyList<string> Matches(IReadOnlyList<ReviewRow> rows, string? query)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var row in rows)
        {
            if (row is not LineRow line || line.Line.Kind == DiffLineKind.NoNewline) continue;
            if (line.Line.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) result.Add(line.Key);
        }
        return result;
    }

    /// <summary>The next index with wrap-around. From -1 forward gives the first match, backward the last. -1 when there are none.</summary>
    public static int Step(int count, int current, int delta)
    {
        if (count <= 0) return -1;
        if (current < 0 || current >= count) return delta >= 0 ? 0 : count - 1;
        var next = (current + (delta >= 0 ? 1 : -1)) % count;
        return next < 0 ? next + count : next;
    }

    /// <summary>"3 of 12", "No matches" or an empty string for an empty query.</summary>
    public static string Label(string? query, int count, int current)
    {
        if (string.IsNullOrEmpty(query)) return string.Empty;
        if (count == 0) return "No matches";
        return (current < 0 ? 1 : current + 1) + " of " + count;
    }
}
