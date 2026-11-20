using System.Globalization;
using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>How the pull request is merged. Independent of the GitHub feature's own enum; the app maps between them.</summary>
public enum MergeMethodChoice { Squash, Merge, Rebase }

public static class MergeMethods
{
    public static IReadOnlyList<MergeMethodChoice> All { get; } =
        new[] { MergeMethodChoice.Squash, MergeMethodChoice.Merge, MergeMethodChoice.Rebase };

    public static string Label(MergeMethodChoice method) => method switch
    {
        MergeMethodChoice.Squash => "Squash and merge",
        MergeMethodChoice.Rebase => "Rebase and merge",
        _ => "Create a merge commit",
    };

    public static string Short(MergeMethodChoice method) => method switch
    {
        MergeMethodChoice.Squash => "Squash",
        MergeMethodChoice.Rebase => "Rebase",
        _ => "Merge commit",
    };

    public static string Describe(MergeMethodChoice method) => method switch
    {
        MergeMethodChoice.Squash => "All commits become one commit on the base branch.",
        MergeMethodChoice.Rebase => "Each commit is replayed on the base branch. No merge commit.",
        _ => "All commits are kept and a merge commit is added.",
    };
}

public enum BlockerKind { Draft, Conflicts, ChangesRequested, ChecksFailing, ChecksPending, ReviewRequired, UnresolvedThreads }

/// <summary>Something that argues against merging. Hard blockers cannot be overridden, soft ones can.</summary>
public sealed record MergeBlocker(BlockerKind Kind, string Text)
{
    public bool IsHard => Kind is BlockerKind.Draft or BlockerKind.Conflicts or BlockerKind.ChangesRequested or BlockerKind.ChecksFailing;
}

/// <summary>What is known about a pull request right before it is merged.</summary>
public sealed record MergeFacts(
    bool IsDraft,
    bool HasConflicts,
    PipelineState Checks,
    int ChecksPassed,
    int ChecksTotal,
    IReadOnlyList<string> Approvers,
    IReadOnlyList<string> ChangesRequestedBy,
    int UnresolvedThreads,
    bool ReviewRequired,
    string HeadSha)
{
    /// <summary>Combines the pull request, its fresh checks and its fresh conversation.</summary>
    public static MergeFacts From(PullRequest pr, PipelineTimeline timeline, PrConversation conversation)
    {
        var approvers = conversation.Approvers;
        return new MergeFacts(
            pr.IsDraft,
            pr.Merge == MergeState.Conflicting,
            timeline.Overall,
            timeline.Passed,
            timeline.Total,
            approvers,
            conversation.ChangesRequestedBy,
            conversation.UnresolvedThreads,
            pr.Review == ReviewDecision.ReviewRequired && approvers.Count == 0,
            conversation.HeadSha);
    }
}

/// <summary>The merge facts, the blockers they imply and the lines shown in the confirmation.</summary>
public sealed record MergeReadiness(MergeFacts Facts, IReadOnlyList<MergeBlocker> Blockers)
{
    public bool CanMerge => !Blockers.Any(b => b.IsHard);

    /// <summary>Mergeable, but only after the user accepts the soft blockers ("Merge anyway").</summary>
    public bool NeedsOverride => CanMerge && Blockers.Count > 0;

    public string ShortSha => PipelineFormat.ShortSha(Facts.HeadSha);

    public string ChecksText => Facts.Checks switch
    {
        PipelineState.None => "No checks",
        PipelineState.Passing => Facts.ChecksTotal == 1 ? "The check passed" : "All " + Facts.ChecksTotal.ToString(CultureInfo.InvariantCulture) + " checks passed",
        PipelineState.Failing => (Facts.ChecksTotal - Facts.ChecksPassed).ToString(CultureInfo.InvariantCulture) + " of " + Facts.ChecksTotal.ToString(CultureInfo.InvariantCulture) + " checks not passing",
        _ => "Checks still running (" + Facts.ChecksPassed.ToString(CultureInfo.InvariantCulture) + " of " + Facts.ChecksTotal.ToString(CultureInfo.InvariantCulture) + " passed)",
    };

