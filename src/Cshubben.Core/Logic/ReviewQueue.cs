using Cshubben.Core.Model;

namespace Cshubben.Core.Logic;

public static class ReviewQueue
{
    /// <summary>Classifies a review-queue PR. Missing info counts as "waiting on you": better too loud than hidden.</summary>
    public static ReviewQueueState StateOf(ReviewInfo? info)
    {
        if (info?.MyReviewAt is not { } reviewedAt) return ReviewQueueState.WaitingOnYou;
        if (info.LastCommitAt > reviewedAt) return ReviewQueueState.UpdatedSinceReview;
        return info.MyDecision switch
        {
            ReviewDecision.Approved => ReviewQueueState.ApprovedByYou,
            ReviewDecision.ChangesRequested => ReviewQueueState.WaitingOnAuthor,
            _ => ReviewQueueState.WaitingOnYou,
        };
    }

    /// <summary>
    /// How the review request reached the viewer: "You" when asked personally (even if a team was asked too),
    /// "Team: name" when only a team was asked, null when that is not known.
    /// </summary>
    public static string? RequestLabel(ReviewInfo? info)
    {
        if (info is null) return null;
        if (info.RequestedDirectly) return "You";
        return info.RequestedTeams is { Count: > 0 } teams ? "Team: " + string.Join(", ", teams) : "Team";
    }

    /// <summary>Sort key: what needs the viewer first, updated-since-review before fresh requests (as in My-hub).</summary>
    public static int Rank(ReviewQueueState state) => state switch
    {
        ReviewQueueState.UpdatedSinceReview => 0,
        ReviewQueueState.WaitingOnYou => 1,
        ReviewQueueState.WaitingOnAuthor => 2,
        _ => 3,
    };

    public static string Label(ReviewQueueState state) => state switch
    {
        ReviewQueueState.UpdatedSinceReview => "Updated since your review",
        ReviewQueueState.WaitingOnYou => "Waiting on you",
        ReviewQueueState.WaitingOnAuthor => "Waiting on author",
        _ => "Approved by you",
    };
}
