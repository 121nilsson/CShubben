using System.Text.Json;
using Cshubben.Core.Features.Pipeline;
using Xunit;
using static Cshubben.Core.Tests.Features.Pipeline.PipelineTestData;

namespace Cshubben.Core.Tests.Features.Pipeline;

public class PipelineJsonTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("completed", "success", RunState.Success)]
    [InlineData("completed", "failure", RunState.Failure)]
    [InlineData("completed", "timed_out", RunState.Failure)]
    [InlineData("completed", "startup_failure", RunState.Failure)]
    [InlineData("completed", "cancelled", RunState.Cancelled)]
    [InlineData("completed", "neutral", RunState.Skipped)]
    [InlineData("completed", "skipped", RunState.Skipped)]
    [InlineData("completed", "something_new", RunState.Skipped)]
    [InlineData("completed", "action_required", RunState.Queued)]
    [InlineData("in_progress", null, RunState.Running)]
    [InlineData("queued", null, RunState.Queued)]
    [InlineData("waiting", null, RunState.Queued)]
    public void Run_state_mapping(string status, string? conclusion, RunState expected) =>
        Assert.Equal(expected, PipelineJson.RunStateOf(status, conclusion));

    [Theory]
    [InlineData("success", DeployState.Success)]
    [InlineData("failure", DeployState.Failure)]
    [InlineData("error", DeployState.Failure)]
    [InlineData("in_progress", DeployState.InProgress)]
    [InlineData("inactive", DeployState.Inactive)]
    [InlineData("pending", DeployState.Queued)]
    [InlineData(null, DeployState.Queued)]
    public void Deploy_state_mapping(string? status, DeployState expected) =>
        Assert.Equal(expected, PipelineJson.DeployStateOf(status));

    [Fact]
    public void Run_id_is_read_from_an_actions_url()
    {
        Assert.Equal(9876543210L, PipelineJson.RunIdFromUrl("https://github.com/acme/api/actions/runs/9876543210/job/55"));
        Assert.Null(PipelineJson.RunIdFromUrl("https://ci.example.com/build/5"));
        Assert.Null(PipelineJson.RunIdFromUrl(null));
    }

    [Fact]
    public void Unsafe_urls_are_dropped()
    {
        Assert.Equal("https://github.com/a/b", PipelineJson.SafeUrl("https://github.com/a/b"));
        Assert.Equal(string.Empty, PipelineJson.SafeUrl("javascript:alert(1)"));
        Assert.Equal(string.Empty, PipelineJson.SafeUrl("file:///c:/secret"));
        Assert.Equal(string.Empty, PipelineJson.SafeUrl("not a url"));
    }

    [Fact]
    public void Check_runs_are_mapped_with_failure_excerpt_and_run_id()
    {
        var root = Parse("""
            { "check_runs": [
              { "id": 1, "name": "build", "status": "completed", "conclusion": "success",
                "started_at": "2026-11-20T10:00:00Z", "completed_at": "2026-11-20T10:02:05Z",
                "html_url": "https://github.com/acme/api/runs/1", "details_url": "https://github.com/acme/api/actions/runs/555/job/1" },
              { "id": 2, "name": "tests", "status": "completed", "conclusion": "failure",
                "started_at": "2026-11-20T10:00:00Z", "completed_at": "2026-11-20T10:01:00Z",
                "html_url": "javascript:bad", "details_url": "https://github.com/acme/api/actions/runs/555/job/2",
                "output": { "title": "Run failed", "summary": "\u001b[31mExpected 200\u001b[0m\nbut got 409" } },
              { "name": "no id", "status": "queued" }
            ] }
            """);
        var runs = PipelineJson.ParseCheckRuns(root);
        Assert.Equal(2, runs.Count);
        Assert.Equal(TimeSpan.FromSeconds(125), runs[0].Duration);
        Assert.Equal(555L, runs[0].WorkflowRunId);
        Assert.Equal(RunState.Failure, runs[1].State);
        Assert.Equal("Expected 200\nbut got 409", runs[1].Excerpt);
        Assert.Equal(string.Empty, runs[1].Url);
    }

    [Fact]
    public void Commit_statuses_become_check_runs()
    {
        var root = Parse("""
            { "statuses": [
              { "id": 7, "context": "ci/circle", "state": "failure", "description": "Build failed", "target_url": "https://circleci.com/x", "created_at": "2026-11-20T10:00:00Z", "updated_at": "2026-11-20T10:03:00Z" },
              { "id": 8, "context": "ci/lint", "state": "pending", "created_at": "2026-11-20T10:00:00Z", "updated_at": "2026-11-20T10:00:00Z" }
            ] }
            """);
        var runs = PipelineJson.ParseCommitStatuses(root);
        Assert.Equal(RunState.Failure, runs[0].State);
        Assert.Equal(TimeSpan.FromMinutes(3), runs[0].Duration);
        Assert.Equal("Build failed", runs[0].Excerpt);
        Assert.Equal(RunState.Running, runs[1].State);
        Assert.Null(runs[1].CompletedAt);
    }

    [Fact]
    public void Failing_step_is_the_first_failed_step()
    {
        var job = Parse("""{ "steps": [ {"name":"Set up job","conclusion":"success"}, {"name":"Run tests","conclusion":"failure"}, {"name":"Post","conclusion":"skipped"} ] }""");
        Assert.Equal("Run tests", PipelineJson.FailingStepOfJob(job));
        Assert.Null(PipelineJson.FailingStepOfJob(Parse("""{ "steps": [] }""")));
    }

    [Fact]
    public void Workflow_runs_skip_invalid_entries_and_collapse_names()
    {
        var root = Parse("""
            { "workflow_runs": [
              { "id": 10, "name": "API  Build\nand Deploy", "status": "completed", "conclusion": "success", "head_branch": "main", "head_sha": "abc1234", "event": "push", "html_url": "https://github.com/acme/api/actions/runs/10", "created_at": "2026-11-20T09:00:00Z", "updated_at": "2026-11-20T09:05:00Z" },
              { "id": 11, "status": "completed", "created_at": "2026-11-20T09:00:00Z" },
              { "id": 12, "name": "x", "status": "in_progress" }
            ] }
            """);
        var runs = PipelineJson.ParseWorkflowRuns(root);
        var run = Assert.Single(runs);
        Assert.Equal("API Build and Deploy", run.Name);
        Assert.Equal(RunState.Success, run.State);
    }

    [Fact]
    public void Deployments_and_status_are_combined()
    {
        var deployments = PipelineJson.ParseDeployments(Parse("""
            [ { "id": 5, "environment": "production", "sha": "a1b2c3d", "ref": "main", "creator": { "login": "deploy-bot" }, "created_at": "2026-11-20T03:00:00Z" },
              { "id": 6, "sha": "x" } ]
            """));
        var d = Assert.Single(deployments);
        Assert.Equal(DeployState.Queued, d.State);
        var done = PipelineJson.WithStatus(d, Parse("""[ { "state": "success", "log_url": "https://github.com/acme/api/runs/9" }, { "state": "in_progress" } ]"""));
        Assert.Equal(DeployState.Success, done.State);
        Assert.Equal("https://github.com/acme/api/runs/9", done.Url);
        Assert.Equal(d, PipelineJson.WithStatus(d, Parse("[]")));
    }

    [Fact]
    public void Release_and_compare_answers()
    {
        var release = PipelineJson.ParseRelease(Parse("""{ "tag_name": "v2.8.0", "published_at": "2026-11-01T00:00:00Z" }"""));
        Assert.Equal("v2.8.0", release!.Tag);
        Assert.Null(PipelineJson.ParseRelease(Parse("""{ "message": "Not Found" }""")));
        Assert.Equal(14, PipelineJson.AheadBy(Parse("""{ "status": "ahead", "ahead_by": 14 }""")));
        Assert.Null(PipelineJson.AheadBy(Parse("""{ "status": "x" }""")));
    }

    [Fact]
    public void Head_sha_and_default_branch()
    {
        Assert.Equal("a1b2c3d4", PipelineJson.HeadShaOf(Parse("""{ "head": { "sha": "a1b2c3d4" } }""")));
        Assert.Null(PipelineJson.HeadShaOf(Parse("""{ "head": { "sha": "../../etc" } }""")));
        Assert.Equal("trunk", PipelineJson.DefaultBranchOf(Parse("""{ "default_branch": "trunk" }""")));
    }

    [Fact]
    public void Parsed_workflow_runs_feed_the_selection_rules()
    {
        var runs = new[]
        {
            Run("CI", RunState.Failure, 5, id: 1),
            Run("CI", RunState.Success, 1, id: 2),
            Run("PR check", RunState.Failure, 1, ev: "pull_request", id: 3),
        };
        var latest = RunSelection.LatestPerWorkflow(runs, Now);
        var only = Assert.Single(latest);
        Assert.Equal(2, only.Id);
    }

    [Fact]
    public void Latest_deployment_per_environment_wins_and_noise_is_dropped()
    {
        var latest = RunSelection.LatestPerEnvironment(new[]
        {
            Deploy("production", DeployState.Success, 30, id: 1),
            Deploy("Production", DeployState.Failure, 2, id: 2),
            Deploy("copilot", DeployState.Success, 1, id: 3),
        });
        var one = Assert.Single(latest);
        Assert.Equal(2, one.Id);
    }
}
