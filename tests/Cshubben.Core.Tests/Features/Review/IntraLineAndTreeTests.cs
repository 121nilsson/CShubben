using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class IntraLineTests
{
    [Fact]
    public void Single_changed_word_is_the_only_changed_span()
    {
        var r = IntraLineDiff.Compute("var x = 1;", "var x = 2;");
        Assert.Equal(3, r.OldSpans.Count);
        Assert.Equal("var x = ", r.OldSpans[0].Text);
        Assert.False(r.OldSpans[0].Changed);
        Assert.Equal("1", r.OldSpans[1].Text);
        Assert.True(r.OldSpans[1].Changed);
        Assert.Equal("2", r.NewSpans[1].Text);
        Assert.True(r.NewSpans[1].Changed);
        Assert.Equal(";", r.NewSpans[2].Text);
        Assert.True(r.IsUseful);
    }

    [Fact]
    public void Identical_lines_have_one_unchanged_span()
    {
        var r = IntraLineDiff.Compute("same", "same");
        Assert.Single(r.OldSpans);
        Assert.False(r.OldSpans[0].Changed);
        Assert.Equal(1.0, r.Ratio);
    }

    [Fact]
    public void Completely_different_lines_are_not_useful()
    {
        var r = IntraLineDiff.Compute("abc", "xyz");
        Assert.Single(r.OldSpans);
        Assert.True(r.OldSpans[0].Changed);
        Assert.Equal(0.0, r.Ratio);
        Assert.False(r.IsUseful);
    }

    [Fact]
    public void Inserted_text_only_marks_the_new_side()
    {
        var r = IntraLineDiff.Compute("foo(a)", "foo(a, b)");
        Assert.Single(r.OldSpans);
        Assert.False(r.OldSpans[0].Changed);
        Assert.Equal(3, r.NewSpans.Count);
        Assert.Equal(", b", r.NewSpans[1].Text);
        Assert.True(r.NewSpans[1].Changed);
    }

    [Fact]
    public void Whitespace_between_two_changed_words_joins_them()
    {
        var r = IntraLineDiff.Compute("a b", "x y");
        Assert.Single(r.OldSpans);
        Assert.True(r.OldSpans[0].Changed);
        Assert.Equal("a b", r.OldSpans[0].Text);
    }

    [Fact]
    public void Spans_always_rebuild_the_original_text()
    {
        var oldText = "public static int Add(int a, int b) => a + b;";
        var newText = "public static long Add(long a, long b) => checked(a + b);";
        var r = IntraLineDiff.Compute(oldText, newText);
        Assert.Equal(oldText, string.Concat(r.OldSpans.Select(s => s.Text)));
        Assert.Equal(newText, string.Concat(r.NewSpans.Select(s => s.Text)));
    }

    [Fact]
    public void Very_long_lines_use_the_prefix_suffix_fallback()
    {
        var oldText = new string('x', 3000) + "A";
        var newText = new string('x', 3000) + "B";
        var r = IntraLineDiff.Compute(oldText, newText);
        Assert.Equal(2, r.OldSpans.Count);
        Assert.False(r.OldSpans[0].Changed);
        Assert.Equal("A", r.OldSpans[1].Text);
        Assert.True(r.OldSpans[1].Changed);
    }

    [Fact]
    public void Tokenizer_splits_words_whitespace_and_punctuation()
    {
        var tokens = IntraLineDiff.Tokenize("a_b  c.d");
        Assert.Equal(new[] { "a_b", "  ", "c", ".", "d" }, tokens.ToArray());
    }

    [Fact]
    public void Removes_followed_by_adds_are_paired_from_the_top()
    {
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Remove, 1, null, "a"),
            new(DiffLineKind.Remove, 2, null, "b"),
            new(DiffLineKind.Add, null, 1, "c"),
            new(DiffLineKind.Context, 3, 2, "d"),
            new(DiffLineKind.Add, null, 3, "e"),
        };
        var pairs = IntraLineDiff.PairLines(lines);
        Assert.Equal(2, pairs.Count);
        Assert.Equal(2, pairs[0]);
        Assert.Equal(0, pairs[2]);
        Assert.False(pairs.ContainsKey(1));
        Assert.False(pairs.ContainsKey(4));
    }
}

public class FileTreeTests
{
    private static DiffFile F(string path, int add, int del) =>
        new(path, null, DiffFileStatus.Modified, false, false, add, del, Array.Empty<DiffHunk>());

