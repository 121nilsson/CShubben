using Cshubben.Core.Features.Linear;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

sealed record LinearProps(HubSnapshot Snap, ILinearData Data, Action<string> OnSelectPr, Action? OpenSettings);

/// <summary>My open issues by state and the places where Linear and GitHub disagree.</summary>
sealed record LinearGroups(
    IReadOnlyList<LinearIssue> InProgress,
    IReadOnlyList<LinearIssue> Todo,
    IReadOnlyList<LinearIssue> Backlog,
    IReadOnlyList<LinearIssue> Done);

/// <summary>Grouping and ordering of the board. Pure, so it could move to Core when it is needed elsewhere.</summary>
static class LinearGrouping
{
    /// <summary>Urgent first, then high, medium, low; "no priority" last.</summary>
    public static int Rank(LinearIssue issue) => issue.Priority is >= 1 and <= 4 ? issue.Priority : 5;

    private static IReadOnlyList<LinearIssue> Sort(IEnumerable<LinearIssue> issues) =>
        issues.OrderBy(Rank).ThenByDescending(i => i.UpdatedAt).ThenBy(i => i.Identifier, StringComparer.Ordinal).ToList();

    public static LinearGroups Group(LinearBoard board)
    {
        var open = board.Issues.Where(i => !i.IsDone).ToList();
        var done = board.RecentlyDone.Concat(board.Issues.Where(i => i.IsDone))
            .GroupBy(i => i.Id, StringComparer.Ordinal).Select(g => g.First())
            .OrderByDescending(i => i.CompletedAt ?? i.UpdatedAt)
            .ToList();
        return new LinearGroups(
            Sort(open.Where(i => i.StateType == LinearStateType.Started)),
            Sort(open.Where(i => i.StateType is LinearStateType.Unstarted or LinearStateType.Triage)),
            Sort(open.Where(i => i.StateType is LinearStateType.Backlog or LinearStateType.Unknown)),
            done);
    }
}

/// <summary>
/// The Linear page. Wire it as <c>LinearPage.Build(snap, IntegrationServices.Linear, onSelectPr, openSettings)</c>; the data and
/// callbacks are optional (data defaults to <c>IntegrationServices.Linear</c>).
/// </summary>
static class LinearPage
{
    public static Element Build(HubSnapshot snap, ILinearData? data = null, Action<string>? onSelectPr = null, Action? openSettings = null) =>
        Component<LinearView, LinearProps>(new LinearProps(snap, data ?? IntegrationServices.Linear, onSelectPr ?? (_ => { }), openSettings));
}

sealed class LinearView : Component<LinearProps>
{
    private const int DoneShown = 5;

    public override Element Render()
    {
        var snap = Props.Snap;
        var data = Props.Data;
        var openPrInspector = Props.OnSelectPr;

        var (tick, setTick) = UseState(0, true);
        var (activeKey, setActiveKey) = UseState("");
        var (pick, setPick) = UseState(0);
        var (busyKey, setBusyKey) = UseState("", true);
        var (status, setStatus) = UseState("", true);
        var (statusTone, setStatusTone) = UseState(Tone.Success, true);
        var (resolved, setResolved) = UseState<IReadOnlyList<string>>(Array.Empty<string>(), true);
        var (showAllDone, setShowAllDone) = UseState(false);
        var (selectedIssue, setSelectedIssue) = UseState(AppOptions.SelectedIssue ?? "");
        // Opening a pull request from here closes the issue panel, so two side panels never compete for the width.
        Action<string> onSelectPr = id => { setSelectedIssue(""); openPrInspector(id); };

        var wantTeams = activeKey.StartsWith(ReconKind.PrWithoutIssue + "|", StringComparison.Ordinal);

        var boardKey = "linear|" + snap.Viewer + "|" + (data.IsDemo ? "demo" : "live");

        var boardResource = UseResource<LinearBoard>(
            async ct =>
            {
                if (!data.IsConnected)
                    return new LinearBoard(Array.Empty<LinearIssue>(), Array.Empty<LinearIssue>(), Array.Empty<ReconPr>());
                return await PageCaches.Linear.GetAsync(boardKey, PageCaches.LinearTtl, () => data.LoadAsync(snap, ct));
            },
            new object[] { data, tick, snap.At, data.IsConnected });

        var teamsResource = UseResource<IReadOnlyList<LinearTeam>>(
            async ct =>
            {
                if (!wantTeams || !data.IsConnected) return Array.Empty<LinearTeam>();
                return await data.GetTeamsAsync(ct);
            },
            new object[] { data, wantTeams });

        void Refresh()
        {
            PageCaches.Linear.Remove(boardKey);
            setTick(tick + 1);
        }

        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Kit.PageHeader("Linear", "Your issues, linked to pull requests").Grid(column: 0),
            data.IsConnected
                ? Button("Refresh", Refresh).SubtleButton().VAlign(VerticalAlignment.Top).AutomationName("Refresh Linear").Grid(column: 1)
                : Empty().Grid(column: 1));

