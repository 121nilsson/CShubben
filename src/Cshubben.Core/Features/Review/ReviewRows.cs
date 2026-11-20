using System.Globalization;

namespace Cshubben.Core.Features.Review;

/// <summary>
/// One row of the flattened body of a file's diff. The UI shows a list of rows through a virtualized list, so only
/// the visible rows are ever built, however large the diff is. Keys are unique within one file.
/// </summary>
public abstract record ReviewRow(string Key);

/// <summary>A one-line explanation instead of a diff: collapsed generated file, binary file, patch too large, renamed only.</summary>
public sealed record NoticeRow(string Text, NoticeKind Kind) : ReviewRow("notice-" + Kind);

public enum NoticeKind { Collapsed, Binary, TooLarge, RenameOnly, Empty }

public sealed record HunkHeaderRow(int HunkIndex, string Header, string Context) : ReviewRow("hunk-" + HunkIndex.ToString(CultureInfo.InvariantCulture));

/// <summary>"N hidden lines": click to expand the unchanged lines between two hunks (or around them).</summary>
public sealed record GapRow(int GapIndex, int HiddenLines, bool Loading) : ReviewRow("gap-" + GapIndex.ToString(CultureInfo.InvariantCulture));

/// <summary>
/// A code line. <see cref="Partner"/> is the paired line for word-level highlighting. Lines that come from an expanded
/// gap are not part of the diff (LineIndex is -1) and cannot be commented on.
/// </summary>
public sealed record LineRow(string LineKey, int HunkIndex, int LineIndex, DiffLine Line, DiffLine? Partner) : ReviewRow(LineKey)
{
    public bool Commentable => LineIndex >= 0 && Line.Kind != DiffLineKind.NoNewline;
}

public sealed record ThreadRow(ReviewThread Thread) : ReviewRow("thread-" + Thread.Id);

public sealed record PendingRow(PendingComment Comment) : ReviewRow("pending-" + Comment.Id);

public sealed record ComposerRow(string LineKey) : ReviewRow("composer-" + LineKey);

/// <summary>Where the inline composer is open.</summary>
public sealed record ComposerTarget(string Path, int Line, DiffSide Side);

public sealed record FlattenOptions
{
    /// <summary>Files the reviewer expanded although they are collapsed by default.</summary>
    public IReadOnlyCollection<string> ExpandedFiles { get; init; } = Array.Empty<string>();

    /// <summary>Gap keys ("gap-1") of this file that are expanded.</summary>
    public IReadOnlyCollection<string> ExpandedGaps { get; init; } = Array.Empty<string>();

    /// <summary>The new-side text of the file, one entry per line, when it has been loaded. Needed to expand gaps.</summary>
    public IReadOnlyList<string>? FileLines { get; init; }

    public IReadOnlyList<ReviewThread> Threads { get; init; } = Array.Empty<ReviewThread>();

    public IReadOnlyList<PendingComment> Pending { get; init; } = Array.Empty<PendingComment>();

    public ComposerTarget? Composer { get; init; }

    public bool HideResolvedThreads { get; init; }
}

/// <summary>A run of unchanged lines that the diff does not show. NewEnd is -1 when unknown (after the last hunk).</summary>
public sealed record ContextGap(int Index, int NewStart, int NewEnd, int OldOffset)
{
    public int Count => NewEnd < 0 ? -1 : Math.Max(0, NewEnd - NewStart + 1);
}

public static class ContextGaps
{
    /// <summary>Gap 0 is above the first hunk, gap i sits above hunk i, the last gap is below the last hunk.</summary>
    public static IReadOnlyList<ContextGap> Compute(DiffFile file, int? newLineCount)
    {
        var gaps = new List<ContextGap>();
        if (file.Status == DiffFileStatus.Added || file.Status == DiffFileStatus.Deleted || file.Hunks.Count == 0) return gaps;

        for (var i = 0; i < file.Hunks.Count; i++)
        {
            var hunk = file.Hunks[i];
            var firstNew = FirstNew(hunk);
            var firstOld = FirstOld(hunk);
            var previousEnd = i == 0 ? 0 : LastNew(file.Hunks[i - 1]);
            gaps.Add(new ContextGap(i, previousEnd + 1, firstNew - 1, firstOld - firstNew));
        }

        var last = file.Hunks[file.Hunks.Count - 1];
        var lastNew = LastNew(last);
        var lastOld = last.OldCount == 0 ? last.OldStart : last.OldStart + last.OldCount - 1;
        var end = newLineCount is int n ? n : -1;
        gaps.Add(new ContextGap(file.Hunks.Count, lastNew + 1, end, lastOld - lastNew));
        return gaps;
    }

