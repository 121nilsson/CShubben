using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>
/// The pipeline, conversation and drift data used by the inspector tabs and the Drift page, plus the two write actions
/// that belong with them (re-run failed jobs, reply). The real implementation is <see cref="GitHubPipelineData"/>;
/// <see cref="DemoPipelineData"/> is deterministic and never sends anything.
/// </summary>
public interface IPipelineData
{
    /// <summary>True for demo data.</summary>
    bool IsDemo { get; }

    /// <summary>True when writes are only simulated (demo mode or a dry-run setting).</summary>
    bool IsDryRun { get; }

    /// <summary>Every check run on the head commit of the pull request (all attempts).</summary>
    Task<IReadOnlyList<CheckRun>> GetChecksAsync(PullRequest pr, CancellationToken cancellationToken);

    /// <summary>Comments, reviews, threads, commits and events of the pull request.</summary>
    Task<PrConversation> GetConversationAsync(PullRequest pr, CancellationToken cancellationToken);

    /// <summary>Workflow runs, deployments and release distance per repository. A repository that fails comes back with an error, not as an exception.</summary>
    Task<IReadOnlyList<RepoActivity>> GetDriftAsync(IReadOnlyList<string> repos, CancellationToken cancellationToken);

    /// <summary>Re-runs the failed jobs of each workflow run.</summary>
    Task<ActionOutcome> RerunFailedJobsAsync(string repo, IReadOnlyList<long> runIds, CancellationToken cancellationToken);

    /// <summary>Posts a comment on the pull request.</summary>
    Task<ActionOutcome> ReplyAsync(PullRequest pr, string body, CancellationToken cancellationToken);
}

/// <summary>Merging a pull request. The demo implementation never merges anything.</summary>
public interface IMergeActions
{
    /// <summary>True when the merge is only simulated.</summary>
    bool IsDryRun { get; }

    /// <param name="expectedHeadSha">The commit the checks and approvals were read for; when the branch has moved on, nothing is merged.</param>
    Task<ActionOutcome> MergeAsync(PullRequest pr, MergeMethodChoice method, CancellationToken cancellationToken, string? expectedHeadSha = null);
}

/// <summary>
/// Where the pipeline data and merge actions come from. The defaults are the demo implementations, so nothing real is ever
/// sent unless the app assigns real ones at startup (for example <c>new GitHubPipelineData(rest, graphql, writer) { DryRun = true }</c>).
/// </summary>
public static class PipelineServices
{
    public static IPipelineData Data { get; set; } = new DemoPipelineData();

    public static IMergeActions Merge { get; set; } = new DemoMergeActions();

    /// <summary>Called after a pull request was really merged (not in a dry run). The shell can assign a refresh here.</summary>
    public static Action<PullRequest> Merged { get; set; } = _ => { };
}
