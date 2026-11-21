using Cshubben.Core.Features.Review;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Review;

sealed partial class ReviewWorkspace
{
    // ------------------------------------------------------------------ find in file (Ctrl+F)

    private void OpenFind()
    {
        if (Current is null) return;
        // The diff must be visible on narrow windows, otherwise there is nothing to search.
        Update(s => s with { FindOpen = true, Screen = s.Screen == 0 ? 1 : s.Screen, FindFocusNonce = s.FindFocusNonce + 1 });
    }

    private void CloseFind() =>
        Update(s => s with { FindOpen = false, FindFocused = false, FindQuery = string.Empty, FindIndex = -1 });

    private void FindQueryChanged(string query)
    {
        var matches = DiffSearch.Matches(CurrentRows(), query);
        Update(s => matches.Count == 0
            ? s with { FindQuery = query, FindIndex = -1 }
            : s with
            {
                FindQuery = query,
                FindIndex = 0,
                CursorKey = matches[0],
                ScrollKey = matches[0],
                ScrollNonce = s.ScrollNonce + 1,
            });
    }

    private void FindStep(int delta)
    {
        var matches = DiffSearch.Matches(CurrentRows(), S.FindQuery);
        var index = DiffSearch.Step(matches.Count, S.FindIndex, delta);
        if (index < 0) return;
        var key = matches[index];
        Update(s => s with { FindIndex = index, CursorKey = key, ScrollKey = key, ScrollNonce = s.ScrollNonce + 1 });
    }

    private static bool IsShiftDown() =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
         & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private Element BuildFindBar(int matchCount)
    {
        var box = TextBox(S.FindQuery, FindQueryChanged, placeholderText: "Find in this file")
            .AutomationName("Find in this file")
            .OnGotFocus((o, e) => Update(s => s with { FindFocused = true }))
            .OnLostFocus((o, e) => Update(s => s with { FindFocused = false }))
            .OnPreviewKeyDown((o, e) =>
            {
                if (e.Key == VirtualKey.Escape)
                {
                    CloseFind();
                    e.Handled = true;
                }
                else if (e.Key == VirtualKey.Enter)
                {
                    FindStep(IsShiftDown() ? -1 : 1);
                    e.Handled = true;
                }
            });

        var seen = FindSeen;
        var nonce = S.FindFocusNonce;
        box = box.Set(tb =>
        {
            if (seen.Current == nonce) return;
            seen.Current = nonce;
            tb.DispatcherQueue.TryEnqueue(() => tb.Focus(FocusState.Programmatic));
        });

        var label = DiffSearch.Label(S.FindQuery, matchCount, S.FindIndex);
        var bar = FlexRow(
            box.Flex(grow: 1),
            Kit.Muted(label).VAlign(VerticalAlignment.Center),
            Button(Kit.Glyph("", 12), () => FindStep(-1)).SubtleButton().IsEnabled(matchCount > 0).AutomationName("Previous match").ToolTip("Previous match (Shift+Enter)"),
            Button(Kit.Glyph("", 12), () => FindStep(1)).SubtleButton().IsEnabled(matchCount > 0).AutomationName("Next match").ToolTip("Next match (Enter)"),
            Button("Close", CloseFind).SubtleButton().AutomationName("Close find").ToolTip("Close (Esc)")) with { ColumnGap = 6, AlignItems = FlexAlign.Center };
        return Border(bar).Padding(16, 6).BorderThickness(0, 0, 0, 1).BorderBrush(Theme.DividerStroke);
    }
}