        Element content;
        if (!data.IsConnected)
        {
            content = ConnectState(Props.OpenSettings);
        }
        else
        {
            var board = Parts.Peek(boardResource) ?? PageCaches.Linear.Peek(boardKey);
            if (board is null)
            {
                var failure = boardResource is AsyncValue<LinearBoard>.Error boardError
                    ? ErrorText.Classify(boardError.Exception, "Linear")
                    : null;
                content = failure is null
                    ? Card(Parts.Busy("Loading your issues…"))
                    : Card(VStack(10,
                        Parts.Banner(failure.Title, FailureView.MessageOf(failure), FailureView.ToneOf(failure)),
                        HStack(8, FailureView.Buttons(failure, Refresh, Props.OpenSettings, settingsPrimary: false))));
            }
            else
            {
                content = BoardView(
                    board, snap, data, onSelectPr, status, statusTone, resolved, activeKey, setActiveKey, pick, setPick, busyKey,
                    Parts.Peek(teamsResource) ?? Array.Empty<LinearTeam>(), showAllDone, setShowAllDone,
                    Run, Parts.IsLoading(boardResource) || boardResource is AsyncValue<LinearBoard>.Reloading,
                    selectedIssue, id => setSelectedIssue(id == selectedIssue ? "" : id));
            }
        }

        var left = ScrollView(Kit.Capped(
            FlexColumn(header, content).FlexPadding(24, 16)));

        var shown = selectedIssue.Length == 0 || !data.IsConnected
            ? null
            : (Parts.Peek(boardResource) ?? PageCaches.Linear.Peek(boardKey)) is { } shownBoard
                ? shownBoard.Issues.Concat(shownBoard.RecentlyDone).FirstOrDefault(i => i.Id == selectedIssue || string.Equals(i.Identifier, selectedIssue, StringComparison.OrdinalIgnoreCase))
                : null;
        if (shown is null) return left;

        var linked = LinkedPullRequests(Parts.Peek(boardResource) ?? PageCaches.Linear.Peek(boardKey)!, snap, data);
        return Grid(
            columns: [GridSize.Star(), GridSize.Px(460)],
            rows: [GridSize.Star()],
            left.Grid(column: 0),
            LinearDetail.Build(shown, linked.Open.GetValueOrDefault(shown.Identifier), linked.Merged.GetValueOrDefault(shown.Identifier),
                snap.At, onSelectPr, () => setSelectedIssue("")).Grid(column: 1));

