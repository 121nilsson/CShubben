using System.Globalization;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.CommandPalette;

/// <summary>
/// Orders palette entries for a query.
/// Syntax: plain text matches titles, then repo/number text and keywords at half weight; "#123" finds a pull request by
/// number; "repo#123" narrows to that repo; a leading "&gt;" shows commands and pages only; a leading "#" shows pull requests only.
/// </summary>
public static class PaletteRanker
{
    public const int DefaultMax = 40;

    private static readonly Regex RepoNumber = new(@"^(?<repo>.*?)#(?<num>\d+)$", RegexOptions.CultureInvariant);

    /// <param name="items">All entries, in the order they should appear for an empty query.</param>
    /// <param name="query">What the user typed.</param>
    /// <param name="max">Most results to return.</param>
    /// <param name="recentIds">Entry ids used lately; they come first for an empty query.</param>
    public static IReadOnlyList<PaletteResult> Rank(
        IReadOnlyList<PaletteItem> items,
        string? query,
        int max = DefaultMax,
        IReadOnlyList<string>? recentIds = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (max < 1) return Array.Empty<PaletteResult>();
        var q = (query ?? "").Trim();

        var commandsOnly = q.StartsWith('>');
        if (commandsOnly) q = q.Substring(1).Trim();
        var prsOnly = !commandsOnly && q.StartsWith('#');

        IEnumerable<(PaletteItem Item, int Index)> pool = items.Select((entry, position) => (Item: entry, Index: position));
        if (commandsOnly) pool = pool.Where(x => x.Item.Kind != PaletteKind.PullRequest);
        if (prsOnly) pool = pool.Where(x => x.Item.Kind == PaletteKind.PullRequest);

        if (q.Length == 0 || q == "#")
            return EmptyQuery(pool.ToList(), max, recentIds);

        var results = new List<(PaletteResult Result, int Index)>();
        foreach (var (item, index) in pool)
        {
            var result = Score(item, q);
            if (result is not null) results.Add((result, index));
        }

        return results
            .OrderByDescending(r => r.Result.Score)
            .ThenBy(r => r.Index)
            .Take(max)
            .Select(r => r.Result)
            .ToList();
    }

    private static IReadOnlyList<PaletteResult> EmptyQuery(List<(PaletteItem Item, int Index)> pool, int max, IReadOnlyList<string>? recentIds)
    {
        IEnumerable<PaletteItem> ordered = pool.Select(x => x.Item);
        if (recentIds is { Count: > 0 })
        {
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < recentIds.Count; i++) rank.TryAdd(recentIds[i], i);
            ordered = pool
                .OrderBy(x => rank.TryGetValue(x.Item.Id, out var r) ? r : int.MaxValue)
                .ThenBy(x => x.Index)
                .Select(x => x.Item);
        }

        return ordered.Take(max).Select(i => new PaletteResult(i, 0, Array.Empty<int>())).ToList();
    }

    private static PaletteResult? Score(PaletteItem item, string rawQuery)
    {
        var q = rawQuery.StartsWith('#') ? rawQuery.Substring(1).Trim() : rawQuery;

        // "repo#123"
        var rn = RepoNumber.Match(rawQuery);
        if (rn.Success && rn.Groups["repo"].Value.Length > 0)
        {
            if (item.Kind != PaletteKind.PullRequest) return null;
            if (!int.TryParse(rn.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var wanted) || item.Number != wanted) return null;
            var repoMatch = FuzzyMatcher.Match(rn.Groups["repo"].Value.Trim(), item.Repo);
            return repoMatch is null ? null : new PaletteResult(item, 300 + repoMatch.Score, Array.Empty<int>());
        }

        // "#123" or "123": PR number (exact beats prefix), plus the normal text match below for digits in titles.
        var digits = q.Length > 0 && q.All(char.IsAsciiDigit);
        var numberScore = 0;
        if (digits && item.Kind == PaletteKind.PullRequest && item.Number > 0)
        {
            var number = item.Number.ToString(CultureInfo.InvariantCulture);
            if (number == q) numberScore = 400;
            else if (number.StartsWith(q, StringComparison.Ordinal)) numberScore = 250 - Math.Min(number.Length - q.Length, 20);
        }

        if (rawQuery.StartsWith('#') && digits)
            return numberScore > 0 ? new PaletteResult(item, numberScore, Array.Empty<int>()) : null;

        var title = FuzzyMatcher.Match(q, item.Title);
        var subtitle = item.Subtitle.Length > 0 ? FuzzyMatcher.Match(q, item.Subtitle) : null;
        var keywords = item.Keywords.Length > 0 ? FuzzyMatcher.Match(q, item.Keywords) : null;

        var score = int.MinValue;
        IReadOnlyList<int> indices = Array.Empty<int>();
        if (title is not null)
        {
            score = title.Score;
            indices = title.Indices;
        }

        if (subtitle is not null && subtitle.Score / 2 > score) score = subtitle.Score / 2;
        if (keywords is not null && keywords.Score / 2 > score) score = keywords.Score / 2;
        if (numberScore > score) score = numberScore;
        if (score == int.MinValue) return null;

        // Commands and pages first when the text matches equally well.
        if (item.Kind != PaletteKind.PullRequest) score += 6;
        return new PaletteResult(item, score, indices);
    }
}