    public static int FirstNew(DiffHunk h) => h.NewCount == 0 ? h.NewStart + 1 : h.NewStart;

    public static int LastNew(DiffHunk h) => h.NewCount == 0 ? h.NewStart : h.NewStart + h.NewCount - 1;

    public static int FirstOld(DiffHunk h) => h.OldCount == 0 ? h.OldStart + 1 : h.OldStart;
}

public static class ReviewRows
{
    /// <summary>Turns one file and its threads, drafts and composer into the rows to show.</summary>
    public static IReadOnlyList<ReviewRow> Flatten(DiffFile file, FlattenOptions? options = null)
    {
        options ??= new FlattenOptions();
        var rows = new List<ReviewRow>();
        var threads = options.Threads.Where(t => t.Path == file.Path && !(options.HideResolvedThreads && t.IsResolved)).ToList();
        var pending = options.Pending.Where(p => p.Path == file.Path).ToList();

        var notice = NoticeFor(file, options);
        var anchored = new Dictionary<LineLocation, List<ReviewRow>>();
        var afterHeader = new List<ReviewRow>();
        var endOfFile = new List<ReviewRow>();

        foreach (var thread in threads)
        {
            var location = ThreadAnchor.Find(file, thread);
            var row = new ThreadRow(thread);
            if (location is LineLocation found) AddTo(anchored, found, row);
            else
            {
                var placement = ThreadAnchor.Orphan(thread);
                if (placement == OrphanPlacement.AfterFileHeader) afterHeader.Add(row);
                else if (placement == OrphanPlacement.EndOfFile) endOfFile.Add(row);
            }
        }

        foreach (var comment in pending)
        {
            var location = ThreadAnchor.Find(file, comment);
            var row = new PendingRow(comment);
            if (location is LineLocation found) AddTo(anchored, found, row);
            else endOfFile.Add(row);
        }

        if (notice is not null)
        {
            rows.Add(notice);
            rows.AddRange(afterHeader);
            rows.AddRange(endOfFile);
            return rows;
        }

        rows.AddRange(afterHeader);

        var gaps = ContextGaps.Compute(file, options.FileLines?.Count);
        for (var h = 0; h < file.Hunks.Count; h++)
        {
            var hunk = file.Hunks[h];
            var gapExpanded = false;
            if (h < gaps.Count)
            {
                var gap = gaps[h];
                if (gap.Count > 0)
                    gapExpanded = AddGap(rows, gap, options);
            }

            if (!gapExpanded) rows.Add(new HunkHeaderRow(h, hunk.Header, hunk.Context));

            var pairs = IntraLineDiff.PairLines(hunk.Lines);
            for (var l = 0; l < hunk.Lines.Count; l++)
            {
                var line = hunk.Lines[l];
                DiffLine? partner = pairs.TryGetValue(l, out var p) ? hunk.Lines[p] : null;
                var key = "l" + h.ToString(CultureInfo.InvariantCulture) + "-" + l.ToString(CultureInfo.InvariantCulture);
                rows.Add(new LineRow(key, h, l, line, partner));

                var location = new LineLocation(h, l);
                if (anchored.TryGetValue(location, out var attached)) rows.AddRange(attached);
                if (options.Composer is ComposerTarget target && target.Path == file.Path && Matches(line, target))
                    rows.Add(new ComposerRow(key));
            }
        }

        if (gaps.Count > file.Hunks.Count)
        {
            var trailing = gaps[file.Hunks.Count];
            if (trailing.Count > 0) AddGap(rows, trailing, options);
        }

        rows.AddRange(endOfFile);
        return rows;
    }

    /// <summary>Number of lines in this file that cannot be shown right now (for the "N hidden lines" text).</summary>
    public static int HiddenLineCount(DiffFile file) =>
        ContextGaps.Compute(file, null).Where(g => g.Count > 0).Sum(g => g.Count);

    private static bool Matches(DiffLine line, ComposerTarget target)
    {
        if (line.Kind == DiffLineKind.NoNewline) return false;
        if (target.Side == DiffSide.Right) return line.Kind != DiffLineKind.Remove && line.NewNumber == target.Line;
        return line.Kind != DiffLineKind.Add && line.OldNumber == target.Line;
    }