        async Task Run(string key, Func<Task<string>> work)
        {
            setBusyKey(key);
            setStatus("");
            try
            {
                var message = await work();
                setStatus(message);
                setStatusTone(Tone.Success);
                setResolved([.. resolved, key]);
                setActiveKey("");
                PageCaches.Linear.Remove(boardKey);
                setTick(tick + 1);
            }
            catch (Exception ex)
            {
                setStatus(Describe(ex.Message, ex));
                setStatusTone(Tone.Danger);
            }

            setBusyKey("");
        }
    }

    private static string Describe(string message, Exception ex) =>
        ex is LinearAuthException ? "Linear rejected the API key. Check it in Settings." : ErrorText.Classify(ex, "Linear").Message;

    // ------------------------------------------------------------------ connect state

    private static Element ConnectState(Action? openSettings)
    {
        Element? button = openSettings is null
            ? null
            : Button("Open Settings", openSettings).AccentButton().HAlign(HorizontalAlignment.Center).Margin(0, 8, 0, 0);
        return Card(VStack(8,
                TextBlock("◇").FontSize(28).Foreground(Theme.AccentText).HAlign(HorizontalAlignment.Center),
                BodyStrong("Connect your Linear account").HAlign(HorizontalAlignment.Center),
                Kit.Muted("Add a personal API key in Settings. Then your issues show up here, grouped by state, next to the pull requests that implement them, and the page points out where the two disagree.")
                    .TextWrapping().HAlign(HorizontalAlignment.Center).TextAlignment(TextAlignment.Center),
                Kit.Muted("Create a key in Linear under Settings, Account, Security and access. The key stays on this PC.")
                    .TextWrapping().HAlign(HorizontalAlignment.Center).TextAlignment(TextAlignment.Center),
                button))
            .MaxWidth(560).HAlign(HorizontalAlignment.Center);
    }

    // ------------------------------------------------------------------ board

    private static Element BoardView(
        LinearBoard board,
        HubSnapshot snap,
        ILinearData data,
        Action<string> onSelectPr,
        string status,
        Tone statusTone,
        IReadOnlyList<string> resolved,
        string activeKey,
        Action<string> setActiveKey,
        int pick,
        Action<int> setPick,
        string busyKey,
        IReadOnlyList<LinearTeam> teams,
        bool showAllDone,
        Action<bool> setShowAllDone,
        Func<string, Func<Task<string>>, Task> run,
        bool reloading,
        string selectedIssue,
        Action<string> onSelectIssue)
    {
        var groups = LinearGrouping.Group(board);
        var all = board.Issues.Concat(board.RecentlyDone).ToList();
        var items = LinearReconciliation.Compute(board.Prs, all)
            .Where(i => !resolved.Contains(KeyOf(i)))
            .ToList();

        var (openPrs, mergedPrs) = LinkedPullRequests(board, snap, data);

        Element Tile(string label, int value, Tone tone, string hint) =>
            Card(VStack(2,
                Kit.Muted(label),
                TextBlock(value.ToString()).FontSize(32).SemiBold().Foreground(value > 0 && tone != Tone.Neutral ? Kit.Fg(tone) : Theme.PrimaryText),
                Kit.Muted(hint).TextWrapping())).Flex(grow: 1, basis: 160);

        var tiles = (FlexRow(
            Tile("In progress", groups.InProgress.Count, Tone.Neutral, "issues you are working on"),
            Tile("Todo", groups.Todo.Count, Tone.Neutral, "ready to pick up"),
            Tile("Out of sync", items.Count, Tone.Warning, items.Count == 0 ? "Linear and GitHub agree" : "need a look below"),
            Tile("Done recently", groups.Done.Count, Tone.Neutral, "completed lately")) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 })
            .Margin(0, 0, 0, 16);

        Element? statusLine = status.Length > 0
            ? Parts.Banner(statusTone == Tone.Danger ? "That did not work" : "Done", status, statusTone).Margin(0, 0, 0, 12)
            : null;

        Element? reconciliation = items.Count == 0
            ? null
            : Card(FlexColumn(
                    HStack(8, BodyStrong("Out of sync"), Kit.Pill(items.Count.ToString(), Tone.Warning)),
                    Kit.Muted("Places where a pull request and its Linear issue disagree. Each has a one-click fix.").TextWrapping(),
                    FlexColumn(items.Select((item, index) => ReconRow(
                        item, snap, data, onSelectPr, activeKey, setActiveKey, pick, setPick, busyKey, teams, run, index == 0)).ToArray()) with { RowGap = 8 })
                with { RowGap = 8 })
                .Margin(0, 0, 0, 12);

        var doneItems = showAllDone ? groups.Done : groups.Done.Take(DoneShown).ToList();
        Element? moreDone = groups.Done.Count > DoneShown
            ? Button(showAllDone ? "Show fewer" : "Show " + (groups.Done.Count - DoneShown) + " more", () => setShowAllDone(!showAllDone))
                .SubtleButton().HAlign(HorizontalAlignment.Left)
            : null;

        Element Group(string title, string empty, IReadOnlyList<LinearIssue> issues, bool quiet, Element? footer = null) =>
            Card(FlexColumn(
                    Kit.Heading2(title, issues.Count).Margin(0, 0, 0, 8),
                    issues.Count == 0
                        ? Kit.EmptyNote(empty)
                        : FlexColumn(issues.Select(i => IssueRow(i, openPrs, mergedPrs, snap.At, onSelectPr, quiet, i.Id == selectedIssue || string.Equals(i.Identifier, selectedIssue, StringComparison.OrdinalIgnoreCase), onSelectIssue)).ToArray()),
                    footer))
                .Margin(0, 0, 0, 12);

        var nothingAtAll = groups.InProgress.Count + groups.Todo.Count + groups.Backlog.Count + groups.Done.Count == 0;

        return FlexColumn(
            statusLine,
            reloading ? Parts.Busy("Refreshing…").Margin(0, 0, 0, 12) : null,
            tiles,
            reconciliation,
            nothingAtAll
                ? Card(Kit.EmptyNote("No issues are assigned to you in Linear."))
                : FlexColumn(
                    Group("In progress", "Nothing in progress.", groups.InProgress, false),
                    Group("Todo", "Nothing queued.", groups.Todo, false),
                    Group("Backlog", "The backlog is empty.", groups.Backlog, false),
                    Group("Done recently", "Nothing finished lately.", doneItems, true, moreDone)));
    }

    /// <summary>My open pull requests and the merged ones, by Linear issue identifier, for the chips on rows and in the panel.</summary>
    internal static (Dictionary<string, List<PullRequest>> Open, Dictionary<string, List<ReconPr>> Merged) LinkedPullRequests(
        LinearBoard board, HubSnapshot snap, ILinearData data)
    {
        var openPrs = new Dictionary<string, List<PullRequest>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pr in snap.Authored)
        {
            var id = data.IdentifierFor(pr);
            if (id is null) continue;
            if (!openPrs.TryGetValue(id, out var list)) openPrs[id] = list = new List<PullRequest>();
            list.Add(pr);
        }

        var mergedPrs = new Dictionary<string, List<ReconPr>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pr in board.Prs.Where(p => p.Merged))
        {
            var id = LinearLinks.FindIdentifier(pr.Title, pr.Branch);
            if (id is null) continue;
            if (!mergedPrs.TryGetValue(id, out var list)) mergedPrs[id] = list = new List<ReconPr>();
            list.Add(pr);
        }

        return (openPrs, mergedPrs);
    }

    // ------------------------------------------------------------------ issue rows

    private static Element IssueRow(
        LinearIssue issue,
        Dictionary<string, List<PullRequest>> openPrs,
        Dictionary<string, List<ReconPr>> mergedPrs,
        DateTimeOffset now,
        Action<string> onSelectPr,
        bool quiet,
        bool selected,
        Action<string> onSelectIssue)
    {
        var title = TextBlock(issue.Title).TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center);
        if (quiet) title = title.Foreground(Theme.SecondaryText);

        // One calm line, like a board: priority, identifier, title, up to two labels, who, how old.
        var labelPills = issue.Labels.Take(2).Select(l => (Element)Kit.Pill(l, Tone.Neutral)).ToArray();
        var assignee = string.IsNullOrEmpty(issue.Assignee)
            ? (Element)Empty()
            : Avatars.Of(issue.Assignee!, 22, issue.AssigneeAvatarUrl).ToolTip(issue.Assignee!);

        var left = Grid(
            columns: [GridSize.Px(28), GridSize.Px(76), GridSize.Star(), GridSize.Auto, GridSize.Px(30), GridSize.Px(44)],
            rows: [GridSize.Auto],
            PriorityIcon(issue.Priority).VAlign(VerticalAlignment.Center).Grid(column: 0),
            Kit.Muted(issue.Identifier).VAlign(VerticalAlignment.Center).Grid(column: 1),
            title.Grid(column: 2),
            (FlexRow(labelPills) with { ColumnGap = 6 }).VAlign(VerticalAlignment.Center).Margin(8, 0, 4, 0).Grid(column: 3),
            assignee.VAlign(VerticalAlignment.Center).HAlign(HorizontalAlignment.Center).Grid(column: 4),
            Kit.Muted(Age.Format(issue.CompletedAt ?? issue.UpdatedAt, now)).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 5));

        var chips = new List<Element>();
        if (openPrs.TryGetValue(issue.Identifier, out var open))
        {
            foreach (var pr in open)
            {
                var id = pr.Id;
                var tone = pr.Ci == CiState.Failure ? Tone.Danger : pr.Merge == MergeState.Conflicting ? Tone.Warning : Tone.Neutral;
                chips.Add(Button(
                        HStack(4,
                            TextBlock("●").FontSize(8).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center),
                            TextBlock("#" + pr.Number).FontSize(12)),
                        () => onSelectPr(id))
                    .SubtleButton().ToolTip(pr.Repo + " #" + pr.Number + ": " + pr.Title).AutomationName("Open pull request " + pr.Number)
                    .WithKey("pr-" + pr.Id));
            }
        }

        if (mergedPrs.TryGetValue(issue.Identifier, out var merged))
        {
            foreach (var pr in merged)
            {
                var url = pr.Url;
                chips.Add(Button(
                        TextBlock("merged #" + pr.Number).FontSize(12).Foreground(Kit.Fg(Tone.Success)),
                        () => ExternalActions.OpenUrl(url))
                    .SubtleButton().ToolTip(pr.Repo + " #" + pr.Number + ": " + pr.Title).AutomationName("Open merged pull request " + pr.Number)
                    .WithKey("mpr-" + pr.Number));
            }
        }

        var button = Button(left, () => onSelectIssue(issue.Id))
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(issue.Identifier + " " + issue.Title);

        return Border(Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                button.Grid(column: 0),
                (FlexRow(chips.ToArray()) with { ColumnGap = 2 }).VAlign(VerticalAlignment.Center).Margin(4, 0, 4, 0).Grid(column: 1)))
            .Selected(selected)
            .CornerRadius(6)
            .WithKey(issue.Id);
    }

    /// <summary>Linear's priority marks: a red "!" for urgent, three to one bars for high to low, a dash for none.</summary>
    private static Element PriorityIcon(int priority)
    {
        if (priority == 1)
        {
            return Border(TextBlock("!").FontSize(11).SemiBold().Foreground(Kit.Fg(Tone.Danger)).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Width(18).Height(18).CornerRadius(4).Background(Kit.Bg(Tone.Danger));
        }

        if (priority is < 2 or > 4)
            return Border(TextBlock("–").Foreground(Theme.TertiaryText).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)).Width(18).Height(18);

        var on = 5 - priority; // high = 3 bars, medium = 2, low = 1
        Element Bar(int level, double height) =>
            Border(Empty()).Width(3).Height(height).CornerRadius(1)
                .Background(level <= on ? Theme.PrimaryText : Theme.TertiaryText)
                .VAlign(VerticalAlignment.Bottom);

        return Border(HStack(2, Bar(1, 5), Bar(2, 9), Bar(3, 13)).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Bottom))
            .Width(18).Height(18);
    }

    // ------------------------------------------------------------------ reconciliation

    private static string KeyOf(ReconItem item) =>
        item.Kind + "|" + (item.Pr is null ? "" : item.Pr.Repo + "#" + item.Pr.Number) + "|" + (item.Issue?.Identifier ?? "");

    private static Element ReconRow(
        ReconItem item,
        HubSnapshot snap,
        ILinearData data,
        Action<string> onSelectPr,
        string activeKey,
        Action<string> setActiveKey,
        int pick,
        Action<int> setPick,
        string busyKey,
        IReadOnlyList<LinearTeam> teams,
        Func<string, Func<Task<string>>, Task> run,
        bool first)
    {
        // Only the first card in the list gets the accent button; the others are quiet until it is done.
        ButtonElement Lead(ButtonElement b) => first ? b.AccentButton() : b;
        var key = KeyOf(item);
        var active = activeKey == key;
        var busy = busyKey == key;
        var pr = item.Pr;
        var issue = item.Issue;

        var (label, tone) = item.Kind switch
        {
            ReconKind.MergedButIssueOpen => ("Merged, issue still open", Tone.Warning),
            ReconKind.IssueDoneButPrOpen => ("Issue done, PR still open", Tone.Warning),
            ReconKind.PrWithoutIssue => ("PR without an issue", Tone.Neutral),
            _ => ("Issue without a PR", Tone.Neutral),
        };

        var snapPr = pr is null ? null : snap.Authored.FirstOrDefault(p => p.Repo == pr.Repo && p.Number == pr.Number);

        Element[] OpenPr() =>
        [
            Button("Open PR", () =>
                {
                    if (snapPr is not null) onSelectPr(snapPr.Id);
                    else if (pr is not null) ExternalActions.OpenUrl(pr.Url);
                })
                .SubtleButton().AutomationName("Open the pull request"),
            CopyLinkButton.For(pr?.Url),
        ];

        Element[] OpenIssue() =>
        [
            Button("Open issue", () => ExternalActions.OpenUrl(issue?.Url)).SubtleButton().AutomationName("Open the issue in Linear"),
            CopyLinkButton.For(issue?.Url),
        ];

        // One flat wrapping row so buttons share lines instead of each group claiming its own line.
        static Element Row(params IEnumerable<Element>[] parts) =>
            FlexRow(parts.SelectMany(g => g).ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 4 };

        Element Cancel() => Button("Cancel", () => setActiveKey("")).SubtleButton().IsEnabled(!busy);

        Element actions;
        switch (item.Kind)
        {
            case ReconKind.MergedButIssueOpen when issue is not null:
                actions = active
                    ? Confirm("Move " + issue.Identifier + " to Done in Linear?",
                        Button("Mark " + issue.Identifier + " done", () => _ = run(key, async () =>
                        {
                            await data.MarkDoneAsync(issue, CancellationToken.None);
                            return "Marked " + issue.Identifier + " as done.";
                        })).AccentButton().IsEnabled(!busy),
                        Cancel(), busy)
                    : Row(
                        [Lead(Button("Mark done", () => setActiveKey(key))).AutomationName("Mark " + issue.Identifier + " done")],
                        OpenPr(), OpenIssue());
                break;

            case ReconKind.IssueDoneButPrOpen:
                actions = Row(
                    [Lead(Button("Review the PR", () =>
                    {
                        if (snapPr is not null) onSelectPr(snapPr.Id);
                        else if (pr is not null) ExternalActions.OpenUrl(pr.Url);
                    })).AutomationName("Review the open pull request")],
                    OpenIssue());
                break;

            case ReconKind.PrWithoutIssue when pr is not null:
                if (active)
                {
                    var names = teams.Select(t => t.Key + " · " + t.Name).ToArray();
                    var chosen = teams.Count == 0 ? null : teams[Math.Clamp(pick, 0, teams.Count - 1)];
                    actions = Confirm(
                        "Create \"" + pr.Title + "\" in Linear and link this pull request?",
                        FlexRow(
                            names.Length > 0
                                ? ComboBox(names, Math.Clamp(pick, 0, names.Length - 1), setPick).MinWidth(180)
                                : Kit.Muted("Loading teams…"),
                            Button("Create and link", () => _ = run(key, async () =>
                            {
                                var made = await data.CreateIssueAsync(chosen!.Id, pr.Title, "Tracks " + pr.Url, null, CancellationToken.None);
                                await data.LinkPullRequestAsync(made.Id, pr.Url, pr.Title, CancellationToken.None);
                                return data.IsDemo
                                    ? "Created " + made.Identifier + " and linked the pull request."
                                    : "Created " + made.Identifier + " and linked the pull request. Add " + made.Identifier + " to the branch name or PR title so it matches here.";
                            })).AccentButton().IsEnabled(!busy && chosen is not null)) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 6 },
                        Cancel(), busy);
                }
                else
                {
                    actions = Row(
                        [Lead(Button("Create issue", () => { setPick(0); setActiveKey(key); })).AutomationName("Create a Linear issue for this pull request")],
                        OpenPr());
                }

                break;

            case ReconKind.InProgressWithoutPr when issue is not null:
                if (active)
                {
                    var candidates = snap.Authored;
                    var names = candidates.Select(p => p.Repo + " #" + p.Number + " " + p.Title).ToArray();
                    var chosen = candidates.Count == 0 ? null : candidates[Math.Clamp(pick, 0, candidates.Count - 1)];
                    actions = Confirm(
                        candidates.Count == 0 ? "You have no open pull requests to link." : "Which pull request implements " + issue.Identifier + "?",
                        FlexRow(
                            names.Length > 0
                                ? ComboBox(names, Math.Clamp(pick, 0, names.Length - 1), setPick).MinWidth(220)
                                : null,
                            Button("Link PR", () => _ = run(key, async () =>
                            {
                                await data.LinkPullRequestAsync(issue.Id, chosen!.Url, chosen.Title, CancellationToken.None);
                                return "Linked " + chosen.Repo + " #" + chosen.Number + " to " + issue.Identifier + ".";
                            })).AccentButton().IsEnabled(!busy && chosen is not null)) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 6 },
                        Cancel(), busy);
                }
                else
                {
                    actions = Row(
                        [Lead(Button("Link a PR…", () => { setPick(0); setActiveKey(key); })).AutomationName("Link a pull request to " + issue.Identifier)],
                        OpenIssue());
                }

                break;

            default:
                actions = Row(OpenPr(), OpenIssue());
                break;
        }

        var where = pr is not null ? pr.Repo + " #" + pr.Number : issue?.Identifier ?? "";

        return Border(VStack(8,
                FlexRow(Kit.Pill(label, tone), Kit.Muted(where).VAlign(VerticalAlignment.Center))
                    with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 },
                Body(item.Text).TextWrapping(),
                actions))
            .Background(Theme.SubtleFill).CornerRadius(8).Padding(12)
            .WithKey(key);
    }

    private static Element Confirm(string question, Element primary, Element cancel, bool busy) =>
        VStack(8,
            BodyStrong(question).TextWrapping(),
            primary,
            busy ? Parts.Busy("Working…") : cancel);
}
