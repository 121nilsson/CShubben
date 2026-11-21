namespace Cshubben.Core.Features.Review;

public enum ReviewCommand
{
    NextLine,
    PreviousLine,
    NextFile,
    PreviousFile,
    NextThread,
    PreviousThread,
    Comment,
    MarkLine,
    AskAboutCode,
    ToggleViewed,
    ToggleSplit,
    FocusFilter,
    Find,
    Help,
}

/// <param name="Key">"j", "/", "?", "F1": a single character or a function key name, lower case.</param>
/// <param name="Keys">How the key is written in the help sheet.</param>
public sealed record ReviewKeyBinding(ReviewCommand Command, string Key, bool Ctrl, bool Shift, string Keys, string What);

/// <summary>Every review shortcut in one table: the UI registers them from here and the help sheet lists them.</summary>
public static class ReviewKeymap
{
    public static IReadOnlyList<ReviewKeyBinding> Bindings { get; } = new[]
    {
        new ReviewKeyBinding(ReviewCommand.NextLine, "j", false, false, "j / k", "Next / previous line or comment"),
        new ReviewKeyBinding(ReviewCommand.PreviousLine, "k", false, false, "j / k", "Next / previous line or comment"),
        new ReviewKeyBinding(ReviewCommand.NextFile, "n", false, false, "n / p", "Next / previous file"),
        new ReviewKeyBinding(ReviewCommand.PreviousFile, "p", false, false, "n / p", "Next / previous file"),
        new ReviewKeyBinding(ReviewCommand.NextThread, "]", false, false, "] / [", "Next / previous open thread"),
        new ReviewKeyBinding(ReviewCommand.PreviousThread, "[", false, false, "] / [", "Next / previous open thread"),
        new ReviewKeyBinding(ReviewCommand.Comment, "c", false, false, "c", "Comment on the selected line, or reply to the selected thread"),
        new ReviewKeyBinding(ReviewCommand.MarkLine, "m", false, false, "m", "Mark the start of a range of lines (move with j / k, then press a)"),
        new ReviewKeyBinding(ReviewCommand.AskAboutCode, "a", false, false, "a", "Ask Claude about the marked lines, or the selected line"),
        new ReviewKeyBinding(ReviewCommand.ToggleViewed, "v", false, false, "v", "Mark the file as viewed"),
        new ReviewKeyBinding(ReviewCommand.ToggleSplit, "s", false, false, "s", "Toggle split view"),
        new ReviewKeyBinding(ReviewCommand.FocusFilter, "/", false, false, "/", "Focus the file filter"),
        new ReviewKeyBinding(ReviewCommand.Find, "f", true, false, "Ctrl+F", "Find in this file (Enter / Shift+Enter jump)"),
        new ReviewKeyBinding(ReviewCommand.Help, "?", false, true, "?", "Show the shortcut list"),
        new ReviewKeyBinding(ReviewCommand.Help, "f1", false, false, "F1", "Show the shortcut list"),
    };

    /// <summary>The command for a key press, or null. Letters ignore case but not Shift; "?" needs Shift (or Shift+/).</summary>
    public static ReviewCommand? Resolve(string key, bool ctrl, bool shift)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var k = key.ToLowerInvariant();
        if (k == "/" && shift) k = "?"; // "?" is Shift+/ on most keyboards
        foreach (var b in Bindings)
        {
            if (b.Key != k || b.Ctrl != ctrl) continue;
            if (b.Key == "?" && !shift) continue;
            if (b.Key.Length == 1 && char.IsLetter(b.Key[0]) && !b.Ctrl && shift) continue;
            return b.Command;
        }
        return null;
    }
}
