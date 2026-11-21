using Cshubben.Core.Features.CommandPalette;
using Cshubben.Services;

namespace Cshubben.Ui.Palette;

/// <summary>Builds the palette entries from the shell context and runs the chosen one.</summary>
static class PaletteCatalog
{
    private const string ActionGlyph = "";
    private const string PrGlyph = "";

    public static IReadOnlyList<PaletteItem> Build(PaletteContext ctx)
    {
        var items = new List<PaletteItem>
        {
            new("action:refresh", PaletteKind.Action, "Refresh", "Reload pull requests now", "", "", "reload sync update"),
            new("action:new-note", PaletteKind.Action, "New note", "Start a note in Notes", "", "", "idea write"),
            new("action:settings", PaletteKind.Action, "Open settings", "Tokens, notifications, appearance", "", "", "preferences options"),
        };

        if (ctx.Snapshot.Find(ctx.SelectedPrId) is { } selected)
            items.Add(new PaletteItem("action:open-github", PaletteKind.Action, "Open on GitHub",
                selected.Repo + " #" + selected.Number + " " + selected.Title, "", "", "browser web link"));

        if (ctx.Snapshot.Find(ctx.SelectedPrId) is { } copyable)
        {
            items.Add(new PaletteItem("action:copy-link", PaletteKind.Action, "Copy link to pull request",
                copyable.Repo + " #" + copyable.Number, "", "", "clipboard url share"));
            items.Add(new PaletteItem("action:copy-markdown", PaletteKind.Action, "Copy pull request as Markdown link",
                "[" + copyable.Repo + "#" + copyable.Number + " " + copyable.Title + "](…)", "", "", "clipboard url share"));
        }

        items.Add(new PaletteItem("action:help", PaletteKind.Action, "Keyboard shortcuts", "Show all shortcuts", "", "?", "help keys hotkeys"));

        foreach (var page in ctx.Pages)
        {
            items.Add(new PaletteItem("page:" + page.Key, PaletteKind.Page, page.Label, "Go to page",
                page.Glyph, ChordFor(page.Key), "", page.Key));
        }

        items.AddRange(PaletteItems.FromSnapshot(ctx.Snapshot).Select(i => i with { Glyph = PrGlyph }));
        return items;
    }

    /// <summary>Runs an entry. Returns false when the entry is unknown.</summary>
    public static bool Execute(PaletteContext ctx, PaletteItem item, Action openHelp, bool secondary = false)
    {
        if (item.Kind == PaletteKind.PullRequest)
        {
            if (secondary) return OpenPr(ctx, item.Ref);
            ctx.SelectPr(item.Ref);
            return true;
        }

        if (item.Kind == PaletteKind.Page)
        {
            ctx.Navigate(item.Ref);
            return true;
        }

        switch (item.Id)
        {
            case "action:refresh":
                ctx.Refresh();
                return true;
            case "action:new-note":
                ctx.NewNote();
                return true;
            case "action:settings":
                ctx.OpenSettings();
                return true;
            case "action:help":
                openHelp();
                return true;
            case "action:copy-link":
            case "action:copy-markdown":
                return ctx.Snapshot.Find(ctx.SelectedPrId) is { } target && !string.IsNullOrWhiteSpace(target.Url)
                    && Cshubben.Ui.Assist.ExternalActions.Copy(item.Id == "action:copy-link"
                        ? target.Url
                        : "[" + (target.Repo + "#" + target.Number + " " + target.Title).Replace("[", "(", StringComparison.Ordinal).Replace("]", ")", StringComparison.Ordinal) + "](" + target.Url + ")");
            case "action:open-github":
                return ctx.SelectedPrId is { } id && OpenPr(ctx, id);
            default:
                return false;
        }
    }

    private static bool OpenPr(PaletteContext ctx, string id)
    {
        var pr = ctx.Snapshot.Find(id);
        if (pr is null || string.IsNullOrWhiteSpace(pr.Url)) return false;
        if (ctx.OpenUrl is { } open) open(pr.Url);
        else Shell.OpenUrl(pr.Url);
        return true;
    }

    private static string ChordFor(string pageKey)
    {
        foreach (var b in ChordMatcher.Default)
            if (string.Equals(b.Target, pageKey, StringComparison.OrdinalIgnoreCase))
                return "G then " + char.ToUpperInvariant(b.Key);
        return "";
    }
}
