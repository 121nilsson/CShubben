using Cshubben.Core.Model;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

public sealed class GitHubHubDataOptions
{
    /// <summary>Ask for the slow `mergeable` field in the enrichment step. Off by default.</summary>
    public bool IncludeMergeable { get; init; }

    public bool IncludeFeed { get; init; } = true;

    public int PageSize { get; init; } = GitHubPullRequests.DefaultPageSize;

    public int MaxPages { get; init; } = GitHubPullRequests.DefaultMaxPages;

    /// <summary>Skip the viewer lookup when the login is already known.</summary>
    public string? ViewerLogin { get; init; }
}

/// <summary>
/// The GitHub-backed <see cref="IHubData"/>. Failing to list PRs fails the load. Failing enrichment, review details or
/// the feed does not: the snapshot is returned with what worked and <see cref="LastWarnings"/> says what did not.
/// </summary>
public sealed class GitHubHubData : IHubData
{
    private readonly GitHubPullRequests _prs;
    private readonly GitHubFeed _feed;
    private readonly GitHubHubDataOptions _options;

    public GitHubHubData(GitHubRest rest, GitHubHubDataOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rest);
        _options = options ?? new GitHubHubDataOptions();
        var gql = new GitHubGraphQl(rest);
        Rest = rest;
        GraphQl = gql;
        _prs = new GitHubPullRequests(gql);
        _feed = new GitHubFeed(rest, gql);
        Writer = new GitHubWriter(rest);
    }

    public GitHubRest Rest { get; }

    public GitHubGraphQl GraphQl { get; }

    /// <summary>Write actions (comment, review, ready, re-run, merge) on the same connection.</summary>
    public GitHubWriter Writer { get; }

    /// <summary>Human-readable problems of the last load that did not stop it.</summary>
    public IReadOnlyList<string> LastWarnings { get; private set; } = Array.Empty<string>();

    /// <summary>Resolves the token (env, then gh CLI) and builds the data source.</summary>
    public static async Task<GitHubHubData> CreateAsync(
        HttpClient? http = null,
        ICommandRunner? runner = null,
        GitHubHubDataOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var token = await GitHubToken.ResolveAsync(runner, null, cancellationToken).ConfigureAwait(false);
        return new GitHubHubData(new GitHubRest(http ?? new HttpClient(), token), options);
    }

    public async Task<HubSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        var viewer = _options.ViewerLogin ?? string.Empty;
        if (viewer.Length == 0) viewer = await _prs.GetViewerAsync(cancellationToken).ConfigureAwait(false);

        var authoredTask = _prs.SearchAsync(PrQueries.AuthoredSearch, PrRole.Authored, _options.PageSize, _options.MaxPages, cancellationToken);
        var reviewTask = _prs.SearchAsync(PrQueries.ReviewSearch, PrRole.ReviewRequested, _options.PageSize, _options.MaxPages, cancellationToken);
        var authoredList = await authoredTask.ConfigureAwait(false);
        var reviewList = await reviewTask.ConfigureAwait(false);

        var all = new List<PullRequest>(authoredList);
        all.AddRange(reviewList);

        // The feed only needs the repositories and keys of the listed PRs, so it runs next to the enrichment instead of after it.
        var feedTask = _options.IncludeFeed ? LoadFeedAsync(all, viewer, cancellationToken) : null;

        var enrichTask = _prs.EnrichAsync(all, _options.IncludeMergeable, cancellationToken);
        var infoTask = _prs.FetchReviewInfosAsync(reviewList, viewer, cancellationToken);
        var enriched = await enrichTask.ConfigureAwait(false);
        var infos = await infoTask.ConfigureAwait(false);
        warnings.AddRange(enriched.Errors);
        warnings.AddRange(infos.Errors);

        var authored = enriched.Prs.Take(authoredList.Count).ToList();
        var reviews = enriched.Prs.Skip(authoredList.Count).ToList();

        IReadOnlyList<FeedItem> feed = Array.Empty<FeedItem>();
        if (feedTask is not null)
        {
            var feedResult = await feedTask.ConfigureAwait(false);
            feed = feedResult.Items;
            warnings.AddRange(feedResult.Errors);
        }

        LastWarnings = warnings;
        return new HubSnapshot(viewer, authored, reviews, infos.Infos, feed, DateTimeOffset.UtcNow);
    }

    private async Task<(IReadOnlyList<FeedItem> Items, IReadOnlyList<string> Errors)> LoadFeedAsync(
        IReadOnlyList<PullRequest> prs, string viewer, CancellationToken cancellationToken)
    {
        try
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pr in prs)
            {
                keys.Add(GitHubNames.PrKey(pr.Repo, pr.Number));
                _feed.SeedTitle(pr.Repo, pr.Number, pr.Title);
            }
            var repos = prs.Select(p => p.Repo).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var result = await _feed.FetchAsync(repos, viewer, keys, cancellationToken).ConfigureAwait(false);
            return (result.Items, result.Errors);
        }
        catch (Exception ex) when (Recoverable.Is(ex, cancellationToken))
        {
            return (Array.Empty<FeedItem>(), new[] { "Activity feed unavailable: " + ex.Message });
        }
    }
}
