using Cshubben.Core.Features.Pipeline;
using Xunit;
using static Cshubben.Core.Tests.Features.Pipeline.PipelineTestData;

namespace Cshubben.Core.Tests.Features.Pipeline;

public class DriftReportTests
{
    [Fact]
    public void Green_main_is_healthy()
    {
        var row = DriftReport.Interpret(Repo("acme/api", new[] { Run("CI", RunState.Success, 3) }), Now);
        Assert.Equal(RepoHealth.Healthy, row.Health);
        Assert.Equal(RunState.Success, row.MainState);
        Assert.Equal("main is green", row.Headline);
    }

    [Fact]
    public void Failing_workflow_on_main_needs_attention_and_is_named()
    {
        var row = DriftReport.Interpret(Repo("acme/web", new[]
        {
            Run("Web CI", RunState.Failure, 1, id: 10),
            Run("Lint", RunState.Success, 2, id: 11),
        }), Now);
        Assert.Equal(RepoHealth.Attention, row.Health);
        Assert.Equal("main is red: Web CI", row.Headline);
        Assert.Equal(10, row.FirstFailing!.Id);
    }

    [Fact]
    public void Only_the_latest_run_per_workflow_counts()
    {
        var row = DriftReport.Interpret(Repo("acme/web", new[]
        {
            Run("CI", RunState.Failure, 5, id: 1),
            Run("CI", RunState.Success, 1, id: 2),
        }), Now);
        Assert.Equal(RepoHealth.Healthy, row.Health);
        Assert.Empty(row.FailingWorkflows);
    }

    [Fact]
    public void Pull_request_runs_do_not_count_for_main()
    {
        var row = DriftReport.Interpret(Repo("acme/web", new[] { Run("CI", RunState.Failure, 1, ev: "pull_request") }), Now);
        Assert.Equal(RepoHealth.Quiet, row.Health);
    }

    [Fact]
    public void Old_failures_are_ignored()
    {
        var row = DriftReport.Interpret(Repo("acme/web", new[] { Run("CI", RunState.Failure, 24 * 20) }), Now);
        Assert.Equal(RepoHealth.Quiet, row.Health);
    }

    [Fact]
    public void Running_workflow_gives_running_health()
    {
        var row = DriftReport.Interpret(Repo("acme/mobile", new[] { Run("Mobile CI", RunState.Running, 0.2) }), Now);
        Assert.Equal(RepoHealth.Running, row.Health);
        Assert.Equal("Running: Mobile CI", row.Headline);
    }

    [Fact]
    public void Failed_production_deploy_needs_attention()
    {
        var row = DriftReport.Interpret(Repo("acme/infra",
            new[] { Run("Plan", RunState.Success, 2) },
            new[] { Deploy("production", DeployState.Failure, 4, "release/1") }), Now);
        Assert.Equal(RepoHealth.Attention, row.Health);
        Assert.Equal("Deploy to production failed 4 h ago", row.Headline);
    }

    [Fact]
    public void Failed_feature_branch_deploy_to_test_is_not_an_alert()
    {
        var row = DriftReport.Interpret(Repo("acme/infra",
            new[] { Run("Plan", RunState.Success, 2) },
            new[] { Deploy("test", DeployState.Failure, 4, "feat/x") }), Now);
        Assert.Equal(RepoHealth.Healthy, row.Health);
    }

    [Fact]
    public void Old_failed_deploy_is_not_an_alert()
    {
        var row = DriftReport.Interpret(Repo("acme/infra",
            new[] { Run("Plan", RunState.Success, 2) },
            new[] { Deploy("production", DeployState.Failure, 24 * 9) }), Now);
        Assert.Equal(RepoHealth.Healthy, row.Health);
    }

    [Fact]
    public void Noise_environment_is_ignored()
    {
        var row = DriftReport.Interpret(Repo("acme/api", null, new[] { Deploy("copilot", DeployState.Failure, 1) }), Now);
        Assert.Equal(RepoHealth.Quiet, row.Health);
        Assert.Empty(row.Environments);
    }

