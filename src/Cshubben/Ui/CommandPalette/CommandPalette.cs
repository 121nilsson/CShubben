using Cshubben.Core.Features.CommandPalette;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using Cshubben.Ui.Pages;

namespace Cshubben.Ui.Palette;

/// <summary>
/// The command palette (Ctrl+K) and the app-wide keyboard shortcuts. Typical use in the shell:
/// <c>return CommandPalette.Host(paletteContext, root);</c> wraps the whole window content, so the shortcuts work anywhere
/// in it. <see cref="Overlay"/> alone only mounts the overlay (shortcuts then work only while focus is inside it).
/// </summary>
static class CommandPalette
{
    private static readonly List<string> RecentIds = new();

    /// <summary>Raised to open the palette from anywhere (for example a title-bar button).</summary>
    public static event Action? OpenRequested;

    /// <summary>Raised to open the keyboard shortcut sheet from anywhere.</summary>
    public static event Action? HelpRequested;

    public static void Open() => OpenRequested?.Invoke();

    public static void OpenHelp() => HelpRequested?.Invoke();

    /// <summary>Wraps <paramref name="content"/> with the palette overlay, the help sheet and the global shortcuts.</summary>
    public static Element Host(PaletteContext ctx, Element content) =>
        Component<PaletteHost, PaletteHostProps>(new PaletteHostProps(ctx, content));

    /// <summary>Only the overlay and help sheet, for mounting in a Grid on top of everything.</summary>
    public static Element Overlay(PaletteContext ctx) =>
        Component<PaletteHost, PaletteHostProps>(new PaletteHostProps(ctx, null));

    internal static IReadOnlyList<string> Recent => RecentIds;

    internal static void Remember(string id)
    {
        RecentIds.Remove(id);
        RecentIds.Insert(0, id);
        if (RecentIds.Count > 8) RecentIds.RemoveRange(8, RecentIds.Count - 8);
    }
}

sealed record PaletteHostProps(PaletteContext Ctx, Element? Content);

/// <summary>Owns the palette state (open, query, selection, help, pending chord) and renders the layers.</summary>
sealed class PaletteHost : Component<PaletteHostProps>
{
    private const int VisibleRows = 9;

