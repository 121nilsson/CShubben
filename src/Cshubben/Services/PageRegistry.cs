using Cshubben.Core.Features.Settings;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Ui;
using Cshubben.Ui.Pages;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Services;

/// <summary>Everything a page needs from the shell. Pages read data from here, never from <see cref="AppOptions"/>.</summary>
/// <param name="Snapshot">The current data (an empty snapshot until the first load finishes; see <see cref="Loaded"/>).</param>
/// <param name="Loaded">False while the very first load is still running.</param>
/// <param name="SelectedId">Id of the PR open in the inspector, if any.</param>
/// <param name="Select">Opens a PR in the inspector.</param>
/// <param name="Services">Composition root: writer, secrets, settings store, connection tests.</param>
/// <param name="Settings">Current app settings.</param>
/// <param name="SaveSettings">Persists new settings and re-renders the shell.</param>
/// <param name="Refresh">Reloads the data now.</param>
/// <param name="OpenSettings">Goes to the Settings page (for "fix your token" buttons).</param>
sealed record PageContext(
    HubSnapshot Snapshot,
    bool Loaded,
    string? SelectedId,
    Action<string> Select,
    AppServices Services,
    AppSettings Settings,
    Action<AppSettings> SaveSettings,
    Action Refresh,
    Action OpenSettings);

/// <summary>A count shown next to a sidebar entry.</summary>
sealed record PageBadge(int Count, Tone Tone);

/// <summary>
/// One page in the app. Add a page by adding a value to <see cref="Page"/> and one line to <see cref="PageRegistry.All"/>.
/// </summary>
/// <param name="Id">The page (also what <c>--start</c> accepts).</param>
/// <param name="Section">Sidebar group title. Entries with the same section are grouped in registry order.</param>
/// <param name="Glyph">Segoe Fluent Icons character.</param>
/// <param name="Label">Sidebar text.</param>
/// <param name="Build">Builds the page from the shared context.</param>
/// <param name="Badge">Optional count for the sidebar.</param>
/// <param name="NeedsData">When true the shell shows a loading state until the first snapshot arrived.</param>
/// <param name="Pinned">Pinned entries sit at the bottom of the sidebar, below the spacer.</param>
sealed record PageDef(
    Page Id,
    string Section,
    string Glyph,
    string Label,
    Func<PageContext, Element> Build,
    Func<PageContext, PageBadge?>? Badge = null,
    bool NeedsData = false,
    bool Pinned = false);

static class PageRegistry
{
    public static readonly IReadOnlyList<PageDef> All = new List<PageDef>
    {
        new(Page.Overview, "Overview", "", "Overview",
            c => OverviewPage.Build(c.Snapshot, c.SelectedId, c.Select),
            c => new PageBadge(OverviewModel.Compute(c.Snapshot).NeedsYou, Tone.Warning), NeedsData: true),
        new(Page.Feed, "Overview", "", "Feed",
            c => FeedPage.Build(c.Snapshot, c.Select), NeedsData: true),

        new(Page.Mine, "Pull requests", "", "My PRs",
            c => PrListPages.Mine(c.Snapshot, c.SelectedId, c.Select),
            c => new PageBadge(c.Snapshot.Authored.Count, Tone.Neutral), NeedsData: true),
        new(Page.Reviews, "Pull requests", "", "Review queue",
            c => PrListPages.Reviews(c.Snapshot, c.SelectedId, c.Select),
            c => new PageBadge(WaitingOnYou(c.Snapshot), Tone.Accent), NeedsData: true),

        new(Page.Review, "Pull requests", "\uE943", "Code review", BuildReview),

        new(Page.Drift, "Workspace", "", "Drift",
            c => Component<DriftPage, PageContext>(c)),
        new(Page.Linear, "Workspace", "", "Linear",
            ctx => LinearPage.Build(ctx.Snapshot, null, ctx.Select, ctx.OpenSettings)),
        new(Page.Notes, "Workspace", "", "Notes",
            _ => Component<NotesPage>()),
        new(Page.Stats, "Workspace", "", "Stats",
            ctx => Component<StatsPageHost, PageContext>(ctx)),

        new(Page.Claude, "Workspace", "\uE8BD", "Claude",
            c => Component<DigestPage, PageContext>(c)),

        new(Page.Settings, "", "", "Settings",
            c => Component<SettingsPage, PageContext>(c), Pinned: true),
    };

    private static GitHubReviewData? _liveReview;

    /// <summary>Code review page for the selected PR (or the first review request). Keyed by PR so state resets per PR.</summary>
    private static Element BuildReview(PageContext c)
    {
        ReviewPage.Snapshot = c.Snapshot;
        ReviewPage.PullRequestId = c.SelectedId ?? AppOptions.SelectedPr;
        ReviewPage.InitialFile = null;
        if (c.Services.GitHub is { } hub)
        {
            // Writes stay in dry-run until the user opts in from Settings (not wired yet), so nothing is sent by accident.
            _liveReview ??= new GitHubReviewData(hub.Rest, hub.GraphQl, hub.Writer) { DryRun = true };
            _liveReview.DryRun = !c.Services.Settings.AllowGitHubWrites;
            ReviewPage.Data = _liveReview;
        }

        return Component<ReviewPage>().WithKey(ReviewPage.PullRequestId ?? "none");
    }

    public static PageDef? Find(Page id)
    {
        foreach (var def in All)
            if (def.Id == id) return def;
        return null;
    }

    /// <summary>Review-queue entries that need the viewer: not reviewed yet, or updated since the review.</summary>
    public static int WaitingOnYou(HubSnapshot snap) =>
        snap.Reviews.Count(p => ReviewQueue.StateOf(snap.ReviewInfos.GetValueOrDefault(p.Id)) is
            ReviewQueueState.WaitingOnYou or ReviewQueueState.UpdatedSinceReview);
}
