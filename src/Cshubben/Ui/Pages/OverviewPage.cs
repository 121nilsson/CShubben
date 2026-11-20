using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

static class OverviewPage
{
    public static Element Build(HubSnapshot snap, string? selectedId, Action<string> onSelect)
    {
        var o = OverviewModel.Compute(snap);

        Element Tile(string label, int value, Tone tone, string hint) =>
            Card(VStack(2,
                Kit.Muted(label),
                TextBlock(value.ToString()).FontSize(32).SemiBold().Foreground(value > 0 ? Kit.Fg(tone) : Theme.PrimaryText),
                Kit.Muted(hint))).Flex(grow: 1, basis: 0);

        Element Section(string title, string empty, IReadOnlyList<Attention> items) =>
            Card(FlexColumn(
                Kit.Heading2(title, items.Count).Margin(0, 0, 0, 8),
                items.Count == 0
                    ? Kit.EmptyNote(empty)
                    : FlexColumn(items.Select(a =>
                        Rows.PrRow(a.Pr, a.Action, a.Pr.Id == selectedId, snap.At, onSelect)).ToArray())))
            ;

        return ScrollView(
            FlexColumn(
                Kit.PageHeader("Overview", "What needs you right now"),
                (FlexRow(
                    Tile("Needs you", o.NeedsYou, o.NeedsYou > 0 ? Tone.Warning : Tone.Neutral, "across your PRs and reviews"),
                    Tile("Ready to merge", o.ReadyToMerge.Count, Tone.Success, "approved, green, no conflicts"),
                    Tile("Reviews for you", o.ReviewsForYou.Count, Tone.Accent, "waiting on your review"),
                    Tile("Waiting on others", o.Waiting, Tone.Neutral, "nothing for you to do")) with { ColumnGap = 12 })
                    .Margin(0, 0, 0, 16),
                Section("Ready to merge", "Nothing is ready to merge.", o.ReadyToMerge).Margin(0, 0, 0, 12),
                Section("Needs your action", "All clear.", o.NeedsAction).Margin(0, 0, 0, 12),
                Section("Reviews waiting on you", "No reviews waiting.", o.ReviewsForYou))
            .FlexPadding(24, 16));
    }
}
