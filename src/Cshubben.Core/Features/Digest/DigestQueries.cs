using System.Globalization;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Digest;

/// <summary>GraphQL document and search strings for merged pull requests across repos (pure, no network).</summary>
public static class DigestQueries
{
    public const int PageSize = 100;

    /// <summary>GitHub search returns at most 1000 results per query, which is 10 pages of 100.</summary>
    public const int MaxPagesPerSearch = 10;

    /// <summary>GitHub rejects search queries longer than 256 characters; this leaves room for the other qualifiers.</summary>
    public const int RepoBudget = 170;

    private static readonly Regex RepoPattern = new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?/[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);

    public const string Query = """
        query($q: String!, $n: Int!, $after: String) {
          search(query: $q, type: ISSUE, first: $n, after: $after) {
            pageInfo { hasNextPage endCursor }
            nodes {
              ... on PullRequest {
                number title url mergedAt additions deletions changedFiles
                author { login }
                repository { nameWithOwner }
              }
            }
          }
        }
        """;

    /// <summary>Splits repos into groups whose "repo:" qualifiers fit the query length limit. Names that are not owner/name are skipped.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ChunkRepos(IEnumerable<string> repos, int budget = RepoBudget)
    {
        var chunks = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        var length = 0;
        foreach (var repo in repos.Where(r => RepoPattern.IsMatch(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cost = repo.Length + 6; // "repo:" plus a space
            if (current.Count > 0 && length + cost > budget)
            {
                chunks.Add(current);
                current = new List<string>();
                length = 0;
            }

            current.Add(repo);
            length += cost;
        }

        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    /// <summary>The time slices to search separately. A year is cut into months so no slice reaches the 1000 result cap too easily.</summary>
    public static IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> Slices(DigestPeriod period, DateTimeOffset from, DateTimeOffset to)
    {
        if (period != DigestPeriod.Year) return new[] { (from, to) };
        var slices = new List<(DateTimeOffset, DateTimeOffset)>();
        var start = from;
        while (start < to)
        {
            var next = new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, start.Offset).AddMonths(1);
            if (next > to) next = to;
            slices.Add((start, next));
            start = next;
        }

        return slices;
    }

    /// <summary>Merged between <paramref name="from"/> (included) and <paramref name="to"/> (excluded in practice: GitHub treats the end as inclusive, so one second is taken off).</summary>
    public static string Search(IReadOnlyList<string> repos, DateTimeOffset from, DateTimeOffset to)
    {
        var qualifiers = string.Join(" ", repos.Select(r => "repo:" + r));
        return qualifiers + " is:pr is:merged merged:" + Stamp(from) + ".." + Stamp(to.AddSeconds(-1));
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static IReadOnlyDictionary<string, object?> Variables(string search, int pageSize, string? after) =>
        new Dictionary<string, object?> { ["q"] = search, ["n"] = pageSize, ["after"] = after };
}
