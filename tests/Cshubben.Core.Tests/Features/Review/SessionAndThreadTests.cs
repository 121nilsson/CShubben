using Cshubben.Core.Features.Review;
using Xunit;

namespace Cshubben.Core.Tests.Features.Review;

internal static class T
{
    public static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    public static DiffFile File(string path, int add = 1, int del = 0, params DiffHunk[] hunks) =>
        new(path, null, DiffFileStatus.Modified, false, false, add, del, hunks);

    public static ReviewComment Comment(string id, string body = "text", string author = "alice", bool mine = false) =>
        new(id, author, body, Now, mine, "https://github.com/o/r/pull/1#discussion_" + id);

    public static ReviewThread Thread(string id, string path, int? line, DiffSide side = DiffSide.Right, bool resolved = false, bool outdated = false) =>
        new(id, path, line, side, null, resolved, outdated, new[] { Comment(id + "-c1") });

    public static PendingComment Pending(string id, string path, int line, string body = "nit") =>
        new(id, path, line, DiffSide.Right, null, body, Now);

    /// <summary>A hunk "@@ -10,4 +10,5 @@": context(10), remove(11), add(11), add(12), context(12->13)...</summary>
    public static DiffHunk Hunk() => new(
        "@@ -10,3 +10,4 @@ void M()", 10, 3, 10, 4,
        new List<DiffLine>
        {
            new(DiffLineKind.Context, 10, 10, "a();"),
            new(DiffLineKind.Remove, 11, null, "b();"),
            new(DiffLineKind.Add, null, 11, "b2();"),
            new(DiffLineKind.Add, null, 12, "b3();"),
            new(DiffLineKind.Context, 12, 13, "c();"),
        });
}

public class ThreadAnchorTests
{
    private static readonly DiffFile File = T.File("a.cs", 2, 1, T.Hunk());

    [Fact]
    public void Right_side_matches_added_and_context_lines()
    {
        Assert.Equal(new LineLocation(0, 2), ThreadAnchor.Find(File, 11, DiffSide.Right));
        Assert.Equal(new LineLocation(0, 0), ThreadAnchor.Find(File, 10, DiffSide.Right));
        Assert.Equal(new LineLocation(0, 4), ThreadAnchor.Find(File, 13, DiffSide.Right));
    }

    [Fact]
    public void Left_side_matches_removed_and_context_lines()
    {
        Assert.Equal(new LineLocation(0, 1), ThreadAnchor.Find(File, 11, DiffSide.Left));
        Assert.Equal(new LineLocation(0, 4), ThreadAnchor.Find(File, 12, DiffSide.Left));
    }

    [Fact]
    public void Lines_outside_the_hunks_have_no_location()
    {
        Assert.Null(ThreadAnchor.Find(File, 99, DiffSide.Right));
        Assert.Null(ThreadAnchor.Find(File, null, DiffSide.Right));
        Assert.Null(ThreadAnchor.Find(File, 0, DiffSide.Right));
    }

    [Fact]
    public void Threads_and_pending_comments_use_the_same_mapping()
    {
        Assert.Equal(new LineLocation(0, 3), ThreadAnchor.Find(File, T.Thread("t", "a.cs", 12)));
        Assert.Equal(new LineLocation(0, 3), ThreadAnchor.Find(File, T.Pending("p", "a.cs", 12)));
    }

    [Fact]
    public void Orphan_policy_never_hides_an_open_thread()
    {
        Assert.Equal(OrphanPlacement.Hidden, ThreadAnchor.Orphan(T.Thread("t", "a", 1, resolved: true, outdated: true)));
        Assert.Equal(OrphanPlacement.AfterFileHeader, ThreadAnchor.Orphan(T.Thread("t", "a", 1, outdated: true)));
        Assert.Equal(OrphanPlacement.AfterFileHeader, ThreadAnchor.Orphan(T.Thread("t", "a", null)));
        Assert.Equal(OrphanPlacement.EndOfFile, ThreadAnchor.Orphan(T.Thread("t", "a", 1, resolved: true)));
        Assert.Equal(OrphanPlacement.EndOfFile, ThreadAnchor.Orphan(T.Thread("t", "a", 1)));
    }

