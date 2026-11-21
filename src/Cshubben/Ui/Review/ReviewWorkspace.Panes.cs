using Cshubben.Core.Features.Review;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Review;

sealed partial class ReviewWorkspace
{
    // ------------------------------------------------------------------ header

    private Element BuildHeader()
    {
        var pr = Props.Pr;
        var title = pr?.Title ?? (Target.Title.Length > 0 ? Target.Title : Target.Label);
        var subtitle = pr is null ? Target.Label : Target.Label + " · " + pr.HeadBranch + " · " + pr.Author;
        var step = ReviewNextStep.Compute(Progress);
        var conversationCount = Progress.UnresolvedThreads + Progress.PendingComments;

        var primary = Button(step.Label, DoNextStep).AccentButton().AutomationName(step.Label + ". " + step.Reason);
        var help = Button("?", () => Update(s => s with { HelpOpen = true })).SubtleButton().AutomationName("Keyboard shortcuts").ToolTip("Keyboard shortcuts (?)");

        // Narrow windows show the full header on the file list only; inside the diff a one-line header is enough.
        if (Narrow && S.Screen != 0)
        {
            return Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    TextBlock(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 0),
                    primary.Grid(column: 1))
                .Padding(12, 8);
        }

        var titleBlock = VStack(2,
            TextBlock(title).FontSize(18).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
            Kit.Muted(subtitle).TextTrimming(TextTrimming.CharacterEllipsis));

        var actions = FlexRow(
            ReviewKit.OpenOnGitHub(Target.Url),
            ThreeColumns ? Empty() : Button("Conversation" + (conversationCount > 0 ? " (" + conversationCount + ")" : ""),
                    () => Update(s => s with { Screen = s.Screen == 2 ? 1 : 2 })).SubtleButton().AutomationName("Show conversation"),
            help,
            primary) with { ColumnGap = 8, RowGap = 4, Wrap = FlexWrap.Wrap, AlignItems = FlexAlign.Center };

