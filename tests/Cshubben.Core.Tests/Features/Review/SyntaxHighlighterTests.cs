using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class SyntaxHighlighterTests
{
    private static string Kinds(string text, string path) =>
        string.Join("|", SyntaxHighlighter.Tokenize(text, path).Select(t => t.Kind + ":" + t.Text));

    [Fact]
    public void Unknown_extension_is_one_plain_token()
    {
        var single = Assert.Single(SyntaxHighlighter.Tokenize("if (x) return;", "notes.txt"));
        Assert.Equal(TokenKind.Plain, single.Kind);
    }

    [Fact]
    public void Tokens_always_reassemble_to_the_original_text()
    {
        foreach (var (text, path) in new[]
        {
            ("public static int Count = 42; // total", "A.cs"),
            ("name = \"a \\\"quoted\\\" b\"  # note", "a.py"),
            ("  \"id\": 12, \"ok\": true,", "x.json"),
            ("let s = 'unterminated", "a.ts"),
        })
            Assert.Equal(text, string.Concat(SyntaxHighlighter.Tokenize(text, path).Select(t => t.Text)));
    }

    [Fact]
    public void CLike_finds_keywords_strings_numbers_and_comments()
    {
        var tokens = SyntaxHighlighter.Tokenize("return \"x\" + 7; // done", "a.cs");
        Assert.Contains(tokens, t => t is { Kind: TokenKind.Keyword, Text: "return" });
        Assert.Contains(tokens, t => t is { Kind: TokenKind.String, Text: "\"x\"" });
        Assert.Contains(tokens, t => t is { Kind: TokenKind.Number, Text: "7" });
        Assert.Contains(tokens, t => t.Kind == TokenKind.Comment && t.Text.StartsWith("// done", StringComparison.Ordinal));
    }

    [Fact]
    public void Keywords_inside_identifiers_and_strings_are_not_keywords()
    {
        Assert.DoesNotContain(SyntaxHighlighter.Tokenize("var interfaceName = 1;", "a.cs"), t => t is { Kind: TokenKind.Keyword, Text: "interfaceName" });
        Assert.DoesNotContain(SyntaxHighlighter.Tokenize("x = \"if\"", "a.cs"), t => t is { Kind: TokenKind.Keyword, Text: "if" });
    }

    [Fact]
    public void Hash_comment_needs_whitespace_before_it()
    {
        Assert.DoesNotContain(SyntaxHighlighter.Tokenize("url = a#b", "a.py"), t => t.Kind == TokenKind.Comment);
        Assert.Contains(SyntaxHighlighter.Tokenize("x = 1  # one", "a.py"), t => t.Kind == TokenKind.Comment);
    }

    [Fact]
    public void Unterminated_string_stays_plain_and_long_lines_are_not_highlighted()
    {
        Assert.DoesNotContain(SyntaxHighlighter.Tokenize("let s = 'oops", "a.ts"), t => t.Kind == TokenKind.String);
        Assert.Single(SyntaxHighlighter.Tokenize(new string('a', 500), "a.cs"));
    }

    [Fact]
    public void Json_highlights_literals_and_strings()
    {
        Assert.Equal("Plain:  |String:\"ok\"|Plain:: |Keyword:true", Kinds("  \"ok\": true", "a.json"));
    }
}
