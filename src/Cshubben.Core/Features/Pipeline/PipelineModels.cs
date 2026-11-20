namespace Cshubben.Core.Features.Pipeline;

/// <summary>State of one check, job or workflow run, simplified from GitHub's status and conclusion.</summary>
public enum RunState { Success, Failure, Running, Queued, Cancelled, Skipped }

/// <summary>State of the latest status of a deployment.</summary>
public enum DeployState { Success, Failure, InProgress, Queued, Inactive }

/// <summary>
/// One check run (a job, or a commit status from an external CI) on a commit.
/// </summary>
/// <param name="Id">Check run id (for GitHub Actions this is the job id).</param>
/// <param name="Name">Name shown to the user, for example "build (windows)".</param>
/// <param name="State">Simplified state.</param>
/// <param name="StartedAt">When it started, if it did.</param>
/// <param name="CompletedAt">When it finished, if it did.</param>
/// <param name="Url">Link to the run on GitHub (https only, or empty).</param>
/// <param name="WorkflowRunId">The Actions workflow run it belongs to, when known. Needed to re-run failed jobs.</param>
/// <param name="FailingStep">Name of the step that failed, when known.</param>
/// <param name="Excerpt">A short, cleaned excerpt of the failure output, when known.</param>
public sealed record CheckRun(
    long Id,
    string Name,
    RunState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string Url,
    long? WorkflowRunId = null,
    string? FailingStep = null,
    string? Excerpt = null)
{
    /// <summary>Time between start and completion; null while running or when a time is missing.</summary>
    public TimeSpan? Duration
    {
        get
        {
            if (StartedAt is null || CompletedAt is null) return null;
            var span = CompletedAt.Value - StartedAt.Value;
            return span < TimeSpan.Zero ? null : span;
        }
    }
}

/// <summary>A GitHub Actions workflow run.</summary>
public sealed record WorkflowRun(
    long Id,
    string Name,
    RunState State,
    string Branch,
    string Sha,
    string Event,
    string Url,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The latest known deployment to one environment.</summary>
public sealed record Deployment(
    string Environment,
    long Id,
    string Sha,
    string Ref,
    string Creator,
    DateTimeOffset CreatedAt,
    DeployState State,
    string Url);

/// <summary>The latest release of a repository.</summary>
public sealed record ReleaseInfo(string Tag, DateTimeOffset At);

/// <summary>
/// Everything read about one repository for the Drift page, before interpretation by <see cref="DriftReport"/>.
/// <paramref name="Error"/> is set when the repository could not be read at all.
/// </summary>
public sealed record RepoActivity(
    string Repo,
    string DefaultBranch,
    IReadOnlyList<WorkflowRun> MainRuns,
    IReadOnlyList<Deployment> Deployments,
    ReleaseInfo? LastRelease,
    int? CommitsSinceRelease,
    int? CommitsSinceDeploy,
    DateTimeOffset FetchedAt,
    string? Error = null)
{
    public static RepoActivity Failed(string repo, string error, DateTimeOffset at) =>
        new(repo, string.Empty, Array.Empty<WorkflowRun>(), Array.Empty<Deployment>(), null, null, null, at, error);
}
