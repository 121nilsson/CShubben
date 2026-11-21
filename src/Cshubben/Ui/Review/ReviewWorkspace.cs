using Cshubben.Core.Features.Review;
using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Review;

/// <summary>
/// The review view once the data is loaded: file tree, diff, and the conversation pane on wide windows; a single-pane
/// flow (file list, then diff, then conversation) on narrow ones. All state lives in <see cref="ReviewUiState"/>.
/// </summary>
sealed partial class ReviewWorkspace : Component<ReviewBundle>
{
    // The latest rendered state. Handlers read these at call time, so they never act on stale values.
    private ReviewUiState S = new();
    private Action<Func<ReviewUiState, ReviewUiState>> Update = _ => { };
    private IReadOnlyList<DiffFile> Files = Array.Empty<DiffFile>();
    private IReadOnlyList<DiffFile> Ordered = Array.Empty<DiffFile>();
    private DiffFile? Current;
    private ReviewProgress Progress = new(0, 0, 0, 0);
    private IReadOnlyDictionary<string, FileChangeState> Changed = new Dictionary<string, FileChangeState>();
    private bool Narrow;
    private bool ThreeColumns;
    private bool CanSplit;
    private Ref<int> Scrolled = new(0);

    private ReviewTarget Target => Props.Target;

    private IReviewData Data => Props.Data;

    public override Element Render()
    {
        var b = Props;
        var (windowWidth, _) = UseWindowSize();
        var narrow = windowWidth < 900;
        var (state, update) = UseReducer(Initial(b, narrow));
        var scrolled = UseRef(0);

        UseEffect(() =>
        {
            try
            {
                b.Drafts.Save(b.Target.Id, state.Session.Pending, state.Session.Summary);
            }
            catch (Exception)
            {
                // Drafts are a safety net; a full disk must not break the review.
            }
        }, state.Session.Pending, state.Session.Summary);

        S = state;
        Scrolled = scrolled;
        Update = update;
        Narrow = narrow;
        ThreeColumns = windowWidth >= 1360;
        var rail = 64.0; // the shell shows the icon rail while reviewing
        var treeWidth = windowWidth >= 1500 ? 320.0 : 280.0;
        var rightWidth = windowWidth >= 1500 ? 400.0 : 320.0;
        var diffWidth = windowWidth - rail - treeWidth - (ThreeColumns ? rightWidth : 0.0);
        CanSplit = !narrow && diffWidth >= 760;

        Files = state.ShowSince && b.Since is not null ? b.Since.Files : b.Full.Files;
        Ordered = FileTreeBuilder.TreeOrder(Files).Select(i => Files[i]).ToList();
        Current = Files.FirstOrDefault(f => f.Path == state.SelectedPath) ?? Ordered.FirstOrDefault();
        Progress = ReviewProgress.Compute(Files, state.Session, state.Threads);
        Changed = b.Since is not null
            ? ReviewRange.MarkChangedSince(b.Full.Files, b.Since.Files)
            : new Dictionary<string, FileChangeState>();

        Element body;
        if (narrow)
        {
            body = state.Screen switch
            {
                1 => BuildDiffPane(true),
                2 => BuildConversationPane(true),
                _ => BuildTreePane(true),
            };
        }
        else if (ThreeColumns)
        {
            body = Grid(
                columns: [GridSize.Px(treeWidth), GridSize.Star(), GridSize.Px(rightWidth)],
                rows: [GridSize.Star()],
                Divided(BuildTreePane(false), right: true).Grid(column: 0),
                BuildDiffPane(false).Grid(column: 1),
                Divided(BuildConversationPane(false), left: true).Grid(column: 2));
        }
        else
        {
            body = Grid(
                columns: [GridSize.Px(treeWidth), GridSize.Star()],
                rows: [GridSize.Star()],
                Divided(BuildTreePane(false), right: true).Grid(column: 0),
                (state.Screen == 2 ? BuildConversationPane(false) : BuildDiffPane(false)).Grid(column: 1));
        }

        var banner = BuildBanner();
        var page = FlexColumn(
            BuildHeader().Flex(shrink: 0),
            banner,
            body.Flex(grow: 1, basis: 0));

        var layers = new List<Element> { page.Grid(row: 0, column: 0) };
        if (state.SubmitOpen) layers.Add(BuildSubmitDialog().Grid(row: 0, column: 0));
        else if (state.HelpOpen) layers.Add(HelpOverlay.Build(() => Update(s => s with { HelpOpen = false })).Grid(row: 0, column: 0));
        var root = Grid(columns: [GridSize.Star()], rows: [GridSize.Star()], layers.ToArray());

        return CommandHost(BuildCommands(!state.Typing), root);
    }

    // ------------------------------------------------------------------ state