    [Fact]
    public void Last_successful_deploy_prefers_production_like_environments()
    {
        var row = DriftReport.Interpret(Repo("acme/api",
            new[] { Run("CI", RunState.Success, 1) },
            new[]
            {
                Deploy("staging", DeployState.Success, 1, id: 1),
                Deploy("production", DeployState.Success, 9, id: 2),
            }), Now);
        Assert.Equal("production", row.LastSuccessfulDeploy!.Environment);
        Assert.Equal(TimeSpan.FromHours(9), row.LastDeployAge);
        Assert.Equal("Deployed to production 9 h ago", row.DeployLine);
    }

    [Fact]
    public void Undeployed_commits_show_in_the_deploy_line()
    {
        var row = DriftReport.Interpret(Repo("acme/api",
            new[] { Run("CI", RunState.Success, 1) },
            new[] { Deploy("production", DeployState.Success, 48) }, sinceDeploy: 9), Now);
        Assert.Equal("Deployed to production 2 d ago · 9 commits not deployed", row.DeployLine);
        Assert.Equal(9, row.UndeployedCommits);
    }

    [Fact]
    public void No_deployments_say_so()
    {
        var row = DriftReport.Interpret(Repo("acme/api", new[] { Run("CI", RunState.Success, 1) }), Now);
        Assert.Equal("No successful deployment yet", row.DeployLine);
        Assert.Null(row.LastDeployAge);
    }

    [Fact]
    public void Release_line_counts_commits_since_the_release()
    {
        var row = DriftReport.Interpret(Repo("acme/api", null, null, new ReleaseInfo("v2.8.0", Ago(100)), sinceRelease: 14), Now);
        Assert.Equal("14 commits since v2.8.0", row.ReleaseLine);
        Assert.Equal(14, row.UnreleasedCommits);
    }

    [Fact]
    public void Repo_with_an_error_is_unavailable()
    {
        var row = DriftReport.Interpret(RepoActivity.Failed("acme/gone", "Not found", Now), Now);
        Assert.Equal(RepoHealth.Unavailable, row.Health);
        Assert.Equal("Could not read this repository", row.Headline);
    }

    [Fact]
    public void Report_sorts_red_first_then_running_healthy_quiet()
    {
        var report = DriftReport.Build(new[]
        {
            Repo("acme/quiet"),
            Repo("acme/ok", new[] { Run("CI", RunState.Success, 1) }),
            Repo("acme/busy", new[] { Run("CI", RunState.Running, 1) }),
            Repo("acme/red", new[] { Run("CI", RunState.Failure, 1) }),
            RepoActivity.Failed("acme/err", "boom", Now),
        }, Now);
        Assert.Equal(new[] { "acme/red", "acme/busy", "acme/ok", "acme/quiet", "acme/err" }, report.Rows.Select(r => r.Repo).ToArray());
    }

    [Fact]
    public void Report_counts_and_summary()
    {
        var report = DriftReport.Build(new[]
        {
            Repo("acme/red", new[] { Run("CI", RunState.Failure, 1), Run("Deploy", RunState.Failure, 2, id: 2) }, sinceDeploy: 3, sinceRelease: 5),
            Repo("acme/ok", new[] { Run("CI", RunState.Success, 1) }, sinceDeploy: 0, sinceRelease: 2),
            Repo("acme/ok2", new[] { Run("CI", RunState.Success, 1) }),
        }, Now);
        Assert.Equal(1, report.NeedAttention);
        Assert.Equal(2, report.Healthy);
        Assert.Equal(2, report.FailingWorkflows);
        Assert.Equal(1, report.ReposBehindDeploy);
        Assert.Equal(7, report.UnreleasedCommits);
        Assert.Equal("1 needs attention · 2 healthy", report.Summary);
    }

    [Fact]
    public void Summary_when_everything_is_healthy()
    {
        var report = DriftReport.Build(new[]
        {
            Repo("acme/a", new[] { Run("CI", RunState.Success, 1) }),
            Repo("acme/b", new[] { Run("CI", RunState.Success, 1) }),
        }, Now);
        Assert.Equal("All 2 repositories are healthy", report.Summary);
    }

    [Fact]
    public void Empty_report_has_a_message()
    {
        Assert.Equal("No repositories to watch yet", DriftReport.Build(Array.Empty<RepoActivity>(), Now).Summary);
    }

    [Fact]
    public void Short_name_drops_the_owner()
    {
        var row = DriftReport.Interpret(Repo("acme/api"), Now);
        Assert.Equal("api", row.ShortName);
    }
}
