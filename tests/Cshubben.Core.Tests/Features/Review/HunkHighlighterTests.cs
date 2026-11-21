using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class HunkHighlighterTests
{
    private static DiffHunk Hunk(params DiffLine[] lines) => new("@@ -1,1 +1,1 @@", 1, 1, 1, 1, lines);

    private static DiffLine Ctx(string text) => new(DiffLineKind.Context, 1, 1, text);
    private static DiffLine Add(string text) => new(DiffLineKind.Add, null, 2, text);
    private static DiffLine Del(string text) => new(DiffLineKind.Remove, 2, null, text);

    [Fact]
    public void Block_comment_colours_the_lines_inside_it()
    {
        var hunk = Hunk(Ctx("/* start"), Add("   still a comment"), Ctx("   end */"), Ctx("var x = 1;"));
        var middle = HunkHighlighter.TokensFor(hunk, 1, "a.cs");
        Assert.All(middle.Where(t => t.Text.Trim().Length > 0), t => Assert.Equal(TokenKind.Comment, t.Kind));
        Assert.Contains(HunkHighlighter.TokensFor(hunk, 3, "a.cs"), t => t is { Kind: TokenKind.Keyword, Text: "var" });
    }

    [Fact]
    public void Removed_lines_follow_the_old_side_and_added_lines_the_new_side()
    {
        // The old file has the comment opener on a removed line; the new file does not, so the added line is code.
        var hunk = Hunk(Del("/* old"), Add("var y = 2;"), Del("   old end */"));
        Assert.Contains(HunkHighlighter.TokensFor(hunk, 1, "a.cs"), t => t is { Kind: TokenKind.Keyword, Text: "var" });
        Assert.All(HunkHighlighter.TokensFor(hunk, 2, "a.cs").Where(t => t.Text.Trim().Length > 0), t => Assert.Equal(TokenKind.Comment, t.Kind));
    }

    [Fact]
    public void Tokens_reassemble_and_unknown_files_stay_plain()
    {
        var hunk = Hunk(Ctx("const a = `x"), Add("y ${b}`;"));
        for (var i = 0; i < 2; i++)
            Assert.Equal(hunk.Lines[i].Text, string.Concat(HunkHighlighter.TokensFor(hunk, i, "a.ts").Select(t => t.Text)));
        Assert.Single(HunkHighlighter.TokensFor(hunk, 0, "README"));
    }
}
