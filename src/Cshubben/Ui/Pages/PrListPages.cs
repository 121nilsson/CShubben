using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

static class PrListPages
{
    /// <summary>My PRs, grouped by what they need.</summary>
    public static Element Mine(HubSnapshot snap, string? selectedId, Action<string> onSelect)
    {
        var all = snap.Authored.Select(p => new Attention(p, NextAction.For(p))).ToList();
        var groups = new (string Title, Func<Attention, bool> Match)[]
        {
            ("Ready to merge", a => a.Action.Kind == NextActionKind.Merge),
            ("Needs your action", a => a.Action.IsActionable && a.Action.Kind != NextActionKind.Merge),
            ("Waiting on others", a => !a.Action.IsActionable),
        };
        return Page("My PRs", "Open pull requests you created",
            groups.Select(g => Group(g.Title, all.Where(g.Match).OrderBy(a => a.Pr.UpdatedAt).ToList(), snap, selectedId, onSelect)));
    }

    /// <summary>The review queue, grouped by who is waiting for whom.</summary>
    public static Element Reviews(HubSnapshot snap, string? selectedId, Action<string> onSelect)
    {
        var all = snap.Reviews.Select(p =>
        {
            snap.ReviewInfos.TryGetValue(p.Id, out var info);
            return (Item: new Attention(p, NextAction.For(p, info)), State: ReviewQueue.StateOf(info));
        }).ToList();

        var groups = Enum.GetValues<ReviewQueueState>()
            .OrderBy(ReviewQueue.Rank)
            .Select(s => Group(ReviewQueue.Label(s),
                all.Where(x => x.State == s).Select(x => x.Item).OrderBy(a => a.Pr.UpdatedAt).ToList(),
                snap, selectedId, onSelect));
        return Page("Review queue", "Pull requests where you are a reviewer", groups);
    }

    private static Element Group(string title, IReadOnlyList<Attention> items, HubSnapshot snap, string? selectedId, Action<string> onSelect) =>
        items.Count == 0
            ? Empty()
            : Card(FlexColumn(
                Kit.Heading2(title, items.Count).Margin(0, 0, 0, 8),
                FlexColumn(items.Select(a => Rows.PrRow(a.Pr, a.Action, a.Pr.Id == selectedId, snap.At, onSelect)).ToArray())))
                .Margin(0, 0, 0, 12);

    private static Element Page(string title, string subtitle, IEnumerable<Element> groups) =>
        ScrollView(FlexColumn(new[] { Kit.PageHeader(title, subtitle) }.Concat(groups).ToArray()).FlexPadding(24, 16));
}