    [Fact]
    public void Side_of_a_line_follows_its_kind()
    {
        Assert.Equal(DiffSide.Left, ThreadAnchor.SideOf(new DiffLine(DiffLineKind.Remove, 1, null, "x")));
        Assert.Equal(DiffSide.Right, ThreadAnchor.SideOf(new DiffLine(DiffLineKind.Add, null, 1, "x")));
        Assert.Equal(DiffSide.Right, ThreadAnchor.SideOf(new DiffLine(DiffLineKind.Context, 1, 1, "x")));
    }

    [Fact]
    public void Ranges_cannot_start_above_the_hunk()
    {
        var hunk = T.Hunk();
        Assert.Equal(10, ThreadAnchor.MinLine(hunk, DiffSide.Right));
        Assert.Equal(10, ThreadAnchor.MinLine(hunk, DiffSide.Left));
    }

    [Fact]
    public void Thread_helpers_update_without_mutating()
    {
        var t = T.Thread("t", "a", 3);
        var replied = t.WithComment(T.Comment("c2"));
        Assert.Single(t.Comments);
        Assert.Equal(2, replied.Comments.Count);
        Assert.Single(replied.Replies);
        Assert.True(t.WithResolved(true).IsResolved);
        Assert.False(t.IsResolved);
        Assert.Null(t.WithoutComment("t-c1"));
        Assert.Single(replied.WithoutComment("c2")!.Comments);
        Assert.Equal("changed", replied.ReplaceComment("c2", T.Comment("c2", "changed")).Comments[1].Body);
    }

    [Fact]
    public void Range_thread_is_recognised()
    {
        var t = T.Thread("t", "a", 5) with { StartLine = 3 };
        Assert.True(t.IsRange);
        Assert.False(T.Thread("t", "a", 5).IsRange);
    }
}

public class SessionTests
{
    private static readonly IReadOnlyList<DiffFile> Files = new[] { T.File("a.cs"), T.File("b.cs"), T.File("c.cs") };

    [Fact]
    public void Viewed_state_is_toggled_without_mutating_the_old_session()
    {
        var s = new ReviewSession("pr1");
        var s2 = s.WithViewed("a.cs", true);
        Assert.False(s.IsViewed("a.cs"));
        Assert.True(s2.IsViewed("a.cs"));
        Assert.False(s2.WithViewed("a.cs", false).IsViewed("a.cs"));
    }

    [Fact]
    public void Server_states_seed_the_viewed_set()
    {
        var states = new Dictionary<string, ViewedState>
        {
            ["a.cs"] = ViewedState.Viewed,
            ["b.cs"] = ViewedState.Dismissed,
            ["c.cs"] = ViewedState.Unviewed,
        };
        var s = new ReviewSession("pr1").WithServerViewed(states);
        Assert.True(s.IsViewed("a.cs"));
        Assert.False(s.IsViewed("b.cs"));
        Assert.False(s.IsViewed("c.cs"));
    }

    [Fact]
    public void Pending_comments_can_be_added_edited_and_removed()
    {
        var s = new ReviewSession("pr1").AddPending(T.Pending("p1", "a.cs", 3)).AddPending(T.Pending("p2", "b.cs", 4));
        Assert.Equal(2, s.Pending.Count);
        Assert.Equal("fixed", s.UpdatePending("p1", "fixed").Pending[0].Body);
        Assert.Single(s.RemovePending("p1").Pending);
        Assert.Empty(s.ClearPending().Pending);
        Assert.Single(s.PendingIn("b.cs"));
    }

    [Fact]
    public void Summary_and_verdict_are_stored()
    {
        var s = new ReviewSession("pr1").WithSummary("looks good").WithVerdict(ReviewVerdict.Approve);
        Assert.Equal("looks good", s.Summary);
        Assert.Equal(ReviewVerdict.Approve, s.Verdict);
        Assert.Equal(ReviewVerdict.Comment, new ReviewSession("x").Verdict);
    }

