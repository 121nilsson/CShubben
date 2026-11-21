using Cshubben.Core.Features.CommandPalette;
using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class KeymapAndFindTests
{
    [Theory]
    [InlineData("j", false, false, ReviewCommand.NextLine)]
    [InlineData("k", false, false, ReviewCommand.PreviousLine)]
    [InlineData("n", false, false, ReviewCommand.NextFile)]
    [InlineData("p", false, false, ReviewCommand.PreviousFile)]
    [InlineData("c", false, false, ReviewCommand.Comment)]
    [InlineData("v", false, false, ReviewCommand.ToggleViewed)]
    [InlineData("s", false, false, ReviewCommand.ToggleSplit)]
    [InlineData("/", false, false, ReviewCommand.FocusFilter)]
    [InlineData("f", true, false, ReviewCommand.Find)]
    [InlineData("F", true, false, ReviewCommand.Find)]
    [InlineData("?", false, true, ReviewCommand.Help)]
    [InlineData("/", false, true, ReviewCommand.Help)]
    [InlineData("F1", false, false, ReviewCommand.Help)]
    [InlineData("]", false, false, ReviewCommand.NextThread)]
    [InlineData("[", false, false, ReviewCommand.PreviousThread)]
    public void Keys_map_to_commands(string key, bool ctrl, bool shift, ReviewCommand expected) =>
        Assert.Equal(expected, ReviewKeymap.Resolve(key, ctrl, shift));

    [Theory]
    [InlineData("f", false, false)]
    [InlineData("j", true, false)]
    [InlineData("J", false, true)]
    [InlineData("?", false, false)]
    [InlineData("x", false, false)]
    [InlineData("", false, false)]
    public void Other_keys_do_nothing(string key, bool ctrl, bool shift) =>
        Assert.Null(ReviewKeymap.Resolve(key, ctrl, shift));

    [Fact]
    public void No_two_bindings_share_a_key_chord()
    {
        var chords = ReviewKeymap.Bindings.Select(b => (b.Key, b.Ctrl, b.Shift)).ToList();
        Assert.Equal(chords.Count, chords.Distinct().Count());
    }

    [Fact]
    public void Every_review_key_is_listed_in_the_shortcut_catalog()
    {
        foreach (var b in ReviewKeymap.Bindings.Where(b => b.Command != ReviewCommand.Help))
            Assert.Contains(ShortcutCatalog.All, e => e.Group == "Code review" && e.Keys == b.Keys && e.What == b.What);
        Assert.Contains(ShortcutCatalog.All, e => e.Group == "Everywhere" && e.Keys.StartsWith("?", StringComparison.Ordinal));
    }

    // ---- cursor and search

    private static LineRow Line(string key, string text, DiffLineKind kind = DiffLineKind.Context) =>
        new(key, 0, 0, new DiffLine(kind, 1, 1, text), null);

    private static IReadOnlyList<ReviewRow> Rows()
    {
        var thread = new ReviewThread("t1", "a.cs", 2, DiffSide.Right, null, false, false,
            new[] { new ReviewComment("c1", "x", "hello", DateTimeOffset.UnixEpoch) });
        return new ReviewRow[]
        {
            new HunkHeaderRow(0, "@@ -1 +1 @@", ""),
            Line("l1", "var Foo = 1;"),
            Line("l2", "return foo;"),
            new ThreadRow(thread),
            Line("nn", "\\ No newline at end of file", DiffLineKind.NoNewline),
            Line("l3", "// end"),
        };
    }

    [Fact]
    public void Cursor_visits_lines_and_threads_and_skips_headers_and_markers()
    {
        var rows = Rows();
        Assert.Equal("l1", RowCursor.Move(rows, null, 1));
        Assert.Equal("l3", RowCursor.Move(rows, null, -1));
        Assert.Equal("l2", RowCursor.Move(rows, "l1", 1));
        Assert.Equal("thread-t1", RowCursor.Move(rows, "l2", 1));
        Assert.Equal("l3", RowCursor.Move(rows, "thread-t1", 1));
        Assert.Equal("l2", RowCursor.Move(rows, "thread-t1", -1));
    }

    [Fact]
    public void Cursor_stops_at_the_ends_and_copes_with_unknown_keys_and_empty_files()
    {
        var rows = Rows();
        Assert.Equal("l3", RowCursor.Move(rows, "l3", 1));
        Assert.Equal("l1", RowCursor.Move(rows, "l1", -1));
        Assert.Equal("l1", RowCursor.Move(rows, "gone", 1));
        Assert.Null(RowCursor.Move(new ReviewRow[] { new HunkHeaderRow(0, "@@", "") }, null, 1));
    }

    [Fact]
    public void Search_finds_code_lines_ignoring_case_and_never_matches_an_empty_query()
    {
        var rows = Rows();
        Assert.Equal(new[] { "l1", "l2" }, DiffSearch.Matches(rows, "foo"));
        Assert.Equal(new[] { "l1" }, DiffSearch.Matches(rows, "FOO ="));
        Assert.Empty(DiffSearch.Matches(rows, ""));
        Assert.Empty(DiffSearch.Matches(rows, null));
        Assert.Empty(DiffSearch.Matches(rows, "newline")); // the marker line is not code
        Assert.Empty(DiffSearch.Matches(rows, "hello")); // thread text is not searched
    }

    [Theory]
    [InlineData(3, -1, 1, 0)]
    [InlineData(3, -1, -1, 2)]
    [InlineData(3, 0, 1, 1)]
    [InlineData(3, 2, 1, 0)]
    [InlineData(3, 0, -1, 2)]
    [InlineData(0, -1, 1, -1)]
    [InlineData(1, 0, 1, 0)]
    public void Stepping_wraps_around(int count, int current, int delta, int expected) =>
        Assert.Equal(expected, DiffSearch.Step(count, current, delta));

    [Fact]
    public void Label_reports_position_or_no_matches()
    {
        Assert.Equal(string.Empty, DiffSearch.Label("", 0, -1));
        Assert.Equal("No matches", DiffSearch.Label("zz", 0, -1));
        Assert.Equal("1 of 4", DiffSearch.Label("a", 4, -1));
        Assert.Equal("3 of 4", DiffSearch.Label("a", 4, 2));
    }
}