    private static readonly IReadOnlyList<DiffFile> Files = new[]
    {
        F("src/app/A.cs", 3, 1),
        F("src/app/B.cs", 2, 0),
        F("docs/guide/intro.md", 5, 5),
        F("README.md", 1, 0),
    };

    [Fact]
    public void Single_child_folders_are_merged_and_dirs_come_first()
    {
        var rows = FileTreeBuilder.Build(Files);
        Assert.Equal(new[] { "docs/guide", "intro.md", "src/app", "A.cs", "B.cs", "README.md" }, rows.Select(r => r.Name).ToArray());
        Assert.Equal(TreeRowKind.Dir, rows[0].Kind);
        Assert.Equal(0, rows[0].Depth);
        Assert.Equal(1, rows[1].Depth);
        Assert.Equal(0, rows[5].Depth);
    }

    [Fact]
    public void Folders_aggregate_their_counts()
    {
        var rows = FileTreeBuilder.Build(Files);
        var src = rows.Single(r => r.Name == "src/app");
        Assert.Equal(5, src.Additions);
        Assert.Equal(1, src.Deletions);
        Assert.Equal(2, src.FileCount);
        Assert.Equal("src/app", src.Path);
    }

    [Fact]
    public void Filter_keeps_matching_files_and_their_folders()
    {
        var rows = FileTreeBuilder.Build(Files, "INTRO");
        Assert.Equal(new[] { "docs/guide", "intro.md" }, rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Filter_without_matches_gives_no_rows()
    {
        Assert.Empty(FileTreeBuilder.Build(Files, "zzz"));
    }

    [Fact]
    public void Collapsed_folder_hides_its_files()
    {
        var rows = FileTreeBuilder.Build(Files, null, new[] { "src/app" });
        Assert.DoesNotContain(rows, r => r.Name == "A.cs");
        Assert.True(rows.Single(r => r.Name == "src/app").Collapsed);
    }

    [Fact]
    public void File_rows_point_back_into_the_input_list()
    {
        var rows = FileTreeBuilder.Build(Files);
        Assert.Equal(2, rows.Single(r => r.Name == "intro.md").FileIndex);
        Assert.Equal(new[] { 2, 0, 1, 3 }, FileTreeBuilder.TreeOrder(Files).ToArray());
    }

    [Fact]
    public void Folder_with_files_and_a_subfolder_is_not_merged()
    {
        var files = new[] { F("a/x.txt", 1, 0), F("a/b/y.txt", 1, 0) };
        var rows = FileTreeBuilder.Build(files);
        Assert.Equal(new[] { "a", "b", "y.txt", "x.txt" }, rows.Select(r => r.Name).ToArray());
        Assert.Equal(1, rows[1].Depth);
    }
}

public class FileKindTests
{
    [Theory]
    [InlineData("package-lock.json", FileKind.Lockfile)]
    [InlineData("frontend/yarn.lock", FileKind.Lockfile)]
    [InlineData("Cargo.lock", FileKind.Lockfile)]
    [InlineData("go.sum", FileKind.Lockfile)]
    [InlineData("src/Foo.g.cs", FileKind.Generated)]
    [InlineData("src/Form1.Designer.cs", FileKind.Generated)]
    [InlineData("api/user.pb.go", FileKind.Generated)]
    [InlineData("dist/app.js", FileKind.Generated)]
    [InlineData("wwwroot/app.min.js", FileKind.Minified)]
    [InlineData("tests/__snapshots__/a.test.ts.snap", FileKind.Snapshot)]
    [InlineData("tests/Foo.verified.txt", FileKind.Snapshot)]
    [InlineData("src/Program.cs", FileKind.Source)]
    [InlineData("src/lock.cs", FileKind.Source)]
    public void Classifies_by_path(string path, FileKind expected)
    {
        Assert.Equal(expected, FileKinds.Classify(path));
    }

    [Fact]
    public void Binary_wins_over_the_name()
    {
        Assert.Equal(FileKind.Binary, FileKinds.Classify("a.lock", true));
    }

    [Fact]
    public void Only_generated_like_kinds_collapse_by_default()
    {
        Assert.True(FileKinds.CollapsedByDefault(FileKind.Lockfile));
        Assert.True(FileKinds.CollapsedByDefault(FileKind.Generated));
        Assert.True(FileKinds.CollapsedByDefault(FileKind.Minified));
        Assert.True(FileKinds.CollapsedByDefault(FileKind.Snapshot));
        Assert.False(FileKinds.CollapsedByDefault(FileKind.Source));
        Assert.False(FileKinds.CollapsedByDefault(FileKind.Binary));
    }
}
