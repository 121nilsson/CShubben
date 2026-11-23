using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Xunit;

using Cshubben.Core.Data;
namespace Cshubben.Core.Tests.GitHubTests;

public class ReviewInfoTests
{
    private static ReviewInfo Info(string json, string viewer = "alex") =>
        ReviewInfoMapper.FromNode(Setup.Parse(json), viewer)!;

    [Fact]
    public void Viewer_review_decision_and_time_come_from_the_viewers_own_review()
    {
        var info = Info(Fixtures.ReviewInfoNode);
        Assert.Equal("PR_r1", info.PrId);
        Assert.Equal(ReviewDecision.ChangesRequested, info.MyDecision);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), info.MyReviewAt);
    }

    [Fact]
    public void Newest_commit_time_is_used()
    {
        var info = Info(Fixtures.ReviewInfoNode);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), info.LastCommitAt);
    }

    [Fact]
    public void Commit_after_review_means_updated_since_review()
    {
        var state = ReviewQueue.StateOf(Info(Fixtures.ReviewInfoNode));
        Assert.Equal(ReviewQueueState.UpdatedSinceReview, state);
    }

    [Fact]
    public void Personal_request_is_direct_even_with_a_team_request_too()
    {
        Assert.True(Info(Fixtures.ReviewInfoNode).RequestedDirectly);
    }

    [Fact]
    public void Team_only_request_is_not_direct()
    {
        var json = """
            { "id": "P", "reviewRequests": { "nodes": [ { "requestedReviewer": { "__typename": "Team", "name": "platform" } } ] },
              "timelineItems": { "nodes": [] }, "latestReviews": { "nodes": [] }, "commits": { "nodes": [] } }
            """;
        var info = Info(json);
        Assert.False(info.RequestedDirectly);
        Assert.Null(info.MyReviewAt);
        Assert.Equal(ReviewDecision.None, info.MyDecision);
        Assert.Equal(ReviewQueueState.WaitingOnYou, ReviewQueue.StateOf(info));
    }

    [Fact]
    public void Direct_request_found_in_timeline_after_it_was_consumed_by_a_review()
    {
        var json = """
            { "id": "P", "reviewRequests": { "nodes": [] },
              "timelineItems": { "nodes": [ { "createdAt": "2026-11-01T00:00:00Z", "requestedReviewer": { "login": "ALEX" } } ] },
              "latestReviews": { "nodes": [] }, "commits": { "nodes": [] } }
            """;
        Assert.True(Info(json).RequestedDirectly);
    }

    [Fact]
    public void Other_peoples_reviews_are_ignored()
    {
        var json = """
            { "id": "P", "latestReviews": { "nodes": [ { "author": { "login": "maria" }, "state": "APPROVED", "submittedAt": "2026-11-19T10:00:00Z" } ] } }
            """;
        var info = Info(json);
        Assert.Null(info.MyReviewAt);
        Assert.Equal(ReviewDecision.None, info.MyDecision);
    }

    [Fact]
    public void Dismissed_review_does_not_count()
    {
        var json = """
            { "id": "P", "latestReviews": { "nodes": [ { "author": { "login": "alex" }, "state": "DISMISSED", "submittedAt": "2026-11-19T10:00:00Z" } ] } }
            """;
        Assert.Null(Info(json).MyReviewAt);
    }

    [Fact]
    public void Newest_of_several_viewer_reviews_wins()
    {
        var json = """
            { "id": "P", "latestReviews": { "nodes": [
                { "author": { "login": "alex" }, "state": "CHANGES_REQUESTED", "submittedAt": "2026-11-10T10:00:00Z" },
                { "author": { "login": "alex" }, "state": "APPROVED", "submittedAt": "2026-11-12T10:00:00Z" } ] } }
            """;
        var info = Info(json);
        Assert.Equal(ReviewDecision.Approved, info.MyDecision);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero), info.MyReviewAt);
    }

    [Fact]
    public void Comment_only_review_gives_a_time_but_no_decision()
    {
        var json = """
            { "id": "P", "latestReviews": { "nodes": [ { "author": { "login": "alex" }, "state": "COMMENTED", "submittedAt": "2026-11-12T10:00:00Z" } ] } }
            """;
        var info = Info(json);
        Assert.NotNull(info.MyReviewAt);
        Assert.Equal(ReviewDecision.None, info.MyDecision);
    }

    [Fact]
    public void Missing_commit_time_never_counts_as_updated_since_review()
    {
        var json = """
            { "id": "P", "latestReviews": { "nodes": [ { "author": { "login": "alex" }, "state": "APPROVED", "submittedAt": "2026-11-12T10:00:00Z" } ] } }
            """;
        var info = Info(json);
        Assert.Equal(DateTimeOffset.MinValue, info.LastCommitAt);
        Assert.Equal(ReviewQueueState.ApprovedByYou, ReviewQueue.StateOf(info));
    }

    [Fact]
    public void Node_without_id_gives_null()
    {
        Assert.Null(ReviewInfoMapper.FromNode(Setup.Parse("{}"), "alex"));
    }

    [Fact]
    public void FromNodes_keys_by_pr_id()
    {
        var data = Setup.Parse("{\"nodes\":[null,{\"id\":\"A\"},{\"id\":\"B\"}]}");
        var map = ReviewInfoMapper.FromNodes(data, "alex");
        Assert.Equal(2, map.Count);
        Assert.Equal("B", map["B"].PrId);
    }

    [Fact]
    public void Team_only_request_records_the_team_name()
    {
        var json = """
            { "id": "P", "reviewRequests": { "nodes": [ { "requestedReviewer": { "__typename": "Team", "name": "Backend" } },
                                                       { "requestedReviewer": { "__typename": "Team", "name": "backend" } } ] },
              "timelineItems": { "nodes": [] }, "latestReviews": { "nodes": [] }, "commits": { "nodes": [] } }
            """;
        var info = Info(json);
        Assert.Equal(new[] { "Backend" }, info.RequestedTeams);
        Assert.Equal("Team: Backend", ReviewQueue.RequestLabel(info));
    }

    [Fact]
    public void Personal_request_is_labelled_You_even_when_a_team_was_asked_too()
    {
        Assert.Equal("You", ReviewQueue.RequestLabel(Info(Fixtures.ReviewInfoNode)));
    }

    [Fact]
    public void Request_label_is_null_without_info_and_plain_Team_when_no_name_is_known()
    {
        Assert.Null(ReviewQueue.RequestLabel(null));
        Assert.Equal("Team", ReviewQueue.RequestLabel(new ReviewInfo("x", false, null, DateTimeOffset.MinValue, ReviewDecision.None)));
    }

    private static readonly DateTimeOffset T = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sections_split_personal_requests_team_requests_and_what_is_done()
    {
        var teams = new[] { "Backend" };
        Assert.Equal(ReviewSection.AskedOfYou, ReviewQueue.SectionOf(new ReviewInfo("a", true, null, T, ReviewDecision.None, teams)));
        Assert.Equal(ReviewSection.AskedOfTeams, ReviewQueue.SectionOf(new ReviewInfo("b", false, null, T, ReviewDecision.None, teams)));
        // Updated since the viewer's review still needs them, so it stays in its request section.
        Assert.Equal(ReviewSection.AskedOfTeams, ReviewQueue.SectionOf(new ReviewInfo("c", false, T.AddHours(-2), T, ReviewDecision.Approved, teams)));
        Assert.Equal(ReviewSection.AskedOfYou, ReviewQueue.SectionOf(new ReviewInfo("d", true, T.AddHours(-2), T, ReviewDecision.ChangesRequested, teams)));
    }

    [Fact]
    public void Reviewed_and_settled_goes_to_waiting_on_others_whoever_was_asked()
    {
        Assert.Equal(ReviewSection.WaitingOnOthers, ReviewQueue.SectionOf(new ReviewInfo("a", true, T, T.AddHours(-1), ReviewDecision.Approved)));
        Assert.Equal(ReviewSection.WaitingOnOthers, ReviewQueue.SectionOf(new ReviewInfo("b", false, T, T.AddHours(-1), ReviewDecision.ChangesRequested)));
    }

    [Fact]
    public void Missing_info_is_asked_of_you()
    {
        Assert.Equal(ReviewSection.AskedOfYou, ReviewQueue.SectionOf(null));
    }
}
