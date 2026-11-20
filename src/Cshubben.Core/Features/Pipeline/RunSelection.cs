namespace Cshubben.Core.Features.Pipeline;

/// <summary>
/// Rules for which workflow runs and deployments count. Ported from My-hub: a run from a pull request does not say
/// anything about the branch, a workflow that stopped running should not stay red forever, and noise environments
/// (for example "copilot") are ignored.
/// </summary>
public static class RunSelection
{
    /// <summary>Runs on the default branch older than this are not shown.</summary>
    public const int MaxRunAgeDays = 14;

    /// <summary>A failed deployment older than this no longer needs the user.</summary>
    public const int MaxAlertAgeDays = 7;

    private static readonly string[] PullRequestEvents = { "pull_request", "pull_request_target", "issue_comment", "dynamic" };
    private static readonly string[] NoiseEnvironments = { "copilot" };

    public static bool IsPullRequestEvent(string? eventName) =>
        eventName is not null && PullRequestEvents.Contains(eventName, StringComparer.Ordinal);

    public static bool IsNoiseEnvironment(string? name) =>
        name is not null && NoiseEnvironments.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>"production", "prod-eu" and similar.</summary>
    public static bool IsProdLike(string? name) =>
        name is not null && name.Contains("prod", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The newest run per workflow name, newest first. Pull request events and runs not updated within
    /// <see cref="MaxRunAgeDays"/> days are dropped.
    /// </summary>
    public static IReadOnlyList<WorkflowRun> LatestPerWorkflow(IEnumerable<WorkflowRun> runs, DateTimeOffset now)
    {
        var ordered = runs
            .Where(r => !IsPullRequestEvent(r.Event))
            .Where(r => now - r.UpdatedAt <= TimeSpan.FromDays(MaxRunAgeDays))
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<WorkflowRun>();
        foreach (var run in ordered)
            if (seen.Add(run.Name)) result.Add(run);
        return result;
    }

    /// <summary>The newest deployment per environment, newest first. Noise environments are dropped.</summary>
    public static IReadOnlyList<Deployment> LatestPerEnvironment(IEnumerable<Deployment> deployments)
    {
        var ordered = deployments
            .Where(d => !IsNoiseEnvironment(d.Environment))
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Deployment>();
        foreach (var deployment in ordered)
            if (seen.Add(deployment.Environment)) result.Add(deployment);
        return result;
    }

    /// <summary>
    /// Worth noting: not noise, and either deployed from the default branch or to a production-like environment.
    /// A feature branch deployed to test is everyday work and should not raise an alarm.
    /// </summary>
    public static bool IsNotable(Deployment deployment, string defaultBranch) =>
        !IsNoiseEnvironment(deployment.Environment)
        && (string.Equals(deployment.Ref, defaultBranch, StringComparison.Ordinal) || IsProdLike(deployment.Environment));

    /// <summary>A failed, notable deployment that is recent enough to still matter.</summary>
    public static bool IsAlert(Deployment deployment, string defaultBranch, DateTimeOffset now) =>
        deployment.State == DeployState.Failure
        && IsNotable(deployment, defaultBranch)
        && now - deployment.CreatedAt <= TimeSpan.FromDays(MaxAlertAgeDays);
}
