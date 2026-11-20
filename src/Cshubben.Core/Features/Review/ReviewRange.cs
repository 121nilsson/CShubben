using System.Globalization;

namespace Cshubben.Core.Features.Review;

public enum ReviewRangeKind
{
    /// <summary>The whole pull request.</summary>
    Full,

    /// <summary>Only the commits after the viewer's last review.</summary>
    SinceLastReview,

    /// <summary>The viewer reviewed the current head already: nothing new.</summary>
    NothingNew,
}

/// <summary>What to show for a re-review. BaseSha is the last reviewed commit when Kind is not Full.</summary>
public sealed record ReviewRangeInfo(
    ReviewRangeKind Kind,
    string? BaseSha,
    string HeadSha,
    IReadOnlyList<CommitInfo> NewCommits,
    string Reason)
{
    public bool IsIncremental => Kind != ReviewRangeKind.Full;
}

/// <summary>How a file relates to the viewer's last review.</summary>
public enum FileChangeState { Unchanged, ChangedSince, AddedSince }

public static class ReviewRange
{
    /// <summary>
    /// Works out the range for a re-review. <paramref name="commits"/> are oldest first, the last one is the head.
    /// If the last reviewed commit is not in the list (force push, rebase) the full diff is the only safe answer.
    /// </summary>
    public static ReviewRangeInfo SinceLastReview(IReadOnlyList<CommitInfo> commits, string? myLastReviewedSha)
    {
        var head = commits.Count > 0 ? commits[commits.Count - 1].Sha : string.Empty;
        if (string.IsNullOrWhiteSpace(myLastReviewedSha))
            return new ReviewRangeInfo(ReviewRangeKind.Full, null, head, commits, "You have not reviewed this pull request yet.");
        if (commits.Count == 0)
            return new ReviewRangeInfo(ReviewRangeKind.Full, null, head, commits, "No commits to compare.");

        var index = -1;
        for (var i = 0; i < commits.Count; i++)
        {
            if (SameSha(commits[i].Sha, myLastReviewedSha))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return new ReviewRangeInfo(ReviewRangeKind.Full, null, head, commits, "Your last reviewed commit is gone (force push), so the full diff is shown.");

        if (index == commits.Count - 1)
            return new ReviewRangeInfo(ReviewRangeKind.NothingNew, commits[index].Sha, head, Array.Empty<CommitInfo>(), "No new commits since your review.");

        var fresh = commits.Skip(index + 1).ToList();
        var reason = fresh.Count == 1 ? "1 new commit since your review." : fresh.Count.ToString(CultureInfo.InvariantCulture) + " new commits since your review.";
        return new ReviewRangeInfo(ReviewRangeKind.SinceLastReview, commits[index].Sha, head, fresh, reason);
    }

    /// <summary>
    /// Compares the full diff with the diff since the last review: a file in both changed after the review;
    /// a file that the full diff has as added and the since-diff touches was added after it.
    /// </summary>
    public static IReadOnlyDictionary<string, FileChangeState> MarkChangedSince(
        IReadOnlyList<DiffFile> full,
        IReadOnlyList<DiffFile> since)
    {
        var sincePaths = new Dictionary<string, DiffFile>(StringComparer.Ordinal);
        foreach (var f in since) sincePaths[f.Path] = f;

        var result = new Dictionary<string, FileChangeState>(StringComparer.Ordinal);
        foreach (var file in full)
        {
            if (!sincePaths.TryGetValue(file.Path, out var touched))
            {
                result[file.Path] = FileChangeState.Unchanged;
            }
            else
            {
                result[file.Path] = touched.Status == DiffFileStatus.Added ? FileChangeState.AddedSince : FileChangeState.ChangedSince;
            }
        }
        return result;
    }

    /// <summary>Files of the full diff that the since-diff touches (in the order of the full diff).</summary>
    public static IReadOnlyList<DiffFile> FilesChangedSince(IReadOnlyList<DiffFile> full, IReadOnlyDictionary<string, FileChangeState> states) =>
        full.Where(f => states.TryGetValue(f.Path, out var s) && s != FileChangeState.Unchanged).ToList();

    private static bool SameSha(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (a.Length == b.Length) return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var shorter = a.Length < b.Length ? a : b;
        var longer = a.Length < b.Length ? b : a;
        return shorter.Length >= 7 && longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A rough, honest estimate of the time a careful review takes, from the lines changed.</summary>
public static class ReviewTimeEstimate
{
    private const double LinesPerMinute = 5.0;
    private const double MinutesPerFile = 0.5;
    private const double GeneratedWeight = 0.05;

    public static double Minutes(IReadOnlyList<DiffFile> files)
    {
        double lines = 0;
        var reviewedFiles = 0;
        foreach (var file in files)
        {
            var kind = FileKinds.Classify(file.Path, file.IsBinary);
            if (kind == FileKind.Binary) continue;
            var weight = FileKinds.CollapsedByDefault(kind) ? GeneratedWeight : 1.0;
            lines += file.TotalChanges * weight;
            if (weight >= 1.0) reviewedFiles++;
        }
        return (lines / LinesPerMinute) + (reviewedFiles * MinutesPerFile);
    }

    public static string Label(IReadOnlyList<DiffFile> files) => Format(Minutes(files));

    public static string Format(double minutes)
    {
        if (minutes < 1) return "under a minute";
        var rounded = minutes < 10 ? (int)Math.Round(minutes) : (int)(Math.Round(minutes / 5.0) * 5);
        if (rounded < 60) return "~" + rounded.ToString(CultureInfo.InvariantCulture) + " min";
        var hours = rounded / 60;
        var rest = rounded % 60;
        return rest == 0
            ? "~" + hours.ToString(CultureInfo.InvariantCulture) + " h"
            : "~" + hours.ToString(CultureInfo.InvariantCulture) + " h " + rest.ToString(CultureInfo.InvariantCulture) + " min";
    }
}