    [Fact]
    public void Progress_counts_viewed_files_and_open_threads()
    {
        var s = new ReviewSession("pr1").WithViewed("a.cs", true).AddPending(T.Pending("p", "a.cs", 1));
        var threads = new[] { T.Thread("t1", "a.cs", 1), T.Thread("t2", "a.cs", 2, resolved: true), T.Thread("t3", "b.cs", 3) };
        var progress = ReviewProgress.Compute(Files, s, threads);
        Assert.Equal("1/3 viewed", progress.ViewedLabel);
        Assert.Equal("2 unresolved", progress.ThreadsLabel);
        Assert.Equal(1, progress.PendingComments);
        Assert.False(progress.AllViewed);
        Assert.Equal(1.0 / 3.0, progress.Fraction, 3);
    }

    [Fact]
    public void Progress_labels_cover_the_quiet_cases()
    {
        var progress = ReviewProgress.Compute(Array.Empty<DiffFile>(), new ReviewSession("x"), Array.Empty<ReviewThread>());
        Assert.Equal("No open threads", progress.ThreadsLabel);
        Assert.Equal(1.0, progress.Fraction);
        Assert.True(progress.AllViewed);
    }

    [Fact]
    public void Next_unviewed_file_starts_after_the_current_one_and_wraps()
    {
        var s = new ReviewSession("pr1").WithViewed("b.cs", true);
        Assert.Equal("c.cs", s.NextUnviewedFile(Files, "a.cs"));
        Assert.Equal("a.cs", s.NextUnviewedFile(Files, "c.cs"));
        Assert.Equal("a.cs", s.NextUnviewedFile(Files, null));
    }

    [Fact]
    public void Next_unviewed_file_is_null_when_everything_is_viewed()
    {
        var s = new ReviewSession("pr1").WithViewed("a.cs", true).WithViewed("b.cs", true).WithViewed("c.cs", true);
        Assert.Null(s.NextUnviewedFile(Files, "a.cs"));
        Assert.Null(new ReviewSession("x").NextUnviewedFile(Array.Empty<DiffFile>(), null));
    }

    [Fact]
    public void Next_thread_follows_file_and_line_order_and_skips_resolved()
    {
        var threads = new[]
        {
            T.Thread("late", "c.cs", 1),
            T.Thread("b2", "b.cs", 20),
            T.Thread("b1", "b.cs", 5),
            T.Thread("done", "a.cs", 1, resolved: true),
        };
        Assert.Equal("b1", ReviewSession.NextUnresolvedThread(threads, Files, null)!.Id);
        Assert.Equal("b2", ReviewSession.NextUnresolvedThread(threads, Files, "b1")!.Id);
        Assert.Equal("late", ReviewSession.NextUnresolvedThread(threads, Files, "b2")!.Id);
        Assert.Equal("b1", ReviewSession.NextUnresolvedThread(threads, Files, "late")!.Id);
        Assert.Equal("b2", ReviewSession.NextUnresolvedThread(threads, Files, "late", forward: false)!.Id);
    }

    [Fact]
    public void Next_thread_is_null_without_open_threads()
    {
        var threads = new[] { T.Thread("t", "a.cs", 1, resolved: true) };
        Assert.Null(ReviewSession.NextUnresolvedThread(threads, Files, null));
    }

    [Fact]
    public void Next_step_prefers_files_then_threads_then_submit()
    {
        Assert.Equal(ReviewStepKind.NextFile, ReviewNextStep.Compute(new ReviewProgress(1, 3, 2, 0)).Kind);
        Assert.Equal(ReviewStepKind.NextThread, ReviewNextStep.Compute(new ReviewProgress(3, 3, 2, 0)).Kind);
        Assert.Equal(ReviewStepKind.Submit, ReviewNextStep.Compute(new ReviewProgress(3, 3, 0, 1)).Kind);
        Assert.Equal(ReviewStepKind.Done, ReviewNextStep.Compute(new ReviewProgress(3, 3, 0, 0), alreadySubmitted: true).Kind);
        Assert.Contains("2 files", ReviewNextStep.Compute(new ReviewProgress(1, 3, 0, 0)).Reason);
    }
}
