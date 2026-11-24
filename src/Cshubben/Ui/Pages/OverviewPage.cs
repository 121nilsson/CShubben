using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

static class OverviewPage
{
    public static Element Build(HubSnapshot snap, string? selectedId, Action<string> onSelect, bool compact = false)
    {
        var o = OverviewModel.Compute(snap);

        Element Tile(string label, int value, Tone tone, string hint) =>
            Card(VStack(2,
                Kit.Muted(label),
                TextBlock(value.ToString()).FontSize(32).SemiBold().Foreground(value > 0 ? Kit.Fg(tone) : Theme.PrimaryText),
                Kit.Muted(hint).TextWrapping())).Flex(grow: 1, basis: 200);

        Element Section(string title, string empty, IReadOnlyList<Attention> items) =>
            Card(FlexColumn(
                Kit.Heading2(title, items.Count).Margin(0, 0, 0, 8),
                items.Count == 0
                    ? Kit.EmptyNote(empty)
                    : FlexColumn(items.Select(a =>
                        Rows.PrRow(a.Pr, a.Action, a.Pr.Id == selectedId, snap.At, onSelect, null, compact).WithKey(a.Pr.Id)).ToArray())))
            ;

        var nothingYet = snap.Authored.Count == 0 && snap.Reviews.Count == 0;
        Element emptyNote = nothingYet
            ? Card(VStack(6,
                BodyStrong("No pull requests found"),
                Kit.Muted("You have no open pull requests and no review requests right now. If that is not what you expect, "
                    + "check the muted repositories and organisations in Settings, and that your GitHub token can see those repositories "
                    + "(Settings, Run checks, tells you).").TextWrapping())).Margin(0, 0, 0, 16)
            : Empty();

        return ScrollView(Kit.Capped(
            FlexColumn(
                Kit.PageHeader("Overview", "What needs you right now"),
                emptyNote,
                Border(Component<BriefingCard, BriefingProps>(new BriefingProps(snap, onSelect))).Margin(0, 0, 0, 16),
                (FlexRow(
                    Tile("Needs you", o.NeedsYou, o.NeedsYou > 0 ? Tone.Warning : Tone.Neutral, "across your PRs and reviews"),
                    Tile("Ready to merge", o.ReadyToMerge.Count, Tone.Success, "approved, green, no conflicts"),
                    Tile("Reviews for you", o.ReviewsForYou.Count, Tone.Accent, "waiting on your review"),
                    Tile("Waiting on others", o.Waiting, Tone.Neutral, "nothing for you to do")) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 })
                    .Margin(0, 0, 0, 16),
                Section("Ready to merge", "Nothing is ready to merge.", o.ReadyToMerge).Margin(0, 0, 0, 12),
                Section("Needs your action", "All clear.", o.NeedsAction).Margin(0, 0, 0, 12),
                Section("Reviews waiting on you", "No reviews waiting.", o.ReviewsForYou))
            .FlexPadding(24, 16)));
    }
}
