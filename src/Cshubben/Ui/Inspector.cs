using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

/// <summary>
/// The right-hand panel for a pull request. Built for a fast decision: the state of the PR is readable in one
/// glance (status strip), the single best next step is the one big button, and the integrations (Claude, Linear,
/// notes) are one click away instead of buried at the bottom of a long scroll.
/// </summary>
static class Inspector
{
    public static readonly string[] Tabs = ["Overview", "Conversation", "Files", "Pipeline"];

    public static Element Build(PullRequest pr, HubSnapshot snap, int tab, Action<int> setTab, Action close)
    {
        snap.ReviewInfos.TryGetValue(pr.Id, out var info);
        var action = NextAction.For(pr, info);

        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            VStack(4,
                Kit.Muted($"{pr.Repo} · #{pr.Number}"),
                TextBlock(pr.Title).FontSize(18).SemiBold().TextWrapping()).Grid(column: 0),
            Button("✕", close).SubtleButton().AutomationName("Close").VAlign(VerticalAlignment.Top).Grid(column: 1));

        var status = (FlexRow(
            Chip("CI", CiText(pr.Ci), CiTone(pr.Ci)),
            Chip("Review", ReviewText(pr.Review), ReviewTone(pr.Review)),
            Chip("Threads", pr.UnresolvedThreads == 0 ? "None open" : $"{pr.UnresolvedThreads} open",
                pr.UnresolvedThreads == 0 ? Tone.Neutral : Tone.Warning),
            Chip("Merge", MergeText(pr.Merge), MergeTone(pr.Merge))) with { ColumnGap = 8 });

        var nextStep = Border(VStack(10,
                Kit.Muted("NEXT STEP"),
                action.IsActionable
                    ? Button(action.Label, () => { }).AccentButton().HAlign(HorizontalAlignment.Stretch)
                    : TextBlock(action.Label).FontSize(16).SemiBold().Foreground(Kit.Fg(action.Tone)),
                Kit.Muted(action.Reason).TextWrapping()))
            .Background(Kit.Bg(action.Tone)).CornerRadius(8).Padding(16);

        var quick = (FlexRow(
            pr.Role == PrRole.ReviewRequested ? Button("Approve", () => { }).SubtleButton() : null,
            pr.Role == PrRole.ReviewRequested ? Button("Request changes", () => { }).SubtleButton() : null,
            Button("✦ Ask Claude", () => { }).SubtleButton(),
            Button("◇ Linear issue", () => { }).SubtleButton(),
            Button("✎ Save to notes", () => { }).SubtleButton(),
            Button("↗ GitHub", () => { }).SubtleButton()) with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 });

        var tabs = SelectorBar([.. Tabs.Select(t => SelectorBarItem(t))], tab, setTab);

        Element body = tab switch
        {
            0 => OverviewTab(pr, snap.At),
            _ => Card(VStack(6, BodyStrong(Tabs[tab]), Kit.Muted("Coming soon.")))
        };

        return Border(FlexColumn(
                header.Flex(shrink: 0),
                status.Flex(shrink: 0),
                nextStep.Flex(shrink: 0),
                quick.Flex(shrink: 0),
                tabs.Flex(shrink: 0),
                ScrollView(body).Flex(grow: 1, basis: 0)) with { RowGap = 12 })
            .Background(Theme.LayerFill).CornerRadius(8).Padding(20).Margin(0, 0, 8, 8);
    }

    private static Element OverviewTab(PullRequest pr, DateTimeOffset now)
    {
        var total = Math.Max(1, pr.Additions + pr.Deletions);
        var changes = Grid(
            columns: [GridSize.Star(Math.Max(pr.Additions, 0.0001)), GridSize.Star(Math.Max(pr.Deletions, 0.0001))],
            rows: [GridSize.Auto],
            Border(Empty()).Height(6).Background(Theme.SystemSuccess).Grid(column: 0),
            Border(Empty()).Height(6).Background(Theme.SystemCritical).Grid(column: 1));

        var facts = Card(VStack(10,
            HStack(12,
                VStack(0, Kit.Muted("Author"), BodyStrong(pr.Author)),
                VStack(0, Kit.Muted("Opened"), BodyStrong(Age.Format(pr.CreatedAt, now) + " ago")),
                VStack(0, Kit.Muted("Updated"), BodyStrong(Age.Format(pr.UpdatedAt, now) + " ago")),
                VStack(0, Kit.Muted("Comments"), BodyStrong(pr.Comments.ToString()))),
            changes,
            Kit.Muted($"+{pr.Additions}  −{pr.Deletions}  ·  {pr.ChangedFiles} files  ·  size {pr.SizeLabel}")));

        var description = Card(VStack(6, BodyStrong("Description"), Markdown(pr.Description)));
        return FlexColumn(facts, description) with { RowGap = 12 };
    }

    private static Element Chip(string label, string value, Tone tone) =>
        Border(VStack(2, Kit.Muted(label), TextBlock(value).SemiBold().Foreground(Kit.Fg(tone))))
            .Background(Theme.CardBackground).WithBorder(Theme.ControlStroke).CornerRadius(8).Padding(10, 8)
            .Flex(grow: 1, basis: 0);

    private static string CiText(CiState s) => s switch { CiState.Success => "Passing", CiState.Failure => "Failing", CiState.Pending => "Running", _ => "–" };
    private static Tone CiTone(CiState s) => s switch { CiState.Success => Tone.Success, CiState.Failure => Tone.Danger, CiState.Pending => Tone.Warning, _ => Tone.Neutral };
    private static string ReviewText(ReviewDecision r) => r switch { ReviewDecision.Approved => "Approved", ReviewDecision.ChangesRequested => "Changes", ReviewDecision.ReviewRequired => "Required", _ => "–" };
    private static Tone ReviewTone(ReviewDecision r) => r switch { ReviewDecision.Approved => Tone.Success, ReviewDecision.ChangesRequested => Tone.Warning, _ => Tone.Neutral };
    private static string MergeText(MergeState m) => m switch { MergeState.Mergeable => "Clean", MergeState.Conflicting => "Conflicts", _ => "Checking" };
    private static Tone MergeTone(MergeState m) => m switch { MergeState.Mergeable => Tone.Success, MergeState.Conflicting => Tone.Danger, _ => Tone.Neutral };
}
