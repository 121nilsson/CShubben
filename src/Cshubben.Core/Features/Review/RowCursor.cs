namespace Cshubben.Core.Features.Review;

/// <summary>Moving the keyboard cursor through the rows of a file: lines you can comment on, threads and drafts.</summary>
public static class RowCursor
{
    public static bool IsStop(ReviewRow row) => row switch
    {
        LineRow l => l.Line.Kind != DiffLineKind.NoNewline,
        ThreadRow => true,
        PendingRow => true,
        _ => false,
    };

    /// <summary>
    /// The key of the next (delta &gt; 0) or previous (delta &lt; 0) stop. Without a current key the first or last stop
    /// is returned. The cursor stops at the ends. Null when the file has no stop at all.
    /// </summary>
    public static string? Move(IReadOnlyList<ReviewRow> rows, string? currentKey, int delta)
    {
        var stops = new List<string>();
        foreach (var row in rows)
            if (IsStop(row)) stops.Add(row.Key);
        if (stops.Count == 0) return null;

        var index = currentKey is null ? -1 : stops.IndexOf(currentKey);
        if (index < 0) return delta >= 0 ? stops[0] : stops[stops.Count - 1];
        var next = Math.Clamp(index + (delta >= 0 ? 1 : -1), 0, stops.Count - 1);
        return stops[next];
    }
}