    private static ReviewUiState Initial(ReviewBundle b, bool narrow)
    {
        var showSince = b.Since is not null && b.Range.Kind == ReviewRangeKind.SinceLastReview;
        var files = showSince ? b.Since!.Files : b.Full.Files;
        var ordered = FileTreeBuilder.TreeOrder(files).Select(i => files[i]).ToList();
        string? start = null;
        if (AppOptions.StartFile is { } wanted && files.FirstOrDefault(f => f.Path.Contains(wanted, StringComparison.OrdinalIgnoreCase)) is { } match) start = match.Path;
        else if (b.InitialFile is not null && files.Any(f => f.Path == b.InitialFile)) start = b.InitialFile;
        else
        {
            var firstReadable = ordered.FirstOrDefault(f => !FileKinds.CollapsedByDefault(FileKinds.Classify(f.Path, f.IsBinary)));
            start = (firstReadable ?? ordered.FirstOrDefault())?.Path;
        }

        return new ReviewUiState
        {
            Session = b.Session,
            Threads = b.Threads,
            ShowSince = showSince,
            SelectedPath = start,
            Screen = narrow && b.InitialFile is not null ? 1 : 0,
        };
    }

    private Command[] BuildCommands(bool enabled)
    {
        Command Cmd(string label, VirtualKey key, Action action, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) =>
            new Command { Label = label, Execute = action, Accelerator = Accelerator(key, modifiers), CanExecute = enabled };

        return new[]
        {
            Cmd("Next file", VirtualKey.J, () => MoveFile(1)),
            Cmd("Previous file", VirtualKey.K, () => MoveFile(-1)),
            Cmd("Next open thread", VirtualKey.N, () => GoToThread(true)),
            Cmd("Previous open thread", VirtualKey.P, () => GoToThread(false)),
            Cmd("Toggle viewed", VirtualKey.V, ToggleCurrentViewed),
            Cmd("Comment on the selected line", VirtualKey.C, CommentOnCursor),
            Cmd("Keyboard help", VirtualKey.F1, () => Update(s => s with { HelpOpen = true })),
            Cmd("Keyboard help", (VirtualKey)191, () => Update(s => s with { HelpOpen = true }), VirtualKeyModifiers.Shift),
        };
    }

    // ------------------------------------------------------------------ actions

