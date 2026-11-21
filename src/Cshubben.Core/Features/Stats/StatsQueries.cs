using System.Globalization;

namespace Cshubben.Core.Features.Stats;

/// <summary>GraphQL documents and search strings for the history behind the Stats page (pure, no network).</summary>
public static class StatsQueries
{
    public const int PageSize = 50;

    public const int MaxPages = 4;

    /// <summary>Merged pull requests I opened, merged on or after <paramref name="since"/>.</summary>
    public static string MergedSearch(DateTimeOffset since) =>
        "is:pr is:merged author:@me archived:false merged:>=" + Day(since) + " sort:updated-desc";

    /// <summary>Pull requests by others that I reviewed, updated on or after <paramref name="since"/>.</summary>
    public static string ReviewedSearch(DateTimeOffset since) =>
        "is:pr reviewed-by:@me -author:@me archived:false updated:>=" + Day(since) + " sort:updated-desc";

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public const string MergedQuery = """
        query($q: String!, $n: Int!, $after: String) {
          search(query: $q, type: ISSUE, first: $n, after: $after) {
            pageInfo { hasNextPage endCursor }
            nodes {
              ... on PullRequest {
                number title createdAt mergedAt additions deletions changedFiles
                author { login }
                repository { nameWithOwner }
                reviews(first: 5) { nodes { submittedAt state author { login } } }
              }
            }
          }
        }
        """;

    public const string ReviewedQuery = """
        query($q: String!, $n: Int!, $after: String, $login: String!) {
          search(query: $q, type: ISSUE, first: $n, after: $after) {
            pageInfo { hasNextPage endCursor }
            nodes {
              ... on PullRequest {
                number
                repository { nameWithOwner }
                reviews(first: 30, author: $login) { nodes { submittedAt state } }
              }
            }
          }
        }
        """;

    public static IReadOnlyDictionary<string, object?> Variables(string search, int pageSize, string? after, string? login = null)
    {
        var vars = new Dictionary<string, object?> { ["q"] = search, ["n"] = pageSize, ["after"] = after };
        if (login is not null) vars["login"] = login;
        return vars;
    }
}
