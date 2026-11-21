using Cshubben.Core.Features.Digest;
using Cshubben.Core.Features.GitHub;

namespace Cshubben.Services;

/// <summary>
/// Reads the merged pull requests behind an activity summary. Read-only. Search strings are built from repository names that
/// passed a strict pattern and from dates, so nothing untrusted reaches a query.
/// </summary>
public sealed class GitHubDigestData(GitHubGraphQl graphql)
{
    /// <summary>Upper bound on requests for one summary, so a year over many repos cannot run away.</summary>
    private const int MaxRequests = 120;

    public async Task<DigestInput> LoadAsync(DigestPeriod period, IReadOnlyList<string> repos, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repos);
        var (from, to) = DigestRange.For(period, now);
        var chunks = DigestQueries.ChunkRepos(repos);
        var found = new Dictionary<(string, int), MergedPr>();
        var truncated = false;
        var requests = 0;

        foreach (var slice in DigestQueries.Slices(period, from, to))
        {
            foreach (var chunk in chunks)
            {
                var search = DigestQueries.Search(chunk, slice.From, slice.To);
                string? after = null;
                for (var page = 0; page < DigestQueries.MaxPagesPerSearch; page++)
                {
                    if (requests++ >= MaxRequests) { truncated = true; break; }
                    var data = await graphql.QueryAsync(DigestQueries.Query, DigestQueries.Variables(search, DigestQueries.PageSize, after), true, cancellationToken)
                        .ConfigureAwait(false);
                    var (info, items) = DigestMapper.Parse(data);
                    foreach (var pr in items) found[(pr.Repo.ToLowerInvariant(), pr.Number)] = pr;
                    if (!info.HasNextPage || info.EndCursor is null) break;
                    after = info.EndCursor;
                    if (page == DigestQueries.MaxPagesPerSearch - 1) truncated = true;
                }
            }
        }

        var prs = found.Values.OrderBy(p => p.MergedAt).ToList();
        return new DigestInput(period, from, to, chunks.SelectMany(c => c).ToList(), prs, truncated);
    }
}
