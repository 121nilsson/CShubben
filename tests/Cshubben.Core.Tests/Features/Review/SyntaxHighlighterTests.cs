using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class SyntaxHighlighterTests
{
    private static string Kinds(string text, string path) =>
        string.Join("|", SyntaxHighlighter.TokenizeBasic(text, path).Select(t => t.Kind + ":" + t.Text));

    [Fact]
    public void Unknown_extension_is_one_plain_token()
    {
        var single = Assert.Single(SyntaxHighlighter.TokenizeBasic("if (x) return;", "notes.txt"));
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
            Assert.Equal(text, string.Concat(SyntaxHighlighter.TokenizeBasic(text, path).Select(t => t.Text)));
    }

    [Fact]
    public void CLike_finds_keywords_strings_numbers_and_comments()
    {
        var tokens = SyntaxHighlighter.TokenizeBasic("return \"x\" + 7; // done", "a.cs");
        Assert.Contains(tokens, t => t is { Kind: TokenKind.Keyword, Text: "return" });
        Assert.Contains(tokens, t => t is { Kind: TokenKind.String, Text: "\"x\"" });
        Assert.Contains(tokens, t => t is { Kind: TokenKind.Number, Text: "7" });
        Assert.Contains(tokens, t => t.Kind == TokenKind.Comment && t.Text.StartsWith("// done", StringComparison.Ordinal));
    }

    [Fact]
    public void Keywords_inside_identifiers_and_strings_are_not_keywords()
    {
        Assert.DoesNotContain(SyntaxHighlighter.TokenizeBasic("var interfaceName = 1;", "a.cs"), t => t is { Kind: TokenKind.Keyword, Text: "interfaceName" });
        Assert.DoesNotContain(SyntaxHighlighter.TokenizeBasic("x = \"if\"", "a.cs"), t => t is { Kind: TokenKind.Keyword, Text: "if" });
    }

    [Fact]
    public void Hash_comment_needs_whitespace_before_it()
    {
        Assert.DoesNotContain(SyntaxHighlighter.TokenizeBasic("url = a#b", "a.py"), t => t.Kind == TokenKind.Comment);
        Assert.Contains(SyntaxHighlighter.TokenizeBasic("x = 1  # one", "a.py"), t => t.Kind == TokenKind.Comment);
    }

    [Fact]
    public void Unterminated_string_stays_plain_and_long_lines_are_not_highlighted()
    {
        Assert.DoesNotContain(SyntaxHighlighter.TokenizeBasic("let s = 'oops", "a.ts"), t => t.Kind == TokenKind.String);
        Assert.Single(SyntaxHighlighter.TokenizeBasic(new string('a', 500), "a.cs"));
    }

    [Fact]
    public void Json_highlights_literals_and_strings()
    {
        Assert.Equal("Plain:  |String:\"ok\"|Plain:: |Keyword:true", Kinds("  \"ok\": true", "a.json"));
    }

    [Fact]
    public void CSharp_verbatim_raw_strings_and_preprocessor_lines()
    {
        Assert.Contains(SyntaxHighlighter.TokenizeBasic("var p = @\"C:\\dir\\\" + x;", "a.cs"), t => t is { Kind: TokenKind.String, Text: "\"C:\\dir\\\"" });
        Assert.Contains(SyntaxHighlighter.TokenizeBasic("var q = \"\"\"raw \" text\"\"\";", "a.cs"), t => t.Kind == TokenKind.String && t.Text.Contains("raw", StringComparison.Ordinal));
        var region = Assert.Single(SyntaxHighlighter.TokenizeBasic("#region Helpers", "a.cs"));
        Assert.Equal(TokenKind.Keyword, region.Kind);
        Assert.Contains(SyntaxHighlighter.TokenizeBasic("public required string Name { get; init; }", "a.cs"), t => t is { Kind: TokenKind.Keyword, Text: "required" });
    }

    [Fact]
    public void Jsx_tags_are_highlighted_but_generics_are_not()
    {
        Assert.Contains(SyntaxHighlighter.TokenizeBasic("return <Button label=\"x\" onClick={go}>Go</Button>;", "a.tsx"), t => t is { Kind: TokenKind.Keyword, Text: "Button" });
        Assert.DoesNotContain(SyntaxHighlighter.TokenizeBasic("const [u, setU] = useState<User>(null);", "a.tsx"), t => t is { Kind: TokenKind.Keyword, Text: "User" });
        Assert.Equal("x=<div>", string.Concat(SyntaxHighlighter.TokenizeBasic("x=<div>", "a.jsx").Select(t => t.Text)));
    }

    [Fact]
    public void Template_literals_and_typescript_keywords()
    {
        Assert.Contains(SyntaxHighlighter.TokenizeBasic("const s = `hi ${name}`;", "a.ts"), t => t.Kind == TokenKind.String);
        Assert.Contains(SyntaxHighlighter.TokenizeBasic("if (x instanceof Foo) return undefined;", "a.ts"), t => t is { Kind: TokenKind.Keyword, Text: "instanceof" });
    }

    [Theory]
    [InlineData("public async Task<int> Run() { return 42; } // go", "A.cs")]
    [InlineData("export const App = () => <Button label=\"x\" onClick={go}>Go</Button>;", "a.tsx")]
    [InlineData("const s = `hi ${name}`; /* x */", "a.ts")]
    [InlineData("def f(x): return \"a\" # c", "a.py")]
    public void Full_tokenizer_reassembles_and_finds_keywords_and_strings_or_comments(string text, string path)
    {
        var tokens = SyntaxHighlighter.Tokenize(text, path);
        Assert.Equal(text, string.Concat(tokens.Select(t => t.Text)));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword);
        Assert.Contains(tokens, t => t.Kind is TokenKind.String or TokenKind.Comment or TokenKind.Number);
    }

    [Fact]
    public void Falls_back_to_plain_for_unknown_files_and_marks_known_ones_highlighted()
    {
        Assert.Single(SyntaxHighlighter.Tokenize("if (x) return;", "notes.unknownext"));
        Assert.True(SyntaxHighlighter.IsHighlighted("src/App.cs"));
        Assert.False(SyntaxHighlighter.IsHighlighted("LICENSE"));
    }
}
