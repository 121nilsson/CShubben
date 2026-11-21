namespace Cshubben.Core.Features.Review;

/// <summary>
/// Everything the review view needs from the outside world. Implemented by the demo data and by the GitHub adapter.
/// Writes honour a dry-run switch that lives on the adapter, not on each call.
/// </summary>
public interface IReviewData
{
    /// <summary>Head and base commits, commits, the viewer's last reviewed commit and the PR description.</summary>
    Task<ReviewContext> GetContextAsync(ReviewTarget pr, CancellationToken cancellationToken = default);

    /// <summary>The full diff of the pull request with the viewed state of each file.</summary>
    Task<PullRequestDiff> GetPullRequestFilesAsync(ReviewTarget pr, CancellationToken cancellationToken = default);

    /// <summary>The diff between two commits, used for "since my last review".</summary>
    Task<PullRequestDiff> GetCompareFilesAsync(ReviewTarget pr, string baseSha, string headSha, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewThread>> GetThreadsAsync(ReviewTarget pr, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(ReviewTarget pr, CancellationToken cancellationToken = default);

    /// <summary>The text of a file at a commit, for expanding context. Null when it cannot be read.</summary>
    Task<string?> GetFileContentAsync(ReviewTarget pr, string path, string sha, CancellationToken cancellationToken = default);

    Task MarkFileViewedAsync(ReviewTarget pr, string path, CancellationToken cancellationToken = default);

    Task UnmarkFileViewedAsync(ReviewTarget pr, string path, CancellationToken cancellationToken = default);

    /// <summary>Creates a pending review on the server and returns its id.</summary>
    Task<string> StartReviewAsync(ReviewTarget pr, CancellationToken cancellationToken = default);

    /// <summary>Adds a comment to a server-side pending review and returns the id of the new thread.</summary>
    Task<string> AddPendingCommentAsync(ReviewTarget pr, string reviewId, PendingComment comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the review. Local pending comments travel with it in one call, so the review is posted atomically.
    /// When <paramref name="reviewId"/> is given the existing pending review is submitted instead.
    /// </summary>
    Task SubmitReviewAsync(
        ReviewTarget pr,
        ReviewVerdict verdict,
        string body,
        IReadOnlyList<PendingComment> comments,
        string? reviewId = null,
        CancellationToken cancellationToken = default);

    Task<ReviewComment> ReplyAsync(ReviewTarget pr, string threadId, string body, CancellationToken cancellationToken = default);

    Task ResolveThreadAsync(ReviewTarget pr, string threadId, CancellationToken cancellationToken = default);

    Task UnresolveThreadAsync(ReviewTarget pr, string threadId, CancellationToken cancellationToken = default);

    Task<ReviewComment> EditCommentAsync(ReviewTarget pr, string commentId, string body, CancellationToken cancellationToken = default);

    Task DeleteCommentAsync(ReviewTarget pr, string commentId, CancellationToken cancellationToken = default);

    /// <summary>True when writes are only simulated (nothing is sent to GitHub).</summary>
    bool IsDryRun { get; }

    /// <summary>
    /// Reads the file and works out what applying the suggestion would change. Read-only, also in a dry run. The plan
    /// is not Ok when the branch moved since the review was loaded or the suggestion no longer fits.
    /// </summary>
    Task<SuggestionPlan> PreviewSuggestionAsync(ReviewTarget pr, SuggestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the suggestion to the pull request branch. Re-reads the file first, so it never overwrites newer work.
    /// In a dry run nothing is written and the outcome says so.
    /// </summary>
    Task<SuggestionOutcome> ApplySuggestionAsync(ReviewTarget pr, SuggestionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Where unsubmitted comments are kept between runs, so nothing written is ever lost.</summary>
public interface IDraftStore
{
    IReadOnlyList<PendingComment> Load(string pullRequestId);

    void Save(string pullRequestId, IReadOnlyList<PendingComment> comments, string summary);

    string LoadSummary(string pullRequestId);
}
