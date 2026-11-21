using System.Text;

namespace Cshubben.Core.Features.Review;

/// <summary>The code lines picked in the diff to ask a question about.</summary>
/// <param name="Label">"lines 10 to 14", or "line 12".</param>
/// <param name="Code">The lines as diff text: "+" added, "-" removed, a space for context.</param>
public sealed record CodeSelection(string Label, string Code, int LineCount);

public static class CodeSelectionBuilder
{
    public const int MaxLines = 80;

    /// <summary>
    /// The code lines between the mark and the cursor (either order), or just the cursor line when nothing is marked. Threads and
    /// drafts in between are skipped. Null when the cursor is not on a code line.
    /// </summary>
    public static CodeSelection? Build(IReadOnlyList<ReviewRow> rows, string? markKey, string? cursorKey)
    {
        if (cursorKey is null) return null;
        var cursor = IndexOf(rows, cursorKey);
        if (cursor < 0) return null;
        var mark = markKey is null ? cursor : IndexOf(rows, markKey);
        if (mark < 0) mark = cursor;

        var from = Math.Min(mark, cursor);
        var to = Math.Max(mark, cursor);
        var lines = rows.Skip(from).Take(to - from + 1).OfType<LineRow>().Select(r => r.Line).ToList();
        if (lines.Count == 0) return null;
        if (lines.Count > MaxLines) lines = lines.Take(MaxLines).ToList();

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var sign = line.Kind switch { DiffLineKind.Add => '+', DiffLineKind.Remove => '-', _ => ' ' };
            sb.Append(sign).Append(line.Text).Append('\n');
        }

        var numbers = lines.Select(l => l.NewNumber ?? l.OldNumber).Where(n => n is not null).Select(n => n!.Value).ToList();
        var label = numbers.Count == 0 ? "the selected lines"
            : numbers.Min() == numbers.Max() ? "line " + numbers[0]
            : "lines " + numbers.Min() + " to " + numbers.Max();
        return new CodeSelection(label, sb.ToString().TrimEnd('\n'), lines.Count);
    }

    private static int IndexOf(IReadOnlyList<ReviewRow> rows, string key)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Key == key) return i;
        return -1;
    }
}
