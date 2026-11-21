using Cshubben.Core.Model;

namespace Cshubben.Core.Logic;

/// <summary>A PR together with the action it needs.</summary>
public sealed record Attention(PullRequest Pr, NextAction Action);

/// <summary>The "what needs me right now" picture shown on the start page.</summary>
public sealed record OverviewModel(
    IReadOnlyList<Attention> ReadyToMerge,
    IReadOnlyList<Attention> NeedsAction,
    IReadOnlyList<Attention> ReviewsForYou,
    int Waiting)
{
    /// <summary>Total number of things that need the viewer.</summary>
    public int NeedsYou => ReadyToMerge.Count + NeedsAction.Count + ReviewsForYou.Count;

    public static OverviewModel Compute(HubSnapshot snapshot)
    {
        var authored = snapshot.Authored
            .Select(p => new Attention(p, NextAction.For(p)))
            .ToList();

        var ready = authored.Where(a => a.Action.Kind == NextActionKind.Merge).ToList();
        var needs = authored
            .Where(a => a.Action.IsActionable && a.Action.Kind != NextActionKind.Merge)
            .OrderBy(a => Severity(a.Action.Tone))
            .ThenByDescending(a => a.Pr.UpdatedAt)
            .ToList();

        var reviews = snapshot.Reviews
            .Where(p => !p.IsDraft)
            .Select(p =>
            {
                snapshot.ReviewInfos.TryGetValue(p.Id, out var info);
                return new Attention(p, NextAction.For(p, info));
            })
            .Where(a => a.Action.IsActionable)
            .OrderBy(a => a.Action.Kind == NextActionKind.ReReview ? 0 : 1)
            .ThenByDescending(a => a.Pr.UpdatedAt)
            .ToList();

        var waiting = authored.Count(a => !a.Action.IsActionable) + snapshot.Reviews.Count(p => !p.IsDraft) - reviews.Count;
        return new OverviewModel(ready, needs, reviews, waiting);
    }

    private static int Severity(Tone tone) => tone switch
    {
        Tone.Danger => 0,
        Tone.Warning => 1,
        Tone.Accent => 2,
        _ => 3,
    };
}
