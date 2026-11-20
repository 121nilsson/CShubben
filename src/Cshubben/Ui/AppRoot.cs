using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Ui.Pages;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

/// <summary>Window shell: sidebar, current page and (when a PR is selected) the inspector on the right.</summary>
sealed class AppRoot : Component
{
    public override Element Render()
    {
        var snap = AppOptions.Snapshot;
        var (page, setPage) = UseState(AppOptions.StartPage);
        var (selectedId, setSelected) = UseState<string?>(AppOptions.SelectedPr);
        var (tab, setTab) = UseState(AppOptions.StartTab);
        var (windowWidth, _) = UseWindowSize();

        // Responsive: on narrow windows the sidebar becomes an icon rail and the inspector gets slimmer,
        // so the page in the middle keeps enough room for its rows.
        var compact = windowWidth < 1200;
        var railWidth = compact ? 64.0 : 248.0;
        var inspectorWidth = windowWidth >= 1500 ? 480.0 : windowWidth >= 1200 ? 420.0 : 360.0;

        var selected = snap.Find(selectedId);
        var overview = OverviewModel.Compute(snap);
        var waitingOnYou = snap.Reviews.Count(p => ReviewQueue.StateOf(snap.ReviewInfos.GetValueOrDefault(p.Id)) is
            ReviewQueueState.WaitingOnYou or ReviewQueueState.UpdatedSinceReview);

        void Select(string id) { setSelected(id); setTab(0); }

        Element Item(Page target, string glyph, string label, int? count = null, Tone tone = Tone.Neutral)
        {
            var active = page == target;
            Element row = compact
                ? VStack(2,
                    Kit.Glyph(glyph).HAlign(HorizontalAlignment.Center),
                    count is > 0
                        ? TextBlock(count.Value.ToString()).FontSize(10).SemiBold().Foreground(Kit.Fg(tone)).HAlign(HorizontalAlignment.Center)
                        : null)
                : Grid(
                    columns: [GridSize.Px(28), GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    Kit.Glyph(glyph).VAlign(VerticalAlignment.Center).Grid(column: 0),
                    (active ? BodyStrong(label) : Body(label)).VAlign(VerticalAlignment.Center).Grid(column: 1),
                    count is > 0 ? Kit.Pill(count.Value.ToString(), tone).Grid(column: 2) : Empty());
            return Border(Button(row, () => setPage(target))
                    .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .ToolTip(label).AutomationName(label))
                .Selected(active).CornerRadius(6);
        }

        Element Section(string text) => compact ? Border(Empty()).Height(12) : Kit.SectionLabel(text);

        var sidebar = FlexColumn(
                (compact
                    ? (Element)TextBlock("◆").FontSize(18).Foreground(Theme.AccentText).HAlign(HorizontalAlignment.Center)
                    : HStack(8, TextBlock("◆").FontSize(18).Foreground(Theme.AccentText), TextBlock("Cshubben").FontSize(16).SemiBold()))
                    .Margin(compact ? 0 : 12, 8, 0, 8),
                Section("Overview"),
                Item(Page.Overview, "", "Overview", overview.NeedsYou, Tone.Warning),
                Item(Page.Feed, "", "Feed"),
                Section("Pull requests"),
                Item(Page.Mine, "", "My PRs", snap.Authored.Count),
                Item(Page.Reviews, "", "Review queue", waitingOnYou, Tone.Accent),
                Section("Workspace"),
                Item(Page.Drift, "", "Drift"),
                Item(Page.Linear, "", "Linear"),
                Item(Page.Notes, "", "Notes"),
                Item(Page.Stats, "", "Stats"),
                Empty().Flex(grow: 1),
                Item(Page.Settings, "", "Settings"))
            .FlexPadding(8, 4);

        Element content = page switch
        {
            Page.Overview => OverviewPage.Build(snap, selectedId, Select),
            Page.Feed => FeedPage.Build(snap),
            Page.Mine => PrListPages.Mine(snap, selectedId, Select),
            Page.Reviews => PrListPages.Reviews(snap, selectedId, Select),
            Page.Drift => Kit.ComingSoon("Drift", "Actions and deployments on your repos"),
            Page.Linear => Kit.ComingSoon("Linear", "Your issues, linked to pull requests"),
            Page.Notes => Component<NotesPage>(),
            Page.Stats => Kit.ComingSoon("Stats", "Cycle time and review load"),
            _ => Kit.ComingSoon("Settings", "Repos, notifications and integrations"),
        };

        var main = Border(content).Background(Theme.LayerFill).CornerRadius(8).Margin(0, 0, selected is null ? 8 : 8, 8);

        Element grid = selected is null
            ? Grid(columns: [GridSize.Px(railWidth), GridSize.Star()], rows: [GridSize.Star()],
                sidebar.Grid(column: 0), main.Grid(column: 1))
            : Grid(columns: [GridSize.Px(railWidth), GridSize.Star(), GridSize.Px(inspectorWidth)], rows: [GridSize.Star()],
                sidebar.Grid(column: 0), main.Grid(column: 1),
                Inspector.Build(selected, snap, tab, setTab, () => setSelected(null)).Grid(column: 2));

        return FlexColumn(TitleBar("Cshubben").Flex(shrink: 0), grid.Flex(grow: 1, basis: 0))
            .Backdrop(BackdropKind.Mica);
    }
}
