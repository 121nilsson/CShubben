using Cshubben.Core.Data;
using Cshubben.Core.Features.Review;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Services;
using Cshubben.Ui.Review;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// The code review page. The coordinator sets the statics before navigating here:
/// <c>Data</c> (the adapter), <c>PullRequestId</c> (which PR; defaults to the first review request), <c>InitialFile</c>
/// (the file to open first) and optionally <c>Snapshot</c> (where to look the PR up) and <c>Drafts</c> (local draft storage).
/// Render it with <c>Component&lt;ReviewPage&gt;()</c>.
/// </summary>
sealed class ReviewPage : Component
{
    /// <summary>Where review data comes from. The demo adapter until the composition root assigns the real one.</summary>
    public static IReviewData Data { get; set; } = new DemoReviewData();

    /// <summary>The pull request to review (its id in the snapshot). Null picks the first review request.</summary>
    public static string? PullRequestId { get; set; }

    /// <summary>Path of the file to show first, or null to start at the first unviewed file.</summary>
    public static string? InitialFile { get; set; }

    /// <summary>The current hub data, used to resolve <see cref="PullRequestId"/>. Null falls back to the demo data.</summary>
    public static HubSnapshot? Snapshot { get; set; }

    /// <summary>Where pending comments are kept between sessions.</summary>
    public static IDraftStore Drafts { get; set; } = new InMemoryDraftStore();

    public override Element Render()
    {
        var snap = Snapshot ?? new DemoData().Build();
        var pr = Resolve(snap, PullRequestId);
        var data = Data;
        var drafts = Drafts;
        var initial = InitialFile;
        var target = pr is null ? null : new ReviewTarget(pr.Id, pr.Repo, pr.Number, pr.Url, pr.Title);

        // Hooks run unconditionally; the fetcher returns null when there is nothing to review.
        var resource = UseResource<ReviewBundle?>(
            async ct => target is null ? null : await ReviewBundle.LoadAsync(data, drafts, target, pr, initial, ct),
            new object?[] { data, drafts, target?.Id, initial },
            new ResourceOptions(CacheTime: TimeSpan.Zero));

        if (target is null)
            return Kit.EmptyNote("There is no pull request to review.");

        if (resource is AsyncValue<ReviewBundle?>.Error error)
            return Kit.Banner(Tone.Danger, "Could not load " + target.Label, error.Exception.Message);

        ReviewBundle? bundle = resource switch
        {
            AsyncValue<ReviewBundle?>.Data d => d.Value,
            AsyncValue<ReviewBundle?>.Reloading r => r.Previous,
            _ => null,
        };
        if (bundle is null)
            return Kit.LoadingState("Loading " + target.Label);

        return Component<ReviewWorkspace, ReviewBundle>(bundle).WithKey(target.Id);
    }

    private static PullRequest? Resolve(HubSnapshot snap, string? id)
    {
        if (id is not null)
        {
            var found = snap.All.FirstOrDefault(p => p.Id == id);
            if (found is not null) return found;
        }
        return snap.Reviews.FirstOrDefault() ?? snap.Authored.FirstOrDefault();
    }
}
