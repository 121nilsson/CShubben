using Cshubben.Core.Features.Review;
using Cshubben.Core.Model;

namespace Cshubben.Ui.Review;

/// <summary>Everything the review workspace needs after loading, so the component starts with real state.</summary>
sealed record ReviewBundle(
    ReviewTarget Target,
    PullRequest? Pr,
    IReviewData Data,
    IDraftStore Drafts,
    ReviewContext Context,
    PullRequestDiff Full,
    PullRequestDiff? Since,
    ReviewRangeInfo Range,
    IReadOnlyList<ReviewThread> Threads,
    ReviewSession Session,
    string? InitialFile)
{
    /// <summary>Loads context, diff and threads in parallel; the since-last-review diff is fetched only when there is one.</summary>
    public static async Task<ReviewBundle> LoadAsync(
        IReviewData data,
        IDraftStore drafts,
        ReviewTarget target,
        PullRequest? pr,
        string? initialFile,
        CancellationToken cancellationToken)
    {
        var contextTask = data.GetContextAsync(target, cancellationToken);
        var diffTask = data.GetPullRequestFilesAsync(target, cancellationToken);
        var threadsTask = data.GetThreadsAsync(target, cancellationToken);
        await Task.WhenAll(contextTask, diffTask, threadsTask).ConfigureAwait(false);
        var context = await contextTask.ConfigureAwait(false);
        var full = await diffTask.ConfigureAwait(false);
        var threads = await threadsTask.ConfigureAwait(false);

        var range = ReviewRange.SinceLastReview(context.Commits, context.MyLastReviewedSha);
        PullRequestDiff? since = null;
        if (range.Kind == ReviewRangeKind.SinceLastReview && range.BaseSha is not null)
        {
            try
            {
                since = await data.GetCompareFilesAsync(target, range.BaseSha, range.HeadSha, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                since = null;
            }
        }

        var session = new ReviewSession(target.Id)
            .WithServerViewed(full.Viewed)
            .WithSummary(drafts.LoadSummary(target.Id));
        foreach (var pending in drafts.Load(target.Id)) session = session.AddPending(pending);

        return new ReviewBundle(target, pr, data, drafts, context, full, since, range, threads, session, initialFile);
    }
}
