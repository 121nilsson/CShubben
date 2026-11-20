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

        var selected = snap.Find(selectedId);
        var overview = OverviewModel.Compute(snap);
        var waitingOnYou = snap.Reviews.Count(p => ReviewQueue.StateOf(snap.ReviewInfos.GetValueOrDefault(p.Id)) is
            ReviewQueueState.WaitingOnYou or ReviewQueueState.UpdatedSinceReview);

        void Select(string id) { setSelected(id); setTab(0); }

        Element Item(Page target, string glyph, string label, int? count = null, Tone tone = Tone.Neutral)
        {
            var active = page == target;
            var row = Grid(
                columns: [GridSize.Px(28), GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                Kit.Glyph(glyph).VAlign(VerticalAlignment.Center).Grid(column: 0),
                (active ? BodyStrong(label) : Body(label)).VAlign(VerticalAlignment.Center).Grid(column: 1),
                count is > 0 ? Kit.Pill(count.Value.ToString(), tone).Grid(column: 2) : Empty());
            return Border(Button(row, () => setPage(target))
                    .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .AutomationName(label))
                .Selected(active).CornerRadius(6);
        }

        var sidebar = FlexColumn(
                HStack(8, TextBlock("◆").FontSize(18).Foreground(Theme.AccentText), TextBlock("Cshubben").FontSize(16).SemiBold())
                    .Margin(12, 8, 0, 8),
                Kit.SectionLabel("Overview"),
                Item(Page.Overview, "", "Overview", overview.NeedsYou, Tone.Warning),
                Item(Page.Feed, "", "Feed"),
                Kit.SectionLabel("Pull requests"),
                Item(Page.Mine, "", "My PRs", snap.Authored.Count),
                Item(Page.Reviews, "", "Review queue", waitingOnYou, Tone.Accent),
                Kit.SectionLabel("Workspace"),
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
            Page.Notes => Kit.ComingSoon("Notes", "Ideas, notes and files in one place"),
            Page.Stats => Kit.ComingSoon("Stats", "Cycle time and review load"),
            _ => Kit.ComingSoon("Settings", "Repos, notifications and integrations"),
        };

        var main = Border(content).Background(Theme.LayerFill).CornerRadius(8).Margin(0, 0, selected is null ? 8 : 8, 8);

        Element grid = selected is null
            ? Grid(columns: [GridSize.Px(248), GridSize.Star()], rows: [GridSize.Star()],
                sidebar.Grid(column: 0), main.Grid(column: 1))
            : Grid(columns: [GridSize.Px(248), GridSize.Star(), GridSize.Px(460)], rows: [GridSize.Star()],
                sidebar.Grid(column: 0), main.Grid(column: 1),
                Inspector.Build(selected, snap, tab, setTab, () => setSelected(null)).Grid(column: 2));

        return FlexColumn(TitleBar("Cshubben").Flex(shrink: 0), grid.Flex(grow: 1, basis: 0))
            .Backdrop(BackdropKind.Mica);
    }
}
