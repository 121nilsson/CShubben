using System.Text;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>
/// Cleans failure output before it is shown. The text comes from GitHub and is untrusted: escape codes and control
/// characters are removed, and the length is capped.
/// </summary>
public static class LogExcerpt
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);

    /// <summary>
    /// The first <paramref name="maxLines"/> non-trailing-blank lines, at most <paramref name="maxChars"/> characters,
    /// with a trailing ellipsis when something was cut. Null when nothing readable remains.
    /// </summary>
    public static string? Clean(string? text, int maxLines = 10, int maxChars = 900)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var withoutAnsi = Ansi.Replace(text, string.Empty);
        var normalized = withoutAnsi.Replace("\r\n", "\n").Replace('\r', '\n');

        var lines = new List<string>();
        foreach (var raw in normalized.Split('\n'))
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (c == '\t') sb.Append("  ");
                else if (!char.IsControl(c)) sb.Append(c);
            }
            lines.Add(sb.ToString().TrimEnd());
        }

        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return null;

        var cut = false;
        if (lines.Count > maxLines)
        {
            lines = lines.GetRange(0, maxLines);
            cut = true;
        }

        var result = string.Join("\n", lines);
        if (result.Length > maxChars)
        {
            result = result.Substring(0, maxChars).TrimEnd();
            cut = true;
        }

        return cut ? result + "\n…" : result;
    }
}
