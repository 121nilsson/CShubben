using Cshubben.Core.Features.Cache;
using Cshubben.Core.Features.Digest;
using Cshubben.Core.Features.Notes;
using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Features.Stats;
using Cshubben.Core.Model;
using Cshubben.Ui.Assist;
using Cshubben.Ui.Pages;

namespace Cshubben.Services;

/// <summary>
/// Loads the slow pages (Linear, Drift, Stats) in the background so they are ready when opened. Pages you open often are reloaded
/// every couple of minutes, the others rarely (see <see cref="PrefetchPolicy"/>). The results go to <see cref="PageCaches"/> and to
/// disk, so the next start paints them at once. Throttled, one load per page at a time, and it never throws.
/// </summary>
static class PagePrefetch
{
    private static readonly PageUsage Usage = PageUsage.Load(AppPaths.DataDirectory);
    private static readonly Dictionary<string, DateTimeOffset> LastRun = new();
    private static readonly HashSet<string> Busy = new();
    private static DateTimeOffset _usageSavedAt = DateTimeOffset.MinValue;

    /// <summary>Counts a page visit. The file is written at most once a minute.</summary>
    public static void RecordVisit(Page page)
    {
        try
        {
            lock (Usage)
            {
                Usage.Record(page.ToString());
                if (DateTimeOffset.UtcNow - _usageSavedAt < TimeSpan.FromMinutes(1)) return;
                _usageSavedAt = DateTimeOffset.UtcNow;
            }

            _ = Task.Run(() => { lock (Usage) Usage.TrySave(AppPaths.DataDirectory); });
        }
        catch (Exception)
        {
            // Habits are a nicety.
        }
    }

    /// <summary>Call after every successful refresh. Returns at once.</summary>
    public static void Tick(HubSnapshot snapshot, AppServices services)
    {
        try
        {
            if (services.IsDemo || services.GitHub is not { } hub || snapshot.Viewer.Length == 0) return;
            var settings = services.Settings;
            var viewer = snapshot.Viewer;

            Start(Page.Linear, "linear", async ct =>
            {
                var data = IntegrationServices.Linear;
                if (!data.IsConnected || data.IsDemo) return;
                await PageCaches.Linear.GetAsync(PageKeys.Linear(viewer, false), Interval(Page.Linear), () => data.LoadAsync(snapshot, ct));
            }, viewer);

            Start(Page.Drift, "drift", async ct =>
            {
                var data = PipelineServices.Data;
                var repos = PageKeys.DriftRepos(snapshot, settings);
                if (data.IsDemo || repos.Count == 0) return;
                await PageCaches.Drift.GetAsync(PageKeys.Drift(false, repos), Interval(Page.Drift), async () =>
                    DriftReport.Build(await data.GetDriftAsync(repos, ct), DateTimeOffset.UtcNow));
            }, viewer);

            Start(Page.Stats, "stats", async ct =>
            {
                await PageCaches.Stats.GetAsync(PageKeys.Stats(viewer), Interval(Page.Stats), () =>
                    new GitHubStatsData(hub.GraphQl, viewer).LoadAsync(StatsCalculator.DefaultWeeks, DateTimeOffset.UtcNow, ct));
                var repos = DigestPage.WatchedRepos(services.RepoActivity, settings, false);
                if (repos.Count == 0) return;
                await PageCaches.Team.GetAsync(PageKeys.Team(viewer, repos), Interval(Page.Stats), async () =>
                {
                    var input = await new GitHubDigestData(hub.GraphQl).LoadAsync(DigestPeriod.Month, repos, DateTimeOffset.Now, ct);
                    return TeamStats.Compute(input.Prs.Select(p => new TeamPr(p.Repo, p.Author, p.MergedAt, p.Size)));
                });
            }, viewer);
        }
        catch (Exception)
        {
            // A convenience; never break refreshing.
        }
    }

    private static TimeSpan Interval(Page page)
    {
        lock (Usage) return PrefetchPolicy.Interval(Usage, page.ToString());
    }

    private static void Start(Page page, string name, Func<CancellationToken, Task> load, string viewer)
    {
        var now = DateTimeOffset.UtcNow;
        lock (LastRun)
        {
            if (Busy.Contains(name)) return;
            if (LastRun.TryGetValue(name, out var at) && now - at < Interval(page)) return;
            Busy.Add(name);
            LastRun[name] = now;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await load(timeout.Token).ConfigureAwait(false);
                PageCaches.Persist(AppPaths.DataDirectory, viewer);
            }
            catch (Exception)
            {
                // Try again after the interval.
            }
            finally
            {
                lock (LastRun) Busy.Remove(name);
            }
        });
    }
}
