using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

internal static class D
{
    public static string Join(params string[] lines) => string.Join("\n", lines) + "\n";

    public const string Simple = "diff --git a/src/A.cs b/src/A.cs\n"
        + "index 111..222 100644\n"
        + "--- a/src/A.cs\n"
        + "+++ b/src/A.cs\n"
        + "@@ -1,4 +1,5 @@ namespace X\n"
        + " using System;\n"
        + "-using Old;\n"
        + "+using New;\n"
        + "+using More;\n"
        + " \n"
        + " class A { }\n";
}

public class ParserTests
{
    [Fact]
    public void Empty_input_gives_no_files()
    {
        Assert.Empty(UnifiedDiffParser.Parse(""));
        Assert.Empty(UnifiedDiffParser.Parse(null));
    }

    [Fact]
    public void Parses_a_single_file_with_line_numbers()
    {
        var file = Assert.Single(UnifiedDiffParser.Parse(D.Simple));
        Assert.Equal("src/A.cs", file.Path);
        Assert.Equal(DiffFileStatus.Modified, file.Status);
        var hunk = Assert.Single(file.Hunks);
        Assert.Equal(6, hunk.Lines.Count);
        Assert.Equal(DiffLineKind.Context, hunk.Lines[0].Kind);
        Assert.Equal(1, hunk.Lines[0].OldNumber);
        Assert.Equal(1, hunk.Lines[0].NewNumber);
        Assert.Equal(DiffLineKind.Remove, hunk.Lines[1].Kind);
        Assert.Equal(2, hunk.Lines[1].OldNumber);
        Assert.Null(hunk.Lines[1].NewNumber);
        Assert.Equal(DiffLineKind.Add, hunk.Lines[2].Kind);
        Assert.Null(hunk.Lines[2].OldNumber);
        Assert.Equal(2, hunk.Lines[2].NewNumber);
        Assert.Equal(3, hunk.Lines[3].NewNumber);
        Assert.Equal(4, hunk.Lines[4].NewNumber);
        Assert.Equal(3, hunk.Lines[4].OldNumber);
        Assert.Equal(5, hunk.Lines[5].NewNumber);
        Assert.Equal(4, hunk.Lines[5].OldNumber);
    }

    [Fact]
    public void Counts_additions_and_deletions()
    {
        var file = Assert.Single(UnifiedDiffParser.Parse(D.Simple));
        Assert.Equal(2, file.Additions);
        Assert.Equal(1, file.Deletions);
        Assert.Equal(3, file.TotalChanges);
    }

