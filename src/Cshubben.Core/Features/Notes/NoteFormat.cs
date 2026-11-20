using System.Globalization;
using System.Text;

namespace Cshubben.Core.Features.Notes;

/// <summary>
/// Reads and writes a note as markdown with a small YAML-like front matter block. Every text value is written as a
/// double-quoted string with escapes, so any title, tag or label survives a round trip.
/// </summary>
public static class NoteFormat
{
    private const string Fence = "---";

    public static string Serialize(Note note)
    {
        var sb = new StringBuilder();
        sb.Append(Fence).Append('\n');
        sb.Append("title: ").Append(Quote(note.Title)).Append('\n');
        sb.Append("pinned: ").Append(note.Pinned ? "true" : "false").Append('\n');
        sb.Append("created: ").Append(note.CreatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("updated: ").Append(note.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("tags:\n");
        foreach (var tag in note.Tags)
            sb.Append("- ").Append(Quote(tag)).Append('\n');
        sb.Append("links:\n");
        foreach (var link in note.Links)
            sb.Append("- ").Append(link.Kind.ToString()).Append(' ').Append(Quote(link.Target)).Append(' ').Append(Quote(link.Label)).Append('\n');
        sb.Append(Fence).Append('\n');
        sb.Append(note.Body);
        return sb.ToString();
    }

    /// <summary>Parses a note file. Files without front matter are accepted; the whole text becomes the body.</summary>
    public static Note Parse(string id, string text, DateTimeOffset fallbackTime)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);

        var pos = 0;
        var front = new List<string>();
        var body = text;
        var first = ReadLine(text, ref pos);
        if (first == Fence)
        {
            var closed = false;
            while (pos < text.Length)
            {
                var line = ReadLine(text, ref pos);
                if (line == Fence) { closed = true; break; }
                front.Add(line);
            }
            if (closed) body = text.Substring(pos);
            else front.Clear();
        }

        var title = "";
        var pinned = false;
        var created = fallbackTime;
        var updated = fallbackTime;
        var tags = new List<string>();
        var links = new List<NoteLink>();
        var listKey = "";

        foreach (var line in front)
        {
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var item = line.Substring(2);
                if (listKey == "tags")
                {
                    var p = 0;
                    if (TryUnquote(item, ref p, out var tag)) tags.Add(tag);
                }
                else if (listKey == "links")
                {
                    var link = ParseLink(item);
                    if (link is not null) links.Add(link);
                }
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line.Substring(0, colon).Trim();
            var rest = line.Substring(colon + 1).Trim();
            listKey = "";
            switch (key)
            {
                case "title": title = ReadValue(rest); break;
                case "pinned": pinned = rest == "true"; break;
                case "created": created = ParseTime(rest, fallbackTime); break;
                case "updated": updated = ParseTime(rest, fallbackTime); break;
                case "tags": listKey = "tags"; break;
                case "links": listKey = "links"; break;
            }
        }

        return new Note(id, title, body, tags, links, Array.Empty<string>(), pinned, created, updated);
    }

    private static NoteLink? ParseLink(string item)
    {
        var space = item.IndexOf(' ');
        if (space <= 0) return null;
        if (!Enum.TryParse<NoteLinkKind>(item.Substring(0, space), out var kind)) return null;
        var p = space + 1;
        if (!TryUnquote(item, ref p, out var target)) return null;
        while (p < item.Length && item[p] == ' ') p++;
        var label = "";
        if (p < item.Length && !TryUnquote(item, ref p, out label)) return null;
        return new NoteLink(kind, target, label);
    }

    private static DateTimeOffset ParseTime(string text, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : fallback;

    private static string ReadValue(string rest)
    {
        if (rest.StartsWith('"'))
        {
            var p = 0;
            if (TryUnquote(rest, ref p, out var value)) return value;
        }
        return rest;
    }

    /// <summary>Reads one line (without its line break) and moves past the break.</summary>
    private static string ReadLine(string text, ref int pos)
    {
        var nl = text.IndexOf('\n', pos);
        string line;
        if (nl < 0)
        {
            line = text.Substring(pos);
            pos = text.Length;
        }
        else
        {
            line = text.Substring(pos, nl - pos);
            pos = nl + 1;
        }
        if (line.EndsWith('\r')) line = line.Substring(0, line.Length - 1);
        return line;
    }

    public static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        sb.Append(c).Append(s[i + 1]);
                        i++;
                    }
                    else if (c < ' ' || char.IsSurrogate(c) || c == '\u0085' || c == '\u2028' || c == '\u2029')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Reads a quoted string starting at pos (which must point at the opening quote).</summary>
    public static bool TryUnquote(string s, ref int pos, out string result)
    {
        result = "";
        if (pos >= s.Length || s[pos] != '"') return false;
        var i = pos + 1;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            var c = s[i++];
            if (c == '"')
            {
                result = sb.ToString();
                pos = i;
                return true;
            }
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            if (i >= s.Length) return false;
            var e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case 'u':
                    if (i + 4 > s.Length) return false;
                    if (!int.TryParse(s.AsSpan(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        return false;
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: return false;
            }
        }
        return false;
    }
}
