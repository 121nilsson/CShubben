using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

public class RowsTests
{
    private static DiffFile TwoHunks() => new(
        "src/a.cs", null, DiffFileStatus.Modified, false, false, 2, 2,
        new List<DiffHunk>
        {
            new("@@ -1,2 +1,2 @@", 1, 2, 1, 2, new List<DiffLine>
            {
                new(DiffLineKind.Context, 1, 1, "one"),
                new(DiffLineKind.Remove, 2, null, "two"),
                new(DiffLineKind.Add, null, 2, "TWO"),
            }),
            new("@@ -10,2 +10,2 @@ class X", 10, 2, 10, 2, new List<DiffLine>
            {
                new(DiffLineKind.Context, 10, 10, "ten"),
                new(DiffLineKind.Remove, 11, null, "eleven"),
                new(DiffLineKind.Add, null, 11, "ELEVEN"),
            }),
        });

    private static DiffFile Simple(string path = "src/b.cs") =>
        new(path, null, DiffFileStatus.Modified, false, false, 1, 1, new List<DiffHunk> { T.Hunk() });

    [Fact]
    public void Rows_are_a_gap_for_the_hidden_lines_above_then_the_hunk_header_then_lines()
    {
        var rows = ReviewRows.Flatten(Simple());
        var gap = Assert.IsType<GapRow>(rows[0]);
        Assert.Equal(9, gap.HiddenLines);
        Assert.IsType<HunkHeaderRow>(rows[1]);
        Assert.Equal(7, rows.Count);
        Assert.All(rows.Skip(2), r => Assert.IsType<LineRow>(r));
    }

    [Fact]
    public void A_hunk_at_the_top_of_the_file_has_no_gap_above()
    {
        var hunk = new DiffHunk("@@ -1 +1 @@", 1, 1, 1, 1, new List<DiffLine> { new(DiffLineKind.Add, null, 1, "x") });
        var file = new DiffFile("f", null, DiffFileStatus.Modified, false, false, 1, 0, new List<DiffHunk> { hunk });
        Assert.IsType<HunkHeaderRow>(ReviewRows.Flatten(file)[0]);
    }

