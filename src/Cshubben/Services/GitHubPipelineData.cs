using System.Globalization;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>
/// <see cref="IPipelineData"/> against the real GitHub API. Reads use REST (check runs, statuses, jobs, workflow runs,
/// deployments, releases, compare) and one GraphQL query (conversation); writes go through <see cref="GitHubWriter"/>,
/// so <see cref="DryRun"/> is honoured. Repository names and commit ids are validated before they reach a URL, and
/// untrusted text only ever travels as GraphQL variables.
/// </summary>
public sealed class GitHubPipelineData : IPipelineData
{
    private const int MaxParallelRepos = 4;
    private const int MaxFailureLookups = 4;
    private const int MaxEnvironments = 8;
    private const int MaxRepos = 16;

    private readonly GitHubRest _rest;
    private readonly GitHubGraphQl _graphql;
    private readonly GitHubWriter _writer;

    public GitHubPipelineData(GitHubRest rest, GitHubGraphQl graphql, GitHubWriter writer)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(graphql);
        ArgumentNullException.ThrowIfNull(writer);
        _rest = rest;
        _graphql = graphql;
        _writer = writer;
    }

    /// <summary>When true nothing is sent; writes pretend to succeed.</summary>
    public bool DryRun { get; set; }

    public bool IsDemo => false;

    public bool IsDryRun => DryRun;

    // ------------------------------------------------------------------ checks

    public async Task<IReadOnlyList<CheckRun>> GetChecksAsync(PullRequest pr, CancellationToken cancellationToken)
    {
        var (owner, name) = GitHubNames.SplitRepo(pr.Repo);
        GitHubNames.CheckNumber(pr.Number);
        var repoUrl = RepoUrl(owner, name);

        var prAnswer = await _rest.GetAsync(repoUrl + "/pulls/" + pr.Number.ToString(CultureInfo.InvariantCulture), null, cancellationToken).ConfigureAwait(false);
        var sha = PipelineJson.HeadShaOf(prAnswer.Json)
            ?? throw new GitHubApiException(200, "Could not read the head commit of the pull request.");

        var checksTask = _rest.GetAsync(repoUrl + "/commits/" + sha + "/check-runs?per_page=100&filter=all", null, cancellationToken);
        var statusTask = ReadOptionalAsync(repoUrl + "/commits/" + sha + "/status?per_page=100", cancellationToken);
        await Task.WhenAll(checksTask, statusTask).ConfigureAwait(false);

        var runs = new List<CheckRun>(PipelineJson.ParseCheckRuns((await checksTask.ConfigureAwait(false)).Json));
        runs.AddRange(PipelineJson.ParseCommitStatuses(await statusTask.ConfigureAwait(false)));

        // The failing step is only in the job, so look it up for the first few failed Actions jobs.
        var failed = runs
            .Select((run, index) => (run, index))
            .Where(x => x.run.State == RunState.Failure && x.run.WorkflowRunId is not null && x.run.Id > 0)
            .Take(MaxFailureLookups)
            .ToList();
        var lookups = failed.Select(x => ReadOptionalAsync(
            repoUrl + "/actions/jobs/" + x.run.Id.ToString(CultureInfo.InvariantCulture), cancellationToken)).ToList();
        var jobs = await Task.WhenAll(lookups).ConfigureAwait(false);
        for (var i = 0; i < failed.Count; i++)
        {
            var step = PipelineJson.FailingStepOfJob(jobs[i]);
            if (step is not null) runs[failed[i].index] = failed[i].run with { FailingStep = step };
        }

        return runs;
    }

    // ------------------------------------------------------------------ conversation

    public async Task<PrConversation> GetConversationAsync(PullRequest pr, CancellationToken cancellationToken)
    {
        var (owner, name) = GitHubNames.SplitRepo(pr.Repo);
        GitHubNames.CheckNumber(pr.Number);
        var variables = new Dictionary<string, object?>
        {
            ["owner"] = owner,
            ["name"] = name,
            ["number"] = pr.Number,
        };
        var data = await _graphql.QueryAsync(ConversationJson.Query, variables, false, cancellationToken).ConfigureAwait(false);
        return ConversationJson.Parse(data);
    }

    // ------------------------------------------------------------------ drift

    public async Task<IReadOnlyList<RepoActivity>> GetDriftAsync(IReadOnlyList<string> repos, CancellationToken cancellationToken)
    {
        var list = repos.Take(MaxRepos).ToList();
        var results = new RepoActivity[list.Count];
        using var gate = new SemaphoreSlim(MaxParallelRepos);
        var tasks = list.Select(async (repo, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                results[index] = await ReadRepoAsync(repo, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<RepoActivity> ReadRepoAsync(string repo, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var (owner, name) = GitHubNames.SplitRepo(repo);
            var baseUrl = RepoUrl(owner, name);

            var info = await _rest.GetAsync(baseUrl, null, ct).ConfigureAwait(false);
            var branch = PipelineJson.DefaultBranchOf(info.Json) ?? "main";
            var encodedBranch = Uri.EscapeDataString(branch);

            var runsTask = ReadOptionalAsync(baseUrl + "/actions/runs?branch=" + encodedBranch + "&per_page=30", ct);
            var deploymentsTask = ReadOptionalAsync(baseUrl + "/deployments?per_page=40", ct);
            var releaseTask = ReadOptionalAsync(baseUrl + "/releases/latest", ct);
            await Task.WhenAll(runsTask, deploymentsTask, releaseTask).ConfigureAwait(false);

            var runs = PipelineJson.ParseWorkflowRuns(await runsTask.ConfigureAwait(false));
            var latest = RunSelection.LatestPerEnvironment(PipelineJson.ParseDeployments(await deploymentsTask.ConfigureAwait(false)))
                .Take(MaxEnvironments)
                .ToList();
            var statusTasks = latest.Select(d => ReadOptionalAsync(
                baseUrl + "/deployments/" + d.Id.ToString(CultureInfo.InvariantCulture) + "/statuses?per_page=1", ct)).ToList();
            var statuses = await Task.WhenAll(statusTasks).ConfigureAwait(false);
            var deployments = latest.Select((d, i) => PipelineJson.WithStatus(d, statuses[i])).ToList();

            var release = PipelineJson.ParseRelease(await releaseTask.ConfigureAwait(false));

            var successes = deployments.Where(d => d.State == DeployState.Success && PipelineJson.IsSha(d.Sha)).ToList();
            var deployed = successes.Where(d => RunSelection.IsProdLike(d.Environment)).OrderByDescending(d => d.CreatedAt).FirstOrDefault()
                ?? successes.OrderByDescending(d => d.CreatedAt).FirstOrDefault();

            var sinceRelease = release is null
                ? Task.FromResult<int?>(null)
                : AheadAsync(baseUrl + "/compare/" + Uri.EscapeDataString(release.Tag) + "..." + encodedBranch, ct);
            var sinceDeploy = deployed is null
                ? Task.FromResult<int?>(null)
                : AheadAsync(baseUrl + "/compare/" + deployed.Sha + "..." + encodedBranch, ct);
            await Task.WhenAll(sinceRelease, sinceDeploy).ConfigureAwait(false);

            return new RepoActivity(repo, branch, runs, deployments, release,
                await sinceRelease.ConfigureAwait(false), await sinceDeploy.ConfigureAwait(false), now);
        }
        catch (Exception ex) when (ex is GitHubException or HttpRequestException or ArgumentException)
        {
            return RepoActivity.Failed(repo, ErrorText.Describe(ex), now);
        }
    }

    private async Task<int?> AheadAsync(string url, CancellationToken ct)
    {
        var answer = await ReadOptionalAsync(url, ct).ConfigureAwait(false);
        return PipelineJson.AheadBy(answer);
    }

    /// <summary>A GET whose failure is not fatal: GitHub errors (404, 403, ...) give null. Cancellation still propagates.</summary>
    private async Task<System.Text.Json.JsonElement?> ReadOptionalAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _rest.GetAsync(url, null, ct).ConfigureAwait(false);
            return response.Json;
        }
        catch (GitHubApiException ex) when (!ex.IsRateLimited)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ writes

    public async Task<ActionOutcome> RerunFailedJobsAsync(string repo, IReadOnlyList<long> runIds, CancellationToken cancellationToken)
    {
        if (runIds.Count == 0) return new ActionOutcome(false, false, "There are no failed workflow runs to re-run.");
        try
        {
            var sent = false;
            foreach (var runId in runIds)
            {
                var result = await _writer.RerunFailedJobsAsync(repo, runId, DryRun, cancellationToken).ConfigureAwait(false);
                sent |= result.Sent;
            }

            return sent
                ? new ActionOutcome(true, false, "Re-running the failed jobs of " + PipelineFormat.Count(runIds.Count, "workflow run") + ".")
                : new ActionOutcome(true, true, "Dry run: nothing was sent to GitHub.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionOutcome(false, false, ErrorText.Describe(ex));
        }
    }

    public async Task<ActionOutcome> ReplyAsync(PullRequest pr, string body, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _writer.CommentAsync(pr.Repo, pr.Number, body, DryRun, cancellationToken).ConfigureAwait(false);
            return result.Sent
                ? new ActionOutcome(true, false, "Comment posted on " + pr.Repo + " #" + pr.Number + ".")
                : new ActionOutcome(true, true, "Dry run: nothing was sent to GitHub.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionOutcome(false, false, ErrorText.Describe(ex));
        }
    }

    private static string RepoUrl(string owner, string name) => GitHubRest.ApiBase + "/repos/" + owner + "/" + name;
}

/// <summary>Merges through <see cref="GitHubWriter.MergeAsync"/>: reads the head sha first and merges with it, so GitHub refuses if the branch moved.</summary>
public sealed class GitHubMergeActions : IMergeActions
{
    private readonly GitHubWriter _writer;

    public GitHubMergeActions(GitHubWriter writer, bool dryRun = true)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        DryRun = dryRun;
    }

    /// <summary>When true the merge request is built and checked but not sent.</summary>
    public bool DryRun { get; set; }

    public bool IsDryRun => DryRun;

    public async Task<ActionOutcome> MergeAsync(PullRequest pr, MergeMethodChoice method, CancellationToken cancellationToken)
    {
        try
        {
            var mapped = method switch
            {
                MergeMethodChoice.Squash => GitHubMergeMethod.Squash,
                MergeMethodChoice.Rebase => GitHubMergeMethod.Rebase,
                _ => GitHubMergeMethod.Merge,
            };
            var result = await _writer.MergeAsync(pr.Repo, pr.Number, mapped, DryRun, cancellationToken).ConfigureAwait(false);
            return result.Sent
                ? new ActionOutcome(true, false, "Merged " + pr.Repo + " #" + pr.Number + " (" + MergeMethods.Short(method).ToLowerInvariant() + ").")
                : new ActionOutcome(true, true, "Dry run: nothing was merged.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionOutcome(false, false, ErrorText.Describe(ex));
        }
    }
}
