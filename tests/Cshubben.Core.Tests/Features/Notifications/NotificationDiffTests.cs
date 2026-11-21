using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.Features.Notifications;

public class NotificationDiffTests
{
    [Fact]
    public void First_snapshot_is_only_a_baseline()
    {
        var current = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure) });
        Assert.Empty(NotificationDiff.Compute(null, current));
    }

    [Fact]
    public void Identical_snapshots_give_nothing()
    {
        var s = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure) }, reviews: new[] { N.Review() });
        Assert.Empty(NotificationDiff.Compute(s, s));
    }

    [Fact]
    public void Ci_turning_red_on_my_pr_is_reported()
    {
        var before = N.Snap(authored: new[] { N.Pr(ci: CiState.Pending) });
        var after = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure, updatedMinutes: 5) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.CiFailed, e.Kind);
        Assert.Equal("acme/api", e.Repo);
        Assert.Equal(1, e.Number);
    }

    [Fact]
    public void Ci_that_was_already_red_is_not_reported_again()
    {
        var before = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure) });
        var after = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure, updatedMinutes: 5) });
        Assert.Empty(NotificationDiff.Compute(before, after));
    }

    [Fact]
    public void Changes_requested_is_reported_once()
    {
        var before = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.ReviewRequired) });
        var after = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.ChangesRequested, updatedMinutes: 1) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.ChangesRequested, e.Kind);
    }

    [Fact]
    public void Approved_green_mergeable_pr_becomes_ready_to_merge()
    {
        var before = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.Approved, ci: CiState.Pending) });
        var after = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.Approved, ci: CiState.Success, updatedMinutes: 3) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.ReadyToMerge, e.Kind);
    }

    [Fact]
    public void Draft_or_conflicting_pr_is_not_ready_to_merge()
    {
        var before = N.Snap(authored: new[] { N.Pr("a", number: 1), N.Pr("b", number: 2) });
        var after = N.Snap(authored: new[]
        {
            N.Pr("a", number: 1, review: ReviewDecision.Approved, ci: CiState.Success, draft: true),
            N.Pr("b", number: 2, review: ReviewDecision.Approved, ci: CiState.Success, merge: MergeState.Conflicting),
        });
        var events = NotificationDiff.Compute(before, after);
        Assert.DoesNotContain(events, e => e.Kind == NotificationKind.ReadyToMerge);
        Assert.All(events, e => Assert.Equal(NotificationKind.Approved, e.Kind));
    }

    [Fact]
    public void New_review_request_is_reported_with_the_author()
    {
        var before = N.Snap();
        var after = N.Snap(reviews: new[] { N.Review() });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.ReviewRequested, e.Kind);
        Assert.Equal("carol", e.Detail);
    }

    [Fact]
    public void Review_request_on_a_draft_is_ignored()
    {
        Assert.Empty(NotificationDiff.Compute(N.Snap(), N.Snap(reviews: new[] { N.Review(draft: true) })));
    }

    [Fact]
    public void Back_in_the_queue_after_my_review_is_a_re_review()
    {
        var info = N.Info("r1", N.T0.AddHours(-3), N.T0.AddHours(-1));
        var after = N.Snap(reviews: new[] { N.Review() }, infos: new Dictionary<string, ReviewInfo> { ["r1"] = info });
        var e = Assert.Single(NotificationDiff.Compute(N.Snap(), after));
        Assert.Equal(NotificationKind.ReReviewRequested, e.Kind);
    }

    [Fact]
    public void New_commits_after_my_review_are_a_re_review()
    {
        var reviewed = N.T0.AddHours(-3);
        var before = N.Snap(reviews: new[] { N.Review() },
            infos: new Dictionary<string, ReviewInfo> { ["r1"] = N.Info("r1", reviewed, reviewed.AddMinutes(-10)) });
        var after = N.Snap(reviews: new[] { N.Review() },
            infos: new Dictionary<string, ReviewInfo> { ["r1"] = N.Info("r1", reviewed, N.T0) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.ReReviewRequested, e.Kind);
    }

    [Fact]
    public void Already_updated_since_review_does_not_repeat()
    {
        var reviewed = N.T0.AddHours(-3);
        var infos = new Dictionary<string, ReviewInfo> { ["r1"] = N.Info("r1", reviewed, N.T0) };
        var s = N.Snap(reviews: new[] { N.Review() }, infos: infos);
        Assert.Empty(NotificationDiff.Compute(s, N.Snap(reviews: new[] { N.Review() }, infos: infos)));
    }

    [Fact]
    public void New_comment_on_my_pr_is_reported_with_the_count()
    {
        var before = N.Snap(authored: new[] { N.Pr(comments: 1) });
        var after = N.Snap(authored: new[] { N.Pr(comments: 4, updatedMinutes: 2) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.Comment, e.Kind);
        Assert.Equal("3 new comments", e.Detail);
    }

    [Fact]
    public void Mention_in_a_new_feed_comment_is_reported()
    {
        var pr = N.Pr();
        var before = N.Snap(authored: new[] { pr });
        var after = N.Snap(authored: new[] { pr },
            feed: new[] { N.Feed("f1", FeedKind.Commented, detail: "cc @me can you look?") });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.Mention, e.Kind);
        Assert.Equal("bob", e.Detail);
        Assert.Equal("https://github.com/acme/api/pull/1", e.Url);
    }

    [Fact]
    public void Mention_replaces_the_plain_comment_event_for_the_same_pr()
    {
        var before = N.Snap(authored: new[] { N.Pr(comments: 0) });
        var after = N.Snap(authored: new[] { N.Pr(comments: 1, updatedMinutes: 1) },
            feed: new[] { N.Feed("f1", FeedKind.Commented, detail: "@me ping") });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.Mention, e.Kind);
    }

    [Theory]
    [InlineData("hello @me!", true)]
    [InlineData("@ME hi", true)]
    [InlineData("email@me.com", false)]
    [InlineData("@meow is a cat", false)]
    [InlineData("@me-too", false)]
    [InlineData("nothing here", false)]
    [InlineData("", false)]
    public void Mention_detection_needs_a_whole_word(string text, bool expected)
    {
        Assert.Equal(expected, NotificationDiff.MentionsUser(text, "me"));
    }

    [Fact]
    public void Own_and_bot_comments_never_count_as_mentions()
    {
        var before = N.Snap(authored: new[] { N.Pr() });
        var after = N.Snap(authored: new[] { N.Pr() }, feed: new[]
        {
            N.Feed("f1", FeedKind.Commented, actor: "me", detail: "@me note to self"),
            N.Feed("f2", FeedKind.Commented, actor: "dependabot", detail: "@me bump", bot: true),
        });
        Assert.Empty(NotificationDiff.Compute(before, after));
    }

    [Fact]
    public void Old_feed_items_are_not_mentions_again()
    {
        var item = N.Feed("f1", FeedKind.Commented, detail: "@me ping");
        var s = N.Snap(authored: new[] { N.Pr() }, feed: new[] { item });
        Assert.Empty(NotificationDiff.Compute(s, N.Snap(authored: new[] { N.Pr() }, feed: new[] { item })));
    }

    [Fact]
    public void Pr_that_left_the_list_with_a_merged_feed_item_is_reported_as_merged()
    {
        var before = N.Snap(authored: new[] { N.Pr() });
        var after = N.Snap(feed: new[] { N.Feed("m1", FeedKind.Merged) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.Merged, e.Kind);
    }

    [Fact]
    public void Pr_that_vanished_without_a_merge_is_silent()
    {
        Assert.Empty(NotificationDiff.Compute(N.Snap(authored: new[] { N.Pr() }), N.Snap()));
    }

    [Fact]
    public void Events_are_ordered_by_priority()
    {
        var before = N.Snap(authored: new[] { N.Pr("a", number: 1, comments: 0), N.Pr("b", number: 2) });
        var after = N.Snap(
            authored: new[] { N.Pr("a", number: 1, comments: 2, updatedMinutes: 1), N.Pr("b", number: 2, ci: CiState.Failure, updatedMinutes: 1) },
            reviews: new[] { N.Review() });
        var kinds = NotificationDiff.Compute(before, after).Select(e => e.Kind).ToList();
        Assert.Equal(new[] { NotificationKind.CiFailed, NotificationKind.ReviewRequested, NotificationKind.Comment }, kinds);
    }

    [Fact]
    public void A_pr_that_is_new_to_my_list_does_not_notify_about_its_state()
    {
        var after = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure, review: ReviewDecision.ChangesRequested) });
        Assert.Empty(NotificationDiff.Compute(N.Snap(), after));
    }
}
