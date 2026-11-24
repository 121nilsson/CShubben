using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests;

public class PrStatusChipsTests
{
    private static PullRequest Pr(
        PrRole role = PrRole.Authored, bool draft = false, ReviewDecision review = ReviewDecision.Approved,
        CiState ci = CiState.Success, int threads = 1, MergeState merge = MergeState.Mergeable) =>
        new("id", role, "o/r", 1, "t", "https://github.com/o/r/pull/1", "me", draft,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, 1, 1, review, ci, merge, 0, threads, "b");

    [Fact]
    public void An_approved_green_pr_with_an_open_thread_shows_only_what_the_action_pill_leaves_out()
    {
        var pr = Pr();
        var action = NextAction.For(pr);

        Assert.Equal(NextActionKind.ResolveThreads, action.Kind);
        Assert.Equal(new[] { "Approved", "CI passing" }, PrStatusChips.For(pr, action).Select(c => c.Text).ToArray());
    }

    [Fact]
    public void Open_threads_get_a_chip_when_another_action_comes_first()
    {
        var pr = Pr(ci: CiState.Failure, threads: 4);
        var action = NextAction.For(pr);

        Assert.NotEqual(NextActionKind.ResolveThreads, action.Kind);
        var chip = Assert.Single(PrStatusChips.For(pr, action), c => c.Text == "4 open threads");
        Assert.Equal(Tone.Warning, chip.Tone);
        Assert.Contains(PrStatusChips.For(Pr(ci: CiState.Failure), NextAction.For(Pr(ci: CiState.Failure))), c => c.Text == "1 open thread");
    }

    [Fact]
    public void Nothing_for_ready_to_merge_drafts_or_prs_you_review()
    {
        var ready = Pr(threads: 0);
        Assert.Equal(NextActionKind.Merge, NextAction.For(ready).Kind);
        Assert.Empty(PrStatusChips.For(ready, NextAction.For(ready)));

        var draft = Pr(draft: true);
        Assert.Empty(PrStatusChips.For(draft, NextAction.For(draft)));

        var review = Pr(role: PrRole.ReviewRequested);
        Assert.Empty(PrStatusChips.For(review, NextAction.For(review)));
    }

    [Fact]
    public void Failing_ci_and_missing_approval_show_no_green_chips()
    {
        var pr = Pr(review: ReviewDecision.ReviewRequired, ci: CiState.Failure, threads: 0);
        Assert.Empty(PrStatusChips.For(pr, NextAction.For(pr)));
    }
}
