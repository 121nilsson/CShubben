using Cshubben.Core.Features.Review;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Review;

/// <param name="Initial">Text the box starts with (a draft being edited, or a suggestion template).</param>
/// <param name="Header">Small line above the box, for example "Comment on lines 12-14".</param>
/// <param name="SubmitLabel">Text of the primary button.</param>
/// <param name="SuggestionTemplate">When set, a "Suggest change" button appends this block to the text.</param>
/// <param name="OnExtendUp">When set, a button extends a line range upwards by one line.</param>
/// <param name="OnShrink">When set, a button shrinks the range again.</param>
sealed record TextEntryProps(
    string Initial,
    string Header,
    string Placeholder,
    string SubmitLabel,
    Action<string> OnSubmit,
    Action OnCancel,
    string? SuggestionTemplate = null,
    Action? OnExtendUp = null,
    Action? OnShrink = null);

/// <summary>
/// A markdown text box with write/preview, used for inline comments and replies. It owns its own text, so typing
/// never re-renders the whole diff. Ctrl+Enter submits, Escape cancels.
/// </summary>
sealed class TextEntry : Component<TextEntryProps>
{
    public override Element Render()
    {
        var p = Props;
        var (text, setText) = UseState(p.Initial);
        var (preview, setPreview) = UseState(false);

        var canSubmit = text.Trim().Length > 0;

        void Submit()
        {
            if (text.Trim().Length > 0) p.OnSubmit(text);
        }

        Element body = preview
            ? Border(text.Trim().Length == 0 ? Kit.Muted("Nothing to preview.") : Kit.Md(text))
                .MinHeight(90).Padding(8).Background(Theme.LayerFill).CornerRadius(4)
            : TextBox(text, setText, placeholderText: p.Placeholder)
                .AcceptsReturn()
                .TextWrapping()
                .MinHeight(90)
                .AutomationName(p.Header)
                .OnPreviewKeyDown((s, e) =>
                {
                    if (e.Key == VirtualKey.Escape)
                    {
                        p.OnCancel();
                        e.Handled = true;
                    }
                    else if (e.Key == VirtualKey.Enter && IsCtrlDown())
                    {
                        Submit();
                        e.Handled = true;
                    }
                })
                .Set(tb => tb.Loaded += (s, e) => tb.Focus(FocusState.Programmatic));

        var header = FlexRow(
            Kit.Muted(p.Header).VAlign(VerticalAlignment.Center).Flex(grow: 1),
            p.OnExtendUp is null ? Empty() : Button("↑ Line above", p.OnExtendUp).SubtleButton().AutomationName("Extend range up"),
            p.OnShrink is null ? Empty() : Button("↓ Fewer", p.OnShrink).SubtleButton().AutomationName("Shrink range"),
            ToggleButton(preview ? "Edit" : "Preview", preview, setPreview).AutomationName("Toggle preview")) with { ColumnGap = 4 };

        var buttons = FlexRow(
            p.SuggestionTemplate is null || preview
                ? Empty()
                : Button(HStack(6, Kit.Glyph(ReviewKit.GlyphEdit, 12), TextBlock("Suggest change")),
                        () => setText(text.Length == 0 ? p.SuggestionTemplate : text.TrimEnd() + "\n\n" + p.SuggestionTemplate))
                    .SubtleButton().AutomationName("Suggest a change"),
            Empty().Flex(grow: 1),
            Kit.Muted("Ctrl+Enter").VAlign(VerticalAlignment.Center),
            Button("Cancel", p.OnCancel).AutomationName("Cancel"),
            Button(p.SubmitLabel, Submit).AccentButton().IsEnabled(canSubmit).AutomationName(p.SubmitLabel)) with { ColumnGap = 8 };

        return ReviewKit.Surface(FlexColumn(header.Flex(shrink: 0), body.Flex(shrink: 0), buttons.Flex(shrink: 0)) with { RowGap = 8 });
    }

    internal static bool IsCtrlDown() =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
         & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
}

sealed record SubmitProps(
    int PendingCount,
    string InitialSummary,
    ReviewVerdict InitialVerdict,
    bool IsOwnPullRequest,
    bool Busy,
    string Error,
    Action<ReviewVerdict, string> OnSubmit,
    Action<ReviewVerdict, string> OnCancel);

/// <summary>The one dialog that finishes a review: verdict, summary text and the number of pending comments.</summary>
sealed class SubmitDialog : Component<SubmitProps>
{
    private static readonly ReviewVerdict[] Order = { ReviewVerdict.Comment, ReviewVerdict.Approve, ReviewVerdict.RequestChanges };

