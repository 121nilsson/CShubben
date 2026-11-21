using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class SuggestionApplyTests
{
    private static string Body(string replacement) => "Please change this.\n\n```suggestion\n" + replacement + "\n```";

    [Fact]
    public void Replaces_a_single_line_and_keeps_the_rest()
    {
        var plan = SuggestionApplier.Plan("a.cs", "one\ntwo\nthree\n", 2, 2, Body("TWO"));
        Assert.True(plan.Ok);
        Assert.Equal("one\nTWO\nthree\n", plan.NewContent);
        Assert.Equal(new[] { "two" }, plan.Removed);
        Assert.Equal(new[] { "TWO" }, plan.Added);
    }

    [Fact]
    public void Replaces_a_range_with_more_or_fewer_lines()
    {
        var more = SuggestionApplier.Plan("a.cs", "a\nb\nc\nd\n", 2, 3, Body("x\ny\nz"));
        Assert.Equal("a\nx\ny\nz\nd\n", more.NewContent);
        var fewer = SuggestionApplier.Plan("a.cs", "a\nb\nc\nd\n", 2, 3, Body("only"));
        Assert.Equal("a\nonly\nd\n", fewer.NewContent);
        Assert.Equal(new[] { "b", "c" }, fewer.Removed);
    }

    [Fact]
    public void An_empty_suggestion_deletes_the_lines()
    {
        var plan = SuggestionApplier.Plan("a.cs", "a\nb\nc\n", 2, 2, "```suggestion\n```");
        Assert.True(plan.Ok);
        Assert.Equal("a\nc\n", plan.NewContent);
        Assert.Empty(plan.Added);
    }

    [Fact]
    public void Keeps_crlf_and_a_missing_final_newline()
    {
        var crlf = SuggestionApplier.Plan("a.cs", "a\r\nb\r\nc\r\n", 2, 2, Body("B1\nB2"));
        Assert.Equal("a\r\nB1\r\nB2\r\nc\r\n", crlf.NewContent);
        var noFinal = SuggestionApplier.Plan("a.cs", "a\nb\nc", 3, 3, Body("C"));
        Assert.Equal("a\nb\nC", noFinal.NewContent);
    }

    [Fact]
    public void Works_on_the_first_and_last_line()
    {
        Assert.Equal("A\nb\nc\n", SuggestionApplier.Plan("a", "a\nb\nc\n", 1, 1, Body("A")).NewContent);
        Assert.Equal("a\nb\nC\n", SuggestionApplier.Plan("a", "a\nb\nc\n", 3, 3, Body("C")).NewContent);
    }

    [Fact]
    public void Fails_cleanly_when_the_range_does_not_fit_or_there_is_no_suggestion()
    {
        Assert.False(SuggestionApplier.Plan("a", "a\nb\n", 3, 3, Body("x")).Ok);
        Assert.False(SuggestionApplier.Plan("a", "a\nb\n", 0, 1, Body("x")).Ok);
        var none = SuggestionApplier.Plan("a", "a\nb\n", 1, 1, "just words");
        Assert.False(none.Ok);
        Assert.NotEmpty(none.Error);
    }

    [Fact]
    public void Detects_a_suggestion_that_changes_nothing()
    {
        Assert.True(SuggestionApplier.Plan("a", "a\nb\n", 2, 2, Body("b")).IsNoChange);
        Assert.False(SuggestionApplier.Plan("a", "a\nb\n", 2, 2, Body("c")).IsNoChange);
    }

    [Fact]
    public void Commit_message_names_the_file_and_lines()
    {
        Assert.Equal("Apply suggested change to X.cs (line 4)", SuggestionApplier.CommitMessage("src/X.cs", 4, 4));
        Assert.Equal("Apply suggested change to X.cs (lines 4-6)", SuggestionApplier.CommitMessage("src/X.cs", 4, 6));
    }

    [Theory]
    [InlineData("me", "me", null, true)]
    [InlineData("me", "ME", "READ", true)]
    [InlineData("me", "other", "WRITE", true)]
    [InlineData("me", "other", "MAINTAIN", true)]
    [InlineData("me", "other", "ADMIN", true)]
    [InlineData("me", "other", "READ", false)]
    [InlineData("me", "other", null, false)]
    [InlineData("", "", "READ", false)]
    public void Only_the_author_or_someone_with_write_access_can_apply(string viewer, string author, string? permission, bool expected) =>
        Assert.Equal(expected, SuggestionApplier.CanApply(viewer, author, permission));

    private static ReviewThread Thread(int? line, int? start, DiffSide side = DiffSide.Right, bool outdated = false, bool resolved = false) =>
        new("t", "src/a.cs", line, side, start, resolved, outdated, new[] { new ReviewComment("c", "x", Body("y"), DateTimeOffset.UnixEpoch) });

    [Fact]
    public void Request_uses_the_thread_range_and_rejects_threads_that_cannot_be_applied()
    {
        var thread = Thread(12, 10);
        var request = SuggestionApplier.RequestFor(thread, thread.Comments[0], "abcdef1");
        Assert.NotNull(request);
        Assert.Equal(10, request!.StartLine);
        Assert.Equal(12, request.EndLine);
        Assert.Equal("src/a.cs", request.Path);

        Assert.Equal(7, SuggestionApplier.RequestFor(Thread(7, null), Thread(7, null).Comments[0], "abcdef1")!.StartLine);
        Assert.Null(SuggestionApplier.RequestFor(Thread(12, 10, DiffSide.Left), thread.Comments[0], "abcdef1"));
        Assert.Null(SuggestionApplier.RequestFor(Thread(12, 10, outdated: true), thread.Comments[0], "abcdef1"));
        Assert.Null(SuggestionApplier.RequestFor(Thread(12, 10, resolved: true), thread.Comments[0], "abcdef1"));
        Assert.Null(SuggestionApplier.RequestFor(Thread(null, null), thread.Comments[0], "abcdef1"));
        var plain = new ReviewComment("c2", "x", "no block here", DateTimeOffset.UnixEpoch);
        Assert.Null(SuggestionApplier.RequestFor(thread, plain, "abcdef1"));
    }
}
