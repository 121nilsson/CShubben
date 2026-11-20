namespace Cshubben.Core.Features.Review;

/// <summary>Where a thread (or draft) lands in a file's hunks.</summary>
public readonly record struct LineLocation(int HunkIndex, int LineIndex);

/// <summary>Where to show a thread that could not be anchored to a line.</summary>
public enum OrphanPlacement { AfterFileHeader, EndOfFile, Hidden }

/// <summary>Maps review threads (path, line, side) onto parsed diff lines.</summary>
public static class ThreadAnchor
{
    /// <summary>
    /// Finds the line a thread belongs to. Right-side threads match new line numbers of context and added lines,
    /// left-side threads match old line numbers of context and removed lines.
    /// </summary>
    public static LineLocation? Find(DiffFile file, int? line, DiffSide side)
    {
        if (line is null || line.Value <= 0) return null;
        for (var h = 0; h < file.Hunks.Count; h++)
        {
            var lines = file.Hunks[h].Lines;
            for (var l = 0; l < lines.Count; l++)
            {
                var candidate = lines[l];
                if (side == DiffSide.Right)
                {
                    if (candidate.Kind != DiffLineKind.Remove && candidate.NewNumber == line) return new LineLocation(h, l);
                }
                else
                {
                    if (candidate.Kind != DiffLineKind.Add && candidate.OldNumber == line) return new LineLocation(h, l);
                }
            }
        }
        return null;
    }

    public static LineLocation? Find(DiffFile file, ReviewThread thread) => Find(file, thread.Line, thread.Side);

    public static LineLocation? Find(DiffFile file, PendingComment comment) => Find(file, comment.Line, comment.Side);

    /// <summary>
    /// Policy for threads without a line in this diff. Open threads are never hidden: a comment that needs an answer
    /// must not vanish from the review.
    /// </summary>
    public static OrphanPlacement Orphan(ReviewThread thread)
    {
        if (thread.IsResolved && thread.IsOutdated) return OrphanPlacement.Hidden;
        if (!thread.IsResolved && (thread.IsOutdated || thread.Line is null)) return OrphanPlacement.AfterFileHeader;
        return OrphanPlacement.EndOfFile;
    }

    /// <summary>Smallest line number of the hunk on the given side; ranges cannot start above it.</summary>
    public static int MinLine(DiffHunk hunk, DiffSide side) => side == DiffSide.Right ? hunk.NewStart : hunk.OldStart;

    /// <summary>The line number of a diff line on the given side, if it has one.</summary>
    public static int? NumberOn(DiffLine line, DiffSide side) => side == DiffSide.Right ? line.NewNumber : line.OldNumber;

    /// <summary>The side a click on this line should comment on: removed lines are left, everything else right.</summary>
    public static DiffSide SideOf(DiffLine line) => line.Kind == DiffLineKind.Remove ? DiffSide.Left : DiffSide.Right;
}
