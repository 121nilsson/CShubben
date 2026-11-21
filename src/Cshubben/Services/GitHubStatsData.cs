using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Stats;

namespace Cshubben.Services;

/// <summary>
/// Reads the history behind the Stats page from GitHub: my merged pull requests (with first review and size) and the
/// reviews I gave. Read-only. Search strings are built from dates only; nothing untrusted reaches a query.
/// </summary>
public sealed class GitHubStatsData
{
    private readonly GitHubGraphQl _graphql;
    private readonly string _viewer;

    public GitHubStatsData(GitHubGraphQl graphql, string viewer)
    {
        ArgumentNullException.ThrowIfNull(graphql);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewer);
        _graphql = graphql;
        _viewer = viewer;
    }

    public async Task<StatsInput> LoadAsync(int weeks, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var since = StatsCalculator.WeekStarts(now, weeks)[0];

        var mergedTask = ReadAsync(StatsQueries.MergedQuery, StatsQueries.MergedSearch(since), null, StatsMapper.ParseMerged, cancellationToken);
        var reviewedTask = ReadAsync(StatsQueries.ReviewedQuery, StatsQueries.ReviewedSearch(since), _viewer, StatsMapper.ParseReviewed, cancellationToken);
        var merged = await mergedTask.ConfigureAwait(false);
        var reviewed = await reviewedTask.ConfigureAwait(false);

        var mergedUnique = merged
            .Where(m => m.MergedAt >= since)
            .GroupBy(m => (m.Repo.ToLowerInvariant(), m.Number))
            .Select(g => g.First())
            .ToList();
        var reviews = reviewed.Where(r => r.At >= since).ToList();
        return new StatsInput(mergedUnique, reviews);
    }

    private async Task<List<T>> ReadAsync<T>(
        string query,
        string search,
        string? login,
        Func<System.Text.Json.JsonElement, (StatsPageInfo Page, IReadOnlyList<T> Items)> parse,
        CancellationToken cancellationToken)
    {
        var all = new List<T>();
        string? after = null;
        for (var page = 0; page < StatsQueries.MaxPages; page++)
        {
            var data = await _graphql.QueryAsync(query, StatsQueries.Variables(search, StatsQueries.PageSize, after, login), true, cancellationToken)
                .ConfigureAwait(false);
            var (info, items) = parse(data);
            all.AddRange(items);
            if (!info.HasNextPage || info.EndCursor is null) break;
            after = info.EndCursor;
        }
        return all;
    }
}