    private async void Run(Func<Task> work, Func<ReviewUiState, ReviewUiState>? rollback = null)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var message = ex.Message.Length == 0 ? "Something went wrong." : ex.Message;
            Update(s => (rollback is null ? s : rollback(s)) with { Error = message });
        }
    }

    private void SelectFile(string path) =>
        Update(s => s with
        {
            SelectedPath = path,
            Screen = 1,
            Composer = null,
            ReplyThreadId = null,
            EditCommentId = null,
            CursorKey = null,
            ScrollKey = null,
        });

    private void MoveFile(int delta)
    {
        if (Ordered.Count == 0) return;
        var index = -1;
        for (var i = 0; i < Ordered.Count; i++)
            if (Current is not null && Ordered[i].Path == Current.Path) index = i;
        var next = Ordered[((index + delta) % Ordered.Count + Ordered.Count) % Ordered.Count];
        SelectFile(next.Path);
    }

    private void ToggleCurrentViewed()
    {
        if (Current is null) return;
        ToggleViewed(Current.Path, !S.Session.IsViewed(Current.Path));
    }

    private void ToggleViewed(string path, bool viewed)
    {
        Update(s => s with { Session = s.Session.WithViewed(path, viewed), Error = string.Empty });
        Run(
            () => viewed ? Data.MarkFileViewedAsync(Target, path) : Data.UnmarkFileViewedAsync(Target, path),
            s => s with { Session = s.Session.WithViewed(path, !viewed) });
    }

    private void GoToThread(bool forward)
    {
        var thread = ReviewSession.NextUnresolvedThread(S.Threads, Ordered, S.FocusedThreadId, forward);
        if (thread is null)
        {
            Update(s => s with { Info = "No open threads.", FocusedThreadId = null });
            return;
        }
        ShowThread(thread);
    }

    private void ShowThread(ReviewThread thread)
    {
        var inView = Files.Any(f => f.Path == thread.Path);
        Update(s => s with
        {
            ShowSince = inView ? s.ShowSince : false,
            SelectedPath = thread.Path,
            FocusedThreadId = thread.Id,
            ScrollKey = "thread-" + thread.Id,
            ScrollNonce = s.ScrollNonce + 1,
            Screen = 1,
            Composer = null,
            Info = string.Empty,
        });
    }

    private void ShowPending(PendingComment comment) =>
        Update(s => s with
        {
            SelectedPath = comment.Path,
            ScrollKey = "pending-" + comment.Id,
            ScrollNonce = s.ScrollNonce + 1,
            Screen = 1,
        });

    private void DoNextStep()
    {
        var step = ReviewNextStep.Compute(Progress);
        switch (step.Kind)
        {
            case ReviewStepKind.NextFile:
                var path = S.Session.NextUnviewedFile(Ordered, Current?.Path);
                if (path is not null) SelectFile(path);
                break;
            case ReviewStepKind.NextThread:
                GoToThread(true);
                break;
            default:
                Update(s => s with { SubmitOpen = true, Error = string.Empty });
                break;
        }
    }

    private void CommentOnCursor()
    {
        if (Current is null) return;
        if (S.CursorKey is null)
        {
            Update(s => s with { Info = "Click a line first, then press c." });
            return;
        }
        var row = ReviewRows.Flatten(Current, RowOptions(Current)).OfType<LineRow>().FirstOrDefault(r => r.Key == S.CursorKey);
        if (row is not null) OpenComposer(Current, row, ThreadAnchor.SideOf(row.Line));
    }

    private void OpenComposer(DiffFile file, LineRow row, DiffSide side)
    {
        if (!row.Commentable) return;
        var line = ThreadAnchor.NumberOn(row.Line, side);
        if (line is null) return;
        var number = line.Value;
        Update(s => s with
        {
            Composer = new ComposerState(file.Path, number, side, null, string.Empty),
            CursorKey = row.Key,
            ReplyThreadId = null,
            EditCommentId = null,
        });
    }

    private void ExtendComposer(DiffFile file, int delta)
    {
        var composer = S.Composer;
        if (composer is null) return;
        var location = ThreadAnchor.Find(file, composer.Line, composer.Side);
        if (location is null) return;
        var hunk = file.Hunks[location.Value.HunkIndex];
        var min = ThreadAnchor.MinLine(hunk, composer.Side);
        var start = composer.StartLine ?? composer.Line;
        var next = start - delta;
        if (next < min || next > composer.Line) return;
        var newStart = next == composer.Line ? (int?)null : next;
        Update(s => s.Composer is null ? s : s with { Composer = s.Composer with { StartLine = newStart } });
    }

    private void SavePending(string text)
    {
        var composer = S.Composer;
        if (composer is null) return;
        var body = text.Trim();
        if (body.Length == 0) return;
        if (composer.EditPendingId is not null)
        {
            var id = composer.EditPendingId;
            Update(s => s with { Session = s.Session.UpdatePending(id, body), Composer = null });
            return;
        }
        var comment = new PendingComment(Guid.NewGuid().ToString("N"), composer.Path, composer.Line, composer.Side, composer.StartLine, body, DateTimeOffset.UtcNow);
        Update(s => s with { Session = s.Session.AddPending(comment), Composer = null });
    }

    private void EditPending(PendingComment comment) =>
        Update(s => s with
        {
            Composer = new ComposerState(comment.Path, comment.Line, comment.Side, comment.StartLine, comment.Body, comment.Id),
            ReplyThreadId = null,
        });

    private void DeletePending(string id) =>
        Update(s => s with { Session = s.Session.RemovePending(id) });

    private static IReadOnlyList<ReviewThread> MapThread(IReadOnlyList<ReviewThread> threads, string id, Func<ReviewThread, ReviewThread?> change)
    {
        var result = new List<ReviewThread>();
        foreach (var thread in threads)
        {
            if (thread.Id != id)
            {
                result.Add(thread);
                continue;
            }
            var changed = change(thread);
            if (changed is not null) result.Add(changed);
        }
        return result;
    }

    private void SetResolved(ReviewThread thread, bool resolved)
    {
        var id = thread.Id;
        Update(s => s with { Threads = MapThread(s.Threads, id, t => t.WithResolved(resolved)), Error = string.Empty });
        Run(
            () => resolved ? Data.ResolveThreadAsync(Target, id) : Data.UnresolveThreadAsync(Target, id),
            s => s with { Threads = MapThread(s.Threads, id, t => t.WithResolved(!resolved)) });
    }

    private void Reply(ReviewThread thread, string text)
    {
        var id = thread.Id;
        var body = text.Trim();
        Update(s => s with { Busy = true, Error = string.Empty });
        Run(async () =>
        {
            var comment = await Data.ReplyAsync(Target, id, body);
            Update(s => s with { Busy = false, ReplyThreadId = null, Threads = MapThread(s.Threads, id, t => t.WithComment(comment)) });
        }, s => s with { Busy = false });
    }

    private void EditComment(ReviewThread thread, ReviewComment comment, string text)
    {
        var threadId = thread.Id;
        var body = text.Trim();
        Update(s => s with { Busy = true, Error = string.Empty });
        Run(async () =>
        {
            var updated = await Data.EditCommentAsync(Target, comment.Id, body);
            Update(s => s with { Busy = false, EditCommentId = null, Threads = MapThread(s.Threads, threadId, t => t.ReplaceComment(comment.Id, updated)) });
        }, s => s with { Busy = false });
    }

    private void DeleteComment(ReviewThread thread, ReviewComment comment)
    {
        var threadId = thread.Id;
        Update(s => s with { Busy = true, Error = string.Empty });
        Run(async () =>
        {
            await Data.DeleteCommentAsync(Target, comment.Id);
            Update(s => s with { Busy = false, Threads = MapThread(s.Threads, threadId, t => t.WithoutComment(comment.Id)) });
        }, s => s with { Busy = false });
    }

    private void Submit(ReviewVerdict verdict, string summary)
    {
        var pending = S.Session.Pending;
        var body = summary.Trim();
        Update(s => s with { Busy = true, Error = string.Empty, Session = s.Session.WithSummary(summary).WithVerdict(verdict) });
        Run(async () =>
        {
            await Data.SubmitReviewAsync(Target, verdict, body, pending);
            IReadOnlyList<ReviewThread>? fresh = null;
            try
            {
                fresh = await Data.GetThreadsAsync(Target);
            }
            catch (Exception)
            {
                fresh = null;
            }
            Update(s => s with
            {
                Busy = false,
                SubmitOpen = false,
                Session = s.Session.ClearPending().WithSummary(string.Empty),
                Threads = fresh ?? s.Threads,
                Info = "Review submitted.",
            });
        }, s => s with { Busy = false });
    }

    private void ExpandGap(DiffFile file, int gapIndex)
    {
        var key = file.Path + "|gap-" + gapIndex;
        Update(s => s with { ExpandedGaps = s.ExpandedGaps.Append(key).Distinct().ToList() });
        if (S.FileLines.ContainsKey(file.Path)) return;
        var path = file.Path;
        Run(async () =>
        {
            var text = await Data.GetFileContentAsync(Target, path, Props.Context.HeadSha);
            if (text is null) throw new InvalidOperationException("Could not load " + path + " to show the hidden lines.");
            var lines = SplitLines(text);
            Update(s =>
            {
                var map = new Dictionary<string, IReadOnlyList<string>>(s.FileLines) { [path] = lines };
                return s with { FileLines = map };
            });
        }, s => s with { ExpandedGaps = s.ExpandedGaps.Where(k => k != key).ToList() });
    }

    internal static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private FlattenOptions RowOptions(DiffFile file)
    {
        var prefix = file.Path + "|";
        var gaps = S.ExpandedGaps.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k.Substring(prefix.Length)).ToList();
        S.FileLines.TryGetValue(file.Path, out var lines);
        var composer = S.Composer is not null && S.Composer.Path == file.Path
            ? new ComposerTarget(S.Composer.Path, S.Composer.Line, S.Composer.Side)
            : null;
        return new FlattenOptions
        {
            ExpandedFiles = S.ExpandedFiles,
            ExpandedGaps = gaps,
            FileLines = lines,
            Threads = S.Threads,
            Pending = S.Session.Pending,
            Composer = composer,
        };
    }

    // ------------------------------------------------------------------ small views

    private static Element Divided(Element content, bool left = false, bool right = false) =>
        Border(content).BorderThickness(left ? 1 : 0, 0, right ? 1 : 0, 0).BorderBrush(Theme.DividerStroke);

    private Element BuildBanner()
    {
        if (S.Error.Length > 0) return BannerView(S.Error, Tone.Danger);
        if (S.Info.Length > 0) return BannerView(S.Info, Tone.Neutral);
        return Empty();
    }

    private Element BannerView(string text, Tone tone) =>
        Border(Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                TextBlock(text).TextWrapping().Foreground(tone == Tone.Neutral ? Theme.PrimaryText : Kit.Fg(tone)).VAlign(VerticalAlignment.Center).Grid(column: 0),
                Button("Dismiss", () => Update(s => s with { Error = string.Empty, Info = string.Empty })).SubtleButton().AutomationName("Dismiss message").Grid(column: 1)))
            .Background(Kit.Bg(tone)).Padding(16, 6);

    private Element BuildSubmitDialog()
    {
        var own = Props.Pr is not null && Props.Pr.Role == Cshubben.Core.Model.PrRole.Authored;
        var props = new SubmitProps(
            S.Session.Pending.Count,
            S.Session.Summary,
            S.Session.Verdict,
            own,
            S.Busy,
            S.Error,
            Submit,
            (verdict, summary) => Update(s => s with { SubmitOpen = false, Session = s.Session.WithSummary(summary).WithVerdict(verdict) }));
        return Component<SubmitDialog, SubmitProps>(props).WithKey("submit-dialog");
    }
}