    private static NoticeRow? NoticeFor(DiffFile file, FlattenOptions options)
    {
        if (file.IsBinary) return new NoticeRow("Binary file not shown.", NoticeKind.Binary);
        if (file.IsTooLarge) return new NoticeRow("This diff is too large to display here. Open it on GitHub.", NoticeKind.TooLarge);
        if (file.Hunks.Count == 0)
        {
            return file.Status == DiffFileStatus.Renamed
                ? new NoticeRow("File renamed without changes.", NoticeKind.RenameOnly)
                : new NoticeRow("No content changes.", NoticeKind.Empty);
        }
        var kind = FileKinds.Classify(file.Path, file.IsBinary);
        if (FileKinds.CollapsedByDefault(kind) && !options.ExpandedFiles.Contains(file.Path))
            return new NoticeRow(FileKinds.Label(kind) + " collapsed. " + file.TotalChanges.ToString(CultureInfo.InvariantCulture) + " changed lines.", NoticeKind.Collapsed);
        return null;
    }

    /// <summary>Adds the gap row, or the expanded lines. Returns true when the lines were added.</summary>
    private static bool AddGap(List<ReviewRow> rows, ContextGap gap, FlattenOptions options)
    {
        var key = "gap-" + gap.Index.ToString(CultureInfo.InvariantCulture);
        var expanded = options.ExpandedGaps.Contains(key);
        var lines = options.FileLines;
        if (expanded && lines is not null)
        {
            for (var n = gap.NewStart; n <= gap.NewEnd && n <= lines.Count; n++)
            {
                var line = new DiffLine(DiffLineKind.Context, n + gap.OldOffset, n, lines[n - 1]);
                rows.Add(new LineRow("x" + gap.Index.ToString(CultureInfo.InvariantCulture) + "-" + n.ToString(CultureInfo.InvariantCulture), gap.Index, -1, line, null));
            }
            return true;
        }
        rows.Add(new GapRow(gap.Index, gap.Count, expanded));
        return false;
    }

    private static void AddTo(Dictionary<LineLocation, List<ReviewRow>> map, LineLocation location, ReviewRow row)
    {
        if (!map.TryGetValue(location, out var list))
        {
            list = new List<ReviewRow>();
            map[location] = list;
        }
        list.Add(row);
    }
}

/// <summary>A side-by-side row: removed text on the left, added text on the right, context on both sides.</summary>
public sealed record SplitRow(LineRow? Left, LineRow? Right) : ReviewRow("s-" + (Left?.Key ?? "none") + "-" + (Right?.Key ?? "none"));

public static class SplitRows
{
    /// <summary>
    /// Converts unified rows to split rows: a run of removed lines is shown next to the run of added lines that follows it,
    /// context lines appear on both sides, and every other row (hunk headers, threads, composer) keeps its full width.
    /// The "no newline" marker is dropped in split view.
    /// </summary>
    public static IReadOnlyList<ReviewRow> From(IReadOnlyList<ReviewRow> rows)
    {
        var result = new List<ReviewRow>();
        var i = 0;
        while (i < rows.Count)
        {
            if (rows[i] is not LineRow line)
            {
                result.Add(rows[i]);
                i++;
                continue;
            }

            if (line.Line.Kind == DiffLineKind.NoNewline)
            {
                i++;
                continue;
            }

            if (line.Line.Kind == DiffLineKind.Context)
            {
                result.Add(new SplitRow(line, line));
                i++;
                continue;
            }

            var removed = new List<LineRow>();
            var added = new List<LineRow>();
            while (i < rows.Count && rows[i] is LineRow r && r.Line.Kind == DiffLineKind.Remove)
            {
                removed.Add(r);
                i++;
            }
            while (i < rows.Count && rows[i] is LineRow a && (a.Line.Kind == DiffLineKind.Add || a.Line.Kind == DiffLineKind.NoNewline))
            {
                if (a.Line.Kind == DiffLineKind.Add) added.Add(a);
                i++;
            }

            var count = Math.Max(removed.Count, added.Count);
            for (var k = 0; k < count; k++)
            {
                result.Add(new SplitRow(k < removed.Count ? removed[k] : null, k < added.Count ? added[k] : null));
            }
        }
        return result;
    }
}
