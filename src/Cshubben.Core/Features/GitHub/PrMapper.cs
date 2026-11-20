using System.Text.Json;
using Cshubben.Core.Model;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

/// <summary>One page of a search.</summary>
public sealed record SearchPage(IReadOnlyList<PullRequest> Items, bool HasNextPage, string? EndCursor);

/// <summary>Pure mappers from GitHub GraphQL JSON to <see cref="PullRequest"/>.</summary>
public static class PrMapper
{
    /// <summary>Maps one search node. Returns null for hits that are not pull requests (empty objects).</summary>
    public static PullRequest? FromListNode(JsonElement node, PrRole role)
    {
        var id = J.Str(node, "id");
        if (id.Length == 0) return null;
        var author = J.Str(node, "author", "login");
        return new PullRequest(
            Id: id,
            Role: role,
            Repo: J.Str(node, "repository", "nameWithOwner"),
            Number: J.Int(node, "number"),
            Title: J.Str(node, "title"),
            Url: J.Str(node, "url"),
            Author: author.Length == 0 ? "ghost" : author,
            IsDraft: J.Bool(node, "isDraft"),
            CreatedAt: J.Time(node, "createdAt") ?? DateTimeOffset.UnixEpoch,
            UpdatedAt: J.Time(node, "updatedAt") ?? DateTimeOffset.UnixEpoch,
            Additions: J.Int(node, "additions"),
            Deletions: J.Int(node, "deletions"),
            ChangedFiles: J.Int(node, "changedFiles"),
            Review: ReviewDecision.None,
            Ci: CiState.Unknown,
            Merge: ParseMerge(J.StrOrNull(node, "mergeable")),
            Comments: J.Int(node, "comments", "totalCount"),
            UnresolvedThreads: 0,
            HeadBranch: J.Str(node, "headRefName"),
            Description: J.Str(node, "body"));
    }

    /// <summary>Reads data.search: items, hasNextPage and the cursor for the next page.</summary>
    public static SearchPage ParseSearchPage(JsonElement data, PrRole role)
    {
        var items = new List<PullRequest>();
        foreach (var node in J.Items(data, "search", "nodes"))
        {
            var pr = FromListNode(node, role);
            if (pr is not null) items.Add(pr);
        }
        var hasNext = J.Bool(data, "search", "pageInfo", "hasNextPage");
        var cursor = J.StrOrNull(data, "search", "pageInfo", "endCursor");
        return new SearchPage(items, hasNext, cursor);
    }

    /// <summary>Indexes data.nodes by id. Null nodes and nodes without id are skipped.</summary>
    public static Dictionary<string, JsonElement> IndexNodesById(JsonElement data)
    {
        var map = new Dictionary<string, JsonElement>();
        foreach (var node in J.Items(data, "nodes"))
        {
            var id = J.Str(node, "id");
            if (id.Length > 0) map[id] = node;
        }
        return map;
    }

    /// <summary>Adds review decision, CI state, unresolved threads (and mergeable when present) from an enrichment node.</summary>
    public static PullRequest ApplyEnrichment(PullRequest pr, JsonElement node)
    {
        var merge = pr.Merge;
        var mergeable = J.StrOrNull(node, "mergeable");
        if (mergeable is not null) merge = ParseMerge(mergeable);

        var threads = 0;
        foreach (var t in J.Items(node, "reviewThreads", "nodes"))
        {
            if (!J.Bool(t, "isResolved")) threads++;
        }

        var commits = J.Items(node, "commits", "nodes");
        var ci = CiState.Unknown;
        if (commits.Count > 0) ci = ParseCi(J.StrOrNull(commits[0], "commit", "statusCheckRollup", "state"));

        return pr with
        {
            Review = ParseReview(J.StrOrNull(node, "reviewDecision")),
            Ci = ci,
            Merge = merge,
            UnresolvedThreads = threads,
        };
    }

    public static ReviewDecision ParseReview(string? value) => value switch
    {
        "APPROVED" => ReviewDecision.Approved,
        "CHANGES_REQUESTED" => ReviewDecision.ChangesRequested,
        "REVIEW_REQUIRED" => ReviewDecision.ReviewRequired,
        _ => ReviewDecision.None,
    };

    public static CiState ParseCi(string? value) => value switch
    {
        "SUCCESS" => CiState.Success,
        "FAILURE" or "ERROR" => CiState.Failure,
        "PENDING" or "EXPECTED" => CiState.Pending,
        _ => CiState.Unknown,
    };

    public static MergeState ParseMerge(string? value) => value switch
    {
        "MERGEABLE" => MergeState.Mergeable,
        "CONFLICTING" => MergeState.Conflicting,
        _ => MergeState.Unknown,
    };
}
