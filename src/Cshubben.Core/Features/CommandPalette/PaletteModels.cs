using Cshubben.Core.Model;

namespace Cshubben.Core.Features.CommandPalette;

public enum PaletteKind
{
    Action,
    Page,
    PullRequest,
}

/// <summary>One entry the palette can show and run.</summary>
/// <param name="Id">Unique id; the UI maps it back to what to do ("action:refresh", "page:reviews", "pr:ID").</param>
/// <param name="Kind">Group of the entry.</param>
/// <param name="Title">Main text, the one that is matched and highlighted.</param>
/// <param name="Subtitle">Secondary text (the repo and number for a PR). Matched with half weight.</param>
/// <param name="Glyph">Optional Segoe Fluent Icons character.</param>
/// <param name="Shortcut">Optional shortcut text shown on the right ("Ctrl+K", "G then O").</param>
/// <param name="Keywords">Extra words that find the entry without being shown ("author name", "prefs").</param>
/// <param name="Ref">What the entry points at: the PR id for a PR, the page key for a page.</param>
/// <param name="Repo">owner/name for a PR entry.</param>
/// <param name="Number">PR number for a PR entry; 0 otherwise.</param>
public sealed record PaletteItem(
    string Id,
    PaletteKind Kind,
    string Title,
    string Subtitle = "",
    string Glyph = "",
    string Shortcut = "",
    string Keywords = "",
    string Ref = "",
    string Repo = "",
    int Number = 0);

/// <summary>A ranked entry plus which characters of its title matched (for highlighting).</summary>
public sealed record PaletteResult(PaletteItem Item, int Score, IReadOnlyList<int> TitleMatches);

/// <summary>Builds palette entries from data.</summary>
public static class PaletteItems
{
    /// <summary>One entry per pull request (own first, then review requests), each PR once.</summary>
    public static IReadOnlyList<PaletteItem> FromSnapshot(HubSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<PaletteItem>();
        foreach (var pr in snapshot.Authored.Concat(snapshot.Reviews))
        {
            if (!seen.Add(pr.Id)) continue;
            items.Add(new PaletteItem(
                "pr:" + pr.Id,
                PaletteKind.PullRequest,
                pr.Title,
                pr.Repo + " #" + pr.Number,
                Keywords: pr.Author,
                Ref: pr.Id,
                Repo: pr.Repo,
                Number: pr.Number));
        }

        return items;
    }
}
