using Cshubben.Core.Features.Pipeline;
using Xunit;
using static Cshubben.Core.Tests.Features.Pipeline.PipelineTestData;

namespace Cshubben.Core.Tests.Features.Pipeline;

public class MergeAndMiscTests
{
    private static MergeFacts Facts(
        bool draft = false,
        bool conflicts = false,
        PipelineState checks = PipelineState.Passing,
        int passed = 5,
        int total = 5,
        string[]? approvers = null,
        string[]? changes = null,
        int threads = 0,
        bool reviewRequired = false,
        string sha = "a1b2c3d4e5f60718") =>
        new(draft, conflicts, checks, passed, total, approvers ?? new[] { "priya" }, changes ?? Array.Empty<string>(), threads, reviewRequired, sha);

    // ---- readiness ----------------------------------------------------------------------------------

    [Fact]
    public void Clean_pull_request_has_no_blockers()
    {
        var r = MergeReadiness.Evaluate(Facts());
        Assert.Empty(r.Blockers);
        Assert.True(r.CanMerge);
        Assert.False(r.NeedsOverride);
        Assert.Equal("a1b2c3d", r.ShortSha);
        Assert.Equal("All 5 checks passed", r.ChecksText);
        Assert.Equal("Approved by priya", r.ApprovalsText);
        Assert.True(r.ChecksGreen);
        Assert.True(r.Approved);
    }

    [Theory]
    [InlineData(true, false, PipelineState.Passing, 0)]
    [InlineData(false, true, PipelineState.Passing, 0)]
    [InlineData(false, false, PipelineState.Failing, 0)]
    public void Hard_blockers_stop_the_merge(bool draft, bool conflicts, PipelineState checks, int threads)
    {
        var r = MergeReadiness.Evaluate(Facts(draft, conflicts, checks, threads: threads));
        Assert.False(r.CanMerge);
        Assert.All(r.Blockers, b => Assert.True(b.IsHard));
    }

    [Fact]
    public void Changes_requested_is_a_hard_blocker()
    {
        var r = MergeReadiness.Evaluate(Facts(changes: new[] { "sofia" }, approvers: Array.Empty<string>()));
        Assert.False(r.CanMerge);
        Assert.Contains(r.Blockers, b => b.Kind == BlockerKind.ChangesRequested && b.Text.Contains("sofia"));
    }

    [Fact]
    public void Open_threads_and_running_checks_are_soft_blockers()
    {
        var r = MergeReadiness.Evaluate(Facts(checks: PipelineState.Running, passed: 3, threads: 2));
        Assert.True(r.CanMerge);
        Assert.True(r.NeedsOverride);
        Assert.Equal(2, r.Blockers.Count);
        Assert.Equal("Checks still running (3 of 5 passed)", r.ChecksText);
        Assert.Equal("2 open threads", r.ThreadsText);
    }

    [Fact]
    public void Missing_review_is_a_soft_blocker()
    {
        var r = MergeReadiness.Evaluate(Facts(approvers: Array.Empty<string>(), reviewRequired: true));
        Assert.True(r.NeedsOverride);
        Assert.Equal("No approvals", r.ApprovalsText);
    }

    [Fact]
    public void No_checks_is_not_a_blocker()
    {
        var r = MergeReadiness.Evaluate(Facts(checks: PipelineState.None, passed: 0, total: 0));
        Assert.Empty(r.Blockers);
        Assert.Equal("No checks", r.ChecksText);
    }

    // ---- merge flow -----------------------------------------------------------------------------------

    [Fact]
    public void First_click_opens_and_second_click_submits()
    {
        var s = MergeFlowState.Initial;
        Assert.False(s.CanConfirm);
        s = s.Open();
        Assert.Equal(MergeStep.Confirming, s.Step);
        Assert.True(s.CanConfirm);
        s = s.BeginSubmit();
        Assert.Equal(MergeStep.Submitting, s.Step);
    }

    [Fact]
    public void Cannot_submit_twice()
    {
        var submitting = MergeFlowState.Initial.Open().BeginSubmit();
        Assert.False(submitting.CanConfirm);
        Assert.Same(submitting, submitting.BeginSubmit());
    }

    [Fact]
    public void Cannot_submit_before_it_is_opened()
    {
        Assert.Same(MergeFlowState.Initial, MergeFlowState.Initial.BeginSubmit());
    }

    [Fact]
    public void Success_and_failure_only_apply_while_submitting()
    {
        var open = MergeFlowState.Initial.Open();
        Assert.Same(open, open.Succeed("x"));
        Assert.Same(open, open.Fail("x"));
        var done = open.BeginSubmit().Succeed("Merged");
        Assert.Equal(MergeStep.Done, done.Step);
        Assert.Equal("Merged", done.Message);
        Assert.False(done.CanConfirm);
    }

