using Cshubben.Core.Data;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests;

public class OverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly HubSnapshot Demo = new DemoData(Now).Build();

    [Fact]
    public void Ready_to_merge_contains_only_mergeable_prs() =>
        Assert.Equal(["a1", "a2"], Overview().ReadyToMerge.Select(a => a.Pr.Id).Order());

    [Fact]
    public void Needs_action_is_sorted_with_danger_first()
    {
        var kinds = Overview().NeedsAction.Select(a => a.Action.Tone).ToList();
        Assert.Equal(kinds.OrderBy(t => t == Tone.Danger ? 0 : t == Tone.Warning ? 1 : 2), kinds);
        Assert.Contains(Overview().NeedsAction, a => a.Pr.Id == "a5");
        Assert.Contains(Overview().NeedsAction, a => a.Pr.Id == "a4");
    }

    [Fact]
    public void Reviews_for_you_puts_updated_since_review_first_and_skips_drafts()
    {
        var ids = Overview().ReviewsForYou.Select(a => a.Pr.Id).ToList();
        Assert.DoesNotContain("r7", ids);
        Assert.DoesNotContain("r4", ids);
        Assert.Equal(NextActionKind.ReReview, Overview().ReviewsForYou[0].Action.Kind);
    }

    [Fact]
    public void Needs_you_is_the_sum_of_the_three_lists()
    {
        var o = Overview();
        Assert.Equal(o.ReadyToMerge.Count + o.NeedsAction.Count + o.ReviewsForYou.Count, o.NeedsYou);
    }

    private static OverviewModel Overview() => OverviewModel.Compute(Demo);
}
