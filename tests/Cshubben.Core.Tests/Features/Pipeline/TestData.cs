using Cshubben.Core.Features.Pipeline;

namespace Cshubben.Core.Tests.Features.Pipeline;

internal static class PipelineTestData
{
    // Friday 2026-11-20 12:00 UTC.
    public static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset Ago(double hours) => Now - TimeSpan.FromHours(hours);

    public static CheckRun Check(
        string name,
        RunState state,
        double startedHoursAgo = 1,
        double? seconds = 60,
        long id = 0,
        long? workflowRunId = 100,
        string? failingStep = null)
    {
        var started = Ago(startedHoursAgo);
        DateTimeOffset? completed = seconds is null || state is RunState.Running or RunState.Queued
            ? null
            : started + TimeSpan.FromSeconds(seconds.Value);
        if (state == RunState.Queued) started = Ago(startedHoursAgo);
        return new CheckRun(
            id == 0 ? Math.Abs((long)name.GetHashCode() % 100000) + (long)(startedHoursAgo * 1000) : id,
            name, state, state == RunState.Queued ? null : started, completed, "https://github.com/acme/api/runs/1", workflowRunId, failingStep);
    }

    public static WorkflowRun Run(
        string name,
        RunState state,
        double hoursAgo = 1,
        string ev = "push",
        long id = 1,
        string branch = "main") =>
        new(id, name, state, branch, "a1b2c3d4e5f6", ev, "https://github.com/acme/api/actions/runs/" + id, Ago(hoursAgo), Ago(hoursAgo));

    public static Deployment Deploy(
        string env,
        DeployState state,
        double hoursAgo = 1,
        string gitRef = "main",
        long id = 1) =>
        new(env, id, "a1b2c3d4e5f6", gitRef, "deploy-bot", Ago(hoursAgo), state, "");

    public static RepoActivity Repo(
        string name,
        IReadOnlyList<WorkflowRun>? runs = null,
        IReadOnlyList<Deployment>? deployments = null,
        ReleaseInfo? release = null,
        int? sinceRelease = null,
        int? sinceDeploy = null,
        string? error = null) =>
        new(name, "main", runs ?? Array.Empty<WorkflowRun>(), deployments ?? Array.Empty<Deployment>(), release, sinceRelease, sinceDeploy, Now, error);
}
