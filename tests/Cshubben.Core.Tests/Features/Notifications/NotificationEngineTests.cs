using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.Features.Notifications;

public class NotificationEngineTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Night = new(2026, 11, 20, 23, 30, 0, TimeSpan.FromHours(1));

    private static (HubSnapshot Before, HubSnapshot After) CiFailure(string repo = "acme/api")
    {
        var before = N.Snap(authored: new[] { N.Pr(repo: repo, ci: CiState.Pending) });
        var after = N.Snap(authored: new[] { N.Pr(repo: repo, ci: CiState.Failure, updatedMinutes: 5) });
        return (before, after);
    }

    private static HubSnapshot WithReviewRequests(int count) =>
        N.Snap(reviews: Enumerable.Range(1, count).Select(i => N.Review("r" + i, i)));

    [Fact]
    public void A_single_event_becomes_one_toast_that_opens_the_pr()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var (before, after) = CiFailure();
        var plan = engine.Plan(before, after, new NotificationPrefs(), Noon);
        var toast = Assert.Single(plan.Messages);
        Assert.Equal("CI failed: acme/api #1", toast.Title);
        Assert.Equal("Add things", toast.Body);
        Assert.Equal("https://github.com/acme/api/pull/1", toast.Url);
        Assert.False(toast.IsDigest);
    }

    [Fact]
    public void Baseline_snapshot_shows_nothing()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var plan = engine.Plan(null, CiFailure().After, new NotificationPrefs(), Noon);
        Assert.Empty(plan.Messages);
    }

    [Fact]
    public void Master_switch_off_shows_nothing_and_counts_the_drops()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var (before, after) = CiFailure();
        var plan = engine.Plan(before, after, new NotificationPrefs { Enabled = false }, Noon);
        Assert.Empty(plan.Messages);
        Assert.Equal(1, plan.Dropped);
    }

    [Fact]
    public void A_switched_off_kind_is_dropped_but_others_still_show()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var before = N.Snap(authored: new[] { N.Pr(ci: CiState.Pending) });
        var after = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure, updatedMinutes: 1) }, reviews: new[] { N.Review() });
        var plan = engine.Plan(before, after, new NotificationPrefs { CiFailed = false }, Noon);
        var toast = Assert.Single(plan.Messages);
        Assert.Equal(NotificationKind.ReviewRequested, toast.Kind);
        Assert.Equal(1, plan.Dropped);
    }

    [Fact]
    public void Merged_is_off_by_default()
    {
        Assert.False(new NotificationPrefs().IsKindEnabled(NotificationKind.Merged));
        Assert.True(new NotificationPrefs().IsKindEnabled(NotificationKind.CiFailed));
    }

    [Fact]
    public void Muted_repo_and_muted_org_are_silent_case_insensitively()
    {
        var (before, after) = CiFailure("Acme/Api");
        var byRepo = new NotificationPrefs { MutedRepos = new[] { "acme/api" } };
        var byOrg = new NotificationPrefs { MutedOrgs = new[] { "ACME" } };
        Assert.Empty(new NotificationEngine(new InMemoryNotificationState()).Plan(before, after, byRepo, Noon).Messages);
        Assert.Empty(new NotificationEngine(new InMemoryNotificationState()).Plan(before, after, byOrg, Noon).Messages);
        Assert.Single(new NotificationEngine(new InMemoryNotificationState()).Plan(before, after, new NotificationPrefs(), Noon).Messages);
    }

    [Fact]
    public void The_same_event_is_not_shown_twice_by_the_same_engine()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var (before, after) = CiFailure();
        Assert.Single(engine.Plan(before, after, new NotificationPrefs(), Noon).Messages);
        Assert.Empty(engine.Plan(before, after, new NotificationPrefs(), Noon.AddMinutes(1)).Messages);
    }

    [Fact]
    public void The_seen_set_survives_a_restart()
    {
        var state = new InMemoryNotificationState();
        var (before, after) = CiFailure();
        Assert.Single(new NotificationEngine(state).Plan(before, after, new NotificationPrefs(), Noon).Messages);
        Assert.True(state.SaveCount > 0);
        var restarted = new NotificationEngine(state);
        Assert.Empty(restarted.Plan(before, after, new NotificationPrefs(), Noon.AddHours(1)).Messages);
    }

    [Fact]
    public void Up_to_the_limit_events_stay_individual()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var plan = engine.Plan(N.Snap(), WithReviewRequests(3), new NotificationPrefs(), Noon);
        Assert.Equal(3, plan.Messages.Count);
        Assert.All(plan.Messages, m => Assert.False(m.IsDigest));
    }

    [Fact]
    public void More_than_the_limit_become_one_digest()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var plan = engine.Plan(N.Snap(), WithReviewRequests(7), new NotificationPrefs(), Noon);
        var digest = Assert.Single(plan.Messages);
        Assert.True(digest.IsDigest);
        Assert.Equal(7, digest.Count);
        Assert.Equal("7 pull request updates", digest.Title);
        Assert.Null(digest.Url);
        Assert.Contains("and 3 more", digest.Body);
        Assert.Equal(7, plan.Delivered.Count);
    }

    [Fact]
    public void The_digest_limit_is_configurable()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var plan = engine.Plan(N.Snap(), WithReviewRequests(2), new NotificationPrefs { MaxIndividual = 1 }, Noon);
        Assert.True(Assert.Single(plan.Messages).IsDigest);
    }

    [Fact]
    public void Quiet_hours_hold_events_back_and_release_them_later()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var prefs = new NotificationPrefs { QuietHoursEnabled = true };
        var (before, after) = CiFailure();

        var quiet = engine.Plan(before, after, prefs, Night);
        Assert.Empty(quiet.Messages);
        Assert.Equal(1, quiet.Deferred);
        Assert.Equal(1, engine.DeferredCount);

        var morning = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.FromHours(1));
        var later = engine.Plan(after, after, prefs, morning);
        var toast = Assert.Single(later.Messages);
        Assert.Equal(NotificationKind.CiFailed, toast.Kind);
        Assert.Equal(0, engine.DeferredCount);
    }

    [Fact]
    public void Quiet_hours_off_means_night_time_toasts_show()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var (before, after) = CiFailure();
        Assert.Single(engine.Plan(before, after, new NotificationPrefs(), Night).Messages);
    }

    [Fact]
    public void Deferred_events_that_were_muted_meanwhile_are_dropped()
    {
        var engine = new NotificationEngine(new InMemoryNotificationState());
        var (before, after) = CiFailure();
        engine.Plan(before, after, new NotificationPrefs { QuietHoursEnabled = true }, Night);
        var morning = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.FromHours(1));
        var plan = engine.Plan(after, after, new NotificationPrefs { MutedRepos = new[] { "acme/api" } }, morning);
        Assert.Empty(plan.Messages);
    }

    [Fact]
    public void Digest_lists_the_most_important_events_first()
    {
        var before = N.Snap(authored: new[] { N.Pr(ci: CiState.Pending) });
        var reviews = Enumerable.Range(1, 4).Select(i => N.Review("r" + i, i)).ToList();
        var after = N.Snap(authored: new[] { N.Pr(ci: CiState.Failure, updatedMinutes: 1) }, reviews: reviews);
        var plan = new NotificationEngine(new InMemoryNotificationState()).Plan(before, after, new NotificationPrefs(), Noon);
        var digest = Assert.Single(plan.Messages);
        Assert.StartsWith("CI failed", digest.Body);
    }
}
