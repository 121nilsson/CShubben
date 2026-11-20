using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Feed;

/// <summary>
/// Pure repo matching for muted and followed lists. Takes plain string lists (the Settings feature is not referenced here;
/// the composition root passes the lists in). Names compare case-insensitively.
/// </summary>
public static class RepoFilter
{
    public static string OwnerOf(string repo)
    {
        var slash = repo.IndexOf('/');
        return slash > 0 ? repo.Substring(0, slash) : repo;
    }

    private static bool Contains(IReadOnlyList<string>? list, string name)
    {
        if (list is null) return false;
        foreach (var entry in list)
        {
            if (string.Equals(entry, name, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>True when the repo is in <paramref name="mutedRepos"/> or its owner is in <paramref name="mutedOrgs"/>.</summary>
    public static bool IsMuted(string repo, IReadOnlyList<string>? mutedRepos, IReadOnlyList<string>? mutedOrgs) =>
        Contains(mutedRepos, repo) || Contains(mutedOrgs, OwnerOf(repo));

    /// <summary>An empty followed list means everything is followed. A followed owner (bare name) follows all its repos.</summary>
    public static bool IsFollowed(string repo, IReadOnlyList<string>? followed)
    {
        if (followed is null || followed.Count == 0) return true;
        return Contains(followed, repo) || Contains(followed, OwnerOf(repo));
    }

    /// <summary>
    /// Removes everything that belongs to a muted repo: authored PRs, review requests, their review infos and feed items.
    /// Returns the same instance when nothing is muted.
    /// </summary>
    public static HubSnapshot FilterSnapshot(HubSnapshot snapshot, IReadOnlyList<string>? mutedRepos, IReadOnlyList<string>? mutedOrgs)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var none = (mutedRepos is null || mutedRepos.Count == 0) && (mutedOrgs is null || mutedOrgs.Count == 0);
        if (none) return snapshot;

        var authored = snapshot.Authored.Where(p => !IsMuted(p.Repo, mutedRepos, mutedOrgs)).ToList();
        var reviews = snapshot.Reviews.Where(p => !IsMuted(p.Repo, mutedRepos, mutedOrgs)).ToList();
        var keep = new HashSet<string>(reviews.Select(p => p.Id), StringComparer.Ordinal);
        var infos = new Dictionary<string, ReviewInfo>(StringComparer.Ordinal);
        foreach (var pair in snapshot.ReviewInfos)
        {
            if (keep.Contains(pair.Key)) infos[pair.Key] = pair.Value;
        }

        var feed = snapshot.Feed.Where(i => !IsMuted(i.Repo, mutedRepos, mutedOrgs)).ToList();
        return snapshot with { Authored = authored, Reviews = reviews, ReviewInfos = infos, Feed = feed };
    }
}
