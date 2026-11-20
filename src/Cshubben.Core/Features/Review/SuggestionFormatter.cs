using System.Text;

namespace Cshubben.Core.Features.Review;

/// <summary>Builds and applies GitHub "suggested change" blocks.</summary>
public static class SuggestionFormatter
{
    /// <summary>
    /// A fenced suggestion block with the replacement text. The fence grows when the text itself contains backtick runs,
    /// so the block never ends early.
    /// </summary>
    public static string Format(string replacement)
    {
        var text = (replacement ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        var fence = new string('`', Math.Max(3, LongestBacktickRun(text) + 1));
        var sb = new StringBuilder();
        sb.Append(fence).Append("suggestion\n");
        if (text.Length > 0) sb.Append(text).Append('\n');
        sb.Append(fence);
        return sb.ToString();
    }

    /// <summary>The current text of lines start..end (1-based, inclusive) on the right side of a file, to prefill the suggestion.</summary>
    public static string CurrentText(DiffFile file, int startLine, int endLine)
    {
        var lo = Math.Min(startLine, endLine);
        var hi = Math.Max(startLine, endLine);
        var lines = new List<string>();
        foreach (var hunk in file.Hunks)
        {
            foreach (var line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Remove || line.Kind == DiffLineKind.NoNewline) continue;
                if (line.NewNumber is int n && n >= lo && n <= hi) lines.Add(line.Text);
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>A ready-made body: the suggestion block prefilled with the current lines.</summary>
    public static string Template(DiffFile file, int startLine, int endLine) => Format(CurrentText(file, startLine, endLine));

    /// <summary>Extracts the replacement text from the first suggestion block of a comment, or null.</summary>
    public static string? Extract(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("suggestion", StringComparison.Ordinal)) continue;
            var fenceLength = 0;
            while (fenceLength < trimmed.Length && trimmed[fenceLength] == '`') fenceLength++;
            if (trimmed.Substring(fenceLength) != "suggestion") continue;

            var content = new List<string>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                var t = lines[j].TrimEnd();
                if (t.Length >= fenceLength && t.All(c => c == '`')) return string.Join("\n", content);
                content.Add(lines[j]);
            }
            return null;
        }
        return null;
    }

    /// <summary>Applies a replacement to lines start..end (1-based, inclusive) of a file's text. Used by "apply suggestion".</summary>
    public static IReadOnlyList<string> Apply(IReadOnlyList<string> fileLines, int startLine, int endLine, string replacement)
    {
        var lo = Math.Max(1, Math.Min(startLine, endLine));
        var hi = Math.Min(fileLines.Count, Math.Max(startLine, endLine));
        var result = new List<string>();
        for (var i = 0; i < Math.Min(lo - 1, fileLines.Count); i++) result.Add(fileLines[i]);
        var normalized = replacement.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Length > 0) result.AddRange(normalized.Split('\n'));
        for (var i = hi; i < fileLines.Count; i++) result.Add(fileLines[i]);
        return result;
    }

    private static int LongestBacktickRun(string text)
    {
        var best = 0;
        var run = 0;
        foreach (var c in text)
        {
            if (c == '`')
            {
                run++;
                if (run > best) best = run;
            }
            else
            {
                run = 0;
            }
        }
        return best;
    }
}
