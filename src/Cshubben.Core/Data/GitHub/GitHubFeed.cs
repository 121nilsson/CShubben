using System.Collections.Concurrent;
using System.Text.Json;
using Cshubben.Core.Model;

namespace Cshubben.Core.Data.GitHub;

public sealed record FeedResult(IReadOnlyList<FeedItem> Items, IReadOnlyList<string> Errors);

/// <summary>
/// Activity feed from REST repo events. At most eight repos in flight, one ETag per repo kept in memory
/// (a 304 answer reuses the cached events and does not count against the rate limit),
/// PR titles missing from event payloads are looked up with one aliased GraphQL call per 30 PRs and cached.
/// </summary>
public sealed class GitHubFeed
{
    public const int DefaultMaxParallel = 8;
    public const int TitleChunkSize = 30;
    public const int MaxItems = 300;

    private readonly GitHubRest _rest;
    private readonly GitHubGraphQl _gql;
    private readonly int _maxParallel;
    private readonly ConcurrentDictionary<string, string> _etags = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, JsonElement> _events = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _titles = new(StringComparer.Ordinal);

    public GitHubFeed(GitHubRest rest, GitHubGraphQl gql, int maxParallel = DefaultMaxParallel)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(gql);
        _rest = rest;
        _gql = gql;
        _maxParallel = Math.Clamp(maxParallel, 1, DefaultMaxParallel);
    }

    /// <summary>Remember a known PR title so no lookup is needed for it.</summary>
    public void SeedTitle(string repo, int number, string title)
    {
        if (!string.IsNullOrEmpty(title)) _titles[GitHubNames.PrKey(repo, number)] = title;
    }

    /// <summary>Loads events for the given repos. Per-repo failures end up in Errors; the other repos still count.</summary>
    public async Task<FeedResult> FetchAsync(
        IEnumerable<string> repos,
        string viewer,
        IReadOnlySet<string> myPrKeys,
        CancellationToken cancellationToken = default)
    {
        var errors = new ConcurrentQueue<string>();
        var items = new ConcurrentQueue<FeedItem>();
        using (var gate = new SemaphoreSlim(_maxParallel))
        {
            var tasks = repos.Distinct(StringComparer.OrdinalIgnoreCase).Select(async repo =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await FetchRepoAsync(repo, viewer, myPrKeys, items, errors, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        var collapsed = FeedFilter.Collapse(items).Take(MaxItems).ToList();
        var withTitles = await ResolveTitlesAsync(collapsed, errors, cancellationToken).ConfigureAwait(false);
        return new FeedResult(withTitles, errors.ToList());
    }

    private async Task FetchRepoAsync(
        string repo,
        string viewer,
        IReadOnlySet<string> myPrKeys,
        ConcurrentQueue<FeedItem> items,
        ConcurrentQueue<string> errors,
        CancellationToken ct)
    {
        if (!GitHubNames.TrySplitRepo(repo, out var owner, out var name))
        {
            errors.Enqueue("Skipped invalid repository name: " + repo);
            return;
        }

        var key = repo.ToLowerInvariant();
        _etags.TryGetValue(key, out var etag);
        try
        {
            var url = GitHubRest.ApiBase + "/repos/" + owner + "/" + name + "/events?per_page=50";
            var response = await _rest.GetAsync(url, etag, ct).ConfigureAwait(false);

            JsonElement? events = null;
            if (response.NotModified)
            {
                if (_events.TryGetValue(key, out var cached)) events = cached;
            }
            else if (response.Json is not null)
            {
                events = response.Json;
                _events[key] = response.Json.Value;
                if (response.ETag is not null) _etags[key] = response.ETag;
                else _etags.TryRemove(key, out _);
            }

            foreach (var ev in J.Items(events))
            {
                var item = FeedMapper.Map(repo, ev, viewer, myPrKeys);
                if (item is not null) items.Enqueue(item);
            }
        }
        catch (Exception ex) when (Recoverable.Is(ex, ct))
        {
            errors.Enqueue("Could not load activity for " + repo + ": " + ex.Message);
        }
    }

    private async Task<List<FeedItem>> ResolveTitlesAsync(List<FeedItem> items, ConcurrentQueue<string> errors, CancellationToken ct)
    {
        var wanted = new List<(string Repo, int Number)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Title is not null || item.Number is null) continue;
            var key = GitHubNames.PrKey(item.Repo, item.Number.Value);
            if (_titles.ContainsKey(key) || !seen.Add(key)) continue;
            wanted.Add((item.Repo, item.Number.Value));
        }

        foreach (var chunk in wanted.Chunk(TitleChunkSize))
        {
            var titlesQuery = PrQueries.BuildTitlesQuery(chunk);
            if (titlesQuery.Aliases.Count == 0) continue;
            try
            {
                var data = await _gql.QueryAsync(titlesQuery.Query, null, true, ct).ConfigureAwait(false);
                foreach (var t in PrQueries.ParseTitles(data, titlesQuery.Aliases))
                {
                    _titles[GitHubNames.PrKey(t.Repo, t.Number)] = t.Title;
                }
            }
            catch (Exception ex) when (Recoverable.Is(ex, ct))
            {
                errors.Enqueue("Could not look up PR titles: " + ex.Message);
            }
        }

        var result = new List<FeedItem>(items.Count);
        foreach (var item in items)
        {
            if (item.Title is null && item.Number is not null
                && _titles.TryGetValue(GitHubNames.PrKey(item.Repo, item.Number.Value), out var title))
            {
                result.Add(item with { Title = title });
            }
            else
            {
                result.Add(item);
            }
        }
        return result;
    }
}
