using System.Text.Json;
using Cshubben.Core.Model;

namespace Cshubben.Core.Data.GitHub;

/// <summary>Network side of the PR lists: paginated search, enrichment by node id, review infos.</summary>
public sealed class GitHubPullRequests
{
    public const int DefaultPageSize = 50;
    public const int DefaultMaxPages = 4;
    public const int ChunkSize = 20;
    public const int Parallelism = 4;

    private readonly GitHubGraphQl _gql;

    public GitHubPullRequests(GitHubGraphQl gql)
    {
        ArgumentNullException.ThrowIfNull(gql);
        _gql = gql;
    }

    private sealed record ChunkResult(JsonElement? Data, string? Error);

    public async Task<string> GetViewerAsync(CancellationToken cancellationToken = default)
    {
        var data = await _gql.QueryAsync(PrQueries.ViewerQuery, null, false, cancellationToken).ConfigureAwait(false);
        var login = J.Str(data, "viewer", "login");
        if (login.Length == 0) throw new GitHubApiException(200, "Could not determine the signed-in GitHub user.");
        return login;
    }

    /// <summary>
    /// One search with cursor pagination: follows endCursor while hasNextPage is true, at most maxPages pages.
    /// Use <see cref="PrQueries.AuthoredSearch"/> or <see cref="PrQueries.ReviewSearch"/>.
    /// </summary>
    public async Task<IReadOnlyList<PullRequest>> SearchAsync(
        string search,
        PrRole role,
        int pageSize = DefaultPageSize,
        int maxPages = DefaultMaxPages,
        CancellationToken cancellationToken = default)
    {
        var result = new List<PullRequest>();
        string? after = null;
        for (var page = 0; page < maxPages; page++)
        {
            var data = await _gql.QueryAsync(PrQueries.ListQuery, PrQueries.ListVariables(search, pageSize, after), false, cancellationToken)
                .ConfigureAwait(false);
            var parsed = PrMapper.ParseSearchPage(data, role);
            result.AddRange(parsed.Items);
            if (!parsed.HasNextPage || string.IsNullOrEmpty(parsed.EndCursor)) break;
            after = parsed.EndCursor;
        }
        return result;
    }

    /// <summary>
    /// Second step: CI, review decision, unresolved threads (and mergeable when asked) by node id.
    /// The returned list has the same order and length as the input. A failed chunk leaves its PRs
    /// un-enriched and adds an entry to Errors.
    /// </summary>
    public async Task<(IReadOnlyList<PullRequest> Prs, IReadOnlyList<string> Errors)> EnrichAsync(
        IReadOnlyList<PullRequest> prs,
        bool includeMergeable = false,
        CancellationToken cancellationToken = default)
    {
        var query = includeMergeable ? PrQueries.EnrichWithMergeableQuery : PrQueries.EnrichQuery;
        var chunks = prs.Select(p => p.Id).Distinct().Chunk(ChunkSize);
        var results = await RunChunksAsync(chunks, query, cancellationToken).ConfigureAwait(false);

        var errors = new List<string>();
        var byId = new Dictionary<string, JsonElement>();
        foreach (var r in results)
        {
            if (r.Error is not null) errors.Add("Could not load CI and review status: " + r.Error);
            if (r.Data is null) continue;
            foreach (var pair in PrMapper.IndexNodesById(r.Data.Value)) byId[pair.Key] = pair.Value;
        }

        var enriched = new List<PullRequest>(prs.Count);
        foreach (var pr in prs)
        {
            if (byId.TryGetValue(pr.Id, out var node)) enriched.Add(PrMapper.ApplyEnrichment(pr, node));
            else enriched.Add(pr);
        }
        return (enriched, errors);
    }

    /// <summary>Review info (viewer's latest review, newest commit, direct vs team request) for review-queue PRs.</summary>
    public async Task<(IReadOnlyDictionary<string, ReviewInfo> Infos, IReadOnlyList<string> Errors)> FetchReviewInfosAsync(
        IReadOnlyList<PullRequest> reviews,
        string viewer,
        CancellationToken cancellationToken = default)
    {
        var chunks = reviews.Select(p => p.Id).Distinct().Chunk(ChunkSize);
        var results = await RunChunksAsync(chunks, PrQueries.ReviewInfoQuery, cancellationToken).ConfigureAwait(false);

        var errors = new List<string>();
        var infos = new Dictionary<string, ReviewInfo>();
        foreach (var r in results)
        {
            if (r.Error is not null) errors.Add("Could not load review details: " + r.Error);
            if (r.Data is null) continue;
            foreach (var pair in ReviewInfoMapper.FromNodes(r.Data.Value, viewer)) infos[pair.Key] = pair.Value;
        }
        return (infos, errors);
    }

    private async Task<List<ChunkResult>> RunChunksAsync(IEnumerable<string[]> chunks, string query, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Parallelism);
        var tasks = chunks.Select(async chunk =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var data = await _gql.QueryAsync(query, PrQueries.IdsVariables(chunk), true, ct).ConfigureAwait(false);
                return new ChunkResult(data, null);
            }
            catch (Exception ex) when (Recoverable.Is(ex, ct))
            {
                return new ChunkResult(null, ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        var all = await Task.WhenAll(tasks).ConfigureAwait(false);
        return all.ToList();
    }
}
