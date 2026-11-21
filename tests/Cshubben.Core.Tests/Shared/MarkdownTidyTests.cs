using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Shared;

public class MarkdownTidyTests
{
    [Fact]
    public void Footers_and_lone_linear_links_are_removed()
    {
        var text = "## Summary\nAdds the API.\n\nCloses https://linear.app/acme/issue/ABC-1/add-api\n[ABC-1](https://linear.app/acme/issue/ABC-1)\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)\nCo-Authored-By: Claude <x@y.z>\nhttps://claude.ai/code/session_123";
        Assert.Equal("## Summary\nAdds the API.", MarkdownTidy.Tidy(text));
    }

    [Fact]
    public void A_link_inside_a_sentence_stays()
    {
        const string text = "See https://linear.app/acme/issue/ABC-1 for context and a long story.";
        Assert.Equal(text, MarkdownTidy.Tidy(text));
    }

    [Fact]
    public void Blank_runs_collapse_and_null_is_empty()
    {
        Assert.Equal("a\n\nb", MarkdownTidy.Tidy("a\n\n\n\nb"));
        Assert.Equal("", MarkdownTidy.Tidy(null));
    }

    [Fact]
    public void Preview_cuts_at_a_line_and_never_leaves_a_lone_heading()
    {
        var text = "Intro line that is reasonably long.\n\nSecond paragraph of text here.\n\n## Next steps\n- first thing to do after this";
        var (preview, truncated) = MarkdownTidy.Preview(text, 80);
        Assert.True(truncated);
        Assert.DoesNotContain("##", preview);
        Assert.DoesNotContain("Next steps", preview);
        Assert.StartsWith("Intro line", preview);
    }

    [Fact]
    public void Short_text_is_not_cut_and_one_long_line_is_cut_inside()
    {
        Assert.Equal(("short", false), MarkdownTidy.Preview("short", 100));
        var (p, t) = MarkdownTidy.Preview(new string('x', 500), 100);
        Assert.True(t);
        Assert.Equal(100, p.Length);
    }
}
