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
    // ------------------------------------------------------------------ apply suggestion

    private const int MaxPreviewLines = 12;

    /// <summary>Step one: read the file and show what would change. Nothing is written.</summary>
    private void PreviewSuggestion(ReviewThread thread, ReviewComment comment, SuggestionRequest request)
    {
        var threadId = thread.Id;
        var commentId = comment.Id;
        Update(s => s with { Busy = true, Error = string.Empty, Info = string.Empty, Suggestion = null });
        Run(async () =>
        {
            var plan = await Data.PreviewSuggestionAsync(Target, request);
            Update(s => s with { Busy = false, Suggestion = new SuggestionState(threadId, commentId, request, plan) });
        }, s => s with { Busy = false });
    }

    /// <summary>Step two, after the reviewer saw the change: commit it (or only report it in a dry run).</summary>
    private void CommitSuggestion()
    {
        var state = S.Suggestion;
        if (state is null || !state.Plan.Ok) return;
        Update(s => s with { Busy = true, Error = string.Empty });
        Run(async () =>
        {
            var outcome = await Data.ApplySuggestionAsync(Target, state.Request);
            Update(s => s with
            {
                Busy = false,
                Suggestion = outcome.Plan.Ok ? null : new SuggestionState(state.ThreadId, state.CommentId, state.Request, outcome.Plan),
                Info = outcome.Plan.Ok ? outcome.Message : string.Empty,
                Error = outcome.Plan.Ok ? string.Empty : outcome.Message,
            });
        }, s => s with { Busy = false });
    }

    private Element SuggestionPanel(SuggestionState state)
    {
        var plan = state.Plan;
        var close = Button("Cancel", () => Update(s => s with { Suggestion = null })).AutomationName("Cancel applying the suggestion");

        if (!plan.Ok)
        {
            return ReviewKit.Surface(FlexColumn(
                TextBlock(plan.Error).TextWrapping().Foreground(Kit.Fg(Tone.Danger)),
                close.HAlign(HorizontalAlignment.Right)) with { RowGap = 8 });
        }

        var range = plan.StartLine == plan.EndLine ? "line " + plan.StartLine : "lines " + plan.StartLine + "–" + plan.EndLine;
        var parts = new List<Element>
        {
            Kit.Muted("This changes " + range + " of " + plan.Path),
        };
        parts.AddRange(LineBlock(plan.Removed, "−", Theme.SystemCriticalBackground));
        parts.AddRange(LineBlock(plan.Added, "+", Theme.SystemSuccessBackground));
        if (plan.Removed.Count == 0 && plan.Added.Count == 0) parts.Add(Kit.Muted("The suggestion is empty."));
        if (plan.IsNoChange) parts.Add(Kit.Muted("The suggestion matches the current code, so there is nothing to commit."));

        parts.Add(Kit.Muted(Data.IsDryRun
            ? "Dry run: committing only shows the result. Nothing is written to GitHub until you turn on \"Allow writes to GitHub\" in Settings."
            : "Committing adds a commit to the pull request branch right away.").TextWrapping());

        var commitLabel = S.Busy ? "Committing…" : Data.IsDryRun ? "Commit (dry run)" : "Commit suggestion";
        parts.Add(FlexRow(
            Empty().Flex(grow: 1),
            close,
            Button(commitLabel, CommitSuggestion).AccentButton().IsEnabled(!S.Busy && !plan.IsNoChange).AutomationName("Commit suggestion")) with { ColumnGap = 8 });

        return ReviewKit.Surface(FlexColumn(parts.ToArray()) with { RowGap = 6 });
    }

    private static IEnumerable<Element> LineBlock(IReadOnlyList<string> lines, string sign, ThemeRef background)
    {
        var shown = lines.Take(MaxPreviewLines).ToList();
        foreach (var line in shown)
        {
            yield return Border(TextBlock(sign + " " + ReviewKit.CodeText(line)).FontFamily(ReviewKit.Mono).FontSize(12)
                    .TextWrapping(TextWrapping.NoWrap).TextTrimming(TextTrimming.CharacterEllipsis))
                .Background(background).Padding(8, 1);
        }
        if (lines.Count > shown.Count) yield return Kit.Muted("… " + (lines.Count - shown.Count) + " more lines");
    }
}
