using System.Text;

namespace Cshubben.Core.Features.Review;

/// <summary>A piece of a line; Changed pieces are the words that differ from the paired line.</summary>
public sealed record DiffSpan(string Text, bool Changed);

/// <summary>Spans for both sides of a changed pair. Ratio is the share of characters that stayed the same (0..1).</summary>
public sealed record IntraLineResult(IReadOnlyList<DiffSpan> OldSpans, IReadOnlyList<DiffSpan> NewSpans, double Ratio)
{
    /// <summary>Highlighting words only helps when the two lines are alike; for rewrites it is just noise.</summary>
    public bool IsUseful => Ratio >= IntraLineDiff.MinUsefulRatio;
}

/// <summary>Word-level diff between a removed and an added line: LCS over tokens, capped for long lines.</summary>
public static class IntraLineDiff
{
    public const int MaxTokens = 400;
    public const int MaxChars = 2000;
    public const double MinUsefulRatio = 0.35;

    public static IntraLineResult Compute(string oldText, string newText)
    {
        oldText ??= string.Empty;
        newText ??= string.Empty;

        if (oldText == newText)
        {
            var same = oldText.Length == 0 ? Array.Empty<DiffSpan>() : new[] { new DiffSpan(oldText, false) };
            return new IntraLineResult(same, same, 1.0);
        }

        if (oldText.Length > MaxChars || newText.Length > MaxChars)
        {
            return PrefixSuffix(oldText, newText);
        }

        var a = Tokenize(oldText);
        var b = Tokenize(newText);
        if (a.Count > MaxTokens || b.Count > MaxTokens)
        {
            return PrefixSuffix(oldText, newText);
        }

        var keepA = new bool[a.Count];
        var keepB = new bool[b.Count];
        Lcs(a, b, keepA, keepB);

        var oldSpans = BuildSpans(a, keepA);
        var newSpans = BuildSpans(b, keepB);
        return new IntraLineResult(oldSpans, newSpans, Ratio(oldSpans, newSpans));
    }

    /// <summary>
    /// Pairs removed and added lines inside one hunk: a run of removes directly followed by a run of adds is
    /// paired one to one from the top. Returns line index to partner index (both directions).
    /// </summary>
    public static IReadOnlyDictionary<int, int> PairLines(IReadOnlyList<DiffLine> lines)
    {
        var pairs = new Dictionary<int, int>();
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Kind != DiffLineKind.Remove)
            {
                i++;
                continue;
            }

            var removeStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Remove) i++;
            var removeEnd = i;
            var addStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Add) i++;
            var addEnd = i;

            var n = Math.Min(removeEnd - removeStart, addEnd - addStart);
            for (var k = 0; k < n; k++)
            {
                pairs[removeStart + k] = addStart + k;
                pairs[addStart + k] = removeStart + k;
            }
        }
        return pairs;
    }

    /// <summary>Splits into identifiers/numbers, whitespace runs, and single punctuation characters.</summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var start = i;
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            }
            else
            {
                i++;
            }
            tokens.Add(text.Substring(start, i - start));
        }
        return tokens;
    }

    private static void Lcs(List<string> a, List<string> b, bool[] keepA, bool[] keepB)
    {
        var n = a.Count;
        var m = b.Count;
        var table = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == b[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                keepA[x] = true;
                keepB[y] = true;
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }
    }

    private static IReadOnlyList<DiffSpan> BuildSpans(List<string> tokens, bool[] keep)
    {
        var spans = new List<DiffSpan>();
        var sb = new StringBuilder();
        var current = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var changed = !keep[i];
            if (sb.Length > 0 && changed != current)
            {
                spans.Add(new DiffSpan(sb.ToString(), current));
                sb.Clear();
            }
            current = changed;
            sb.Append(tokens[i]);
        }
        if (sb.Length > 0) spans.Add(new DiffSpan(sb.ToString(), current));
        return Normalize(spans);
    }

    /// <summary>Whitespace-only unchanged pieces between two changed pieces are absorbed so highlights read as one phrase.</summary>
    private static IReadOnlyList<DiffSpan> Normalize(List<DiffSpan> spans)
    {
        var result = new List<DiffSpan>();
        for (var i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            var isBridge = !s.Changed
                && string.IsNullOrWhiteSpace(s.Text)
                && i > 0 && i < spans.Count - 1
                && spans[i - 1].Changed && spans[i + 1].Changed;
            if (isBridge) s = new DiffSpan(s.Text, true);

            if (result.Count > 0 && result[result.Count - 1].Changed == s.Changed)
            {
                var last = result[result.Count - 1];
                result[result.Count - 1] = new DiffSpan(last.Text + s.Text, s.Changed);
            }
            else
            {
                result.Add(s);
            }
        }
        return result;
    }

    private static double Ratio(IReadOnlyList<DiffSpan> oldSpans, IReadOnlyList<DiffSpan> newSpans)
    {
        var total = 0;
        var same = 0;
        foreach (var s in oldSpans)
        {
            total += s.Text.Length;
            if (!s.Changed) same += s.Text.Length;
        }
        foreach (var s in newSpans)
        {
            total += s.Text.Length;
            if (!s.Changed) same += s.Text.Length;
        }
        return total == 0 ? 1.0 : (double)same / total;
    }

    private static IntraLineResult PrefixSuffix(string oldText, string newText)
    {
        var max = Math.Min(oldText.Length, newText.Length);
        var prefix = 0;
        while (prefix < max && oldText[prefix] == newText[prefix]) prefix++;
        var suffix = 0;
        while (suffix < max - prefix && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) suffix++;

        var oldSpans = Cut(oldText, prefix, suffix);
        var newSpans = Cut(newText, prefix, suffix);
        return new IntraLineResult(oldSpans, newSpans, Ratio(oldSpans, newSpans));
    }

    private static IReadOnlyList<DiffSpan> Cut(string text, int prefix, int suffix)
    {
        var spans = new List<DiffSpan>();
        if (prefix > 0) spans.Add(new DiffSpan(text.Substring(0, prefix), false));
        var middle = text.Length - prefix - suffix;
        if (middle > 0) spans.Add(new DiffSpan(text.Substring(prefix, middle), true));
        if (suffix > 0) spans.Add(new DiffSpan(text.Substring(text.Length - suffix), false));
        return spans;
    }
}
