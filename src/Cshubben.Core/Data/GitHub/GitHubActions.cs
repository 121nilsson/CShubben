using System.Globalization;
using System.Text.Json;

namespace Cshubben.Core.Data.GitHub;

public enum ReviewVerdict { Approve, RequestChanges, Comment }

public enum GitHubMergeMethod { Merge, Squash, Rebase }

/// <param name="Method">HTTP method.</param>
/// <param name="Url">Absolute https://api.github.com URL.</param>
/// <param name="JsonBody">JSON body, or null for GET.</param>
public sealed record WriteRequest(HttpMethod Method, string Url, string? JsonBody);

/// <summary>What happened. Sent is false for a dry run. Response is the parsed answer when something was sent.</summary>
public sealed record WriteResult(bool Sent, WriteRequest Request, JsonElement? Response);

/// <summary>Pure builders for write requests. Everything is validated here, before any network use.</summary>
public static class GitHubActions
{
    public const int MaxTextLength = 65536;

    public static WriteRequest CommentRequest(string repo, int number, string body)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        GitHubNames.CheckNumber(number);
        var text = CleanText(body, true);
        return new WriteRequest(
            HttpMethod.Post,
            RepoUrl(owner, name, "/issues/" + Num(number) + "/comments"),
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["body"] = text }));
    }

    /// <summary>Approve may have no text; request changes and comment need text.</summary>
    public static WriteRequest ReviewRequest(string repo, int number, ReviewVerdict verdict, string? body)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        GitHubNames.CheckNumber(number);
        var text = CleanText(body, verdict != ReviewVerdict.Approve);
        var json = new Dictionary<string, object?> { ["event"] = EventName(verdict) };
        if (text.Length > 0) json["body"] = text;
        return new WriteRequest(
            HttpMethod.Post,
            RepoUrl(owner, name, "/pulls/" + Num(number) + "/reviews"),
            JsonSerializer.Serialize(json));
    }

    /// <summary>GraphQL mutation. The id travels as a variable, never inside the query text.</summary>
    public static WriteRequest MarkReadyRequest(string pullRequestNodeId)
    {
        if (!GitHubNames.IsNodeId(pullRequestNodeId)) throw new ArgumentException("Invalid pull request id.", nameof(pullRequestNodeId));
        var body = PrQueries.BuildBody(
            "mutation($id: ID!) { markPullRequestReadyForReview(input:{pullRequestId:$id}) { pullRequest { isDraft } } }",
            new Dictionary<string, object?> { ["id"] = pullRequestNodeId });
        return new WriteRequest(HttpMethod.Post, GitHubGraphQl.Endpoint, body);
    }

    public static WriteRequest RerunFailedJobsRequest(string repo, long runId)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId), "Invalid workflow run id.");
        return new WriteRequest(
            HttpMethod.Post,
            RepoUrl(owner, name, "/actions/runs/" + runId.ToString(CultureInfo.InvariantCulture) + "/rerun-failed-jobs"),
            "{}");
    }

    /// <summary>Step one of a merge: read the PR to learn its current head sha.</summary>
    public static WriteRequest FetchHeadShaRequest(string repo, int number)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        GitHubNames.CheckNumber(number);
        return new WriteRequest(HttpMethod.Get, RepoUrl(owner, name, "/pulls/" + Num(number)), null);
    }

    /// <summary>Reads head.sha from the PR answer; null when missing or not a plausible sha.</summary>
    public static string? ParseHeadSha(JsonElement? pullRequest)
    {
        var sha = J.StrOrNull(pullRequest, "head", "sha");
        return GitHubNames.IsCommitSha(sha) ? sha : null;
    }

    /// <summary>Step two: merge, with the sha so GitHub refuses if the branch moved since it was reviewed.</summary>
    public static WriteRequest MergeRequest(string repo, int number, GitHubMergeMethod method, string expectedHeadSha)
    {
        var (owner, name) = GitHubNames.SplitRepo(repo);
        GitHubNames.CheckNumber(number);
        if (!GitHubNames.IsCommitSha(expectedHeadSha)) throw new ArgumentException("Invalid commit sha.", nameof(expectedHeadSha));
        var json = new Dictionary<string, object?>
        {
            ["merge_method"] = method switch
            {
                GitHubMergeMethod.Squash => "squash",
                GitHubMergeMethod.Rebase => "rebase",
                _ => "merge",
            },
            ["sha"] = expectedHeadSha,
        };
        return new WriteRequest(HttpMethod.Put, RepoUrl(owner, name, "/pulls/" + Num(number) + "/merge"), JsonSerializer.Serialize(json));
    }

    private static string EventName(ReviewVerdict verdict) => verdict switch
    {
        ReviewVerdict.Approve => "APPROVE",
        ReviewVerdict.RequestChanges => "REQUEST_CHANGES",
        _ => "COMMENT",
    };

    private static string Num(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static string RepoUrl(string owner, string name, string suffix) =>
        GitHubRest.ApiBase + "/repos/" + owner + "/" + name + suffix;

    private static string CleanText(string? body, bool required)
    {
        var text = (body ?? string.Empty).Trim();
        if (required && text.Length == 0) throw new ArgumentException("Write some text first.", nameof(body));
        if (text.Length > MaxTextLength) throw new ArgumentException("The text is too long.", nameof(body));
        return text;
    }
}
