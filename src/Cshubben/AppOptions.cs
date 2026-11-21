using Cshubben.Core.Data;
using Cshubben.Core.Model;

namespace Cshubben;

/// <summary>The pages in the sidebar.</summary>
public enum Page { Overview, Feed, Mine, Reviews, Review, Drift, Linear, Notes, Stats, Settings }

/// <summary>
/// Startup options. Used by CI to take screenshots of a specific page:
/// <c>Cshubben.exe --demo --start reviews --select r3 --tab 0</c>.
/// </summary>
public static class AppOptions
{
    public static Page StartPage { get; private set; } = Page.Overview;
    public static string? SelectedPr { get; private set; }
    public static int StartTab { get; private set; }

    /// <summary>Set by <c>--file part-of-path</c>: the review page opens on the first changed file whose path contains it.</summary>
    public static string? StartFile { get; private set; }

    /// <summary>Set by <c>--palette [query]</c>: start with the command palette open (used for screenshots).</summary>
    public static string? PaletteQuery { get; private set; }

    /// <summary>True when <c>--demo</c> was passed: demo data, no network, no real settings or credentials.</summary>
    public static bool Demo { get; private set; }

    /// <summary>
    /// Demo data, kept only so older call sites still compile. Pages must read the live snapshot from
    /// <c>PageContext.Snapshot</c> instead.
    /// </summary>
    [Obsolete("Read the snapshot from PageContext.Snapshot.")]
    public static HubSnapshot Snapshot { get; private set; } = new DemoData().Build();

    public static void Parse(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            var next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--demo":
                    Demo = true;
                    break;
                case "--start" when next is not null && Enum.TryParse<Page>(next, ignoreCase: true, out var page):
                    StartPage = page;
                    break;
                case "--select" when next is not null:
                    SelectedPr = next;
                    break;
                case "--file" when next is not null:
                    StartFile = next;
                    break;
                case "--palette":
                    PaletteQuery = next is not null && !next.StartsWith("--", StringComparison.Ordinal) ? next : "";
                    break;
                case "--tab" when next is not null && int.TryParse(next, out var tab):
                    StartTab = tab;
                    break;
            }
        }
    }
}
