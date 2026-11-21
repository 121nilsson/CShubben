using System.Text;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Notes;

/// <summary>
/// Makes note markdown safe to preview. Notes can hold text pasted from anywhere (Claude answers, issues, web pages), so the
/// preview must not fetch remote images or offer live links. Images become their alt text, links become "text (`url`)" and bare
/// addresses become code spans, which no renderer turns into a link. Explicit "open" actions live in the links and mentions lists
/// instead, where a click is a deliberate choice and only http(s) addresses are opened. Code is left untouched.
/// </summary>
public static class NoteMarkdown
{
    private static readonly Regex Image = new(@"!\[([^\]\r\n]*)\]\(([^)\r\n]*)\)", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]\r\n]*)\]\(\s*<?([^)\s>\r\n]*)>?[^)\r\n]*\)", RegexOptions.Compiled);
    private static readonly Regex AutoLink = new(@"<((?:https?|ftp|file|mailto):[^>\s]*)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Bare = new(@"(?<![`\w/])(?:https?|ftp|file)://[^\s<>`]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Definition = new(@"^(\s{0,3})\[(?=[^\]\r\n]+\]:)", RegexOptions.Compiled);

    public static string Sanitize(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(markdown.Length + 16);
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                sb.Append(line);
            }
            else if (inFence)
            {
                sb.Append(line);
            }
            else
            {
                sb.Append(SanitizeLine(line));
            }
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string SanitizeLine(string line)
    {
        line = Definition.Replace(line, "$1\\[");

        // Only touch text outside inline code. An odd number of backticks means the line is not reliably split, so treat all of it as text.
        var parts = line.Split('`');
        if (parts.Length % 2 == 0) return Clean(line);
        for (var i = 0; i < parts.Length; i += 2) parts[i] = Clean(parts[i]);
        return string.Join("`", parts);
    }

    private static string Clean(string text)
    {
        if (text.Length == 0) return text;
        text = Image.Replace(text, m => (m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value.Trim() : "image") + " (image)");
        text = Link.Replace(text, m =>
        {
            var label = m.Groups[1].Value;
            var url = m.Groups[2].Value.Replace('`', ' ').Trim();
            if (url.Length == 0) return label;
            return label + " (`" + url + "`)";
        });
        text = AutoLink.Replace(text, m => "`" + m.Groups[1].Value + "`");
        text = Bare.Replace(text, m =>
        {
            var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
            return "`" + url + "`" + m.Value.Substring(url.Length);
        });
        return text;
    }
}
