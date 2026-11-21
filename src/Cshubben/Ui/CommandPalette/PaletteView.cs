using Cshubben.Core.Features.CommandPalette;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Palette;

/// <summary>The visual part of the palette: a scrim, a search box and a short, keyboard-driven result list.</summary>
static class PaletteView
{
    public static Element Build(
        string query,
        Action<string> setQuery,
        IReadOnlyList<PaletteResult> results,
        int selected,
        int visibleRows,
        Action<PaletteResult, bool> run,
        Action<int> move,
        Action close)
    {
        var box = TextBox(query, setQuery, placeholderText: "Search pages, pull requests and commands")
            .AutomationName("Command palette search")
            .OnPreviewKeyDown((s, e) =>
            {
                switch (e.Key)
                {
                    case VirtualKey.Down:
                        move(1);
                        e.Handled = true;
                        break;
                    case VirtualKey.Up:
                        move(-1);
                        e.Handled = true;
                        break;
                    case VirtualKey.Tab:
                        move(KeyInput.IsDown(VirtualKey.Shift) ? -1 : 1);
                        e.Handled = true;
                        break;
                    case VirtualKey.Escape:
                        close();
                        e.Handled = true;
                        break;
                    case VirtualKey.Enter:
                        if (results.Count > 0) run(results[Math.Min(selected, results.Count - 1)], KeyInput.IsDown(VirtualKey.Control));
                        e.Handled = true;
                        break;
                }
            })
            .Set(tb => tb.Loaded += (o, args) => tb.Focus(FocusState.Programmatic));

        // Show a window of rows around the selection so the selected row is always visible (no scrolling needed).
        var start = Math.Max(0, Math.Min(selected - visibleRows / 2, results.Count - visibleRows));
        var rows = new List<Element>();
        for (var i = start; i < Math.Min(results.Count, start + visibleRows); i++)
        {
            var index = i;
            rows.Add(Row(results[i], i == selected, () => run(results[index], false)).WithKey(results[i].Item.Id));
        }

        Element list = results.Count == 0
            ? Kit.EmptyNote("Nothing matches. Try a page name, a pull request title or number.")
            : VStack(2, rows.ToArray());
        var more = results.Count > visibleRows
            ? Kit.Muted((results.Count - visibleRows) + " more, keep typing or use the arrow keys").Margin(8, 0, 0, 0)
            : Empty();

        var card = Border(VStack(8,
                box,
                list,
                more,
                Kit.Muted("Enter open  ·  Ctrl+Enter on a pull request opens GitHub  ·  > commands  ·  #123 by number  ·  Esc close")
                    .TextWrapping().Margin(8, 4, 0, 0)))
            .Background(Theme.CardBackground).WithBorder(Theme.ControlStroke).CornerRadius(8).Padding(12)
            .MaxWidth(640).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Top).Margin(24, 88, 24, 24)
            .OnTapped((s, e) => e.Handled = true);

        return Border(card).Background(Theme.SmokeFill).OnTapped((s, e) => close());
    }

    private static Element Row(PaletteResult result, bool selected, Action click)
    {
        var item = result.Item;
        var glyph = item.Glyph.Length > 0
            ? Kit.Glyph(item.Glyph, 14).Foreground(Theme.SecondaryText).VAlign(VerticalAlignment.Center)
            : (Element)Empty();

        var sub = item.Subtitle.Length > 0 ? Kit.Muted(item.Subtitle).TextTrimming(TextTrimming.CharacterEllipsis) : null;
        var text = VStack(0, Title(item.Title, result.TitleMatches), sub);

        var shortcut = item.Shortcut.Length > 0
            ? Kit.Pill(item.Shortcut, Cshubben.Core.Logic.Tone.Neutral).VAlign(VerticalAlignment.Center)
            : Empty();

        var row = Grid(
            columns: [GridSize.Px(28), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            glyph.Grid(column: 0),
            text.VAlign(VerticalAlignment.Center).Grid(column: 1),
            shortcut.Grid(column: 2));

        var button = Button(row, click)
            .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(item.Title);
        var bar = Border(Empty()).Width(3).CornerRadius(2).Margin(0, 6, 0, 6);
        if (selected) bar = bar.Background(Theme.Accent);

        return Border(Grid(
                columns: [GridSize.Px(5), GridSize.Star()],
                rows: [GridSize.Auto],
                bar.Grid(column: 0),
                button.Grid(column: 1)))
            .Selected(selected).CornerRadius(6);
    }

    /// <summary>The title with the matched characters in bold.</summary>
    private static Element Title(string title, IReadOnlyList<int> matches)
    {
        if (matches.Count == 0) return TextBlock(title).TextTrimming(TextTrimming.CharacterEllipsis);

        var hit = new HashSet<int>(matches);
        var inlines = new List<RichTextInline>();
        var i = 0;
        while (i < title.Length)
        {
            var bold = hit.Contains(i);
            var j = i;
            while (j < title.Length && hit.Contains(j) == bold) j++;
            var run = Run(title.Substring(i, j - i));
            inlines.Add(bold ? run with { IsBold = true } : run);
            i = j;
        }

        return RichTextBlock(new[] { Paragraph(inlines.ToArray()) }).MaxLines(1).TextTrimming(TextTrimming.CharacterEllipsis);
    }
}
