using System.Text;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Shared;

/// <summary>
/// Cleans descriptions before they are drawn: drops tool footers and lines that only repeat a link shown elsewhere, and cuts long text
/// at a line break so a heading is never left dangling as raw "##" characters.
/// </summary>
public static class MarkdownTidy
{
    private static readonly Regex Boilerplate = new(
        @"^\W*(generated with .*claude|co-authored-by:|claude-session:|https://claude\.ai/code/session_)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A line that is only a Linear link, optionally behind a word such as "Closes" or as a Markdown link.</summary>
    private static readonly Regex LinearLinkLine = new(
        @"^\W*(?:(?:closes|fixes|resolves|refs?|linear|issue|ticket)\s*:?\s*)?(?:\[[^\]]*\]\()?<?https://linear\.app/\S+?>?\)?\W*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HeadingOnly = new(@"^\s{0,3}#{1,6}\s*\S?.{0,80}$", RegexOptions.CultureInvariant);

    public static string Tidy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = new List<string>();
        var blank = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (Boilerplate.IsMatch(raw) || LinearLinkLine.IsMatch(raw)) continue;
            if (raw.Trim().Length == 0)
            {
                if (++blank > 1) continue;
            }
            else
            {
                blank = 0;
            }

            lines.Add(raw.TrimEnd());
        }

        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// The tidied text, cut to about <paramref name="max"/> characters at a line break. Trailing headings and empty list markers are
    /// dropped so the cut never ends on a lone "##". <see cref="Truncated"/> says whether anything was cut.
    /// </summary>
    public static (string Text, bool Truncated) Preview(string? text, int max)
    {
        var tidy = Tidy(text);
        if (tidy.Length <= max) return (tidy, false);

        var cut = tidy.LastIndexOf('\n', max);
        if (cut < max / 3) cut = max; // one long line: cut inside it
        var lines = tidy[..cut].Split('\n').ToList();
        while (lines.Count > 1 && (lines[^1].Trim().Length == 0 || HeadingOnly.IsMatch(lines[^1]) || Regex.IsMatch(lines[^1], @"^\s*[-*]\s*$")))
            lines.RemoveAt(lines.Count - 1);

        var sb = new StringBuilder(string.Join("\n", lines).TrimEnd());
        return (sb.ToString(), true);
    }
}
