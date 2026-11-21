using Cshubben.Core.Features.Digest;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Stats;
using Cshubben.Core.Logic;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// Loads the real merge and review history for <see cref="StatsPage"/>. Without a GitHub connection (or in demo mode) the
/// page keeps its demo data and says so. History is re-read at most once an hour or on request, since it changes slowly.
/// </summary>
sealed class StatsPageHost : Component<PageContext>
{
    public override Element Render()
    {
        var ctx = Props;
        var snap = ctx.Snapshot;
        var hub = ctx.Services.GitHub;
        var live = !ctx.Services.ForcedDemo && hub is not null && ctx.Loaded && snap.Viewer.Length > 0;
        var (tick, setTick) = UseState(0, true);

        var statsKey = "stats|" + snap.Viewer;

        var resource = UseResource<StatsInput?>(
            async ct =>
            {
                if (!live || hub is null) return null;
                return await PageCaches.Stats.GetAsync(statsKey, PageCaches.StatsTtl, () =>
                    new GitHubStatsData(hub.GraphQl, snap.Viewer).LoadAsync(StatsCalculator.DefaultWeeks, DateTimeOffset.UtcNow, ct));
            },
            new object[] { live, snap.Viewer, snap.At.ToUnixTimeSeconds() / 3600, tick });

        // The team numbers are optional: a failure here must not hide the personal numbers.
        var teamRepos = DigestPage.WatchedRepos(ctx.Services.RepoActivity, ctx.Settings, false);
        var teamKey = "team|" + snap.Viewer + "|" + string.Join(",", teamRepos.OrderBy(r => r, StringComparer.OrdinalIgnoreCase)).GetHashCode();
        var teamResource = UseResource<TeamReport?>(
            async ct =>
            {
                if (!live || hub is null || teamRepos.Count == 0) return null;
                try
                {
                    return await PageCaches.Team.GetAsync(teamKey, PageCaches.StatsTtl, async () =>
                    {
                        var input = await new GitHubDigestData(hub.GraphQl).LoadAsync(DigestPeriod.Month, teamRepos, DateTimeOffset.Now, ct);
                        return TeamStats.Compute(input.Prs.Select(p => new TeamPr(p.Repo, p.Author, p.MergedAt, p.Size)));
                    });
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    return null;
                }
            },
            new object[] { live, teamKey, snap.At.ToUnixTimeSeconds() / 3600, tick });
        var team = Parts.Peek(teamResource) ?? PageCaches.Team.Peek(teamKey);

        void Retry()
        {
            PageCaches.Stats.Remove(statsKey);
            PageCaches.Team.Remove(teamKey);
            setTick(tick + 1);
        }

        if (!live)
        {
            var needsConnection = !ctx.Services.ForcedDemo && ctx.Loaded && ctx.Services.GitHub is null;
            return StatsPage.Build(snap, null, needsConnection
                ? Kit.Banner(Tone.Warning, "Showing demo numbers", "Connect GitHub in Settings to see your own cycle time, review load and throughput.")
                : null);
        }

        var input = Parts.Peek(resource) ?? PageCaches.Stats.Peek(statsKey);
        if (input is not null)
        {
            var refreshing = resource is AsyncValue<StatsInput?>.Reloading;
            return StatsPage.Build(snap, input, refreshing ? Kit.Muted("Updating…") : null, team);
        }

        if (Parts.IsLoading(resource) || resource is not AsyncValue<StatsInput?>.Error)
            return ScrollView(Kit.Capped(FlexColumn(
                Kit.PageHeader("Stats", "Reading your merged pull requests and reviews"),
                Card(Parts.Busy("Reading your last 12 weeks from GitHub…"))).FlexPadding(24, 16)));

        var error = ((AsyncValue<StatsInput?>.Error)resource).Exception;
        var failure = ErrorText.Classify(error, "GitHub");
        return ScrollView(Kit.Capped(FlexColumn(
            Kit.PageHeader("Stats", "Your merged pull requests and reviews"),
            FailureView.Banner(failure, Retry, ctx.OpenSettings)).FlexPadding(24, 16)));
    }
}