    public override Element Render()
    {
        var ctx = Props.Ctx;
        var (_, windowHeight) = UseWindowSize();
        var (open, setOpen) = UseState(AppOptions.PaletteQuery is not null, true);
        var (help, setHelp) = UseState(false, true);
        var (query, setQuery) = UseState(AppOptions.PaletteQuery ?? "");
        var (selected, setSelected) = UseState(0);
        var (chordPending, setChordPending) = UseState(false, true);
        var chord = UseRef(new ChordMatcher());

        // Open requests from outside (title-bar button, other pages).
        UseEffect(() =>
        {
            void OnOpen()
            {
                setQuery("");
                setSelected(0);
                setHelp(false);
                setOpen(true);
            }

            void OnHelp()
            {
                setOpen(false);
                setHelp(true);
            }

            CommandPalette.OpenRequested += OnOpen;
            CommandPalette.HelpRequested += OnHelp;
            return () =>
            {
                CommandPalette.OpenRequested -= OnOpen;
                CommandPalette.HelpRequested -= OnHelp;
            };
        }, Array.Empty<object>());

        var items = UseMemo(
            () => PaletteCatalog.Build(ctx),
            ctx.Snapshot, ctx.SelectedPrId ?? "", ctx.Pages.Count);

        var results = open
            ? PaletteRanker.Rank(items, query, 40, CommandPalette.Recent)
            : Array.Empty<PaletteResult>();
        var current = PaletteNavigation.Clamp(selected, results.Count);

        void Close() => setOpen(false);

        void Run(PaletteResult result, bool secondary)
        {
            CommandPalette.Remember(result.Item.Id);
            if (PaletteCatalog.Execute(ctx, result.Item, () => setHelp(true), secondary)) setOpen(false);
        }

        void Toggle()
        {
            if (open)
            {
                setOpen(false);
                return;
            }

            setQuery("");
            setSelected(0);
            setHelp(false);
            setOpen(true);
        }

        void OnKey(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // The palette's own box handles its keys; the sheet closes with Esc/Enter on its own button.
            if (open || e.Handled) return;
            if (help)
            {
                if (e.Key == VirtualKey.Escape)
                {
                    setHelp(false);
                    e.Handled = true;
                }

                return;
            }

            if (KeyInput.IsTextInput(e.OriginalSource) || KeyInput.IsDown(VirtualKey.Control) || KeyInput.IsDown(VirtualKey.Menu)) return;

            if ((int)e.Key == 191 && KeyInput.IsDown(VirtualKey.Shift))
            {
                // '?' (Shift + the slash key on most layouts).
                setHelp(true);
                e.Handled = true;
                return;
            }

            if (e.Key < VirtualKey.A || e.Key > VirtualKey.Z || KeyInput.IsDown(VirtualKey.Shift)) return;
            var letter = (char)('a' + (e.Key - VirtualKey.A));
            var outcome = chord.Current.Feed(letter, DateTimeOffset.UtcNow);
            switch (outcome.Outcome)
            {
                case ChordOutcome.Pending:
                    setChordPending(true);
                    _ = ClearPendingLater(setChordPending);
                    break;
                case ChordOutcome.Completed:
                    setChordPending(false);
                    var page = ctx.Pages.FirstOrDefault(p => string.Equals(p.Key, outcome.Target, StringComparison.OrdinalIgnoreCase));
                    if (page is not null)
                    {
                        ctx.Navigate(page.Key);
                        e.Handled = true;
                    }

                    break;
                case ChordOutcome.Cancelled:
                    setChordPending(false);
                    break;
            }
        }

        var commands = new[]
        {
            new Command
            {
                Label = "Command palette",
                Execute = Toggle,
                Accelerator = Accelerator(VirtualKey.K, VirtualKeyModifiers.Control),
            },
            new Command
            {
                Label = "Keyboard shortcuts",
                Execute = () => setHelp(!help),
                Accelerator = Accelerator(VirtualKey.F1),
            },
        };

        var layers = new List<Element>();
        if (Props.Content is { } content) layers.Add(content.Grid(row: 0, column: 0));
        if (help) layers.Add(ShortcutsHelp.Build(() => setHelp(false)).Grid(row: 0, column: 0));
        else if (open)
            layers.Add(PaletteView.Build(
                query,
                q =>
                {
                    setQuery(q);
                    setSelected(0);
                },
                results,
                current,
                Math.Clamp((int)((windowHeight - 270) / 48), 3, VisibleRows),
                Run,
                delta => setSelected(PaletteNavigation.Move(current, delta, results.Count)),
                Close).Grid(row: 0, column: 0));
        if (chordPending && !open && !help)
            layers.Add(Kit.Pill("G then " + string.Join(" ", ChordMatcher.Default.Select(b => char.ToUpperInvariant(b.Key).ToString())), Cshubben.Core.Logic.Tone.Accent)
                .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Bottom).Margin(0, 0, 20, 20)
                .IsHitTestVisible(false).Grid(row: 0, column: 0));

        var root = Grid(columns: [GridSize.Star()], rows: [GridSize.Star()], layers.ToArray())
            .OnKeyDown((s, e) => OnKey(e));
        return CommandHost(commands, root);
    }

    private static async Task ClearPendingLater(Action<bool> setPending)
    {
        try
        {
            await Task.Delay(ChordMatcher.Timeout + TimeSpan.FromMilliseconds(100));
            setPending(false);
        }
        catch (Exception)
        {
            // Component gone: nothing to clear.
        }
    }
}

/// <summary>Key state helpers.</summary>
static class KeyInput
{
    public static bool IsDown(VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
         & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    /// <summary>True when the key event comes from a control the user types into.</summary>
    public static bool IsTextInput(object? source) =>
        source is Microsoft.UI.Xaml.Controls.TextBox
            or Microsoft.UI.Xaml.Controls.PasswordBox
            or Microsoft.UI.Xaml.Controls.RichEditBox
            or Microsoft.UI.Xaml.Controls.AutoSuggestBox
            or Microsoft.UI.Xaml.Controls.NumberBox;
}
