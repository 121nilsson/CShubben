using Cshubben.Core.Features.Feed;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

sealed record FeedProps(HubSnapshot Snap, Action<string>? OnSelect);

/// <summary>
/// The activity feed. <c>FeedPage.Build(snapshot, onSelectPr)</c>; the callback is optional (without it rows only expand).
/// All logic (filters, grouping, the new/seen divider) lives in <c>Cshubben.Core.Features.Feed</c>; this page only draws it.
/// </summary>
static class FeedPage
{
    public static Element Build(HubSnapshot snap, Action<string>? onSelect = null) =>
        Component<FeedView, FeedProps>(new FeedProps(snap, onSelect));
}

sealed class FeedView : Component<FeedProps>
{
    public override Element Render()
    {
        var snap = Props.Snap;
        var services = AppServices.Current;
        var settings = services.Settings;

        var (scope, setScope) = UseState(FeedScope.All);
        var (showBots, setShowBots) = UseState(false);
        var (expanded, setExpanded) = UseState<IReadOnlyList<string>>(Array.Empty<string>());
        var (seenTick, setSeenTick) = UseState(0);
        _ = seenTick; // changing it re-renders the page after "Mark all seen"

        var options = new FeedOptions
        {
            Scope = scope,
            ShowBots = showBots,
            FollowedRepos = settings.FollowedRepos,
            MutedRepos = settings.MutedRepos,
            MutedOrgs = settings.MutedOrgs,
        };
        var visible = FeedFiltering.Apply(snap.Feed, options);
        var counts = FeedFiltering.Count(snap.Feed, options);
        var layout = FeedLayout.Build(visible, services.FeedSeen.Load(), snap.At);

        void MarkAllSeen()
        {
            services.FeedSeen.Save(FeedFiltering.MarkAllSeenAt(snap.Feed, snap.At));
            setSeenTick(seenTick + 1);
        }

        void Toggle(string key) =>
            setExpanded(expanded.Contains(key) ? expanded.Where(k => k != key).ToList() : expanded.Concat(new[] { key }).ToList());

        void ShowEverything()
        {
            setScope(FeedScope.All);
            setShowBots(true);
        }

        var subtitle = layout.HasNew
            ? layout.NewEvents + (layout.NewEvents == 1 ? " new event" : " new events") + " since you last looked"
            : "You are all caught up";

        var bar = FilterBar(scope, setScope, showBots, setShowBots, counts, layout.HasNew, MarkAllSeen);

        Element body = layout.IsEmpty
            ? EmptyState(snap.Feed.Count == 0, ShowEverything)
            : Kit.Capped(FlexColumn(layout.Entries.Select(e => Entry(e, snap, expanded, Toggle)).ToArray()) with { RowGap = 2 });

        var column = FlexColumn(
            Kit.PageHeader("Feed", subtitle).Margin(0, 0, 0, 0),
            bar,
            ScrollView(body).Flex(grow: 1, basis: 0)) with { RowGap = 12 };
        return column.FlexPadding(24, 16, 16, 16);
    }

    private Element Entry(FeedEntry entry, HubSnapshot snap, IReadOnlyList<string> expanded, Action<string> toggle)
    {
        switch (entry.Kind)
        {
            case FeedEntryKind.NewHeader:
                return HStack(8,
                    BodyStrong(entry.Label).Foreground(Theme.AccentText),
                    Kit.Pill(entry.Count.ToString(), Tone.Accent)).Margin(4, 4, 0, 4).WithKey("hdr-new");
            case FeedEntryKind.Divider:
                return Grid(
                    columns: [GridSize.Star(), GridSize.Auto, GridSize.Star()],
                    rows: [GridSize.Auto],
                    Border(Empty()).Height(1).Background(Theme.CardStroke).VAlign(VerticalAlignment.Center).Grid(column: 0),
                    Kit.Muted(entry.Label).Margin(10, 0).Grid(column: 1),
                    Border(Empty()).Height(1).Background(Theme.CardStroke).VAlign(VerticalAlignment.Center).Grid(column: 2))
                    .Margin(4, 10, 4, 6).WithKey("divider");
            case FeedEntryKind.DayHeader:
                return Kit.Heading2(entry.Label, entry.Count).Margin(4, 12, 0, 4).WithKey("day-" + entry.Label);
            default:
                return GroupEntry(entry.Group!, snap, expanded.Contains(entry.Group!.Key), toggle);
        }
    }

    private Element GroupEntry(FeedGroup group, HubSnapshot snap, bool isExpanded, Action<string> toggle)
    {
        var prId = FindPr(snap, group);
        Action openPr = () =>
        {
            if (prId is not null) Props.OnSelect?.Invoke(prId);
        };

        var row = Rows.FeedGroupRow(group, snap.At, isExpanded, group.IsCollapsed ? () => toggle(group.Key) : openPr);
        if (!group.IsCollapsed || !isExpanded) return row;

        var children = group.Items.Select(i => Rows.FeedEventRow(i, snap.At, group.IsNew, openPr)).ToArray();
        return FlexColumn(new[] { row }.Concat(children).ToArray()).WithKey(group.Key);
    }

    private static string? FindPr(HubSnapshot snap, FeedGroup group)
    {
        if (group.Number is not int number) return null;
        return snap.All
            .FirstOrDefault(p => p.Number == number && string.Equals(p.Repo, group.Repo, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static Element FilterBar(
        FeedScope scope,
        Action<FeedScope> setScope,
        bool showBots,
        Action<bool> setShowBots,
        FeedCounts counts,
        bool hasNew,
        Action markAllSeen)
    {
        Element Chip(string label, int count, bool active, Action<bool> onChange) =>
            ToggleButton(TextBlock(label + " " + count).FontSize(12), active, onChange).Padding(10, 3).MinHeight(28);

        var items = new List<Element>
        {
            Chip("All", counts.All, scope == FeedScope.All, _ => setScope(FeedScope.All)).WithKey("all"),
            Chip("Personal", counts.Personal, scope == FeedScope.Personal, on => setScope(on ? FeedScope.Personal : FeedScope.All)).WithKey("personal"),
            Chip("Team", counts.Team, scope == FeedScope.Team, on => setScope(on ? FeedScope.Team : FeedScope.All)).WithKey("team"),
            Chip("Bots", counts.Bots, showBots, setShowBots).WithKey("bots"),
            Empty().Flex(grow: 1),
            (hasNew
                ? Button("Mark all seen", markAllSeen).AccentButton()
                : Button("Mark all seen", markAllSeen).IsEnabled(false)).MinHeight(28).WithKey("mark"),
        };

        return FlexRow(items.ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 6 };
    }

    private static Element EmptyState(bool feedIsEmpty, Action showEverything)
    {
        if (feedIsEmpty)
        {
            return VStack(6,
                BodyStrong("No activity yet"),
                Kit.Muted("Reviews, comments, pushes and CI results from your repos show up here as they happen.").TextWrapping())
                .Margin(4, 24);
        }

        return VStack(8,
            BodyStrong("Nothing matches these filters"),
            Kit.Muted("Bots are hidden by default, and muted or unfollowed repos never show here.").TextWrapping(),
            Button("Show everything", showEverything).HAlign(HorizontalAlignment.Left))
            .Margin(4, 24);
    }
}
