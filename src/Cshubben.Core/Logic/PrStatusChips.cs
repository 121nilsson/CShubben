using Cshubben.Core.Model;

namespace Cshubben.Core.Logic;

public enum StatusChipKind { Approved, Ci, Threads }

/// <summary>One small status shown on a pull request row. <paramref name="Count"/> is the number of threads for <see cref="StatusChipKind.Threads"/>.</summary>
public sealed record StatusChip(StatusChipKind Kind, string Text, Tone Tone, int Count = 0);

/// <summary>
/// The small status chips under a pull request in a list. They fill in what the next-action pill leaves out: a PR that is
/// approved and green but has an open thread shows "Resolve 1 thread" as the action, and these chips say it is otherwise fine.
/// Only your own pull requests get them, and "ready to merge" gets none (its pill already says everything is fine).
/// The list shows <see cref="StatusChip.Text"/>, or in a narrow list just an icon (and the count for threads).
/// </summary>
public static class PrStatusChips
{
    public static IReadOnlyList<StatusChip> For(PullRequest pr, NextAction action)
    {
        if (pr.Role != PrRole.Authored || pr.IsDraft || action.Kind == NextActionKind.Merge) return Array.Empty<StatusChip>();

        var chips = new List<StatusChip>();
        if (pr.Review == ReviewDecision.Approved) chips.Add(new StatusChip(StatusChipKind.Approved, "Approved", Tone.Success));
        if (pr.Ci == CiState.Success) chips.Add(new StatusChip(StatusChipKind.Ci, "CI passing", Tone.Success));

        // "Resolve 1 thread" already says it; the chip is for when another action (CI, conflicts) comes first.
        if (pr.UnresolvedThreads > 0 && action.Kind != NextActionKind.ResolveThreads)
            chips.Add(new StatusChip(StatusChipKind.Threads,
                pr.UnresolvedThreads + (pr.UnresolvedThreads == 1 ? " open thread" : " open threads"), Tone.Warning, pr.UnresolvedThreads));
        return chips;
    }
}
