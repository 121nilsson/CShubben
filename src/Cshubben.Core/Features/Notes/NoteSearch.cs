namespace Cshubben.Core.Features.Notes;

/// <summary>Search, tag filter and ordering for the notes list.</summary>
public static class NoteSearch
{
    private const int TitleScore = 100;
    private const int TitleStartBonus = 50;
    private const int TagExactScore = 60;
    private const int TagPartScore = 40;
    private const int BodyScore = 10;

    /// <summary>
    /// Notes matching every word of the query (title, tags or body, any case) and the optional tag,
    /// pinned first, then best match, then most recently updated.
    /// </summary>
    public static IReadOnlyList<Note> Filter(IEnumerable<Note> notes, string? query, string? tag)
    {
        var words = Words(query);
        var scored = new List<(Note Note, int Score)>();
        foreach (var note in notes)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !HasTag(note, tag)) continue;
            var score = Score(note, words);
            if (score < 0) continue;
            scored.Add((note, score));
        }

        return scored
            .OrderByDescending(x => x.Note.Pinned)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.Note.UpdatedAt)
            .ThenBy(x => x.Note.Id, StringComparer.Ordinal)
            .Select(x => x.Note)
            .ToList();
    }

    /// <summary>Relevance of a note for a query; -1 when some word does not match at all. An empty query scores 0.</summary>
    public static int Score(Note note, string? query) => Score(note, Words(query));

    private static int Score(Note note, string[] words)
    {
        var total = 0;
        foreach (var word in words)
        {
            var best = WordScore(note, word);
            if (best == 0) return -1;
            total += best;
        }
        return total;
    }

    private static int WordScore(Note note, string word)
    {
        var score = 0;
        var at = note.Title.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) score += TitleScore + (at == 0 ? TitleStartBonus : 0);

        foreach (var t in note.Tags)
        {
            if (string.Equals(t, word, StringComparison.OrdinalIgnoreCase)) { score += TagExactScore; break; }
            if (t.Contains(word, StringComparison.OrdinalIgnoreCase)) { score += TagPartScore; break; }
        }

        if (note.Body.Contains(word, StringComparison.OrdinalIgnoreCase)) score += BodyScore;
        return score;
    }

    private static string[] Words(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool HasTag(Note note, string tag) =>
        note.Tags.Any(t => string.Equals(t, tag.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>All tags in use, most used first, then alphabetical. Case-insensitive; the first spelling wins.</summary>
    public static IReadOnlyList<string> AllTags(IEnumerable<Note> notes)
    {
        var counts = new Dictionary<string, (string Name, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in notes)
        foreach (var tag in note.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            counts[tag] = counts.TryGetValue(tag, out var c) ? (c.Name, c.Count + 1) : (tag, 1);
        }
        return counts.Values
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => v.Name)
            .ToList();
    }

    /// <summary>Splits "a, b ,c" into clean, distinct tags.</summary>
    public static IReadOnlyList<string> ParseTags(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>The first meaningful line of a body, for list previews: markdown markers removed, code fences skipped.</summary>
    public static string Preview(string body, int maxLength = 120)
    {
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal)) continue;
            line = line.TrimStart('#', '>', '-', '*', ' ', '\t');
            line = line.Replace("**", "").Replace("`", "");
            if (line.Length == 0) continue;
            return line.Length <= maxLength ? line : line.Substring(0, maxLength - 1) + "…";
        }
        return "";
    }
}
