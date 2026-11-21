using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Model;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>The cache keys of the slow pages. One place, so a page and the background prefetch always use the same key.</summary>
static class PageKeys
{
    public static IReadOnlyList<string> DriftRepos(HubSnapshot snap, AppSettings settings)
    {
        var muted = settings.MutedRepos.Concat(settings.MutedOrgs);
        var repos = RepoSelection.Pick(snap.All.Select(p => p.Repo).Concat(snap.Feed.Select(f => f.Repo)), settings.FollowedRepos, muted);
        return RepoSelection.WithImportant(repos, settings.ImportantRepos, muted);
    }

    public static string Drift(bool demo, IReadOnlyList<string> repos) => "drift|" + (demo ? "demo|" : "live|") + string.Join("|", repos);

    public static string Stats(string viewer) => "stats|" + viewer;

    public static string Team(string viewer, IReadOnlyList<string> repos) =>
        "team|" + viewer + "|" + StableKey.Hash(string.Join(",", repos.OrderBy(r => r, StringComparer.OrdinalIgnoreCase)));

    public static string Linear(string viewer, bool demo) => "linear|" + viewer + "|" + (demo ? "demo" : "live");
}
