namespace Cshubben.Core.Features.Review;

/// <summary>What the reviewer concludes when submitting. Independent of the GitHub feature's own enum.</summary>
public enum ReviewVerdict { Comment, Approve, RequestChanges }

/// <summary>GitHub's per-file "viewed" state. Dismissed means the file changed after it was marked viewed.</summary>
public enum ViewedState { Unviewed, Viewed, Dismissed }

/// <summary>Identifies the pull request to the data layer. The id is the GraphQL node id when known.</summary>
public sealed record ReviewTarget(string Id, string Repo, int Number, string Url, string Title = "")
{
    public string Label => $"{Repo}#{Number}";
}

public sealed record ReviewComment(
    string Id,
    string Author,
    string Body,
    DateTimeOffset CreatedAt,
    bool IsMine = false,
    string? Url = null);

/// <summary>
/// A conversation on a line (or line range) of a file. The first comment starts it, the rest are replies.
/// Line is the last line of a range; StartLine is set for ranges only.
/// </summary>
public sealed record ReviewThread(
    string Id,
    string Path,
    int? Line,
    DiffSide Side,
    int? StartLine,
    bool IsResolved,
    bool IsOutdated,
    IReadOnlyList<ReviewComment> Comments)
{
    public ReviewComment? Root => Comments.Count > 0 ? Comments[0] : null;

    public IEnumerable<ReviewComment> Replies => Comments.Skip(1);

    public bool IsRange => StartLine is not null && Line is not null && StartLine.Value < Line.Value;

    /// <summary>Open conversations are the ones a reviewer still has to deal with.</summary>
    public bool NeedsAttention => !IsResolved;

    public string? Url => Root?.Url;

    public ReviewThread WithResolved(bool resolved) => this with { IsResolved = resolved };

    public ReviewThread WithComment(ReviewComment comment)
    {
        var list = new List<ReviewComment>(Comments) { comment };
        return this with { Comments = list };
    }

    public ReviewThread ReplaceComment(string commentId, ReviewComment replacement) =>
        this with { Comments = Comments.Select(c => c.Id == commentId ? replacement : c).ToList() };

    /// <summary>Removes one comment; null when the thread would become empty (deleting the first comment deletes a thread).</summary>
    public ReviewThread? WithoutComment(string commentId)
    {
        var rest = Comments.Where(c => c.Id != commentId).ToList();
        return rest.Count == 0 ? null : this with { Comments = rest };
    }
}

/// <summary>A comment the reviewer has written but not yet submitted. Lives locally until the review is submitted.</summary>
public sealed record PendingComment(
    string Id,
    string Path,
    int Line,
    DiffSide Side,
    int? StartLine,
    string Body,
    DateTimeOffset CreatedAt)
{
    public bool IsRange => StartLine is not null && StartLine.Value < Line;
}

public sealed record CommitInfo(string Sha, string Message, string Author, DateTimeOffset CommittedAt)
{
    public string ShortSha => Sha.Length > 7 ? Sha.Substring(0, 7) : Sha;

    public string Headline
    {
        get
        {
            var nl = Message.IndexOf('\n');
            return (nl < 0 ? Message : Message.Substring(0, nl)).Trim();
        }
    }
}

/// <summary>A diff plus where it came from. Files are parsed by the data layer with <see cref="UnifiedDiffParser"/>.</summary>
public sealed record PullRequestDiff(
    string DiffText,
    IReadOnlyList<DiffFile> Files,
    IReadOnlyDictionary<string, ViewedState> Viewed);

/// <summary>Facts about the review that are not part of the diff.</summary>
public sealed record ReviewContext(
    string HeadSha,
    string BaseSha,
    string? MyLastReviewedSha,
    string Viewer,
    IReadOnlyList<CommitInfo> Commits,
    string Description = "");