    [Fact]
    public void Row_keys_are_unique()
    {
        var rows = ReviewRows.Flatten(TwoHunks(), new FlattenOptions { Threads = new[] { T.Thread("t", "src/a.cs", 2) } });
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void Paired_remove_and_add_lines_know_their_partner()
    {
        var rows = ReviewRows.Flatten(Simple()).OfType<LineRow>().ToList();
        Assert.Equal("b2();", rows[1].Partner!.Text);
        Assert.Equal("b();", rows[2].Partner!.Text);
        Assert.Null(rows[0].Partner);
    }

    [Fact]
    public void Thread_is_placed_right_after_its_line()
    {
        var options = new FlattenOptions { Threads = new[] { T.Thread("t1", "src/b.cs", 11) } };
        var rows = ReviewRows.Flatten(Simple(), options);
        var index = rows.ToList().FindIndex(r => r is ThreadRow);
        Assert.True(index > 0);
        var before = Assert.IsType<LineRow>(rows[index - 1]);
        Assert.Equal(11, before.Line.NewNumber);
    }

    [Fact]
    public void Threads_of_other_files_are_ignored()
    {
        var options = new FlattenOptions { Threads = new[] { T.Thread("t1", "other.cs", 11) } };
        Assert.DoesNotContain(ReviewRows.Flatten(Simple(), options), r => r is ThreadRow);
    }

    [Fact]
    public void Pending_comment_comes_after_the_thread_on_the_same_line()
    {
        var options = new FlattenOptions
        {
            Threads = new[] { T.Thread("t1", "src/b.cs", 11) },
            Pending = new[] { T.Pending("p1", "src/b.cs", 11) },
        };
        var rows = ReviewRows.Flatten(Simple(), options).ToList();
        var thread = rows.FindIndex(r => r is ThreadRow);
        var pending = rows.FindIndex(r => r is PendingRow);
        Assert.Equal(thread + 1, pending);
    }

    [Fact]
    public void Composer_row_opens_under_the_target_line()
    {
        var options = new FlattenOptions { Composer = new ComposerTarget("src/b.cs", 12, DiffSide.Right) };
        var rows = ReviewRows.Flatten(Simple(), options).ToList();
        var index = rows.FindIndex(r => r is ComposerRow);
        Assert.Equal(12, ((LineRow)rows[index - 1]).Line.NewNumber);
    }

    [Fact]
    public void Composer_on_a_removed_line_uses_the_left_side()
    {
        var options = new FlattenOptions { Composer = new ComposerTarget("src/b.cs", 11, DiffSide.Left) };
        var rows = ReviewRows.Flatten(Simple(), options).ToList();
        var index = rows.FindIndex(r => r is ComposerRow);
        Assert.Equal(DiffLineKind.Remove, ((LineRow)rows[index - 1]).Line.Kind);
    }

    [Fact]
    public void Outdated_open_thread_shows_after_the_file_header_position()
    {
        var options = new FlattenOptions { Threads = new[] { T.Thread("old", "src/b.cs", 99, outdated: true) } };
        var rows = ReviewRows.Flatten(Simple(), options);
        Assert.IsType<ThreadRow>(rows[0]);
    }

    [Fact]
    public void Resolved_outdated_thread_is_hidden_and_resolved_thread_without_line_goes_last()
    {
        var options = new FlattenOptions
        {
            Threads = new[] { T.Thread("gone", "src/b.cs", 99, resolved: true, outdated: true), T.Thread("end", "src/b.cs", 99, resolved: true) },
        };
        var rows = ReviewRows.Flatten(Simple(), options);
        Assert.DoesNotContain(rows, r => r is ThreadRow { Thread.Id: "gone" });
        Assert.IsType<ThreadRow>(rows[^1]);
    }

    [Fact]
    public void Hide_resolved_option_drops_resolved_threads()
    {
        var options = new FlattenOptions { HideResolvedThreads = true, Threads = new[] { T.Thread("t", "src/b.cs", 11, resolved: true) } };
        Assert.DoesNotContain(ReviewRows.Flatten(Simple(), options), r => r is ThreadRow);
    }

    [Fact]
    public void Generated_file_is_one_collapsed_notice_until_expanded()
    {
        var file = new DiffFile("yarn.lock", null, DiffFileStatus.Modified, false, false, 40, 10, new List<DiffHunk> { T.Hunk() });
        var collapsed = ReviewRows.Flatten(file);
        var notice = Assert.IsType<NoticeRow>(Assert.Single(collapsed));
        Assert.Equal(NoticeKind.Collapsed, notice.Kind);
        Assert.Contains("50 changed lines", notice.Text);

        var expanded = ReviewRows.Flatten(file, new FlattenOptions { ExpandedFiles = new[] { "yarn.lock" } });
        Assert.Contains(expanded, r => r is HunkHeaderRow);
        Assert.DoesNotContain(expanded, r => r is NoticeRow);
    }

    [Fact]
    public void Binary_too_large_and_rename_only_files_get_notices()
    {
        var binary = new DiffFile("a.png", null, DiffFileStatus.Added, true, false, 0, 0, Array.Empty<DiffHunk>());
        var large = new DiffFile("a.txt", null, DiffFileStatus.Modified, false, true, 9000, 0, Array.Empty<DiffHunk>());
        var renamed = new DiffFile("n.cs", "o.cs", DiffFileStatus.Renamed, false, false, 0, 0, Array.Empty<DiffHunk>());
        Assert.Equal(NoticeKind.Binary, ((NoticeRow)ReviewRows.Flatten(binary)[0]).Kind);
        Assert.Equal(NoticeKind.TooLarge, ((NoticeRow)ReviewRows.Flatten(large)[0]).Kind);
        Assert.Equal(NoticeKind.RenameOnly, ((NoticeRow)ReviewRows.Flatten(renamed)[0]).Kind);
    }

    [Fact]
    public void Threads_still_show_under_a_notice()
    {
        var binary = new DiffFile("a.png", null, DiffFileStatus.Added, true, false, 0, 0, Array.Empty<DiffHunk>());
        var rows = ReviewRows.Flatten(binary, new FlattenOptions { Threads = new[] { T.Thread("t", "a.png", null) } });
        Assert.IsType<NoticeRow>(rows[0]);
        Assert.IsType<ThreadRow>(rows[1]);
    }

    [Fact]
    public void Hidden_lines_between_hunks_become_a_gap_row()
    {
        var rows = ReviewRows.Flatten(TwoHunks());
        var gap = Assert.Single(rows.OfType<GapRow>());
        Assert.Equal(1, gap.GapIndex);
        Assert.Equal(7, gap.HiddenLines);
        Assert.Equal(2, rows.OfType<HunkHeaderRow>().Count());
    }

    [Fact]
    public void Expanded_gap_shows_the_lines_and_drops_the_next_hunk_header()
    {
        var fileLines = Enumerable.Range(1, 12).Select(i => "line " + i).ToList();
        var rows = ReviewRows.Flatten(TwoHunks(), new FlattenOptions { ExpandedGaps = new[] { "gap-1" }, FileLines = fileLines });
        var expanded = rows.OfType<LineRow>().Where(r => r.LineIndex < 0).ToList();
        Assert.Equal(7, expanded.Count);
        Assert.Equal(3, expanded[0].Line.NewNumber);
        Assert.Equal(3, expanded[0].Line.OldNumber);
        Assert.Equal("line 3", expanded[0].Line.Text);
        Assert.All(expanded, r => Assert.False(r.Commentable));
        Assert.Single(rows.OfType<HunkHeaderRow>());
    }

    [Fact]
    public void Trailing_gap_appears_once_the_file_text_is_known()
    {
        var fileLines = Enumerable.Range(1, 15).Select(i => "line " + i).ToList();
        var rows = ReviewRows.Flatten(TwoHunks(), new FlattenOptions { FileLines = fileLines });
        var gaps = rows.OfType<GapRow>().ToList();
        Assert.Equal(2, gaps.Count);
        Assert.Equal(4, gaps[1].HiddenLines);
    }

    [Fact]
    public void Gap_computation_handles_first_hunk_offset_and_pure_additions()
    {
        var file = new DiffFile("x.cs", null, DiffFileStatus.Modified, false, false, 0, 0, new List<DiffHunk>
        {
            new("@@ -5,0 +6,2 @@", 5, 0, 6, 2, new List<DiffLine>
            {
                new(DiffLineKind.Add, null, 6, "a"),
                new(DiffLineKind.Add, null, 7, "b"),
            }),
        });
        var gaps = ContextGaps.Compute(file, 20);
        Assert.Equal(1, gaps[0].NewStart);
        Assert.Equal(5, gaps[0].NewEnd);
        Assert.Equal(0, gaps[0].OldOffset);
        Assert.Equal(-2, gaps[1].OldOffset);
        Assert.Equal(8, gaps[1].NewStart);
        Assert.Equal(13, gaps[1].Count);
    }

    [Fact]
    public void Added_and_deleted_files_have_no_gaps()
    {
        var added = new DiffFile("n", null, DiffFileStatus.Added, false, false, 1, 0, new List<DiffHunk> { T.Hunk() });
        Assert.Empty(ContextGaps.Compute(added, 100));
    }

    [Fact]
    public void Lines_of_a_hunk_are_commentable_except_the_no_newline_marker()
    {
        var hunk = new DiffHunk("@@ -1 +1 @@", 1, 1, 1, 1, new List<DiffLine>
        {
            new(DiffLineKind.Add, null, 1, "x"),
            new(DiffLineKind.NoNewline, null, null, "\\ No newline at end of file"),
        });
        var file = new DiffFile("f", null, DiffFileStatus.Modified, false, false, 1, 0, new List<DiffHunk> { hunk });
        var rows = ReviewRows.Flatten(file).OfType<LineRow>().ToList();
        Assert.True(rows[0].Commentable);
        Assert.False(rows[1].Commentable);
    }
}

public class RangeTests
{
    private static CommitInfo C(string sha) => new(sha, "message " + sha + "\n\nbody", "alice", T.Now);

