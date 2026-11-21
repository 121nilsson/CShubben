using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Settings;

/// <summary>One repository in the settings list.</summary>
/// <param name="Repo">owner/name.</param>
/// <param name="Name">The part after the owner.</param>
/// <param name="Activity">Pull requests plus feed events seen for it in the last load.</param>
/// <param name="Watched">False when the repo or its owner is muted.</param>
/// <param name="Important">Marked as important.</param>
public sealed record RepoEntry(string Repo, string Name, int Activity, bool Watched, bool Important);

/// <summary>An owner (organisation or user) with its repositories.</summary>
public sealed record OrgEntry(string Owner, bool Watched, int Activity, IReadOnlyList<RepoEntry> Repos)
{
    public int WatchedRepos => Repos.Count(r => r.Watched);
}

public sealed record RepoDirectoryModel(IReadOnlyList<OrgEntry> Orgs)
{
    public int TotalRepos => Orgs.Sum(o => o.Repos.Count);

    public int WatchedRepos => Orgs.Sum(o => o.WatchedRepos);
}

/// <summary>
/// Pure logic behind the repository list in settings: which repos exist (seen in the data plus anything named in
/// the settings, so a muted repo can always be turned back on), how they group by owner, and how a toggle changes the settings.
/// </summary>
public static class RepoDirectory
{
    /// <summary>Counts pull requests and feed events per repo (case-insensitive) in a snapshot.</summary>
    public static IReadOnlyDictionary<string, int> CountActivity(HubSnapshot snapshot)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Add(string? repo)
        {
            if (!RepoList.IsRepo(repo)) return;
            counts[repo!] = counts.TryGetValue(repo!, out var n) ? n + 1 : 1;
        }

        foreach (var p in snapshot.Authored) Add(p.Repo);
        foreach (var p in snapshot.Reviews) Add(p.Repo);
        foreach (var f in snapshot.Feed) Add(f.Repo);
        return counts;
    }

    /// <summary>Keeps the old counts for repos that were not in the new load, so a quiet repo does not vanish from the list.</summary>
    public static IReadOnlyDictionary<string, int> Merge(IReadOnlyDictionary<string, int> previous, IReadOnlyDictionary<string, int> latest)
    {
        var merged = new Dictionary<string, int>(previous, StringComparer.OrdinalIgnoreCase);
        foreach (var (repo, count) in latest) merged[repo] = count;
        return merged;
    }

    public static string OwnerOf(string repo)
    {
        var slash = repo.IndexOf('/');
        return slash > 0 ? repo.Substring(0, slash) : repo;
    }

    private static bool Has(IReadOnlyList<string> list, string name) =>
        list.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds the grouped list. A non-empty <paramref name="filter"/> keeps repos and owners whose name contains it.</summary>
    public static RepoDirectoryModel Build(IReadOnlyDictionary<string, int> activity, AppSettings settings, string? filter = null)
    {
        var repos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (repo, count) in activity)
            if (RepoList.IsRepo(repo)) repos[repo] = count;
        foreach (var name in settings.MutedRepos.Concat(settings.ImportantRepos).Concat(settings.FollowedRepos))
            if (RepoList.IsRepo(name)) repos.TryAdd(name, 0);

        var owners = repos.Keys.Select(OwnerOf).Concat(settings.MutedOrgs)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var needle = filter?.Trim() ?? "";
        var orgs = new List<OrgEntry>();
        foreach (var owner in owners)
        {
            var orgMuted = Has(settings.MutedOrgs, owner);
            var entries = repos
                .Where(kv => string.Equals(OwnerOf(kv.Key), owner, StringComparison.OrdinalIgnoreCase))
                .Select(kv => new RepoEntry(
                    kv.Key,
                    kv.Key.Substring(owner.Length + 1),
                    kv.Value,
                    !orgMuted && !Has(settings.MutedRepos, kv.Key),
                    Has(settings.ImportantRepos, kv.Key)))
                .ToList();

            var ownerMatches = needle.Length == 0 || owner.Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (!ownerMatches) entries = entries.Where(e => e.Repo.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0 && !ownerMatches) continue;

            entries = entries.OrderByDescending(e => e.Activity).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            orgs.Add(new OrgEntry(owner, !orgMuted, entries.Sum(e => e.Activity), entries));
        }

        return new RepoDirectoryModel(orgs
            .OrderByDescending(o => o.Activity)
            .ThenBy(o => o.Owner, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>Turns a repo on or off (muted list).</summary>
    public static AppSettings SetRepoWatched(AppSettings settings, string repo, bool watched) => settings with
    {
        MutedRepos = watched ? RepoList.Remove(settings.MutedRepos, repo) : RepoList.Add(settings.MutedRepos, repo),
    };

    /// <summary>Turns a whole owner on or off (muted organisations).</summary>
    public static AppSettings SetOwnerWatched(AppSettings settings, string owner, bool watched) => settings with
    {
        MutedOrgs = watched ? RepoList.Remove(settings.MutedOrgs, owner) : RepoList.Add(settings.MutedOrgs, owner),
    };

    /// <summary>Marks or unmarks a repo as important.</summary>
    public static AppSettings SetImportant(AppSettings settings, string repo, bool important) => settings with
    {
        ImportantRepos = important ? RepoList.Add(settings.ImportantRepos, repo) : RepoList.Remove(settings.ImportantRepos, repo),
    };
}
