using Cshubben.Core.Features.Notes;
using Xunit;

namespace Cshubben.Core.Tests.Notes;

public class NoteMarkdownTests
{
    [Fact]
    public void Images_become_alt_text_and_never_load()
    {
        Assert.Equal("logo (image)", NoteMarkdown.Sanitize("![logo](https://evil.test/p.png?u=1)"));
        Assert.Equal("image (image)", NoteMarkdown.Sanitize("![](https://evil.test/p.png)"));
    }

    [Fact]
    public void Links_show_their_address_as_code_instead_of_being_clickable()
    {
        Assert.Equal("see docs (`https://x.test/a`) now", NoteMarkdown.Sanitize("see [docs](https://x.test/a) now"));
        Assert.Equal("click (`javascript:alert(1`)", NoteMarkdown.Sanitize("[click](javascript:alert(1))").Replace("))", ")"));
        Assert.Equal("plain", NoteMarkdown.Sanitize("[plain]()"));
        Assert.Equal("t (`https://x.test`)", NoteMarkdown.Sanitize("[t](https://x.test \"title\")"));
    }

    [Fact]
    public void Autolinks_and_bare_addresses_become_code_spans()
    {
        Assert.Equal("`https://a.test/x`", NoteMarkdown.Sanitize("<https://a.test/x>"));
        Assert.Equal("go to `https://a.test/x`.", NoteMarkdown.Sanitize("go to https://a.test/x."));
        Assert.Equal("`file:///c:/secret`", NoteMarkdown.Sanitize("<file:///c:/secret>"));
    }

    [Fact]
    public void Code_is_left_alone()
    {
        Assert.Equal("use `[a](b)` and `https://x.test`", NoteMarkdown.Sanitize("use `[a](b)` and `https://x.test`"));
        const string fenced = "```\n[a](https://x.test)\nhttps://y.test\n```";
        Assert.Equal(fenced, NoteMarkdown.Sanitize(fenced));
        Assert.Equal("a (`https://x.test`)\n```\n[a](https://x.test)\n```\nb (`https://y.test`)",
            NoteMarkdown.Sanitize("[a](https://x.test)\n```\n[a](https://x.test)\n```\n[b](https://y.test)"));
    }

    [Fact]
    public void Reference_style_definitions_are_escaped()
    {
        Assert.Equal("\\[ref]: `https://x.test`", NoteMarkdown.Sanitize("[ref]: https://x.test"));
    }

    [Fact]
    public void Ordinary_markdown_and_empty_input_pass_through()
    {
        const string text = "# Title\n\n- one\n- **two**\n\n> quote\n";
        Assert.Equal(text, NoteMarkdown.Sanitize(text));
        Assert.Equal("", NoteMarkdown.Sanitize(null));
        Assert.Equal("", NoteMarkdown.Sanitize(""));
    }

    [Fact]
    public void Demo_notes_sanitize_without_throwing()
    {
        foreach (var n in InMemoryNoteStore.Demo().ListAsync().GetAwaiter().GetResult())
            Assert.NotNull(NoteMarkdown.Sanitize(n.Body));
    }
}