    private static readonly IReadOnlyList<CommitInfo> Commits = new[] { C("aaaaaaa1111"), C("bbbbbbb2222"), C("ccccccc3333") };

    [Fact]
    public void Since_last_review_returns_only_the_newer_commits()
    {
        var range = ReviewRange.SinceLastReview(Commits, "aaaaaaa1111");
        Assert.Equal(ReviewRangeKind.SinceLastReview, range.Kind);
        Assert.Equal("aaaaaaa1111", range.BaseSha);
        Assert.Equal("ccccccc3333", range.HeadSha);
        Assert.Equal(2, range.NewCommits.Count);
        Assert.Contains("2 new commits", range.Reason);
        Assert.True(range.IsIncremental);
    }

    [Fact]
    public void One_new_commit_reads_in_the_singular()
    {
        var range = ReviewRange.SinceLastReview(Commits, "bbbbbbb2222");
        Assert.Single(range.NewCommits);
        Assert.Equal("1 new commit since your review.", range.Reason);
    }

    [Fact]
    public void Reviewing_the_head_means_nothing_new()
    {
        var range = ReviewRange.SinceLastReview(Commits, "ccccccc3333");
        Assert.Equal(ReviewRangeKind.NothingNew, range.Kind);
        Assert.Empty(range.NewCommits);
    }