        var titleRow = Narrow
            ? (Element)(FlexColumn(titleBlock, actions) with { RowGap = 8 })
            : Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                titleBlock.VAlign(VerticalAlignment.Center).Grid(column: 0),
                actions.VAlign(VerticalAlignment.Center).Margin(12, 0, 0, 0).Grid(column: 1));

        var facts = new List<Element>();
        if (pr is not null)
        {
            facts.Add(Kit.Pill(CiText(pr.Ci), CiTone(pr.Ci)));
            facts.Add(Kit.Pill(ReviewText(pr.Review), ReviewTone(pr.Review)));
        }
        facts.Add(Kit.Pill((pr?.SizeLabel ?? SizeOf(Props.Full.Files)) + " · " + ReviewTimeEstimate.Label(Props.Full.Files), Tone.Neutral));
        facts.Add(BuildProgress());
        var range = BuildRangeToggle();
        if (range is not null) facts.Add(range);
        if (CanSplit)
        {
            facts.Add(ToggleButton("Split view", S.Split, v => Update(s => s with { Split = v })).AutomationName("Split view"));
        }

        var factRow = FlexRow(facts.ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 10, RowGap = 6, AlignItems = FlexAlign.Center };
        return Border(FlexColumn(titleRow.Flex(shrink: 0), factRow.Flex(shrink: 0)) with { RowGap = 6 })
            .Padding(16, 8).BorderThickness(0, 0, 0, 1).BorderBrush(Theme.DividerStroke);
    }

    private Element BuildProgress()
    {
        var parts = new List<Element>
        {
            ReviewKit.ProgressBar(Progress.Fraction).Width(80).VAlign(VerticalAlignment.Center),
            TextBlock(Progress.ViewedLabel).SemiBold().VAlign(VerticalAlignment.Center),
        };
        if (Progress.UnresolvedThreads > 0) parts.Add(Kit.Pill(Progress.ThreadsLabel, Tone.Warning));
        if (Progress.PendingComments > 0) parts.Add(Kit.Pill(Progress.PendingComments + " pending", Tone.Accent));
        return (FlexRow(parts.ToArray()) with { ColumnGap = 10, AlignItems = FlexAlign.Center });
    }

    private Element? BuildRangeToggle()
    {
        if (Props.Since is not null)
        {
            var items = new[]
            {
                SelectorBarItem("Since my last review (" + Props.Since.Files.Count + ")"),
                SelectorBarItem("All changes (" + Props.Full.Files.Count + ")"),
            };
            return SelectorBar(items, S.ShowSince ? 0 : 1, i => Update(s => s with
            {
                ShowSince = i == 0,
                SelectedPath = null,
                Screen = Narrow ? 0 : s.Screen,
                CursorKey = null,
                Composer = null,
            }));
        }
        if (Props.Range.Kind == ReviewRangeKind.NothingNew || Props.Context.MyLastReviewedSha is not null)
            return Kit.Muted(Props.Range.Reason).VAlign(VerticalAlignment.Center);
        return null;
    }

    private static string SizeOf(IReadOnlyList<DiffFile> files)
    {
        var total = files.Sum(f => f.TotalChanges);
        return total <= 50 ? "XS" : total <= 200 ? "S" : total <= 500 ? "M" : total <= 1000 ? "L" : "XL";
    }

    private static string CiText(CiState s) => s switch { CiState.Success => "CI passing", CiState.Failure => "CI failing", CiState.Pending => "CI running", _ => "CI unknown" };

    private static Tone CiTone(CiState s) => s switch { CiState.Success => Tone.Success, CiState.Failure => Tone.Danger, CiState.Pending => Tone.Warning, _ => Tone.Neutral };

    private static string ReviewText(ReviewDecision r) => r switch
    {
        ReviewDecision.Approved => "Approved",
        ReviewDecision.ChangesRequested => "Changes requested",
        ReviewDecision.ReviewRequired => "Review required",
        _ => "No review yet",
    };

    private static Tone ReviewTone(ReviewDecision r) => r switch { ReviewDecision.Approved => Tone.Success, ReviewDecision.ChangesRequested => Tone.Warning, _ => Tone.Neutral };

    // ------------------------------------------------------------------ file tree

    private Element BuildTreePane(bool full)
    {
        var rows = FileTreeBuilder.Build(Files, S.Filter, S.CollapsedDirs);
        var adds = Files.Sum(f => f.Additions);
        var dels = Files.Sum(f => f.Deletions);

        var filter = TextBox(S.Filter, v => Update(s => s with { Filter = v }), placeholderText: "Filter files")
            .AutomationName("Filter files")
            .OnGotFocus((o, e) => Update(s => s with { FilterFocused = true }))
            .OnLostFocus((o, e) => Update(s => s with { FilterFocused = false }));
        var filterSeen = FilterSeen;
        var filterNonce = S.FilterFocusNonce;
        filter = filter.Set(tb =>
        {
            if (filterSeen.Current == filterNonce) return;
            filterSeen.Current = filterNonce;
            tb.DispatcherQueue.TryEnqueue(() => tb.Focus(FocusState.Programmatic));
        });

        Element list = rows.Count == 0
            ? Kit.EmptyNote(Files.Count == 0
                ? (S.ShowSince ? "Nothing changed since your last review." : "No files changed.")
                : "No files match the filter.")
            : (LazyVStack<TreeRow>(rows, r => (r.Kind == TreeRowKind.Dir ? "d:" : "f:") + r.Path, (r, i) => TreeRowView(r)) with { EstimatedItemSize = 30, Spacing = 0 });

        var summary = FlexRow(
            Kit.Muted(Files.Count + (Files.Count == 1 ? " file" : " files")).Flex(grow: 1),
            ReviewKit.Counts(adds, dels)) with { ColumnGap = 8 };

        var content = FlexColumn(
            filter.Flex(shrink: 0),
            summary.Flex(shrink: 0),
            list.Flex(grow: 1, basis: 0)) with { RowGap = 8 };

        return Border(content.FlexPadding(12, 12));
    }

    private Element TreeRowView(TreeRow row)
    {
        var indent = row.Depth * 14.0;
        if (row.Kind == TreeRowKind.Dir)
        {
            var path = row.Path;
            var dirContent = Grid(
                columns: [GridSize.Px(indent), GridSize.Px(18), GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                Empty().Grid(column: 0),
                Kit.Glyph(row.Collapsed ? ReviewKit.GlyphChevronRight : ReviewKit.GlyphChevronDown, 10).Foreground(Theme.SecondaryText).VAlign(VerticalAlignment.Center).Grid(column: 1),
                TextBlock(row.Name).TextTrimming(TextTrimming.CharacterEllipsis).Foreground(Theme.SecondaryText).VAlign(VerticalAlignment.Center).Grid(column: 2),
                Kit.Muted(row.FileCount.ToString()).VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 3));
            return Button(dirContent, () => Update(s => s with { CollapsedDirs = Toggle(s.CollapsedDirs, path) }))
                .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .Padding(4, 3).AutomationName((row.Collapsed ? "Expand folder " : "Collapse folder ") + row.Name);
        }

        var file = Files[row.FileIndex];
        var isCurrent = Current is not null && Current.Path == file.Path;
        var viewed = S.Session.IsViewed(file.Path);
        var (glyph, tone) = ReviewKit.StatusIcon(file.Status);
        var open = S.Threads.Count(t => t.Path == file.Path && !t.IsResolved);
        var pending = S.Session.Pending.Count(p => p.Path == file.Path);
        var changedSince = !S.ShowSince && Changed.TryGetValue(file.Path, out var state) && state != FileChangeState.Unchanged;

        var badges = new List<Element>();
        if (changedSince) badges.Add(TextBlock("●").FontSize(9).Foreground(Kit.Fg(Tone.Accent)).VAlign(VerticalAlignment.Center).ToolTip("Changed since your last review"));
        if (open > 0) badges.Add(Kit.Pill(open.ToString(), Tone.Warning));
        if (pending > 0) badges.Add(Kit.Pill(pending.ToString(), Tone.Accent));
        badges.Add(ReviewKit.Counts(file.Additions, file.Deletions, 10));

        var nameText = TextBlock(row.Name).TextTrimming(TextTrimming.CharacterEllipsis)
            .Foreground(viewed ? Theme.SecondaryText : Theme.PrimaryText).VAlign(VerticalAlignment.Center);
        var fileContent = Grid(
            columns: [GridSize.Px(indent), GridSize.Px(22), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Empty().Grid(column: 0),
            Kit.Glyph(glyph, 11).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center).Grid(column: 1),
            (isCurrent ? nameText.SemiBold() : nameText).Grid(column: 2),
            HStack(6, badges.ToArray()).VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 3));

        var path2 = file.Path;
        var selectButton = Button(fileContent, () => SelectFile(path2))
            .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Padding(4, 3).AutomationName(file.Path + (viewed ? ", viewed" : ""));

        var line = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            selectButton.Grid(column: 0),
            CheckBox(viewed, v => ToggleViewed(path2, v)).MinWidth(0).Width(32).AutomationName("Viewed " + file.Name).ToolTip("Viewed").Grid(column: 1));
        return Border(line).Selected(isCurrent).CornerRadius(6);
    }

    private static IReadOnlyCollection<string> Toggle(IReadOnlyCollection<string> set, string value)
    {
        var copy = new HashSet<string>(set, StringComparer.Ordinal);
        if (!copy.Remove(value)) copy.Add(value);
        return copy;
    }

    // ------------------------------------------------------------------ conversation pane

    private Element BuildConversationPane(bool full)
    {
        var parts = new List<Element>();
        if (Narrow)
        {
            parts.Add(Button(HStack(6, Kit.Glyph(ReviewKit.GlyphBack, 12), TextBlock("Diff")), () => Update(s => s with { Screen = 1 }))
                .SubtleButton().AutomationName("Back to the diff"));
        }

        parts.Add(BuildYourReview());
        parts.Add(BuildThreadList());
        parts.Add(BuildCommitList());

        var description = Props.Pr?.Description ?? Props.Context.Description;
        if (description.Trim().Length > 0)
            parts.Add(Collapsible("Description", S.ShowDescription, v => Update(s => s with { ShowDescription = v }),
                Border(Kit.Md(description)).Padding(4, 4)));

        return ScrollView(FlexColumn(parts.ToArray()).FlexPadding(12, 12) with { RowGap = 12 });
    }

    private Element BuildYourReview()
    {
        var pending = S.Session.Pending;
        var items = pending.Select(p => (Element)Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Button(VStack(0,
                        TextBlock(FileName(p.Path) + ":" + p.Line).FontSize(12).SemiBold(),
                        Kit.Muted(ReviewKit.FirstLine(p.Body, 60)).TextTrimming(TextTrimming.CharacterEllipsis)),
                    () => ShowPending(p))
                .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Left)
                .AutomationName("Pending comment on " + p.Path).Grid(column: 0),
            Button(Kit.Glyph(ReviewKit.GlyphDelete, 12), () => DeletePending(p.Id)).SubtleButton().AutomationName("Delete pending comment").Grid(column: 1))
            .WithKey(p.Id)).ToArray();

        var content = new List<Element>
        {
            BodyStrong("Your review"),
            Kit.Muted(pending.Count == 0
                ? "Click a line number to comment. Nothing is posted until you submit."
                : pending.Count + (pending.Count == 1 ? " pending comment, posted when you submit." : " pending comments, posted when you submit.")).TextWrapping(),
        };
        if (items.Length > 0) content.Add(VStack(2, items));
        content.Add(Button("Submit review…", () => Update(s => s with { SubmitOpen = true, Error = string.Empty }))
            .AccentButton().HAlign(HorizontalAlignment.Stretch).AutomationName("Submit review"));
        return ReviewKit.Surface(VStack(8, content.ToArray()));
    }

    private Element BuildThreadList()
    {
        var ordered = ReviewSession.OrderThreads(S.Threads, Ordered);
        var open = ordered.Where(t => !t.IsResolved).ToList();
        var resolved = ordered.Where(t => t.IsResolved).ToList();

        var content = new List<Element> { Heading("Conversations", open.Count) };
        if (ordered.Count == 0) content.Add(Kit.Muted("No conversations yet."));
        foreach (var t in open) content.Add(ThreadItem(t));
        if (resolved.Count > 0)
            content.Add(Collapsible("Resolved (" + resolved.Count + ")", S.ShowResolved, v => Update(s => s with { ShowResolved = v }),
                VStack(2, resolved.Select(ThreadItem).ToArray())));
        return VStack(6, content.ToArray());
    }

    private static Element Heading(string text, int count) =>
        HStack(8, BodyStrong(text), count > 0 ? Kit.Pill(count.ToString(), Tone.Warning) : Empty());

    private Element ThreadItem(ReviewThread t)
    {
        var root = t.Root;
        var line = t.Line is null ? string.Empty : ":" + t.Line;
        var focused = S.FocusedThreadId == t.Id;
        var content = VStack(2,
            HStack(6,
                ReviewKit.Avatar(root?.Author ?? "?", 18),
                TextBlock(root?.Author ?? "unknown").FontSize(12).SemiBold(),
                Kit.Muted(FileName(t.Path) + line),
                t.IsOutdated ? Kit.Pill("outdated", Tone.Neutral) : Empty()),
            Kit.Muted(root is null ? string.Empty : ReviewKit.FirstLine(root.Body, 80)).TextTrimming(TextTrimming.CharacterEllipsis)
                .Opacity(t.IsResolved ? 0.85 : 1.0));
        return Border(Button(content, () => ShowThread(t))
                .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Left)
                .AutomationName("Thread by " + (root?.Author ?? "unknown") + " on " + t.Path))
            .Selected(focused).CornerRadius(6).WithKey(t.Id);
    }

    private Element BuildCommitList()
    {
        var commits = Props.Context.Commits;
        var fresh = new HashSet<string>(Props.Range.NewCommits.Select(c => c.Sha), StringComparer.OrdinalIgnoreCase);
        var items = commits.Reverse().Select(c => (Element)Grid(
            columns: [GridSize.Px(64), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            TextBlock(c.ShortSha).FontFamily(ReviewKit.Mono).FontSize(11).Foreground(Theme.SecondaryText).VAlign(VerticalAlignment.Center).Grid(column: 0),
            TextBlock(c.Headline).FontSize(12).TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 1),
            fresh.Contains(c.Sha) ? Kit.Pill("new", Tone.Accent).Margin(6, 0, 0, 0).Grid(column: 2) : Empty().Grid(column: 2))
            .Padding(0, 3).WithKey(c.Sha)).ToArray();
        return Collapsible("Commits (" + commits.Count + ")", S.ShowCommits, v => Update(s => s with { ShowCommits = v }), VStack(2, items));
    }

    /// <summary>A light section: a subtle header button with a chevron, content only when open (no heavy card).</summary>
    private static Element Collapsible(string title, bool open, Action<bool> set, Element content)
    {
        var header = Button(
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    BodyStrong(title).VAlign(VerticalAlignment.Center).Grid(column: 0),
                    Kit.Glyph(open ? ReviewKit.GlyphChevronDown : ReviewKit.GlyphChevronRight, 10).Foreground(Theme.SecondaryText)
                        .VAlign(VerticalAlignment.Center).Grid(column: 1)),
                () => set(!open))
            .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Padding(8, 4).AutomationName((open ? "Collapse " : "Expand ") + title);
        return open ? VStack(4, header, content) : header;
    }

    private static string FileName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path.Substring(slash + 1);
    }
}