    [Fact]
    public void After_a_failure_the_user_can_try_again()
    {
        var failed = MergeFlowState.Initial.Open().BeginSubmit().Fail("conflict");
        Assert.Equal(MergeStep.Failed, failed.Step);
        Assert.True(failed.CanConfirm);
        Assert.Equal(MergeStep.Submitting, failed.BeginSubmit().Step);
    }

    [Fact]
    public void Method_can_be_chosen_only_while_confirming_and_cancel_closes()
    {
        var s = MergeFlowState.Initial.Open().Choose(MergeMethodChoice.Rebase);
        Assert.Equal(MergeMethodChoice.Rebase, s.Method);
        Assert.Equal(MergeStep.Closed, s.Cancel().Step);
        var submitting = s.BeginSubmit();
        Assert.Equal(MergeMethodChoice.Rebase, submitting.Choose(MergeMethodChoice.Merge).Method);
        Assert.Same(submitting, submitting.Cancel());
    }

    [Fact]
    public void Squash_is_the_default_method_and_labels_exist_for_all()
    {
        Assert.Equal(MergeMethodChoice.Squash, MergeFlowState.Initial.Method);
        foreach (var m in MergeMethods.All)
        {
            Assert.NotEmpty(MergeMethods.Label(m));
            Assert.NotEmpty(MergeMethods.Describe(m));
        }
    }

    // ---- formatting and excerpts ----------------------------------------------------------------------

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(45, "45s")]
    [InlineData(60, "1m")]
    [InlineData(125, "2m 5s")]
    [InlineData(3600, "1h")]
    [InlineData(4320, "1h 12m")]
    [InlineData(-5, "0s")]
    public void Duration_format(int seconds, string expected) =>
        Assert.Equal(expected, PipelineFormat.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Ago_and_short_sha_and_counts()
    {
        Assert.Equal("3 h ago", PipelineFormat.Ago(Ago(3), Now));
        Assert.Equal("just now", PipelineFormat.Ago(Now, Now));
        Assert.Equal("a1b2c3d", PipelineFormat.ShortSha("a1b2c3d4e5"));
        Assert.Equal("abc", PipelineFormat.ShortSha("abc"));
        Assert.Equal("1 commit", PipelineFormat.Count(1, "commit"));
        Assert.Equal("2 commits", PipelineFormat.Count(2, "commit"));
    }

    [Fact]
    public void Excerpt_strips_escape_codes_and_limits_lines()
    {
        var text = "\u001b[31mError\u001b[0m: boom\r\n" + string.Join("\n", Enumerable.Range(1, 30).Select(i => "line " + i));
        var clean = LogExcerpt.Clean(text, maxLines: 3)!;
        Assert.StartsWith("Error: boom\nline 1\nline 2", clean);
        Assert.EndsWith("…", clean);
        Assert.DoesNotContain("\u001b", clean);
    }

    [Fact]
    public void Excerpt_of_blank_text_is_null()
    {
        Assert.Null(LogExcerpt.Clean("  \n\n "));
        Assert.Null(LogExcerpt.Clean(null));
    }

    // ---- repository selection -------------------------------------------------------------------------

    [Fact]
    public void Repos_are_sorted_distinct_and_limited()
    {
        var repos = RepoSelection.Pick(new[] { "b/z", "a/x", "A/X", "c/y" }, Array.Empty<string>(), Array.Empty<string>(), max: 2);
        Assert.Equal(new[] { "a/x", "b/z" }, repos.ToArray());
    }

    [Fact]
    public void Followed_owner_narrows_the_list_and_followed_repos_are_added()
    {
        var repos = RepoSelection.Pick(new[] { "acme/api", "other/tool" }, new[] { "acme", "zed/extra" }, Array.Empty<string>());
        Assert.Equal(new[] { "acme/api", "zed/extra" }, repos.ToArray());
    }

    [Fact]
    public void Muted_repos_and_owners_are_removed()
    {
        var repos = RepoSelection.Pick(new[] { "acme/api", "acme/web", "noisy/x" }, Array.Empty<string>(), new[] { "acme/web", "noisy" });
        Assert.Equal(new[] { "acme/api" }, repos.ToArray());
    }

    [Fact]
    public void Invalid_repo_names_are_ignored()
    {
        var repos = RepoSelection.Pick(new[] { "", "noslash", "a/b/c", "/x", "x/" }, Array.Empty<string>(), Array.Empty<string>());
        Assert.Empty(repos);
    }
}
