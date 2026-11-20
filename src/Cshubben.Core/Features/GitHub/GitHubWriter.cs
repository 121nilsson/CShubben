using System.Text.Json;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

/// <summary>
/// Thin executors for the write actions. Every method has a dryRun flag: the request is built and validated
/// exactly as for real, but nothing is sent. Writes are never retried.
/// </summary>
public sealed class GitHubWriter
{
    private readonly GitHubRest _rest;

    public GitHubWriter(GitHubRest rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        _rest = rest;
    }

    public Task<WriteResult> CommentAsync(string repo, int number, string body, bool dryRun, CancellationToken cancellationToken = default) =>
        SendAsync(GitHubActions.CommentRequest(repo, number, body), dryRun, cancellationToken);

    public Task<WriteResult> SubmitReviewAsync(string repo, int number, ReviewVerdict verdict, string? body, bool dryRun, CancellationToken cancellationToken = default) =>
        SendAsync(GitHubActions.ReviewRequest(repo, number, verdict, body), dryRun, cancellationToken);

    /// <summary>Marks a draft PR as ready for review. Takes the GraphQL node id (PullRequest.Id).</summary>
    public Task<WriteResult> MarkReadyAsync(string pullRequestNodeId, bool dryRun, CancellationToken cancellationToken = default) =>
        SendAsync(GitHubActions.MarkReadyRequest(pullRequestNodeId), dryRun, cancellationToken);

    public Task<WriteResult> RerunFailedJobsAsync(string repo, long runId, bool dryRun, CancellationToken cancellationToken = default) =>
        SendAsync(GitHubActions.RerunFailedJobsRequest(repo, runId), dryRun, cancellationToken);

    /// <summary>
    /// Two steps: read the head sha (a harmless GET, also done in a dry run), then merge with that sha.
    /// In a dry run the merge request is returned but not sent.
    /// </summary>
    public async Task<WriteResult> MergeAsync(string repo, int number, GitHubMergeMethod method, bool dryRun, CancellationToken cancellationToken = default)
    {
        var headRequest = GitHubActions.FetchHeadShaRequest(repo, number);
        var head = await _rest.GetAsync(headRequest.Url, null, cancellationToken).ConfigureAwait(false);
        var sha = GitHubActions.ParseHeadSha(head.Json);
        if (sha is null) throw new GitHubApiException(head.Status, "Could not read the head commit of the pull request.");

        var result = await SendAsync(GitHubActions.MergeRequest(repo, number, method, sha), dryRun, cancellationToken).ConfigureAwait(false);
        if (result.Sent)
        {
            var merged = result.Response is not null && J.Bool(result.Response, "merged");
            if (!merged) throw new GitHubApiException(200, "GitHub did not merge the pull request.");
        }
        return result;
    }

    /// <summary>Sends a built request unless dryRun. GraphQL answers are checked for errors.</summary>
    public async Task<WriteResult> SendAsync(WriteRequest request, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (dryRun) return new WriteResult(false, request, null);
        var response = await _rest.SendAsync(request.Method, request.Url, request.JsonBody, false, cancellationToken).ConfigureAwait(false);
        if (request.Url == GitHubGraphQl.Endpoint && response.Json is not null)
        {
            GitHubGraphQl.ExtractData(response.Json.Value, false);
        }
        return new WriteResult(true, request, response.Json);
    }
}
