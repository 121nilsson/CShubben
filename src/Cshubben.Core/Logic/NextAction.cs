using Cshubben.Core.Model;

namespace Cshubben.Core.Logic;

public enum NextActionKind
{
    ResolveConflicts,
    FixCi,
    AddressChanges,
    ResolveThreads,
    MarkReady,
    Merge,
    WaitForCi,
    WaitForReview,
    StartReview,
    ReReview,
    WaitForAuthor,
    NothingToDo,
}

public enum Tone { Neutral, Accent, Success, Warning, Danger, Merged }

/// <summary>
/// The single most useful thing to do with a PR right now. Drives the primary button in the inspector so the
/// viewer never has to read the badges to work out what is next.
/// </summary>
public sealed record NextAction(NextActionKind Kind, string Label, string Reason, Tone Tone, bool IsActionable)
{
    /// <summary>Computes the next action for the viewer. <paramref name="info"/> is only used for review-queue PRs.</summary>
    public static NextAction For(PullRequest pr, ReviewInfo? info = null) =>
        pr.Role == PrRole.Authored ? ForAuthored(pr) : ForReviewer(pr, info);

    private static NextAction ForAuthored(PullRequest pr)
    {
        if (pr.Merge == MergeState.Conflicting)
            return new(NextActionKind.ResolveConflicts, "Resolve conflicts", "The branch conflicts with the base branch.", Tone.Danger, true);
        if (pr.Ci == CiState.Failure)
            return new(NextActionKind.FixCi, "Fix failing CI", "Checks are failing, so nobody can merge this yet.", Tone.Danger, true);
        if (pr.Review == ReviewDecision.ChangesRequested)
            return new(NextActionKind.AddressChanges, "Address review feedback", "A reviewer asked for changes.", Tone.Warning, true);
        if (pr.UnresolvedThreads > 0)
        {
            var n = pr.UnresolvedThreads;
            return new(NextActionKind.ResolveThreads, n == 1 ? "Resolve 1 thread" : $"Resolve {n} threads",
                "There are open review conversations.", Tone.Warning, true);
        }
        if (pr.IsDraft)
        {
            return pr.Ci == CiState.Success
                ? new(NextActionKind.MarkReady, "Mark ready for review", "Checks pass. This is still a draft.", Tone.Accent, true)
                : new(NextActionKind.WaitForCi, "Waiting for CI", "Checks are still running on this draft.", Tone.Neutral, false);
        }
        if (pr.Review == ReviewDecision.Approved && pr.Ci == CiState.Success && pr.Merge != MergeState.Conflicting)
            return new(NextActionKind.Merge, "Merge pull request", "Approved, checks pass, no conflicts.", Tone.Success, true);
        if (pr.Ci == CiState.Pending)
            return new(NextActionKind.WaitForCi, "Waiting for CI", "Checks are still running.", Tone.Neutral, false);
        if (pr.Review is ReviewDecision.ReviewRequired or ReviewDecision.None)
            return new(NextActionKind.WaitForReview, "Waiting for review", "Nobody has approved yet.", Tone.Neutral, false);
        return new(NextActionKind.NothingToDo, "Nothing to do", "No action needed right now.", Tone.Neutral, false);
    }

    private static NextAction ForReviewer(PullRequest pr, ReviewInfo? info) =>
        ReviewQueue.StateOf(info) switch
        {
            ReviewQueueState.UpdatedSinceReview =>
                new(NextActionKind.ReReview, "Re-review changes", "New commits arrived after your review.", Tone.Accent, true),
            ReviewQueueState.WaitingOnYou =>
                new(NextActionKind.StartReview, "Start review", "Your review has been requested.", Tone.Accent, true),
            ReviewQueueState.WaitingOnAuthor =>
                new(NextActionKind.WaitForAuthor, "Waiting on author", "You asked for changes.", Tone.Neutral, false),
            _ => new(NextActionKind.NothingToDo, "Approved by you", "Nothing changed since your approval.", Tone.Success, false),
        };
}
