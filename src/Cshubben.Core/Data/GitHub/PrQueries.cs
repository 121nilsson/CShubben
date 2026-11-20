using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cshubben.Core.Data.GitHub;

public sealed record TitleAlias(string Alias, string Repo, int Number);

public sealed record TitlesQuery(string Query, IReadOnlyList<TitleAlias> Aliases);

public sealed record PrTitle(string Repo, int Number, string Title);

/// <summary>
/// GraphQL documents and request builders (pure, no network).
/// The list query only asks for cheap fields. CI, review decision and threads come from a second query by node id.
/// The slow `mergeable` field is kept out of the list query and only used by <see cref="EnrichWithMergeableQuery"/>.
/// </summary>
public static class PrQueries
{
    public const string AuthoredSearch = "is:pr is:open author:@me archived:false";

    public const string ReviewSearch = "is:pr is:open review-requested:@me archived:false";

    /// <summary>Appended to the search string when listing.</summary>
    public const string SortSuffix = " sort:updated-desc";

    public const string ViewerQuery = "query { viewer { login } }";

    public const string ListQuery = """
        query($q: String!, $n: Int!, $after: String) {
          rateLimit { cost remaining limit resetAt }
          search(query: $q, type: ISSUE, first: $n, after: $after) {
            pageInfo { hasNextPage endCursor }
            nodes {
              ... on PullRequest {
                id number title url body isDraft createdAt updatedAt headRefName
                additions deletions changedFiles
                author { login }
                repository { nameWithOwner }
                comments { totalCount }
              }
            }
          }
        }
        """;

    public const string EnrichQuery = """
        query($ids: [ID!]!) {
          nodes(ids: $ids) {
            ... on PullRequest {
              id
              reviewDecision
              reviewThreads(first: 50) { nodes { isResolved } }
              commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
            }
          }
        }
        """;

    public const string EnrichWithMergeableQuery = """
        query($ids: [ID!]!) {
          nodes(ids: $ids) {
            ... on PullRequest {
              id
              mergeable
              reviewDecision
              reviewThreads(first: 50) { nodes { isResolved } }
              commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
            }
          }
        }
        """;

    public const string ReviewInfoQuery = """
        query($ids: [ID!]!) {
          nodes(ids: $ids) {
            ... on PullRequest {
              id
              reviewRequests(first: 20) { nodes { requestedReviewer { __typename ... on User { login } ... on Team { name } } } }
              timelineItems(last: 30, itemTypes: [REVIEW_REQUESTED_EVENT]) {
                nodes { ... on ReviewRequestedEvent { createdAt requestedReviewer { __typename ... on User { login } ... on Team { name } } } }
              }
              latestReviews(first: 30) { nodes { author { login } state submittedAt } }
              commits(last: 1) { nodes { commit { committedDate } } }
            }
          }
        }
        """;

    public static Dictionary<string, object?> ListVariables(string search, int pageSize, string? after) =>
        new Dictionary<string, object?>
        {
            ["q"] = search + SortSuffix,
            ["n"] = pageSize,
            ["after"] = after,
        };

    public static Dictionary<string, object?> IdsVariables(IEnumerable<string> ids) =>
        new Dictionary<string, object?> { ["ids"] = ids.ToArray() };

    /// <summary>The JSON body of a GraphQL POST.</summary>
    public static string BuildBody(string query, IReadOnlyDictionary<string, object?>? variables)
    {
        var vars = new Dictionary<string, object?>();
        if (variables is not null)
        {
            foreach (var pair in variables) vars[pair.Key] = pair.Value;
        }
        var body = new Dictionary<string, object?> { ["query"] = query, ["variables"] = vars };
        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// One aliased query that looks up many PR titles at once. Entries with unsafe names
    /// or a non-positive number are left out, because owner and name are placed inside the query text.
    /// </summary>
    public static TitlesQuery BuildTitlesQuery(IReadOnlyList<(string Repo, int Number)> items)
    {
        var sb = new StringBuilder("query {\n");
        var aliases = new List<TitleAlias>();
        for (var i = 0; i < items.Count; i++)
        {
            var (repo, number) = items[i];
            if (number <= 0 || !GitHubNames.TrySplitRepo(repo, out var owner, out var name)) continue;
            var alias = "p" + aliases.Count.ToString(CultureInfo.InvariantCulture);
            sb.Append(alias).Append(": repository(owner: \"").Append(owner).Append("\", name: \"").Append(name)
                .Append("\") { issueOrPullRequest(number: ").Append(number.ToString(CultureInfo.InvariantCulture))
                .Append(") { ... on PullRequest { title } ... on Issue { title } } }\n");
            aliases.Add(new TitleAlias(alias, repo, number));
        }
        sb.Append('}');
        return new TitlesQuery(sb.ToString(), aliases);
    }

    /// <summary>Reads the answer of a titles query. Missing or deleted items are skipped.</summary>
    public static List<PrTitle> ParseTitles(JsonElement data, IReadOnlyList<TitleAlias> aliases)
    {
        var result = new List<PrTitle>();
        foreach (var a in aliases)
        {
            var title = J.StrOrNull(data, a.Alias, "issueOrPullRequest", "title");
            if (!string.IsNullOrEmpty(title)) result.Add(new PrTitle(a.Repo, a.Number, title));
        }
        return result;
    }
}
