using Cshubben.Core.Composition;
using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Model;
using Cshubben.Ui.Assist;

namespace Cshubben.Services;

/// <summary>
/// Looks at Drift (important repos only) and Linear in the background after each refresh, so a red main branch, a failed deployment,
/// a newly assigned issue or an out-of-sync pull request can raise a toast even when those pages are closed.
/// Throttled, one check at a time per source, and it never throws. The first check after start is a baseline and shows nothing.
/// The Linear result also fills the page cache, so the Linear page opens at once.
/// </summary>
static class BackgroundWatch
{
    public static readonly TimeSpan DriftEvery = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LinearEvery = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan ErrorsEvery = TimeSpan.FromMinutes(5);
    private const int MaxDriftRepos = 8;

    private static int _driftBusy;
    private static int _linearBusy;
    private static int _errorsBusy;
    private static DateTimeOffset _errorsAt = DateTimeOffset.MinValue;
    private static IReadOnlyList<WatchedErrorSpike>? _errorsBefore;
    private static string _errorsKey = "";
    private static DateTimeOffset _driftAt = DateTimeOffset.MinValue;
    private static DateTimeOffset _linearAt = DateTimeOffset.MinValue;
    private static IReadOnlyList<WatchedRepoState>? _driftBefore;
    private static WatchedLinear? _linearBefore;

    /// <summary>Call after every successful refresh. Returns at once; the checks run on the thread pool.</summary>
    public static void Tick(HubSnapshot snapshot, AppSettings settings, bool demo, AppServices? services = null)
    {
        try
        {
            if (demo || !settings.Notifications || !NotificationsHub.IsLive) return;
            var prefs = NotificationsHub.Effective(settings, NotificationsHub.Prefs);
            var now = DateTimeOffset.UtcNow;

            if ((prefs.MainRed || prefs.DeployFailed) && now - _driftAt >= DriftEvery)
            {
                var repos = ImportantRepos(settings);
                if (repos.Count > 0 && Interlocked.CompareExchange(ref _driftBusy, 1, 0) == 0)
                {
                    _driftAt = now;
                    _ = Task.Run(() => CheckDriftAsync(repos, settings));
                }
            }

            if (prefs.ErrorSpike && services is not null && settings.Errors.Enabled && now - _errorsAt >= ErrorsEvery
                && ErrorsServices.SourceFor(services, settings.Errors) is { } errors
                && Interlocked.CompareExchange(ref _errorsBusy, 1, 0) == 0)
            {
                _errorsAt = now;
                _ = Task.Run(() => CheckErrorsAsync(errors.Source, errors.Key, settings));
            }

            if ((prefs.LinearAssigned || prefs.LinearOutOfSync) && now - _linearAt >= LinearEvery
                && Interlocked.CompareExchange(ref _linearBusy, 1, 0) == 0)
            {
                _linearAt = now;
                _ = Task.Run(() => CheckLinearAsync(snapshot, settings));
            }
        }
        catch (Exception)
        {
            // A convenience; never break refreshing.
        }
    }

    /// <summary>Important repos that are valid and not muted, at most <see cref="MaxDriftRepos"/>.</summary>
    public static IReadOnlyList<string> ImportantRepos(AppSettings settings) =>
        settings.ImportantRepos
            .Where(r => RepoList.IsRepo(r) && !RepoList.IsMuted(settings, r))
            .Take(MaxDriftRepos)
            .ToList();

    private static async Task CheckDriftAsync(IReadOnlyList<string> repos, AppSettings settings)
    {
        try
        {
            var data = PipelineServices.Data;
            if (data.IsDemo) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var activities = await data.GetDriftAsync(repos, timeout.Token).ConfigureAwait(false);
            var state = WatchSources.FromDrift(DriftReport.Build(activities, DateTimeOffset.UtcNow));
            var events = WatchDiff.Drift(_driftBefore, state);
            _driftBefore = state;
            NotificationsHub.OnExtra(events, settings);
        }
        catch (Exception)
        {
            // Try again at the next refresh.
        }
        finally
        {
            Interlocked.Exchange(ref _driftBusy, 0);
        }
    }

    /// <summary>
    /// Reads the error counts (the same data and cache as the Errors page, so it opens at once) and reports applications that began
    /// to spike. Only a saved sign-in is used, so a background check never opens a browser. The first check is a baseline.
    /// </summary>
    private static async Task CheckErrorsAsync(IErrorSource source, string key, AppSettings settings)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var snapshot = await PageCaches.Errors.GetAsync(key, PageCaches.ErrorsTtl, () => source.LoadAsync(timeout.Token)).ConfigureAwait(false);
            var state = WatchSources.FromErrors(snapshot);

            // A different source or other settings is a new baseline, not a change.
            if (!string.Equals(_errorsKey, key, StringComparison.Ordinal)) _errorsBefore = null;
            _errorsKey = key;

            var events = WatchDiff.Errors(_errorsBefore, state);
            _errorsBefore = state;
            NotificationsHub.OnExtra(events, settings);
        }
        catch (Exception)
        {
            // Not signed in, offline or the service is down: try again later. Keep the last state, so an outage is not a new spike.
        }
        finally
        {
            Interlocked.Exchange(ref _errorsBusy, 0);
        }
    }

    private static async Task CheckLinearAsync(HubSnapshot snapshot, AppSettings settings)
    {
        try
        {
            var data = IntegrationServices.Linear;
            if (!data.IsConnected || data.IsDemo) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var board = await data.LoadAsync(snapshot, timeout.Token).ConfigureAwait(false);
            PageCaches.Linear.Set(PageKeys.Linear(snapshot.Viewer, false), board);
            var state = WatchSources.FromLinear(board.Issues, board.RecentlyDone, board.Prs);
            var events = WatchDiff.Linear(_linearBefore, state);
            _linearBefore = state;
            NotificationsHub.OnExtra(events, settings);
        }
        catch (Exception)
        {
            // Try again at the next refresh.
        }
        finally
        {
            Interlocked.Exchange(ref _linearBusy, 0);
        }
    }
}
