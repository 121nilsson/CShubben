using Cshubben.Core.Model;

namespace Cshubben.Ui.Palette;

/// <summary>A page the palette can jump to.</summary>
/// <param name="Key">Page key, the same string <see cref="PaletteContext.Navigate"/> receives ("overview", "mine", "reviews", ... case-insensitive, equal to the <c>Page</c> enum name).</param>
/// <param name="Label">Name shown in the palette.</param>
/// <param name="Glyph">Segoe Fluent Icons character (may be empty).</param>
sealed record PalettePage(string Key, string Label, string Glyph);

/// <summary>
/// Everything the palette needs from the shell. The shell (AppRoot) fills it on every render; the palette never
/// reaches into app state itself.
/// </summary>
/// <param name="Snapshot">Current data; pull requests are searched from here.</param>
/// <param name="SelectedPrId">PR open in the inspector, if any (enables "Open on GitHub").</param>
/// <param name="Pages">Pages to offer, usually the page registry without hidden entries.</param>
/// <param name="Navigate">Go to a page by key.</param>
/// <param name="SelectPr">Open a PR in the inspector by id.</param>
/// <param name="Refresh">Reload the data now.</param>
/// <param name="NewNote">Go to Notes and start a new note.</param>
/// <param name="OpenSettings">Go to Settings.</param>
/// <param name="OpenUrl">Opens a web link; null uses the default browser.</param>
sealed record PaletteContext(
    HubSnapshot Snapshot,
    string? SelectedPrId,
    IReadOnlyList<PalettePage> Pages,
    Action<string> Navigate,
    Action<string> SelectPr,
    Action Refresh,
    Action NewNote,
    Action OpenSettings,
    Action<string>? OpenUrl = null);
