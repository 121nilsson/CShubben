using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class TokenSlicerTests
{
    private static string Join(IReadOnlyList<CodeToken> tokens) => string.Concat(tokens.Select(t => t.Text));

    [Fact]
    public void A_token_that_crosses_a_span_boundary_keeps_its_kind_on_both_sides()
    {
        var tokens = new[] { new CodeToken("x = ", TokenKind.Plain), new CodeToken("\"abcdef\"", TokenKind.String) };
        var spans = new[] { new DiffSpan("x = \"abc", false), new DiffSpan("def\"", true) };

        var slices = TokenSlicer.Slice(tokens, spans)!;

        Assert.Equal(2, slices.Count);
        Assert.Equal("x = \"abc", Join(slices[0]));
        Assert.Equal("def\"", Join(slices[1]));
        Assert.Equal(TokenKind.String, slices[0][^1].Kind);
        Assert.All(slices[1], t => Assert.Equal(TokenKind.String, t.Kind));
    }

    [Fact]
    public void Empty_spans_and_tokens_are_handled_and_a_length_mismatch_gives_null()
    {
        var tokens = new[] { new CodeToken("", TokenKind.Plain), new CodeToken("ab", TokenKind.Keyword) };
        var slices = TokenSlicer.Slice(tokens, new[] { new DiffSpan("", false), new DiffSpan("a", true), new DiffSpan("b", false) })!;
        Assert.Empty(slices[0]);
        Assert.Equal("a", Join(slices[1]));
        Assert.Equal("b", Join(slices[2]));

        Assert.Null(TokenSlicer.Slice(tokens, new[] { new DiffSpan("abc", false) }));
    }
}
