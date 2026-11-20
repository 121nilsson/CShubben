namespace Cshubben.Core.Features.CommandPalette;

/// <summary>A successful fuzzy match.</summary>
/// <param name="Score">Higher is better.</param>
/// <param name="Indices">Positions in the text of the matched query characters, ascending.</param>
public sealed record FuzzyMatch(int Score, IReadOnlyList<int> Indices);

/// <summary>
/// Subsequence matcher in the style of editor palettes: every query character must appear in order,
/// consecutive runs, word starts and the start of the text score higher, gaps cost a little.
/// Case-insensitive. Finds the best-scoring alignment, not just the first.
/// </summary>
public static class FuzzyMatcher
{
    public const int MaxTextLength = 120;
    public const int MaxQueryLength = 48;

    private const int MatchBase = 16;
    private const int BoundaryBonus = 24;
    private const int StartBonus = 8;
    private const int ConsecutiveBonus = 20;
    private const int MaxGapPenalty = 12;
    private const int MaxLeadingPenalty = 6;
    private const int Window = 40;
    private const int Impossible = int.MinValue / 2;

    /// <summary>Null when the query is not a subsequence of the text. An empty query matches with score 0.</summary>
    public static FuzzyMatch? Match(string? query, string? text)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return new FuzzyMatch(0, Array.Empty<int>());
        if (string.IsNullOrEmpty(text)) return null;
        if (q.Length > MaxQueryLength) q = q.Substring(0, MaxQueryLength);
        var t = text.Length > MaxTextLength ? text.Substring(0, MaxTextLength) : text;
        var m = q.Length;
        var n = t.Length;
        if (m > n) return null;

        var ql = q.ToLowerInvariant();
        var tl = t.ToLowerInvariant();
        if (ql.Length != m || tl.Length != n) return null; // exotic casing that changes length: do not guess

        var scan = 0;
        for (var j = 0; j < n && scan < m; j++)
            if (tl[j] == ql[scan]) scan++;
        if (scan < m) return null;

        var best = new int[m, n];
        var prevIdx = new int[m, n];
        for (var i = 0; i < m; i++)
            for (var j = 0; j < n; j++)
            {
                best[i, j] = Impossible;
                prevIdx[i, j] = -1;
            }

        for (var i = 0; i < m; i++)
        {
            for (var j = i; j < n; j++)
            {
                if (tl[j] != ql[i]) continue;
                var here = MatchBase + (IsBoundary(t, j) ? BoundaryBonus : 0) + (j == 0 ? StartBonus : 0);
                if (i == 0)
                {
                    best[i, j] = here - Math.Min(j, MaxLeadingPenalty);
                    continue;
                }

                var bestPrev = Impossible;
                var bestK = -1;
                var lowest = Math.Max(i - 1, j - 1 - Window);
                for (var k = j - 1; k >= lowest; k--)
                {
                    if (best[i - 1, k] <= Impossible) continue;
                    var step = k == j - 1 ? ConsecutiveBonus : -Math.Min(j - k - 1, MaxGapPenalty);
                    var candidate = best[i - 1, k] + step;
                    if (candidate > bestPrev)
                    {
                        bestPrev = candidate;
                        bestK = k;
                    }
                }

                if (bestK < 0) continue;
                best[i, j] = bestPrev + here;
                prevIdx[i, j] = bestK;
            }
        }

        var endJ = -1;
        var endScore = Impossible;
        for (var j = m - 1; j < n; j++)
        {
            if (best[m - 1, j] > endScore)
            {
                endScore = best[m - 1, j];
                endJ = j;
            }
        }

        if (endJ < 0) return null;

        var indices = new int[m];
        var cursor = endJ;
        for (var i = m - 1; i >= 0; i--)
        {
            indices[i] = cursor;
            cursor = prevIdx[i, cursor];
        }

        // A query that is the whole text (ignoring case) is the best possible answer.
        if (m == n) endScore += 40;
        return new FuzzyMatch(endScore, indices);
    }

    private static bool IsBoundary(string text, int index)
    {
        if (index == 0) return true;
        var prev = text[index - 1];
        if (!char.IsLetterOrDigit(prev)) return true;
        return char.IsLower(prev) && char.IsUpper(text[index]);
    }
}
