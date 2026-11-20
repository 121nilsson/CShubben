using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Feed;

public enum FeedScope { All, Personal, Team }

/// <summary>How the feed is filtered. Bots are hidden unless <see cref="ShowBots"/> is set.</summary>
public sealed record FeedOptions
{
    public FeedScope Scope { get; init; } = FeedScope.All;

    public bool ShowBots { get; init; }

    /// <summary>Empty means every repo.</summary>
    public IReadOnlyList<string> FollowedRepos { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MutedRepos { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MutedOrgs { get; init; } = Array.Empty<string>();
}

/// <summary>Counts shown on the filter chips. <see cref="Bots"/> is the number of bot events (hidden unless bots are shown).</summary>
public sealed record FeedCounts(int All, int Personal, int Team, int Bots);

public static class FeedFiltering
{
    /// <summary>Newest first; ties broken by id so the order is stable.</summary>
    public static List<FeedItem> Newest(IEnumerable<FeedItem> items) =>
        items.OrderByDescending(i => i.At).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();

    private static IEnumerable<FeedItem> ByRepo(IEnumerable<FeedItem> items, FeedOptions options) =>
        items.Where(i => !RepoFilter.IsMuted(i.Repo, options.MutedRepos, options.MutedOrgs)
                         && RepoFilter.IsFollowed(i.Repo, options.FollowedRepos));

    /// <summary>Applies muted, followed, bot and scope filters. Result is newest first.</summary>
    public static List<FeedItem> Apply(IEnumerable<FeedItem> items, FeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        IEnumerable<FeedItem> result = ByRepo(items, options);
        if (!options.ShowBots) result = result.Where(i => !i.IsBot);
        result = options.Scope switch
        {
            FeedScope.Personal => result.Where(i => i.Personal),
            FeedScope.Team => result.Where(i => !i.Personal),
            _ => result,
        };
        return Newest(result);
    }

    /// <summary>Chip counts. They ignore <see cref="FeedOptions.Scope"/> so each chip shows what it would give.</summary>
    public static FeedCounts Count(IEnumerable<FeedItem> items, FeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        var byRepo = ByRepo(items, options).ToList();
        var bots = byRepo.Count(i => i.IsBot);
        var shown = options.ShowBots ? byRepo : byRepo.Where(i => !i.IsBot).ToList();
        var personal = shown.Count(i => i.Personal);
        return new FeedCounts(shown.Count, personal, shown.Count - personal, bots);
    }

    /// <summary>The time to store when the user presses "Mark all seen": the newest event or now, whichever is later.</summary>
    public static DateTimeOffset MarkAllSeenAt(IEnumerable<FeedItem> items, DateTimeOffset now)
    {
        var latest = now;
        foreach (var item in items)
        {
            if (item.At > latest) latest = item.At;
        }

        return latest;
    }
}
