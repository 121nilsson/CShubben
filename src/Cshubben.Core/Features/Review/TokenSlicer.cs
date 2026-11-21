namespace Cshubben.Core.Features.Review;

/// <summary>
/// Cuts the syntax tokens of a whole line at the boundaries of its changed/unchanged spans, so a string or comment
/// that crosses a boundary keeps its colour on both sides.
/// </summary>
public static class TokenSlicer
{
    /// <summary>One token list per span, in order. Null when the spans and the tokens do not cover the same text.</summary>
    public static IReadOnlyList<IReadOnlyList<CodeToken>>? Slice(IReadOnlyList<CodeToken> tokens, IReadOnlyList<DiffSpan> spans)
    {
        if (tokens.Sum(t => t.Text.Length) != spans.Sum(s => s.Text.Length)) return null;

        var result = new List<IReadOnlyList<CodeToken>>(spans.Count);
        var index = 0;
        var offset = 0;
        foreach (var span in spans)
        {
            var parts = new List<CodeToken>();
            var need = span.Text.Length;
            while (need > 0)
            {
                var token = tokens[index];
                var take = Math.Min(token.Text.Length - offset, need);
                if (take > 0) parts.Add(new CodeToken(token.Text.Substring(offset, take), token.Kind));
                offset += take;
                need -= take;
                if (offset >= token.Text.Length)
                {
                    index++;
                    offset = 0;
                }
            }

            result.Add(parts);
        }

        return result;
    }
}
