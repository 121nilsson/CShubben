namespace Cshubben.Core.Features.CommandPalette;

/// <summary>One line in the keyboard help.</summary>
public sealed record ShortcutEntry(string Group, string Keys, string What);

/// <summary>Everything the help sheet lists. One place, so the sheet and the real bindings do not drift apart.</summary>
public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutEntry> All { get; } = Build();

    public static IReadOnlyList<string> Groups => All.Select(e => e.Group).Distinct().ToList();

    private static IReadOnlyList<ShortcutEntry> Build()
    {
        var list = new List<ShortcutEntry>
        {
            new("Everywhere", "Ctrl+K", "Open the command palette"),
            new("Everywhere", "?  or  F1", "Show this help"),
            new("Everywhere", "Esc", "Close the palette, a dialog or this help"),
        };
        foreach (var b in ChordMatcher.Default)
            list.Add(new ShortcutEntry("Go to", "G then " + char.ToUpperInvariant(b.Key), b.Label));
        list.Add(new ShortcutEntry("Command palette", "Up / Down", "Move the selection"));
        list.Add(new ShortcutEntry("Command palette", "Enter", "Run the selected entry"));
        list.Add(new ShortcutEntry("Command palette", ">", "Only commands and pages"));
        list.Add(new ShortcutEntry("Command palette", "#123", "Find a pull request by number"));
        list.Add(new ShortcutEntry("Code review", "j / k", "Next / previous file"));
        list.Add(new ShortcutEntry("Code review", "n / p", "Next / previous open thread"));
        list.Add(new ShortcutEntry("Code review", "v", "Mark the file as viewed"));
        list.Add(new ShortcutEntry("Code review", "c", "Comment on the selected line"));
        list.Add(new ShortcutEntry("Code review", "Ctrl+Enter", "Save the comment you are writing"));
        return list;
    }
}