    public override Element Render()
    {
        var p = Props;
        var (verdict, setVerdict) = UseState(p.InitialVerdict);
        var (summary, setSummary) = UseState(p.InitialSummary);
        var (preview, setPreview) = UseState(false);

        var index = Array.IndexOf(Order, verdict);
        var needsText = verdict != ReviewVerdict.Approve && p.PendingCount == 0;
        var canSubmit = !p.Busy && (!needsText || summary.Trim().Length > 0) && !(p.IsOwnPullRequest && verdict != ReviewVerdict.Comment);

        Element body = preview
            ? Border(summary.Trim().Length == 0 ? Kit.Muted("Nothing to preview.") : Kit.Md(summary))
                .MinHeight(110).Padding(8).Background(Theme.LayerFill).CornerRadius(4)
            : TextBox(summary, setSummary, placeholderText: "Leave a summary (optional for Approve)")
                .AcceptsReturn()
                .TextWrapping()
                .MinHeight(110)
                .AutomationName("Review summary")
                .OnPreviewKeyDown((s, e) =>
                {
                    if (e.Key == VirtualKey.Escape)
                    {
                        p.OnCancel(verdict, summary);
                        e.Handled = true;
                    }
                    else if (e.Key == VirtualKey.Enter && TextEntry.IsCtrlDown() && canSubmit)
                    {
                        p.OnSubmit(verdict, summary);
                        e.Handled = true;
                    }
                })
                .Set(tb => tb.Loaded += (s, e) => tb.Focus(FocusState.Programmatic));

        var pending = p.PendingCount == 0
            ? "No pending comments"
            : p.PendingCount == 1 ? "1 pending comment will be posted" : p.PendingCount + " pending comments will be posted";

        var card = ReviewKit.Surface(FlexColumn(
                FlexRow(
                    BodyStrong("Submit your review").Flex(grow: 1),
                    ToggleButton(preview ? "Edit" : "Preview", preview, setPreview).AutomationName("Toggle preview")) with { ColumnGap = 8 },
                Kit.Muted(pending),
                body,
                RadioButtons(
                    new[]
                    {
                        "Comment: general feedback without approving",
                        "Approve: this is good to merge",
                        "Request changes: this must be fixed first",
                    },
                    index < 0 ? 0 : index,
                    i => setVerdict(Order[Math.Clamp(i, 0, Order.Length - 1)])),
                p.IsOwnPullRequest ? Kit.Muted("You cannot approve or request changes on your own pull request.") : Empty(),
                p.Error.Length == 0 ? Empty() : TextBlock(p.Error).Foreground(Kit.Fg(Cshubben.Core.Logic.Tone.Danger)).TextWrapping(),
                FlexRow(
                    Empty().Flex(grow: 1),
                    Button("Cancel", () => p.OnCancel(verdict, summary)).AutomationName("Cancel"),
                    Button(p.Busy ? "Submitting…" : "Submit review", () => p.OnSubmit(verdict, summary))
                        .AccentButton().IsEnabled(canSubmit).AutomationName("Submit review")) with { ColumnGap = 8 }) with { RowGap = 12 });

        return Border(Border(card).MaxWidth(560).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center).Margin(24))
            .Background(Theme.SmokeFill);
    }
}

/// <summary>The "?" overlay with every shortcut. Buttons exist for all of them, so this is a reminder, not a requirement.</summary>
static class HelpOverlay
{
    private static readonly (string Keys, string What)[] Shortcuts =
    {
        ("j / k", "Next / previous file"),
        ("n / p", "Next / previous open thread"),
        ("v", "Mark the file as viewed"),
        ("c", "Comment on the selected line (click a line first)"),
        ("Ctrl+Enter", "Save the comment you are writing"),
        ("Esc", "Cancel the comment or close a dialog"),
        ("?  or  F1", "Show this help"),
    };

    public static Element Build(Action close)
    {
        var rows = Shortcuts.Select(s => (Element)Grid(
            columns: [GridSize.Px(110), GridSize.Star()],
            rows: [GridSize.Auto],
            TextBlock(s.Keys).FontFamily(ReviewKit.Mono).SemiBold().Grid(column: 0),
            TextBlock(s.What).TextWrapping().Grid(column: 1))).ToArray();

        var card = ReviewKit.Surface(FlexColumn(
            BodyStrong("Keyboard shortcuts"),
            VStack(8, rows),
            Kit.Muted("Shortcuts work after you click inside the review. They pause while you type."),
            Button("Close", close).AccentButton().HAlign(HorizontalAlignment.Right).AutomationName("Close help")) with { RowGap = 12 });

        return Border(Border(card).MaxWidth(460).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center).Margin(24))
            .Background(Theme.SmokeFill);
    }
}
