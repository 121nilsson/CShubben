using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.Features.Notifications;

public class ImportantAndApprovedTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(1));

    private static (HubSnapshot Before, HubSnapshot After) Opened(string repo, string actor = "bob", bool bot = false)
    {
        var before = N.Snap();
        var after = N.Snap(feed: new[] { N.Feed("f1", FeedKind.Opened, repo, actor, 40, bot: bot) });
        return (before, after);
    }

    [Fact]
    public void Approval_that_is_not_yet_ready_is_reported_on_its_own()
    {
        var before = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.ReviewRequired, ci: CiState.Pending) });
        var after = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.Approved, ci: CiState.Pending, updatedMinutes: 4) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.Approved, e.Kind);
    }

    [Fact]
    public void Approval_that_makes_it_ready_gives_only_ready_to_merge()
    {
        var before = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.ReviewRequired, ci: CiState.Success) });
        var after = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.Approved, ci: CiState.Success, updatedMinutes: 4) });
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.ReadyToMerge, e.Kind);
    }

    [Fact]
    public void New_pull_requests_by_others_are_found_but_not_my_own_or_bots()
    {
        var (before, after) = Opened("acme/api");
        var e = Assert.Single(NotificationDiff.Compute(before, after));
        Assert.Equal(NotificationKind.NewInImportantRepo, e.Kind);
        Assert.Equal("bob", e.Detail);
        Assert.Equal("t", e.Title);

        var (b2, a2) = Opened("acme/api", actor: "me");
        Assert.DoesNotContain(NotificationDiff.Compute(b2, a2), x => x.Kind == NotificationKind.NewInImportantRepo);

        var (b3, a3) = Opened("acme/api", actor: "dependabot", bot: true);
        Assert.DoesNotContain(NotificationDiff.Compute(b3, a3), x => x.Kind == NotificationKind.NewInImportantRepo);
    }

    [Fact]
    public void Engine_notifies_new_pull_requests_only_in_important_repos()
    {
        var (before, after) = Opened("acme/api");

        var none = new NotificationEngine(new InMemoryNotificationState())
            .Plan(before, after, new NotificationPrefs(), Noon);
        Assert.Empty(none.Messages);

        var important = new NotificationEngine(new InMemoryNotificationState())
            .Plan(before, after, new NotificationPrefs { ImportantRepos = new[] { "ACME/api" } }, Noon);
        var toast = Assert.Single(important.Messages);
        Assert.Equal("New pull request: acme/api #40", toast.Title);
        Assert.Equal("bob - t", toast.Body);
    }

    [Fact]
    public void Muting_an_important_repo_wins_and_the_toggle_can_turn_it_off()
    {
        var (before, after) = Opened("acme/api");
        var muted = new NotificationEngine(new InMemoryNotificationState()).Plan(before, after,
            new NotificationPrefs { ImportantRepos = new[] { "acme/api" }, MutedRepos = new[] { "acme/api" } }, Noon);
        Assert.Empty(muted.Messages);

        var off = new NotificationEngine(new InMemoryNotificationState()).Plan(before, after,
            new NotificationPrefs { ImportantRepos = new[] { "acme/api" }, NewInImportantRepo = false }, Noon);
        Assert.Empty(off.Messages);
    }

    [Fact]
    public void Same_new_pull_request_is_not_announced_twice()
    {
        var (before, after) = Opened("acme/api");
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var prefs = new NotificationPrefs { ImportantRepos = new[] { "acme/api" } };
        Assert.Single(engine.Plan(before, after, prefs, Noon).Messages);
        Assert.Empty(engine.Plan(before, after, prefs, Noon).Messages);
    }

    [Fact]
    public void Approved_toast_text()
    {
        var before = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.ReviewRequired) });
        var after = N.Snap(authored: new[] { N.Pr(review: ReviewDecision.Approved, updatedMinutes: 2) });
        var toast = Assert.Single(new NotificationEngine(new InMemoryNotificationState()).Plan(before, after, new NotificationPrefs(), Noon).Messages);
        Assert.Equal("Approved: acme/api #1", toast.Title);
    }
}
