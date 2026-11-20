namespace Cshubben.Core.Features.Review;

/// <summary>"7/12 viewed" and friends, computed from the session, the files and the threads.</summary>
public sealed record ReviewProgress(int ViewedFiles, int TotalFiles, int UnresolvedThreads, int PendingComments)
{
    public string ViewedLabel => $"{ViewedFiles}/{TotalFiles} viewed";

    public string ThreadsLabel => UnresolvedThreads == 0 ? "No open threads" : $"{UnresolvedThreads} unresolved";

    public double Fraction => TotalFiles == 0 ? 1.0 : (double)ViewedFiles / TotalFiles;

    public bool AllViewed => ViewedFiles >= TotalFiles;

    public static ReviewProgress Compute(IReadOnlyList<DiffFile> files, ReviewSession session, IReadOnlyList<ReviewThread> threads)
    {
        var viewed = files.Count(f => session.IsViewed(f.Path));
        var open = threads.Count(t => !t.IsResolved);
        return new ReviewProgress(viewed, files.Count, open, session.Pending.Count);
    }
}

public enum ReviewStepKind { NextFile, NextThread, Submit, Done }

/// <summary>The one thing to do next. Drives the big primary button.</summary>
public sealed record ReviewNextStep(ReviewStepKind Kind, string Label, string Reason)
{
    public static ReviewNextStep Compute(ReviewProgress progress, bool alreadySubmitted = false)
    {
        if (progress.TotalFiles == 0 && progress.UnresolvedThreads == 0)
            return new ReviewNextStep(ReviewStepKind.Submit, "Submit review", "Nothing to read in this diff.");
        if (!progress.AllViewed)
        {
            var left = progress.TotalFiles - progress.ViewedFiles;
            return new ReviewNextStep(ReviewStepKind.NextFile, "Next file", left == 1 ? "1 file left to view." : $"{left} files left to view.");
        }
        if (progress.UnresolvedThreads > 0)
            return new ReviewNextStep(ReviewStepKind.NextThread, "Next open thread",
                progress.UnresolvedThreads == 1 ? "1 conversation is still open." : $"{progress.UnresolvedThreads} conversations are still open.");
        if (alreadySubmitted && progress.PendingComments == 0)
            return new ReviewNextStep(ReviewStepKind.Done, "All done", "Everything is viewed and resolved.");
        return new ReviewNextStep(ReviewStepKind.Submit, "Submit review",
            progress.PendingComments == 0 ? "Everything is viewed." : $"{progress.PendingComments} pending comments are waiting.");
    }
}

/// <summary>
/// The reviewer's state for one pull request: files marked viewed, comments not yet submitted, the summary text and
/// the verdict. Immutable; every change returns a new session, which suits UI state.
/// </summary>
public sealed record ReviewSession
{
    public ReviewSession(string pullRequestId)
    {
        PullRequestId = pullRequestId;
    }

    public string PullRequestId { get; init; }

    public IReadOnlyCollection<string> Viewed { get; init; } = Array.Empty<string>();

    public IReadOnlyList<PendingComment> Pending { get; init; } = Array.Empty<PendingComment>();

    public string Summary { get; init; } = string.Empty;

    public ReviewVerdict Verdict { get; init; } = ReviewVerdict.Comment;

    public bool IsViewed(string path) => Viewed.Contains(path);

    public ReviewSession WithViewed(string path, bool viewed)
    {
        var set = new HashSet<string>(Viewed, StringComparer.Ordinal);
        if (viewed) set.Add(path);
        else set.Remove(path);
        return this with { Viewed = set };
    }

    /// <summary>Replaces the viewed set from the server states (Viewed counts, Dismissed and Unviewed do not).</summary>
    public ReviewSession WithServerViewed(IReadOnlyDictionary<string, ViewedState> states)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in states)
            if (pair.Value == ViewedState.Viewed) set.Add(pair.Key);
        return this with { Viewed = set };
    }

    public ReviewSession AddPending(PendingComment comment)
    {
        var list = new List<PendingComment>(Pending) { comment };
        return this with { Pending = list };
    }

    public ReviewSession RemovePending(string id) =>
        this with { Pending = Pending.Where(p => p.Id != id).ToList() };

    public ReviewSession UpdatePending(string id, string body) =>
        this with { Pending = Pending.Select(p => p.Id == id ? p with { Body = body } : p).ToList() };

    public ReviewSession ClearPending() => this with { Pending = Array.Empty<PendingComment>() };

    public ReviewSession WithSummary(string summary) => this with { Summary = summary };

    public ReviewSession WithVerdict(ReviewVerdict verdict) => this with { Verdict = verdict };

    public IEnumerable<PendingComment> PendingIn(string path) => Pending.Where(p => p.Path == path);

    /// <summary>
    /// The next file that is not viewed, searching from just after <paramref name="currentPath"/> and wrapping around.
    /// Null when everything is viewed. The order is the order of <paramref name="orderedFiles"/>.
    /// </summary>
    public string? NextUnviewedFile(IReadOnlyList<DiffFile> orderedFiles, string? currentPath)
    {
        if (orderedFiles.Count == 0) return null;
        var start = IndexOf(orderedFiles, currentPath);
        for (var step = 1; step <= orderedFiles.Count; step++)
        {
            var file = orderedFiles[(start + step + orderedFiles.Count) % orderedFiles.Count];
            if (!IsViewed(file.Path)) return file.Path;
        }
        return null;
    }

    /// <summary>
    /// The next open thread in file order, then line order, after <paramref name="currentThreadId"/>; wraps around.
    /// Backwards when <paramref name="forward"/> is false. Null when no thread is open.
    /// </summary>
    public static ReviewThread? NextUnresolvedThread(
        IReadOnlyList<ReviewThread> threads,
        IReadOnlyList<DiffFile> orderedFiles,
        string? currentThreadId,
        bool forward = true)
    {
        var open = OrderThreads(threads.Where(t => !t.IsResolved).ToList(), orderedFiles);
        if (open.Count == 0) return null;
        var current = open.FindIndex(t => t.Id == currentThreadId);
        if (current < 0) return forward ? open[0] : open[open.Count - 1];
        var next = forward ? (current + 1) % open.Count : (current - 1 + open.Count) % open.Count;
        return open[next];
    }

    /// <summary>Threads in reading order: by file position, then by line, then by creation time.</summary>
    public static List<ReviewThread> OrderThreads(IReadOnlyList<ReviewThread> threads, IReadOnlyList<DiffFile> orderedFiles)
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < orderedFiles.Count; i++) position[orderedFiles[i].Path] = i;
        return threads
            .OrderBy(t => position.TryGetValue(t.Path, out var p) ? p : int.MaxValue)
            .ThenBy(t => t.Line ?? 0)
            .ThenBy(t => t.Root?.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static int IndexOf(IReadOnlyList<DiffFile> files, string? path)
    {
        if (path is null) return -1;
        for (var i = 0; i < files.Count; i++)
            if (files[i].Path == path) return i;
        return -1;
    }
}
