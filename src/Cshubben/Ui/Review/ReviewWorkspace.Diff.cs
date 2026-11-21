using Cshubben.Core.Features.Review;
using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Review;

sealed partial class ReviewWorkspace
{
    private const double UnifiedIndent = 114;

    private bool SplitActive => S.Split && CanSplit;

    // ------------------------------------------------------------------ diff pane

    private Element BuildDiffPane(bool full)
    {
        var file = Current;
        if (file is null)
        {
            return FlexColumn(
                    Narrow ? BackToFiles() : Empty(),
                    Kit.EmptyNote(S.ShowSince ? "Nothing changed since your last review." : "This pull request has no file changes."))
                .FlexPadding(16, 16);
        }

        var rows = ReviewRows.Flatten(file, RowOptions(file));
        var shown = SplitActive ? SplitRows.From(rows) : rows;
        var matches = S.FindOpen ? DiffSearch.Matches(rows, S.FindQuery) : Array.Empty<string>();
        FindHits = new HashSet<string>(matches, StringComparer.Ordinal);

        var scrollIndex = -1;
        if (S.ScrollKey is not null)
        {
            for (var i = 0; i < shown.Count; i++)
            {
                if (shown[i].Key == S.ScrollKey || (shown[i] is SplitRow pair && (pair.Left?.Key == S.ScrollKey || pair.Right?.Key == S.ScrollKey)))
                {
                    scrollIndex = i;
                    break;
                }
            }
        }

        var nonce = S.ScrollNonce;
        var scrolled = Scrolled;
        var list = LazyVStack<ReviewRow>(shown, r => r.Key, (r, i) => RowView(file, r)) with { EstimatedItemSize = 22, Spacing = 0 };
        list = list.SetRepeater(repeater =>
        {
            if (scrollIndex < 0 || scrolled.Current == nonce) return;
            scrolled.Current = nonce;
            var target = scrollIndex;
            repeater.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    repeater.GetOrCreateElement(target).StartBringIntoView();
                }
                catch (Exception)
                {
                    // Scrolling is a nicety; a recycled container must never break the review.
                }
            });
        });

        return FlexColumn(
            BuildFileHeader(file).Flex(shrink: 0),
            S.FindOpen ? BuildFindBar(matches.Count).Flex(shrink: 0) : Empty(),
            list.Flex(grow: 1, basis: 0));
    }

    private Element BackToFiles() =>
        Button(HStack(6, Kit.Glyph(ReviewKit.GlyphBack, 12), TextBlock("Files")), () => Update(s => s with { Screen = 0 }))
            .SubtleButton().AutomationName("Back to the file list");

    private Element BuildFileHeader(DiffFile file)
    {
        var viewed = S.Session.IsViewed(file.Path);
        var kind = FileKinds.Classify(file.Path, file.IsBinary);
        var path = file.OldPath is not null && file.Status == DiffFileStatus.Renamed ? file.OldPath + "  →  " + file.Path : file.Path;
        var index = -1;
        for (var i = 0; i < Ordered.Count; i++)
            if (Ordered[i].Path == file.Path) index = i;

        var tags = new List<Element>
        {
            Kit.Pill(ReviewKit.StatusLabel(file.Status), file.Status == DiffFileStatus.Added ? Tone.Success : file.Status == DiffFileStatus.Deleted ? Tone.Danger : Tone.Neutral),
            ReviewKit.Counts(file.Additions, file.Deletions, 12),
            ReviewKit.ChangeBar(file.Additions, file.Deletions),
        };
        if (kind != FileKind.Source) tags.Add(Kit.Pill(FileKinds.Label(kind), Tone.Neutral));
        if (!S.ShowSince && Changed.TryGetValue(file.Path, out var changeState) && changeState != FileChangeState.Unchanged)
            tags.Add(Kit.Pill("changed since your review", Tone.Accent));
        if (index >= 0) tags.Add(Kit.Muted((index + 1) + " of " + Ordered.Count));

        var nav = FlexRow(
            Button(Kit.Glyph("", 12), () => MoveFile(-1)).SubtleButton().AutomationName("Previous file").ToolTip("Previous file (k)"),
            Button(Kit.Glyph("", 12), () => MoveFile(1)).SubtleButton().AutomationName("Next file").ToolTip("Next file (j)"),
            ToggleButton("Wrap", S.Wrap, v => Update(s => s with { Wrap = v })).AutomationName("Wrap long lines").ToolTip("Wrap long lines"),
            CheckBox(viewed, v => ToggleViewed(file.Path, v), label: "Viewed").AutomationName("Viewed (v)"),
            ReviewKit.OpenOnGitHub(ReviewKit.FileUrl(Target, file.Path), "Open file on GitHub")) with { ColumnGap = 4 };

        var titleLine = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            TextBlock(path).FontFamily(ReviewKit.Mono).FontSize(13).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis)
                .VAlign(VerticalAlignment.Center).Grid(column: 0),
            nav.VAlign(VerticalAlignment.Center).Margin(12, 0, 0, 0).Grid(column: 1));

        var children = new List<Element>();
        if (Narrow) children.Add(BackToFiles());
        children.Add(titleLine);
        children.Add(FlexRow(tags.ToArray()) with { ColumnGap = 10, AlignItems = FlexAlign.Center });
        return Border(VStack(8, children.ToArray()))
            .Padding(16, 10).BorderThickness(0, 0, 0, 1).BorderBrush(Theme.DividerStroke);
    }

    // ------------------------------------------------------------------ rows

    private Element RowView(DiffFile file, ReviewRow row) => row switch
    {
        NoticeRow n => NoticeView(file, n),
        HunkHeaderRow h => HunkView(h),
        GapRow g => GapView(file, g),
        LineRow l => LineView(file, l),
        SplitRow s => SplitView(file, s),
        ThreadRow t => ThreadView(t.Thread),
        PendingRow p => PendingView(p.Comment),
        ComposerRow c => ComposerView(file, c),
        _ => Empty(),
    };

    private Element NoticeView(DiffFile file, NoticeRow notice)
    {
        var path = file.Path;
        Element action = notice.Kind switch
        {
            NoticeKind.Collapsed => Button("Show diff", () => Update(s => s with { ExpandedFiles = s.ExpandedFiles.Append(path).Distinct().ToList() }))
                .AccentButton().AutomationName("Show the diff of " + file.Name),
            NoticeKind.TooLarge or NoticeKind.Binary => ReviewKit.OpenOnGitHub(ReviewKit.FileUrl(Target, path)),
            _ => Empty(),
        };
        return Border(HStack(12, Kit.Muted(notice.Text).VAlign(VerticalAlignment.Center), action)).Padding(16, 16);
    }

    private static Element HunkView(HunkHeaderRow hunk) =>
        Border(TextBlock(hunk.Header).FontFamily(ReviewKit.Mono).FontSize(11).Foreground(Theme.SecondaryText)
                .TextTrimming(TextTrimming.CharacterEllipsis))
            .Background(Theme.SubtleFill).Padding(12, 4);

    private Element GapView(DiffFile file, GapRow gap)
    {
        var text = gap.Loading
            ? "Loading hidden lines…"
            : gap.HiddenLines > 0
                ? "Show " + gap.HiddenLines + (gap.HiddenLines == 1 ? " hidden line" : " hidden lines")
                : "Show more lines";
        var index = gap.GapIndex;
        return Button(HStack(8, Kit.Glyph(ReviewKit.GlyphChevronDown, 10), TextBlock(text).FontSize(12)), () => ExpandGap(file, index))
            .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Left)
            .IsEnabled(!gap.Loading).Padding(UnifiedIndent - 4, 3, 8, 3).AutomationName(text);
    }

    // ---- code lines

    private Element NumberCell(int? number, bool commentable, bool active, Action tap)
    {
        var label = number is null ? string.Empty : number.Value.ToString();
        var text = active
            ? TextBlock("+").FontFamily(ReviewKit.Mono).FontSize(13).SemiBold().Foreground(Theme.AccentText)
            : TextBlock(label).FontFamily(ReviewKit.Mono).FontSize(11).Foreground(Theme.SecondaryText).TextWrapping(TextWrapping.NoWrap);
        var cell = Border(text.HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center)).Padding(0, 0, 8, 0);
        if (!commentable || number is null) return cell;
        return cell
            .OnTapped((s, e) =>
            {
                e.Handled = true;
                tap();
            })
            .ToolTip("Comment on this line (c)");
    }

    private Element CodeCell(DiffFile file, LineRow row)
    {
        var line = row.Line;
        if (row.Partner is not null && (line.Kind == DiffLineKind.Add || line.Kind == DiffLineKind.Remove))
        {
            var oldText = line.Kind == DiffLineKind.Remove ? line.Text : row.Partner.Text;
            var newText = line.Kind == DiffLineKind.Add ? line.Text : row.Partner.Text;
            var result = IntraLineDiff.Compute(oldText, newText);
            if (result.IsUseful)
            {
                var spans = line.Kind == DiffLineKind.Remove ? result.OldSpans : result.NewSpans;
                IReadOnlyList<CodeToken>? lineTokens = null;
                if (SyntaxHighlighter.IsHighlighted(file.Path))
                    lineTokens = row.LineIndex >= 0 && row.HunkIndex >= 0 && row.HunkIndex < file.Hunks.Count
                        ? HunkHighlighter.TokensFor(file.Hunks[row.HunkIndex], row.LineIndex, file.Path)
                        : SyntaxHighlighter.Tokenize(line.Text, file.Path);
                var slices = lineTokens is null ? null : TokenSlicer.Slice(lineTokens, spans);
                return Segments(spans, slices, S.Wrap, file.Path, line.Kind == DiffLineKind.Add ? Theme.SystemSuccess : Theme.SystemCritical);
            }
        }

        if (SyntaxHighlighter.IsHighlighted(file.Path))
        {
            // Lines revealed from a gap are not part of any hunk, so they are highlighted on their own.
            if (row.LineIndex < 0 || row.HunkIndex < 0 || row.HunkIndex >= file.Hunks.Count)
                return Highlighted(ReviewKit.CodeText(line.Text), file.Path, S.Wrap);
            var tokens = HunkHighlighter.TokensFor(file.Hunks[row.HunkIndex], row.LineIndex, file.Path);
            return FlexRow(TokenParts(tokens).ToArray()) with { Wrap = S.Wrap ? FlexWrap.Wrap : FlexWrap.NoWrap };
        }

        var plain = TextBlock(ReviewKit.CodeText(line.Text)).FontFamily(ReviewKit.Mono).FontSize(12);
        return S.Wrap
            ? plain.TextWrapping()
            : plain.TextWrapping(TextWrapping.NoWrap).TextTrimming(TextTrimming.CharacterEllipsis).ToolTip(line.Text.Trim());
    }

    private static ThemeRef TokenColor(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => Theme.AccentText,
        TokenKind.String => Theme.SystemCaution,
        TokenKind.Number => Theme.AccentSecondary,
        TokenKind.Comment => Theme.SecondaryText,
        _ => Theme.PrimaryText,
    };

    /// <summary>One element per token. Spaces become non-breaking so they survive being the edge of a text block.</summary>
    private static List<Element> TokenParts(string text, string path) =>
        TokenParts(SyntaxHighlighter.Tokenize(text.Replace('\u00A0', ' '), path));

    private static List<Element> TokenParts(IReadOnlyList<CodeToken> tokens)
    {
        // A text block drops trailing spaces when it is measured, so they travel to the start of the next token.
        var parts = new List<Element>();
        var carry = "";
        foreach (var token in tokens)
        {
            var full = carry + token.Text.Replace('\u00A0', ' ').Replace("\t", "    ", StringComparison.Ordinal);
            var body = full.TrimEnd(' ');
            carry = full.Substring(body.Length);
            if (body.Length == 0) continue;
            var block = TextBlock(body.Replace(' ', '\u00A0')).FontFamily(ReviewKit.Mono).FontSize(12).Foreground(TokenColor(token.Kind));
            parts.Add(block.Flex(shrink: 0));
        }

        return parts;
    }

    /// <summary>One line of code with syntax colours.</summary>
    private static Element Highlighted(string text, string path, bool wrap) =>
        FlexRow(TokenParts(text, path).ToArray()) with { Wrap = wrap ? FlexWrap.Wrap : FlexWrap.NoWrap };

    private static Element Segments(IReadOnlyList<DiffSpan> spans, IReadOnlyList<IReadOnlyList<CodeToken>>? slices, bool wrap, string path, ThemeRef strong)
    {
        // Trailing spaces travel to the start of the next piece (a text block drops them when it is measured).
        var parts = new List<Element>();
        var carry = "";
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            IReadOnlyList<CodeToken> tokens = slices is not null
                ? slices[i]
                : SyntaxHighlighter.IsHighlighted(path)
                    ? SyntaxHighlighter.Tokenize(span.Text.Replace('\u00A0', ' '), path)
                    : new[] { new CodeToken(span.Text, TokenKind.Plain) };

            foreach (var token in tokens)
            {
                var full = carry + token.Text.Replace('\u00A0', ' ').Replace("\t", "    ", StringComparison.Ordinal);
                var body = full.TrimEnd(' ');
                carry = full.Substring(body.Length);
                if (body.Length == 0) continue;

                var block = TextBlock(body.Replace(' ', '\u00A0')).FontFamily(ReviewKit.Mono).FontSize(12).Foreground(TokenColor(token.Kind));
                parts.Add(span.Changed
                    ? Grid(
                            columns: [GridSize.Auto],
                            rows: [GridSize.Auto],
                            Border(Empty()).Background(strong).Opacity(0.35).Grid(row: 0, column: 0),
                            block.Grid(row: 0, column: 0))
                        .Flex(shrink: 0)
                    : block.Flex(shrink: 0));
            }
        }

        return FlexRow(parts.ToArray()) with { Wrap = wrap ? FlexWrap.Wrap : FlexWrap.NoWrap };
    }

    private Element LineView(DiffFile file, LineRow row)
    {
        var line = row.Line;
        if (line.Kind == DiffLineKind.NoNewline)
            return Border(Kit.Muted(line.Text.TrimStart('\\').Trim())).Padding(UnifiedIndent, 2, 8, 2);

        var cursor = S.CursorKey == row.Key;
        var side = ThreadAnchor.SideOf(line);
        var kind = line.Kind;
        void Comment() => OpenComposer(file, row, side);

        var sign = kind == DiffLineKind.Add ? "+" : kind == DiffLineKind.Remove ? "−" : " ";
        var signColor = kind == DiffLineKind.Add ? Kit.Fg(Tone.Success) : kind == DiffLineKind.Remove ? Kit.Fg(Tone.Danger) : Theme.SecondaryText;
        var showPlusOld = cursor && side == DiffSide.Left;
        var showPlusNew = cursor && side == DiffSide.Right;

        var grid = Grid(
            columns: [GridSize.Px(48), GridSize.Px(48), GridSize.Px(18), GridSize.Star()],
            rows: [GridSize.Auto],
            NumberCell(line.OldNumber, row.Commentable, showPlusOld, Comment).Grid(column: 0),
            NumberCell(line.NewNumber, row.Commentable, showPlusNew, Comment).Grid(column: 1),
            TextBlock(sign).FontFamily(ReviewKit.Mono).FontSize(12).Foreground(signColor).HAlign(HorizontalAlignment.Center).Grid(column: 2),
            CodeCell(file, row).Margin(0, 1, 8, 1).Grid(column: 3));

        return Tinted(Border(grid).MinHeight(20), kind, cursor, FindHits.Contains(row.Key))
            .OnTapped((s, e) => Update(st => st with { CursorKey = row.Key }));
    }

    private static BorderElement Tinted(BorderElement cell, DiffLineKind kind, bool cursor, bool match = false)
    {
        if (match) cell = cell.Background(Theme.SystemCautionBackground);
        else if (kind == DiffLineKind.Add) cell = cell.Background(Theme.SystemSuccessBackground);
        else if (kind == DiffLineKind.Remove) cell = cell.Background(Theme.SystemCriticalBackground);
        else if (cursor) cell = cell.Background(Theme.SubtleFill);
        if (cursor) cell = cell.BorderThickness(3, 0, 0, 0).BorderBrush(Theme.AccentText);
        return cell;
    }

    private Element SplitView(DiffFile file, SplitRow row)
    {
        Element Side(LineRow? r, bool left)
        {
            if (r is null) return Border(Empty()).Background(Theme.SubtleFill).MinHeight(20);
            var kind = r.Line.Kind;
            var number = left ? r.Line.OldNumber : r.Line.NewNumber;
            var diffSide = left ? DiffSide.Left : DiffSide.Right;
            var cursor = S.CursorKey == r.Key;
            var tintKind = kind == DiffLineKind.Remove && left ? DiffLineKind.Remove : kind == DiffLineKind.Add && !left ? DiffLineKind.Add : DiffLineKind.Context;
            var grid = Grid(
                columns: [GridSize.Px(44), GridSize.Star()],
                rows: [GridSize.Auto],
                NumberCell(number, r.Commentable, cursor, () => OpenComposer(file, r, diffSide)).Grid(column: 0),
                CodeCell(file, r).Margin(0, 1, 8, 1).Grid(column: 1));
            return Tinted(Border(grid).MinHeight(20), tintKind, cursor, FindHits.Contains(r.Key))
                .OnTapped((s, e) => Update(st => st with { CursorKey = r.Key }));
        }

        return Grid(
            columns: [GridSize.Star(), GridSize.Px(1), GridSize.Star()],
            rows: [GridSize.Auto],
            Side(row.Left, true).Grid(column: 0),
            Border(Empty()).Background(Theme.DividerStroke).Grid(column: 1),
            Side(row.Right, false).Grid(column: 2));
    }

    // ---- threads, drafts and the composer

    private double InlineIndent => SplitActive ? 16 : UnifiedIndent;

    private Element ComposerView(DiffFile file, ComposerRow row)
    {
        var c = S.Composer;
        if (c is null) return Empty();
        var range = c.StartLine is int start ? "lines " + start + "–" + c.Line : "line " + c.Line;
        var header = "Comment on " + range + " of " + file.Name + (c.Side == DiffSide.Left ? " (old version)" : string.Empty);
        var location = ThreadAnchor.Find(file, c.Line, c.Side);
        var canExtend = location is LineLocation loc
            && (c.StartLine ?? c.Line) - 1 >= ThreadAnchor.MinLine(file.Hunks[loc.HunkIndex], c.Side);
        string? template = c.Side == DiffSide.Right ? SuggestionFormatter.Template(file, c.StartLine ?? c.Line, c.Line) : null;

        var props = new TextEntryProps(
            c.Initial,
            header,
            "Leave a comment (markdown supported)",
            c.EditPendingId is null ? "Add to review" : "Save",
            SavePending,
            () => Update(s => s with { Composer = null }),
            template,
            canExtend ? () => ExtendComposer(file, 1) : null,
            c.StartLine is null ? null : () => ExtendComposer(file, -1));
        return Border(Component<TextEntry, TextEntryProps>(props).WithKey("composer")).Margin(InlineIndent, 4, 16, 8);
    }

    private Element ThreadView(ReviewThread t)
    {
        var root = t.Root;
        var focused = S.FocusedThreadId == t.Id;

        if (t.IsResolved && !S.OpenResolved.Contains(t.Id))
        {
            var id = t.Id;
            var summary = "Resolved · " + (root?.Author ?? "unknown") + ": " + (root is null ? string.Empty : ReviewKit.FirstLine(root.Body, 70));
            var compact = Grid(
                columns: [GridSize.Px(22), GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                Kit.Glyph(ReviewKit.GlyphCheck, 12).Foreground(Kit.Fg(Tone.Success)).VAlign(VerticalAlignment.Center).Grid(column: 0),
                TextBlock(summary).FontSize(12).Foreground(Theme.SecondaryText).TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 1),
                Button("Show", () => Update(s => s with { OpenResolved = s.OpenResolved.Append(id).Distinct().ToList() }))
                    .SubtleButton().AutomationName("Show resolved thread").Grid(column: 2));
            return Border(Border(compact).Background(Theme.SubtleFill).CornerRadius(6).Padding(10, 2)).Margin(InlineIndent, 2, 16, 4);
        }

        var parts = new List<Element>();
        for (var i = 0; i < t.Comments.Count; i++)
        {
            var comment = t.Comments[i];
            if (i == 0)
            {
                var tags = new List<Element>();
                if (t.IsRange) tags.Add(Kit.Pill("lines " + t.StartLine + "–" + t.Line, Tone.Neutral));
                if (t.IsOutdated) tags.Add(Kit.Pill("outdated", Tone.Neutral));
                if (t.IsResolved) tags.Add(Kit.Pill("resolved", Tone.Success));
                parts.Add(CommentView(t, comment, tags));
            }
            else
            {
                parts.Add(Border(CommentView(t, comment, new List<Element>())).BorderThickness(2, 0, 0, 0).BorderBrush(Theme.DividerStroke).Padding(10, 0, 0, 0).Margin(8, 0, 0, 0));
            }
        }

        var threadId = t.Id;
        var actions = FlexRow(
            Button("Reply", () => Update(s => s with { ReplyThreadId = threadId, Composer = null, EditCommentId = null })).SubtleButton().AutomationName("Reply to thread").Flex(shrink: 0),
            t.IsResolved
                ? Button("Unresolve", () => SetResolved(t, false)).SubtleButton().AutomationName("Unresolve conversation").Flex(shrink: 0)
                : Button("Resolve conversation", () => SetResolved(t, true)).SubtleButton().AutomationName("Resolve conversation").Flex(shrink: 0),
            t.IsResolved ? Button("Hide", () => Update(s => s with { OpenResolved = s.OpenResolved.Where(x => x != threadId).ToList() })).SubtleButton().AutomationName("Hide resolved thread").Flex(shrink: 0) : Empty(),
            Empty().Flex(grow: 1),
            ReviewKit.OpenOnGitHub(t.Url, "GitHub").Flex(shrink: 0)) with { ColumnGap = 4, RowGap = 4, Wrap = FlexWrap.Wrap };
        parts.Add(actions);

        if (S.ReplyThreadId == t.Id)
        {
            var props = new TextEntryProps(
                string.Empty,
                "Reply to " + (root?.Author ?? "thread"),
                "Write a reply",
                "Reply",
                text => Reply(t, text),
                () => Update(s => s with { ReplyThreadId = null }));
            parts.Add(Component<TextEntry, TextEntryProps>(props).WithKey("reply-" + t.Id));
        }

        var card = Border(VStack(8, parts.ToArray()))
            .Background(Theme.CardBackground)
            .WithBorder(focused ? Theme.AccentText : Theme.ControlStroke, focused ? 2 : 1)
            .CornerRadius(8).Padding(12);
        return Border(card).Margin(InlineIndent, 4, 16, 8);
    }

    private Element CommentView(ReviewThread t, ReviewComment c, List<Element> tags)
    {
        if (S.EditCommentId == c.Id)
        {
            var props = new TextEntryProps(
                c.Body,
                "Edit your comment",
                string.Empty,
                "Save",
                text => EditComment(t, c, text),
                () => Update(s => s with { EditCommentId = null }));
            return Component<TextEntry, TextEntryProps>(props).WithKey("edit-" + c.Id);
        }

        var header = new List<Element>
        {
            ReviewKit.Avatar(c.Author, 22),
            TextBlock(c.Author).SemiBold().VAlign(VerticalAlignment.Center),
            Kit.Muted(ReviewKit.TimeAgo(c.CreatedAt)).VAlign(VerticalAlignment.Center),
        };
        header.AddRange(tags);
        if (c.IsMine)
        {
            header.Add(Button("Edit", () => Update(s => s with { EditCommentId = c.Id, ReplyThreadId = null, Composer = null })).SubtleButton().AutomationName("Edit comment"));
            header.Add(Button("Delete", () => DeleteComment(t, c)).SubtleButton().AutomationName("Delete comment"));
        }

        return VStack(4, FlexRow(header.ToArray()) with { ColumnGap = 8, Wrap = FlexWrap.Wrap, RowGap = 4 }, CommentBody(c.Body, t, c));
    }

    /// <summary>
    /// The comment text. A suggestion block gets a pill and, for posted comments, either the "Apply suggestion" action
    /// (author or write access, open thread on the new side) or a note saying why it cannot be applied here.
    /// </summary>
    private Element CommentBody(string body, ReviewThread? thread = null, ReviewComment? comment = null)
    {
        if (SuggestionFormatter.Extract(body) is null) return Kit.Md(body);
        var parts = new List<Element>
        {
            Kit.Pill("Suggested change", Tone.Accent).HAlign(HorizontalAlignment.Left),
            Kit.Md(body),
        };

        if (thread is null || comment is null)
        {
            parts.Add(Kit.Muted("Others can apply this once the review is submitted."));
            return VStack(4, parts.ToArray());
        }

        var context = Props.Context;
        var request = context.CanApplySuggestions ? SuggestionApplier.RequestFor(thread, comment, context.HeadSha) : null;
        if (request is not null)
        {
            var open = S.Suggestion is { } st && st.CommentId == comment.Id;
            if (open) parts.Add(SuggestionPanel(S.Suggestion!));
            else
            {
                parts.Add(Button(HStack(6, Kit.Glyph(ReviewKit.GlyphCheck, 12), TextBlock("Apply suggestion")), () => PreviewSuggestion(thread, comment, request))
                    .SubtleButton().HAlign(HorizontalAlignment.Left).IsEnabled(!S.Busy).AutomationName("Apply suggestion"));
            }
        }
        else
        {
            parts.Add(Kit.Muted(SuggestionNote(thread, context.CanApplySuggestions)));
        }
        return VStack(4, parts.ToArray());
    }

    private static string SuggestionNote(ReviewThread thread, bool allowed)
    {
        if (thread.IsOutdated) return "This suggestion is outdated and cannot be applied.";
        if (thread.IsResolved) return "Resolved suggestions cannot be applied.";
        if (!allowed) return "Only the author or someone with write access can apply this suggestion.";
        return "This suggestion cannot be applied here. Open it on GitHub.";
    }

    private Element PendingView(PendingComment p)
    {
        var range = p.IsRange ? "lines " + p.StartLine + "–" + p.Line : "line " + p.Line;
        var header = FlexRow(
            Kit.Pill("Pending", Tone.Warning),
            Kit.Muted(range + ", only visible to you until you submit").VAlign(VerticalAlignment.Center).Flex(grow: 1),
            Button("Edit", () => EditPending(p)).SubtleButton().AutomationName("Edit pending comment"),
            Button("Delete", () => DeletePending(p.Id)).SubtleButton().AutomationName("Delete pending comment")) with { ColumnGap = 8 };
        var card = Border(VStack(6, header, CommentBody(p.Body)))
            .Background(Theme.CardBackground)
            .WithBorder(S.CursorKey == "pending-" + p.Id ? Theme.AccentText : Theme.SystemCaution, S.CursorKey == "pending-" + p.Id ? 2 : 1)
            .CornerRadius(8).Padding(12);
        return Border(card).Margin(InlineIndent, 4, 16, 8);
    }
}
