using Cshubben.Core.Features.Cache;
using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Features.Stats;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>
/// In-memory results of the slower page loads, so changing page shows the last result at once and reloads in the background
/// only when it is older than its time to live. Cleared by each page's Refresh button and after its own writes.
/// </summary>
static class PageCaches
{
    public static readonly StaleCache<LinearBoard> Linear = new();
    public static readonly StaleCache<DriftReport> Drift = new();
    public static readonly StaleCache<StatsInput> Stats = new();
    public static readonly StaleCache<LinearIssue> Issues = new();
    public static readonly StaleCache<TeamReport> Team = new();
    public static readonly StaleCache<ErrorsSnapshot> Errors = new();
    public static readonly StaleCache<IReadOnlyList<ErrorGroup>> ErrorGroups = new();

    private static readonly Dictionary<string, DateTimeOffset> Saved = new();

    /// <summary>Fills the caches from the files of the last run (as old entries), so every page paints at once after a restart.</summary>
    public static void Restore(string directory, string viewer)
    {
        try
        {
            RestoreOne(Linear, "linear");
            RestoreOne(Drift, "drift");
            RestoreOne(Stats, "stats");
            RestoreOne(Team, "team");
        }
        catch (Exception)
        {
            // A convenience; the pages load normally without it.
        }

        void RestoreOne<T>(StaleCache<T> cache, string name) where T : class
        {
            if (PageDiskCache.TryLoad<T>(directory, name, viewer) is not { } hit) return;
            cache.Seed(hit.Key, hit.Value, hit.At);
            lock (Saved) Saved[name] = hit.At;
        }
    }

    /// <summary>Writes the entries that changed since they were last written. Runs on the caller's thread; call it from the pool.</summary>
    public static void Persist(string directory, string viewer)
    {
        try
        {
            PersistOne(Linear, "linear");
            PersistOne(Drift, "drift");
            PersistOne(Stats, "stats");
            PersistOne(Team, "team");
        }
        catch (Exception)
        {
            // Never break the app over a cache file.
        }

        void PersistOne<T>(StaleCache<T> cache, string name) where T : class
        {
            if (cache.Latest() is not { } latest) return;
            lock (Saved)
            {
                if (Saved.TryGetValue(name, out var at) && at >= latest.At) return;
                Saved[name] = latest.At;
            }

            PageDiskCache.TrySave(directory, name, viewer, latest.Key, latest.At, latest.Value);
        }
    }

    public static readonly TimeSpan LinearTtl = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DriftTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IssueTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ErrorsTtl = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(30);
}
