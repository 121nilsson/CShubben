using Cshubben.Core.Composition;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Tests.Features.Pipeline;
using Xunit;

namespace Cshubben.Core.Tests.Features.Notifications;

public class WatchTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(1));

    private static WatchedRepoState Repo(string name, bool red = false, string run = "", params WatchedDeployFailure[] deploys) =>
        new(name, red, run, red ? "main is red: CI" : "main is green", deploys);

    // ---- Drift diff

    [Fact]
    public void Drift_first_check_is_a_baseline()
    {
        Assert.Empty(WatchDiff.Drift(null, new[] { Repo("acme/api", red: true, run: "7") }));
    }

    [Fact]
    public void Main_turning_red_is_reported_once_with_a_link_to_actions()
    {
        var before = new[] { Repo("acme/api") };
        var after = new[] { Repo("acme/api", red: true, run: "7") };
        var e = Assert.Single(WatchDiff.Drift(before, after));
        Assert.Equal(NotificationKind.MainRed, e.Kind);
        Assert.Equal("acme/api", e.Repo);
        Assert.Equal("https://github.com/acme/api/actions", e.Url);
        Assert.Empty(WatchDiff.Drift(after, after));
    }

    [Fact]
    public void A_repo_that_was_already_red_or_is_new_to_the_check_is_not_reported()
    {
        var red = new[] { Repo("acme/api", red: true, run: "7") };
        Assert.Empty(WatchDiff.Drift(red, red));
        Assert.Empty(WatchDiff.Drift(Array.Empty<WatchedRepoState>(), red));
    }

    [Fact]
    public void New_deploy_failures_are_reported_and_known_ones_are_not()
    {
        var known = new WatchedDeployFailure("prod", "11");
        var fresh = new WatchedDeployFailure("staging", "12");
        var before = new[] { Repo("acme/api", deploys: known) };
        var after = new[] { Repo("acme/api", deploys: new[] { known, fresh }) };
        var e = Assert.Single(WatchDiff.Drift(before, after));
        Assert.Equal(NotificationKind.DeployFailed, e.Kind);
        Assert.Equal("staging", e.Detail);
    }

    // ---- Linear diff

    private static WatchedLinear Lin(string[] issues, params WatchedOutOfSync[] sync) =>
        new(issues.Select(i => new WatchedIssue(i, "Title " + i, "https://linear.app/x/issue/" + i)).ToList(), sync);

    [Fact]
    public void Newly_assigned_issue_is_reported_but_the_first_check_is_a_baseline()
    {
        Assert.Empty(WatchDiff.Linear(null, Lin(new[] { "A-1" })));
        var e = Assert.Single(WatchDiff.Linear(Lin(new[] { "A-1" }), Lin(new[] { "A-1", "A-2" })));
        Assert.Equal(NotificationKind.LinearAssigned, e.Kind);
        Assert.Equal("A-2", e.Detail);
        Assert.Equal("Title A-2", e.Title);
    }

    [Fact]
    public void New_out_of_sync_item_is_reported_once()
    {
        var sync = new WatchedOutOfSync("MergedButIssueOpen|A-1|acme/api#5", "A-1", "PR #5 is merged but A-1 is still open.", "https://github.com/acme/api/pull/5");
        var before = Lin(new[] { "A-1" });
        var after = Lin(new[] { "A-1" }, sync);
        var e = Assert.Single(WatchDiff.Linear(before, after));
        Assert.Equal(NotificationKind.LinearOutOfSync, e.Kind);
        Assert.Equal("https://github.com/acme/api/pull/5", e.Url);
        Assert.Empty(WatchDiff.Linear(after, after));
    }

    // ---- Engine rules

    [Fact]
    public void Drift_events_notify_only_for_important_repos_but_linear_events_always_do()
    {
        var drift = new[] { new NotificationEvent("main-red:acme/api:7", NotificationKind.MainRed, "acme/api", "acme/api", 0, "main is red: CI", "https://github.com/acme/api/actions") };
        var linear = new[] { new NotificationEvent("linear-assigned:a-2", NotificationKind.LinearAssigned, "A-2", "", 0, "Title", "https://linear.app/x", "A-2") };

        var plain = new NotificationEngine(new InMemoryNotificationState());
        Assert.Empty(plain.PlanEvents(drift, new NotificationPrefs(), Noon).Messages);

        var important = new NotificationEngine(new InMemoryNotificationState());
        var toast = Assert.Single(important.PlanEvents(drift, new NotificationPrefs { ImportantRepos = new[] { "acme/api" } }, Noon).Messages);
        Assert.Equal("Main is red: acme/api", toast.Title);
        Assert.Equal("main is red: CI", toast.Body);

        var lin = new NotificationEngine(new InMemoryNotificationState());
        var l = Assert.Single(lin.PlanEvents(linear, new NotificationPrefs(), Noon).Messages);
        Assert.Equal("Assigned to you: A-2", l.Title);
        Assert.Equal("Title", l.Body);
    }

    [Fact]
    public void Toggles_and_the_seen_set_apply_to_background_events()
    {
        var e = new[] { new NotificationEvent("linear-assigned:a-2", NotificationKind.LinearAssigned, "A-2", "", 0, "Title", "", "A-2") };
        var off = new NotificationEngine(new InMemoryNotificationState());
        Assert.Empty(off.PlanEvents(e, new NotificationPrefs { LinearAssigned = false }, Noon).Messages);

        var engine = new NotificationEngine(new InMemoryNotificationState());
        Assert.Single(engine.PlanEvents(e, new NotificationPrefs(), Noon).Messages);
        Assert.Empty(engine.PlanEvents(e, new NotificationPrefs(), Noon).Messages);
    }

    // ---- Composition

    [Fact]
    public void Drift_report_becomes_watched_states_and_skips_unreadable_repos()
    {
        var red = PipelineTestData.Repo("acme/api",
            runs: new[] { PipelineTestData.Run("CI", RunState.Failure, id: 9) },
            deployments: new[] { PipelineTestData.Deploy("production", DeployState.Failure, id: 31) });
        var green = PipelineTestData.Repo("acme/web", runs: new[] { PipelineTestData.Run("CI", RunState.Success, id: 4) });
        var broken = PipelineTestData.Repo("acme/old", error: "boom");
        var states = WatchSources.FromDrift(DriftReport.Build(new[] { red, green, broken }, PipelineTestData.Now));

        Assert.Equal(2, states.Count);
        var api = states.First(s => s.Repo == "acme/api");
        Assert.True(api.MainRed);
        Assert.Equal("9", api.RunKey);
        Assert.Contains(api.DeployFailures, d => d.Environment == "production" && d.Key == "31");
        Assert.False(states.First(s => s.Repo == "acme/web").MainRed);
        Assert.DoesNotContain(states, s => s.Repo == "acme/old");
    }

    private static LinearIssue Issue(string identifier, LinearStateType type, string state) =>
        new("id-" + identifier, identifier, "Title " + identifier, state, type, "me", 3, "A", null, Array.Empty<string>(),
            "https://linear.app/x/issue/" + identifier, Noon, null);

    [Fact]
    public void Linear_state_keeps_only_the_out_of_sync_kinds_worth_a_toast()
    {
        var open = new[] { Issue("ABC-1", LinearStateType.Started, "In Progress") };
        var prs = new[]
        {
            new ReconPr("acme/api", 5, "ABC-1: ship it", "https://github.com/acme/api/pull/5", "feat/abc-1", true),
            new ReconPr("acme/web", 6, "No identifier here", "https://github.com/acme/web/pull/6", "feat/x", false),
        };
        var state = WatchSources.FromLinear(open, Array.Empty<LinearIssue>(), prs);
        Assert.Single(state.Issues);
        var sync = Assert.Single(state.OutOfSync);
        Assert.Equal("ABC-1", sync.Identifier);
        Assert.StartsWith("MergedButIssueOpen|ABC-1|acme/api#5", sync.Key);
    }
}