    [Fact]
    public void Unknown_or_missing_review_falls_back_to_the_full_diff()
    {
        Assert.Equal(ReviewRangeKind.Full, ReviewRange.SinceLastReview(Commits, null).Kind);
        var forced = ReviewRange.SinceLastReview(Commits, "deadbeef000");
        Assert.Equal(ReviewRangeKind.Full, forced.Kind);
        Assert.Contains("force push", forced.Reason);
        Assert.False(forced.IsIncremental);
        Assert.Equal(ReviewRangeKind.Full, ReviewRange.SinceLastReview(Array.Empty<CommitInfo>(), "aaaaaaa").Kind);
    }

    [Fact]
    public void Short_sha_matches_the_full_one()
    {
        Assert.Equal(ReviewRangeKind.SinceLastReview, ReviewRange.SinceLastReview(Commits, "AAAAAAA").Kind);
    }

    [Fact]
    public void Commit_helpers_shorten_sha_and_message()
    {
        var c = C("abcdef0123456789");
        Assert.Equal("abcdef0", c.ShortSha);
        Assert.Equal("message abcdef0123456789", c.Headline);
    }

    [Fact]
    public void Files_touched_since_the_review_are_marked()
    {
        var full = new[] { T.File("a.cs"), T.File("b.cs"), T.File("c.cs") };
        var since = new[]
        {
            T.File("b.cs"),
            new DiffFile("c.cs", null, DiffFileStatus.Added, false, false, 3, 0, Array.Empty<DiffHunk>()),
        };
        var states = ReviewRange.MarkChangedSince(full, since);
        Assert.Equal(FileChangeState.Unchanged, states["a.cs"]);
        Assert.Equal(FileChangeState.ChangedSince, states["b.cs"]);
        Assert.Equal(FileChangeState.AddedSince, states["c.cs"]);
        Assert.Equal(new[] { "b.cs", "c.cs" }, ReviewRange.FilesChangedSince(full, states).Select(f => f.Path).ToArray());
    }

    [Theory]
    [InlineData(0.4, "under a minute")]
    [InlineData(3.2, "~3 min")]
    [InlineData(12, "~10 min")]
    [InlineData(90, "~1 h 30 min")]
    [InlineData(120, "~2 h")]
    public void Time_is_formatted_for_humans(double minutes, string expected)
    {
        Assert.Equal(expected, ReviewTimeEstimate.Format(minutes));
    }

    [Fact]
    public void Generated_files_barely_count_towards_the_estimate()
    {
        var source = new[] { T.File("a.cs", 100, 0) };
        var lockOnly = new[] { T.File("yarn.lock", 100, 0) };
        Assert.True(ReviewTimeEstimate.Minutes(source) > 10 * ReviewTimeEstimate.Minutes(lockOnly) - 1);
        Assert.Equal(20.5, ReviewTimeEstimate.Minutes(source), 3);
    }