    public bool ChecksGreen => Facts.Checks is PipelineState.Passing or PipelineState.None;

    public string ApprovalsText => Facts.Approvers.Count == 0
        ? "No approvals"
        : "Approved by " + string.Join(", ", Facts.Approvers);

    public bool Approved => Facts.Approvers.Count > 0 && Facts.ChangesRequestedBy.Count == 0;

    public string ThreadsText => Facts.UnresolvedThreads == 0
        ? "No open threads"
        : PipelineFormat.Count(Facts.UnresolvedThreads, "open thread");

    /// <summary>Hard blockers first, then soft ones, in a fixed order.</summary>
    public static MergeReadiness Evaluate(MergeFacts facts)
    {
        var blockers = new List<MergeBlocker>();
        if (facts.IsDraft) blockers.Add(new MergeBlocker(BlockerKind.Draft, "This is a draft. Mark it ready first."));
        if (facts.HasConflicts) blockers.Add(new MergeBlocker(BlockerKind.Conflicts, "The branch conflicts with the base branch."));
        if (facts.ChangesRequestedBy.Count > 0)
            blockers.Add(new MergeBlocker(BlockerKind.ChangesRequested, "Changes requested by " + string.Join(", ", facts.ChangesRequestedBy) + "."));
        if (facts.Checks == PipelineState.Failing) blockers.Add(new MergeBlocker(BlockerKind.ChecksFailing, "Checks are failing."));
        if (facts.Checks is PipelineState.Running or PipelineState.Queued) blockers.Add(new MergeBlocker(BlockerKind.ChecksPending, "Checks are still running."));
        if (facts.ReviewRequired) blockers.Add(new MergeBlocker(BlockerKind.ReviewRequired, "A review is required and nobody has approved yet."));
        if (facts.UnresolvedThreads > 0)
            blockers.Add(new MergeBlocker(BlockerKind.UnresolvedThreads, PipelineFormat.Count(facts.UnresolvedThreads, "review thread") + " still open."));
        return new MergeReadiness(facts, blockers);
    }
}

/// <summary>Where the merge dialog is.</summary>
public enum MergeStep { Closed, Confirming, Submitting, Done, Failed }

/// <summary>
/// The states of the two-step merge. The first click opens the confirmation, the second one merges. While submitting,
/// every further confirm is ignored, so the request can never be sent twice. Pure, so it is easy to test.
/// </summary>
public sealed record MergeFlowState(MergeStep Step, MergeMethodChoice Method, string Message)
{
    public static MergeFlowState Initial { get; } = new(MergeStep.Closed, MergeMethodChoice.Squash, string.Empty);

    public bool IsOpen => Step is MergeStep.Confirming or MergeStep.Submitting or MergeStep.Failed;

    /// <summary>True when pressing the confirm button may send the request.</summary>
    public bool CanConfirm => Step is MergeStep.Confirming or MergeStep.Failed;

    public MergeFlowState Open() => Step == MergeStep.Closed ? this with { Step = MergeStep.Confirming, Message = string.Empty } : this;

    public MergeFlowState Choose(MergeMethodChoice method) => CanConfirm ? this with { Method = method } : this;

    public MergeFlowState Cancel() => CanConfirm ? this with { Step = MergeStep.Closed, Message = string.Empty } : this;

    /// <summary>Moves to Submitting. Returns the same state when a submit is not allowed (already submitting, done or closed).</summary>
    public MergeFlowState BeginSubmit() => CanConfirm ? this with { Step = MergeStep.Submitting, Message = string.Empty } : this;

    public MergeFlowState Succeed(string message) => Step == MergeStep.Submitting ? this with { Step = MergeStep.Done, Message = message } : this;

    public MergeFlowState Fail(string message) => Step == MergeStep.Submitting ? this with { Step = MergeStep.Failed, Message = message } : this;
}
