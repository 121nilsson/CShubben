namespace Cshubben.Core.Model;

public enum CiState { Unknown, Pending, Success, Failure }

public enum ReviewDecision { None, ReviewRequired, Approved, ChangesRequested }

public enum MergeState { Unknown, Mergeable, Conflicting }

public enum PrRole { Authored, ReviewRequested }

/// <summary>A pull request as shown in lists and in the inspector.</summary>
public sealed record PullRequest(
    string Id,
    PrRole Role,
    string Repo,
    int Number,
    string Title,
    string Url,
    string Author,
    bool IsDraft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Additions,
    int Deletions,
    int ChangedFiles,
    ReviewDecision Review,
    CiState Ci,
    MergeState Merge,
    int Comments,
    int UnresolvedThreads,
    string HeadBranch,
    string Description = "")
{
    /// <summary>T-shirt size from changed lines.</summary>
    public string SizeLabel => (Additions + Deletions) switch
    {
        <= 50 => "XS",
        <= 200 => "S",
        <= 500 => "M",
        <= 1000 => "L",
        _ => "XL",
    };
}

/// <summary>Extra facts about a PR in the review queue.</summary>
/// <param name="PrId">Id of the pull request.</param>
/// <param name="RequestedDirectly">True when requested from the viewer personally, false when via a team.</param>
/// <param name="MyReviewAt">When the viewer last reviewed, if ever.</param>
/// <param name="LastCommitAt">Time of the newest commit.</param>
/// <param name="MyDecision">The viewer's latest review decision, if any.</param>
/// <param name="RequestedTeams">Teams with a pending review request on the PR (names as GitHub shows them); null when unknown.</param>
public sealed record ReviewInfo(
    string PrId,
    bool RequestedDirectly,
    DateTimeOffset? MyReviewAt,
    DateTimeOffset LastCommitAt,
    ReviewDecision MyDecision,
    IReadOnlyList<string>? RequestedTeams = null);

/// <summary>Where a review-queue PR stands from the viewer's point of view.</summary>
public enum ReviewQueueState
{
    /// <summary>The viewer has not reviewed it yet.</summary>
    WaitingOnYou,

    /// <summary>The viewer reviewed, but new commits arrived since.</summary>
    UpdatedSinceReview,

    /// <summary>The viewer asked for changes; the author is working on it.</summary>
    WaitingOnAuthor,

    /// <summary>The viewer approved and nothing changed since.</summary>
    ApprovedByYou,
}

public enum FeedKind
{
    ReviewRequested,
    Approved,
    ChangesRequested,
    Commented,
    CiFailed,
    CiPassed,
    Merged,
    Opened,
    Pushed,
    Deployed,
}

/// <summary>One event in the activity feed.</summary>
public sealed record FeedItem(
    string Id,
    FeedKind Kind,
    string Repo,
    string Actor,
    int? Number,
    string? Title,
    string Detail,
    DateTimeOffset At,
    bool Personal,
    bool IsBot);

/// <summary>Everything the UI needs, loaded in one go.</summary>
public sealed record HubSnapshot(
    string Viewer,
    IReadOnlyList<PullRequest> Authored,
    IReadOnlyList<PullRequest> Reviews,
    IReadOnlyDictionary<string, ReviewInfo> ReviewInfos,
    IReadOnlyList<FeedItem> Feed,
    DateTimeOffset At)
{
    public IEnumerable<PullRequest> All => Authored.Concat(Reviews);

    public PullRequest? Find(string? id) => id is null ? null : All.FirstOrDefault(p => p.Id == id);
}