    [Fact]
    public void Binary_files_cost_nothing()
    {
        var bin = new DiffFile("a.png", null, DiffFileStatus.Added, true, false, 0, 0, Array.Empty<DiffHunk>());
        Assert.Equal(0.0, ReviewTimeEstimate.Minutes(new[] { bin }));
    }
}

public class SuggestionTests
{
    [Fact]
    public void Format_wraps_the_text_in_a_suggestion_fence()
    {
        Assert.Equal("```suggestion\nreturn 1;\n```", SuggestionFormatter.Format("return 1;"));
    }

    [Fact]
    public void Format_normalises_line_endings_and_trailing_newlines()
    {
        Assert.Equal("```suggestion\na\nb\n```", SuggestionFormatter.Format("a\r\nb\r\n"));
    }

    [Fact]
    public void Fence_grows_when_the_code_contains_backticks()
    {
        var block = SuggestionFormatter.Format("var s = \"```\";");
        Assert.StartsWith("````suggestion\n", block);
        Assert.EndsWith("\n````", block);
    }

    [Fact]
    public void Empty_suggestion_means_delete_the_lines()
    {
        Assert.Equal("```suggestion\n```", SuggestionFormatter.Format(""));
    }

    [Fact]
    public void Current_text_of_a_range_is_read_from_the_new_side()
    {
        var file = T.File("a.cs", 2, 1, T.Hunk());
        Assert.Equal("b2();\nb3();", SuggestionFormatter.CurrentText(file, 11, 12));
        Assert.Equal("b3();", SuggestionFormatter.CurrentText(file, 12, 12));
        Assert.Equal("```suggestion\nb3();\n```", SuggestionFormatter.Template(file, 12, 12));
    }

    [Fact]
    public void Extract_finds_the_replacement_in_a_comment()
    {
        var body = "Please use this:\n\n```suggestion\nfoo();\nbar();\n```\nthanks";
        Assert.Equal("foo();\nbar();", SuggestionFormatter.Extract(body));
        Assert.Null(SuggestionFormatter.Extract("no block here"));
        Assert.Null(SuggestionFormatter.Extract("```suggestion\nunterminated"));
        Assert.Null(SuggestionFormatter.Extract("```csharp\ncode\n```"));
    }

    [Fact]
    public void Apply_replaces_the_given_lines()
    {
        var lines = new[] { "a", "b", "c", "d" };
        Assert.Equal(new[] { "a", "X", "Y", "d" }, SuggestionFormatter.Apply(lines, 2, 3, "X\nY").ToArray());
        Assert.Equal(new[] { "a", "d" }, SuggestionFormatter.Apply(lines, 2, 3, "").ToArray());
        Assert.Equal(new[] { "Z", "b", "c", "d" }, SuggestionFormatter.Apply(lines, 1, 1, "Z").ToArray());
    }
}

public class DraftStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "cshubben-drafts-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void In_memory_store_round_trips_and_forgets_when_empty()
    {
        var store = new InMemoryDraftStore();
        Assert.Empty(store.Load("pr"));
        store.Save("pr", new[] { T.Pending("p1", "a.cs", 3, "hello") }, "summary");
        Assert.Equal("hello", store.Load("pr")[0].Body);
        Assert.Equal("summary", store.LoadSummary("pr"));
        store.Save("pr", Array.Empty<PendingComment>(), "");
        Assert.Empty(store.Load("pr"));
        Assert.Equal("", store.LoadSummary("pr"));
    }

