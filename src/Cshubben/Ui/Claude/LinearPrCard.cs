using Cshubben.Core.Shared;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

/// <summary>
/// A card in the pull request panel with the Linear issue the PR belongs to: state, title, who has it and the start of its text,
/// so the context of the change is visible without leaving the app. Nothing without a Linear connection; a quiet line when the PR names no issue.
/// </summary>
sealed class LinearPrCard : Component<PullRequest>
{
    private const int PreviewChars = 420;

    public override Element Render()
    {
        var pr = Props;
        var linear = IntegrationServices.Linear;
        var identifier = linear.IsConnected ? linear.IdentifierFor(pr) : null;
        var (expanded, setExpanded) = UseState(false);

        var resource = UseResource<LinearIssue?>(
            async ct =>
            {
                if (identifier is null) return null;
                return await PageCaches.Issues.GetAsync(identifier, PageCaches.IssueTtl, async () =>
                    await linear.GetIssueAsync(identifier, ct) ?? throw new KeyNotFoundException(identifier));
            },
            new object[] { pr.Id, identifier ?? "", linear.IsConnected });

        if (!linear.IsConnected) return Empty();
        if (identifier is null)
        {
            // Only on my own pull requests: on other people's the missing link is normal and not worth a card.
            return pr.Role == PrRole.Authored
                ? Card(VStack(4, Parts.Label("Linear"), Kit.Muted("No Linear issue is named in this pull request's title or branch. The Linear button above creates one.")))
                : Empty();
        }

        var issue = Parts.Peek(resource) ?? PageCaches.Issues.Peek(identifier);
        if (issue is null)
        {
            return Card(VStack(6,
                Parts.Label("Linear"),
                HStack(8, Kit.Pill(identifier, Tone.Accent),
                    Parts.IsLoading(resource) ? Kit.Muted("Loading…") : Kit.Muted("Not found in Linear."))));
        }

        var meta = new List<string> { issue.PriorityLabel };
        if (!string.IsNullOrEmpty(issue.Assignee)) meta.Add(issue.Assignee!);
        if (!string.IsNullOrEmpty(issue.Project)) meta.Add(issue.Project!);

        var text = MarkdownTidy.Tidy(issue.Description);
        var (preview, long_) = MarkdownTidy.Preview(issue.Description, PreviewChars);
        var shown = long_ && !expanded ? preview : text;

        return Card(VStack(8,
            Parts.Label("Linear"),
            FlexRow(Kit.Pill(issue.Identifier, Tone.Accent), Kit.Pill(issue.StateName, StateTone(issue.StateType)))
                with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 4 },
            TextBlock(issue.Title).SemiBold().TextWrapping(),
            Kit.Muted(string.Join(" · ", meta)),
            text.Length == 0 ? null : Kit.Md(shown),
            long_ ? Button(expanded ? "Show less" : "Show more", () => setExpanded(!expanded)).SubtleButton().HAlign(HorizontalAlignment.Left) : null,
            FlexRow(
                Button("Open in Linear", () => ExternalActions.OpenUrl(issue.Url)).SubtleButton().AutomationName("Open " + issue.Identifier + " in Linear"),
                CopyLinkButton.For(issue.Url, "Copy link", issue.Identifier + " " + issue.Title))
                with { ColumnGap = 6 }));
    }

    private static Tone StateTone(LinearStateType type) => type switch
    {
        LinearStateType.Started => Tone.Accent,
        LinearStateType.Completed => Tone.Success,
        _ => Tone.Neutral,
    };
}