    [Fact]
    public void Hunk_header_numbers_and_context_are_kept()
    {
        var hunk = UnifiedDiffParser.Parse(D.Simple)[0].Hunks[0];
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(4, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(5, hunk.NewCount);
        Assert.Equal("namespace X", hunk.Context);
    }

    [Fact]
    public void Hunk_header_without_counts_means_one_line()
    {
        var text = D.Join("diff --git a/x b/x", "--- a/x", "+++ b/x", "@@ -3 +3 @@", "-a", "+b");
        var hunk = UnifiedDiffParser.Parse(text)[0].Hunks[0];
        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(1, hunk.NewCount);
        Assert.Equal(2, hunk.Lines.Count);
    }

    [Fact]
    public void Parses_several_files()
    {
        var text = D.Simple + D.Join("diff --git a/b.ts b/b.ts", "--- a/b.ts", "+++ b/b.ts", "@@ -1 +1 @@", "-x", "+y");
        var files = UnifiedDiffParser.Parse(text);
        Assert.Equal(2, files.Count);
        Assert.Equal("src/A.cs", files[0].Path);
        Assert.Equal("b.ts", files[1].Path);
        Assert.Equal(1, files[1].Additions);
    }

    [Fact]
    public void Detects_added_file()
    {
        var text = D.Join("diff --git a/n.txt b/n.txt", "new file mode 100644", "index 0000000..abc", "--- /dev/null", "+++ b/n.txt", "@@ -0,0 +1,2 @@", "+one", "+two");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal(DiffFileStatus.Added, file.Status);
        Assert.Equal("n.txt", file.Path);
        Assert.Equal(2, file.Additions);
        Assert.Equal(2, file.Hunks[0].Lines[1].NewNumber);
    }

    [Fact]
    public void Detects_deleted_file()
    {
        var text = D.Join("diff --git a/old.txt b/old.txt", "deleted file mode 100644", "index abc..0000000", "--- a/old.txt", "+++ /dev/null", "@@ -1,2 +0,0 @@", "-one", "-two");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal(DiffFileStatus.Deleted, file.Status);
        Assert.Equal("old.txt", file.Path);
        Assert.Equal(2, file.Deletions);
    }

    [Fact]
    public void Pure_rename_has_old_path_and_no_hunks()
    {
        var text = D.Join("diff --git a/old/Name.cs b/new/Name.cs", "similarity index 100%", "rename from old/Name.cs", "rename to new/Name.cs");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal(DiffFileStatus.Renamed, file.Status);
        Assert.Equal("new/Name.cs", file.Path);
        Assert.Equal("old/Name.cs", file.OldPath);
        Assert.Empty(file.Hunks);
        Assert.False(file.HasPatch);
    }

    [Fact]
    public void Rename_with_changes_keeps_the_new_path()
    {
        var text = D.Join("diff --git a/a/X.cs b/b/X.cs", "similarity index 80%", "rename from a/X.cs", "rename to b/X.cs", "--- a/a/X.cs", "+++ b/b/X.cs", "@@ -1 +1 @@", "-a", "+b");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal(DiffFileStatus.Renamed, file.Status);
        Assert.Equal("b/X.cs", file.Path);
        Assert.Equal("a/X.cs", file.OldPath);
        Assert.Single(file.Hunks);
    }

    [Fact]
    public void Binary_files_are_flagged()
    {
        var text = D.Join("diff --git a/img/logo.png b/img/logo.png", "new file mode 100644", "index 0000000..abc", "Binary files /dev/null and b/img/logo.png differ");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.True(file.IsBinary);
        Assert.Equal(DiffFileStatus.Added, file.Status);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public void Deleted_binary_file_is_detected()
    {
        var text = D.Join("diff --git a/x.png b/x.png", "index abc..000", "Binary files a/x.png and /dev/null differ");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.True(file.IsBinary);
        Assert.Equal(DiffFileStatus.Deleted, file.Status);
    }

    [Fact]
    public void No_newline_marker_becomes_a_line_of_its_own()
    {
        var text = D.Join("diff --git a/f b/f", "--- a/f", "+++ b/f", "@@ -1 +1 @@", "-a", "\\ No newline at end of file", "+b", "\\ No newline at end of file");
        var lines = UnifiedDiffParser.Parse(text)[0].Hunks[0].Lines;
        Assert.Equal(4, lines.Count);
        Assert.Equal(DiffLineKind.NoNewline, lines[1].Kind);
        Assert.Equal(DiffLineKind.Add, lines[2].Kind);
        Assert.Equal(DiffLineKind.NoNewline, lines[3].Kind);
        Assert.Null(lines[1].OldNumber);
    }

    [Fact]
    public void Empty_hunk_is_kept_without_lines()
    {
        var text = D.Join("diff --git a/f b/f", "--- a/f", "+++ b/f", "@@ -0,0 +0,0 @@", "diff --git a/g b/g", "--- a/g", "+++ b/g", "@@ -1 +1 @@", "-x", "+y");
        var files = UnifiedDiffParser.Parse(text);
        Assert.Equal(2, files.Count);
        Assert.Empty(files[0].Hunks[0].Lines);
        Assert.Equal("g", files[1].Path);
    }

    [Fact]
    public void Crlf_line_endings_are_stripped()
    {
        var text = D.Simple.Replace("\n", "\r\n", StringComparison.Ordinal);
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal("src/A.cs", file.Path);
        Assert.Equal("using New;", file.Hunks[0].Lines[2].Text);
        Assert.Equal(6, file.Hunks[0].Lines.Count);
    }

    [Fact]
    public void Quoted_path_with_spaces_is_unquoted()
    {
        var text = D.Join("diff --git \"a/my file.txt\" \"b/my file.txt\"", "--- \"a/my file.txt\"", "+++ \"b/my file.txt\"", "@@ -1 +1 @@", "-a", "+b");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal("my file.txt", file.Path);
    }

    [Fact]
    public void Unquoted_path_with_spaces_is_split_correctly()
    {
        var text = D.Join("diff --git a/my docs/read me.md b/my docs/read me.md", "new file mode 100644", "--- /dev/null", "+++ b/my docs/read me.md", "@@ -0,0 +1 @@", "+hi");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal("my docs/read me.md", file.Path);
    }

    [Fact]
    public void Unquote_decodes_octal_utf8_and_escapes()
    {
        Assert.Equal("café.txt", UnifiedDiffParser.Unquote("\"caf\\303\\251.txt\""));
        Assert.Equal("a\tb", UnifiedDiffParser.Unquote("\"a\\tb\""));
        Assert.Equal("plain", UnifiedDiffParser.Unquote("plain"));
        Assert.Equal("q\"x", UnifiedDiffParser.Unquote("\"q\\\"x\""));
    }

    [Fact]
    public void Patch_over_the_size_cap_is_skipped_but_counted()
    {
        var body = Enumerable.Range(0, 20).Select(i => "+line number " + i).ToArray();
        var lines = new List<string> { "diff --git a/big.txt b/big.txt", "--- a/big.txt", "+++ b/big.txt", "@@ -0,0 +1,20 @@" };
        lines.AddRange(body);
        var file = Assert.Single(UnifiedDiffParser.Parse(D.Join(lines.ToArray()), maxPatchChars: 50));
        Assert.True(file.IsTooLarge);
        Assert.Empty(file.Hunks);
        Assert.Equal(20, file.Additions);
        Assert.False(file.HasPatch);
    }

    [Fact]
    public void Removed_line_that_looks_like_a_file_header_stays_content()
    {
        var text = D.Join("diff --git a/q.sql b/q.sql", "--- a/q.sql", "+++ b/q.sql", "@@ -1,2 +1,1 @@", "--- old comment", " select 1;");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal("q.sql", file.Path);
        Assert.Equal(DiffLineKind.Remove, file.Hunks[0].Lines[0].Kind);
        Assert.Equal("-- old comment", file.Hunks[0].Lines[0].Text);
    }

    [Fact]
    public void Mode_change_only_gives_a_file_without_hunks()
    {
        var text = D.Join("diff --git a/run.sh b/run.sh", "old mode 100644", "new mode 100755");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal("run.sh", file.Path);
        Assert.Equal(DiffFileStatus.Modified, file.Status);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public void Two_hunks_in_one_file_are_separate()
    {
        var text = D.Join("diff --git a/f b/f", "--- a/f", "+++ b/f", "@@ -1,2 +1,2 @@", " a", "-b", "+c", "@@ -10,2 +10,2 @@", " x", "-y", "+z");
        var file = UnifiedDiffParser.Parse(text)[0];
        Assert.Equal(2, file.Hunks.Count);
        Assert.Equal(10, file.Hunks[1].Lines[0].OldNumber);
        Assert.Equal(11, file.Hunks[1].Lines[2].NewNumber);
    }

    [Fact]
    public void Plain_unified_diff_without_git_headers_is_supported()
    {
        var text = D.Join("--- a/p.txt", "+++ b/p.txt", "@@ -1 +1 @@", "-a", "+b");
        var file = Assert.Single(UnifiedDiffParser.Parse(text));
        Assert.Equal("p.txt", file.Path);
        Assert.Equal(1, file.Additions);
    }

    [Fact]
    public void Garbage_input_does_not_throw()
    {
        var files = UnifiedDiffParser.Parse("hello\nworld\n@@ nonsense\n+++\n");
        Assert.Empty(files);
    }
}
