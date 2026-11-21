using Cshubben.Core.Features.Linear;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// The side panel of the Linear page: what the issue says (its text), who has it, its state, labels, the pull requests that
/// implement it, and links to open or copy. Read-only; the writes (mark done, create) stay in the "Out of sync" rows.
/// </summary>
static class LinearDetail
{
    public static Element Build(
        LinearIssue issue,
        IReadOnlyList<PullRequest>? openPrs,
        IReadOnlyList<ReconPr>? mergedPrs,
        DateTimeOffset now,
        Action<string> onSelectPr,
        Action close)
    {
        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            VStack(6,
                FlexRow(Kit.Pill(issue.Identifier, Tone.Accent), Kit.Pill(issue.StateName, StateTone(issue.StateType)))
                    with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 4 },
                TextBlock(issue.Title).FontSize(18).SemiBold().TextWrapping()).Grid(column: 0),
            Button("✕", close).SubtleButton().AutomationName("Close the issue panel").VAlign(VerticalAlignment.Top).Grid(column: 1));

        Element Fact(string label, Element value) => VStack(0, Kit.Muted(label), value);

        var facts = new List<Element>
        {
            Fact("Assignee", string.IsNullOrEmpty(issue.Assignee)
                ? BodyStrong("Unassigned")
                : HStack(6, Avatars.Of(issue.Assignee!, 20, issue.AssigneeAvatarUrl), BodyStrong(issue.Assignee!))),
            Fact("Priority", BodyStrong(issue.PriorityLabel)),
            Fact("Updated", BodyStrong(Age.Format(issue.CompletedAt ?? issue.UpdatedAt, now) + " ago")),
        };
        if (!string.IsNullOrEmpty(issue.Project)) facts.Add(Fact("Project", BodyStrong(issue.Project!)));
        if (issue.TeamKey.Length > 0) facts.Add(Fact("Team", BodyStrong(issue.TeamKey)));

        var factsCard = Card(VStack(10,
            FlexRow(facts.ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 20, RowGap = 8 },
            issue.Labels.Count == 0
                ? null
                : FlexRow(issue.Labels.Select(l => (Element)Kit.Pill(l, Tone.Neutral)).ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 6 }));

        var prButtons = new List<Element>();
        foreach (var pr in openPrs ?? Array.Empty<PullRequest>())
        {
            var id = pr.Id;
            var tone = pr.Ci == CiState.Failure ? Tone.Danger : pr.Merge == MergeState.Conflicting ? Tone.Warning : Tone.Neutral;
            prButtons.Add(Button(
                    HStack(6,
                        TextBlock("●").FontSize(9).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center),
                        TextBlock(pr.Repo + " #" + pr.Number).FontSize(12),
                        TextBlock(pr.Title).FontSize(12).Foreground(Theme.SecondaryText).TextTrimming(TextTrimming.CharacterEllipsis)),
                    () => onSelectPr(id))
                .SubtleButton().HAlign(HorizontalAlignment.Left).AutomationName("Open pull request " + pr.Number)
                .WithKey("open-" + pr.Id));
        }

        foreach (var pr in mergedPrs ?? Array.Empty<ReconPr>())
        {
            var url = pr.Url;
            prButtons.Add(Button(
                    HStack(6,
                        TextBlock("merged").FontSize(12).Foreground(Kit.Fg(Tone.Success)),
                        TextBlock(pr.Repo + " #" + pr.Number).FontSize(12)),
                    () => ExternalActions.OpenUrl(url))
                .SubtleButton().HAlign(HorizontalAlignment.Left).AutomationName("Open merged pull request " + pr.Number)
                .WithKey("merged-" + pr.Repo + "#" + pr.Number));
        }

        Element? prs = prButtons.Count == 0
            ? null
            : Card(VStack(6, BodyStrong("Pull requests"), FlexColumn(prButtons.ToArray()) with { RowGap = 2 }));

        var actions = FlexRow(
            Button("Open in Linear", () => ExternalActions.OpenUrl(issue.Url)).AccentButton().AutomationName("Open " + issue.Identifier + " in Linear"),
            CopyLinkButton.For(issue.Url, "Copy link"),
            CopyLinkButton.For(issue.Url, "Copy Markdown", issue.Identifier + " " + issue.Title))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 6 };

        var description = Card(VStack(6,
            BodyStrong("Description"),
            issue.Description.Trim().Length == 0 ? Kit.Muted("This issue has no description.") : Kit.Md(issue.Description)));

        var scrolling = FlexColumn(
            factsCard.Flex(shrink: 0),
            actions.Flex(shrink: 0),
            prs?.Flex(shrink: 0),
            description.Flex(shrink: 0)) with { RowGap = 12 };

        return Border(FlexColumn(
                header.Flex(shrink: 0),
                ScrollView(scrolling).Flex(grow: 1, basis: 0)) with { RowGap = 12 })
            .Background(Theme.LayerFill).CornerRadius(8).Padding(20).Margin(0, 0, 8, 8);
    }

    private static Tone StateTone(LinearStateType type) => type switch
    {
        LinearStateType.Started => Tone.Accent,
        LinearStateType.Completed => Tone.Success,
        _ => Tone.Neutral,
    };
}