    [Fact]
    public void File_store_survives_a_restart()
    {
        var dir = TempDir();
        try
        {
            var first = new FileDraftStore(dir);
            var comment = new PendingComment("p1", "src/a.cs", 12, DiffSide.Left, 10, "multi\nline", T.Now);
            first.Save("PR_node/1", new[] { comment }, "my summary");

            var second = new FileDraftStore(dir);
            var loaded = Assert.Single(second.Load("PR_node/1"));
            Assert.Equal(comment, loaded);
            Assert.Equal("my summary", second.LoadSummary("PR_node/1"));
            Assert.Empty(second.Load("other"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void File_store_removes_the_file_when_nothing_is_left()
    {
        var dir = TempDir();
        try
        {
            var store = new FileDraftStore(dir);
            store.Save("pr", new[] { T.Pending("p", "a", 1) }, "");
            Assert.True(File.Exists(store.PathFor("pr")));
            store.Save("pr", Array.Empty<PendingComment>(), "");
            Assert.False(File.Exists(store.PathFor("pr")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void File_names_are_hashes_and_never_contain_the_id()
    {
        var name = FileDraftStore.FileNameFor("../../etc/passwd");
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain("/", name);
        Assert.EndsWith(".json", name);
        Assert.Equal(name, FileDraftStore.FileNameFor("../../etc/passwd"));
        Assert.NotEqual(name, FileDraftStore.FileNameFor("other"));
    }

    [Fact]
    public void Corrupt_or_hostile_json_gives_no_drafts()
    {
        Assert.Empty(DraftSerializer.Deserialize("not json").Comments);
        Assert.Empty(DraftSerializer.Deserialize("null").Comments);
        var json = "{\"Version\":1,\"Summary\":\"s\",\"Comments\":[{\"Id\":\"\",\"Path\":\"a\",\"Line\":1,\"Side\":1,\"StartLine\":null,\"Body\":\"x\",\"CreatedAt\":\"2026-01-01T00:00:00+00:00\"}]}";
        var loaded = DraftSerializer.Deserialize(json);
        Assert.Empty(loaded.Comments);
        Assert.Equal("s", loaded.Summary);
    }
}

public class SplitRowTests
{
    private static LineRow L(DiffLineKind kind, int? o, int? n, string text, string key) =>
        new(key, 0, 0, new DiffLine(kind, o, n, text), null);

    [Fact]
    public void Removed_and_added_runs_are_paired_side_by_side()
    {
        var rows = new ReviewRow[]
        {
            L(DiffLineKind.Context, 1, 1, "a", "k0"),
            L(DiffLineKind.Remove, 2, null, "b", "k1"),
            L(DiffLineKind.Remove, 3, null, "c", "k2"),
            L(DiffLineKind.Add, null, 2, "B", "k3"),
        };
        var split = SplitRows.From(rows).Cast<SplitRow>().ToList();
        Assert.Equal(3, split.Count);
        Assert.Same(split[0].Left, split[0].Right);
        Assert.Equal("b", split[1].Left!.Line.Text);
        Assert.Equal("B", split[1].Right!.Line.Text);
        Assert.Equal("c", split[2].Left!.Line.Text);
        Assert.Null(split[2].Right);
    }

    [Fact]
    public void Extra_added_lines_have_an_empty_left_side()
    {
        var rows = new ReviewRow[]
        {
            L(DiffLineKind.Add, null, 1, "x", "k0"),
            L(DiffLineKind.Add, null, 2, "y", "k1"),
        };
        var split = SplitRows.From(rows).Cast<SplitRow>().ToList();
        Assert.Equal(2, split.Count);
        Assert.All(split, s => Assert.Null(s.Left));
    }

    [Fact]
    public void Other_rows_pass_through_and_keys_stay_unique()
    {
        var rows = new ReviewRow[]
        {
            new HunkHeaderRow(0, "@@ -1 +1 @@", ""),
            L(DiffLineKind.Remove, 1, null, "a", "k0"),
            new ThreadRow(T.Thread("t", "f", 1, DiffSide.Left)),
            L(DiffLineKind.Add, null, 1, "b", "k1"),
            L(DiffLineKind.NoNewline, null, null, "\\ No newline", "k2"),
        };
        var split = SplitRows.From(rows);
        Assert.IsType<HunkHeaderRow>(split[0]);
        Assert.IsType<SplitRow>(split[1]);
        Assert.IsType<ThreadRow>(split[2]);
        Assert.IsType<SplitRow>(split[3]);
        Assert.Equal(4, split.Count);
        Assert.Equal(split.Count, split.Select(r => r.Key).Distinct().Count());
    }
}
