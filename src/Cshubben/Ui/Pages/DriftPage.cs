using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Logic;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Cshubben.Ui.Pipeline;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// Drift: GitHub Actions and deployments per repository. An overview strip (needs attention, running, failing workflows,
/// behind on deploys, unreleased commits), then one card per repository with CI on the default branch, the latest deployment
/// per environment, commits not yet deployed or released, and Re-run / Open actions. Ported from My-hub's Drift tab.
/// The data comes from <see cref="PipelineServices.Data"/>; the repositories from the snapshot, narrowed by the followed and muted settings.
/// </summary>
sealed class DriftPage : Component<PageContext>
{
    public override Element Render()
    {
        var ctx = Props;
        var snap = ctx.Snapshot;
        var data = PipelineServices.Data;

        var (tick, setTick) = UseState(0, true);
        var (busyRepo, setBusyRepo) = UseState("", true);
        var (status, setStatus) = UseState("", true);
        var (statusTone, setStatusTone) = UseState(Tone.Success, true);
        var inFlight = UseRef(false);

        var repos = RepoSelection.Pick(
            snap.All.Select(p => p.Repo).Concat(snap.Feed.Select(f => f.Repo)),
            ctx.Settings.FollowedRepos,
            ctx.Settings.MutedRepos.Concat(ctx.Settings.MutedOrgs));
        var reposKey = string.Join("|", repos);

        // Live mode without a GitHub connection: do not show invented data next to real pull requests.
        var needsConnection = !ctx.Services.ForcedDemo && ctx.Services.GitHub is null && data.IsDemo;

        var driftKey = "drift|" + (data.IsDemo ? "demo|" : "live|") + reposKey;

        var resource = UseResource<DriftReport>(
            async ct =>
            {
                if (needsConnection || repos.Count == 0) return DriftReport.Empty;
                return await PageCaches.Drift.GetAsync(driftKey, PageCaches.DriftTtl, async () =>
                {
                    var activities = await data.GetDriftAsync(repos, ct);
                    return DriftReport.Build(activities, DateTimeOffset.UtcNow);
                });
            },
            new object[] { data, reposKey, RefreshBucket(snap.At), tick, needsConnection });

        void Refresh()
        {
            PageCaches.Drift.Remove(driftKey);
            setTick(tick + 1);
        }

        async Task Rerun(RepoRow row)
        {
            if (inFlight.Current) return;
            inFlight.Current = true;
            setBusyRepo(row.Repo);
            setStatus("");
            try
            {
                var ids = row.FailingWorkflows.Select(r => r.Id).ToList();
                var result = await data.RerunFailedJobsAsync(row.Repo, ids, CancellationToken.None);
                setStatus(row.Repo + ": " + result.Message);
                setStatusTone(result.Ok ? Tone.Success : Tone.Danger);
                if (result.Ok && !result.DryRun)
                {
                    PageCaches.Drift.Remove(driftKey);
                    setTick(tick + 1);
                }
            }
            catch (Exception ex)
            {
                setStatus(row.Repo + ": " + ex.Message);
                setStatusTone(Tone.Danger);
            }
            finally
            {
                inFlight.Current = false;
                setBusyRepo("");
            }
        }

        var report = Parts.Peek(resource) ?? (needsConnection ? null : PageCaches.Drift.Peek(driftKey));
        var loading = Parts.IsLoading(resource) || resource is AsyncValue<DriftReport>.Reloading;

        var subtitle = needsConnection
            ? "Actions runs and deployments on your repositories"
            : report is { Rows.Count: > 0 }
                ? report.Summary + (data.IsDemo ? " · demo data" : "")
                : "Actions runs and deployments on your repositories";

        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Kit.PageHeader("Drift", subtitle).Grid(column: 0),
            (loading
                ? ProgressRing().IsActive().Width(18).Height(18)
                : (Element)Button("Refresh", Refresh).SubtleButton().AutomationName("Refresh drift"))
                .VAlign(VerticalAlignment.Top).Margin(8, 4, 0, 0).Grid(column: 1));

        Element content;
        if (!ctx.Loaded)
        {
            content = Card(Parts.Busy("Loading your repositories…"));
        }
        else if (needsConnection)
        {
            content = ConnectState(ctx.Refresh);
        }
        else if (repos.Count == 0)
        {
            content = Card(VStack(8,
                BodyStrong("No repositories to watch yet"),
                Kit.Muted("Repositories show up here when you have pull requests in them. You can also follow a repository in Settings.").TextWrapping()));
        }
        else if (report is null)
        {
            var failure = Parts.ErrorOf(resource);
            content = failure is null
                ? Card(Parts.Busy("Reading Actions runs and deployments…"))
                : Card(VStack(10,
                    Parts.Banner("Could not load drift", failure, Tone.Danger),
                    Button("Try again", Refresh).HAlign(HorizontalAlignment.Left)));
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            var cards = report.Rows.Select(row => DriftCards.RepoCard(
                row, now, busyRepo == row.Repo,
                () => _ = Rerun(row),
                () => ExternalActions.OpenUrl("https://github.com/" + row.Repo + "/actions"))).ToArray();
            content = FlexColumn(
                DriftCards.Strip(report).Margin(0, 0, 0, 12),
                FlexRow(cards) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 }) with { RowGap = 0 };
        }

        return ScrollView(Kit.Capped(
            FlexColumn(
                header,
                status.Length == 0
                    ? null
                    : Kit.Banner(statusTone, statusTone == Tone.Success ? "Done" : "Not sent", status).Margin(0, 0, 0, 12),
                content)
            .FlexPadding(24, 16)));
    }

    /// <summary>Drift costs several GitHub calls per repository, so it is re-read every five minutes, not on every refresh of the list.</summary>
    private static long RefreshBucket(DateTimeOffset at) => at.ToUnixTimeSeconds() / 300;

    private static Element ConnectState(Action retry) =>
        Card(VStack(10,
            BodyStrong("Connect GitHub"),
            Kit.Muted("Drift reads Actions runs and deployments from GitHub. Add a token in Settings, or sign in with the GitHub CLI (gh auth login) or set GITHUB_TOKEN.").TextWrapping(),
            Button("Check again", retry).HAlign(HorizontalAlignment.Left).AutomationName("Check the GitHub connection again")));
}
