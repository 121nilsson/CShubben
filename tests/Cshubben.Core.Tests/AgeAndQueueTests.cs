using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests;

public class AgeAndQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "now")]
    [InlineData(5 * 60, "5 min")]
    [InlineData(3 * 3600, "3 h")]
    [InlineData(2 * 86400, "2 d")]
    [InlineData(90 * 86400, "3 mo")]
    public void Age_is_short_and_relative(int secondsAgo, string expected) =>
        Assert.Equal(expected, Age.Format(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void Missing_info_counts_as_waiting_on_you() =>
        Assert.Equal(ReviewQueueState.WaitingOnYou, ReviewQueue.StateOf(null));

    [Fact]
    public void New_commits_after_review_means_updated_since_review()
    {
        var info = new ReviewInfo("x", true, Now.AddHours(-5), Now.AddHours(-1), ReviewDecision.Approved);
        Assert.Equal(ReviewQueueState.UpdatedSinceReview, ReviewQueue.StateOf(info));
    }

    [Fact]
    public void Approved_and_unchanged_is_approved_by_you()
    {
        var info = new ReviewInfo("x", true, Now.AddHours(-1), Now.AddHours(-5), ReviewDecision.Approved);
        Assert.Equal(ReviewQueueState.ApprovedByYou, ReviewQueue.StateOf(info));
    }

    [Fact]
    public void Rank_orders_updated_before_waiting()
    {
        Assert.True(ReviewQueue.Rank(ReviewQueueState.UpdatedSinceReview) < ReviewQueue.Rank(ReviewQueueState.WaitingOnYou));
        Assert.True(ReviewQueue.Rank(ReviewQueueState.WaitingOnYou) < ReviewQueue.Rank(ReviewQueueState.ApprovedByYou));
    }
}
