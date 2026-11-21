using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>The result of a write action. <see cref="DryRun"/> is true when nothing was really sent.</summary>
public sealed record ActionOutcome(bool Ok, bool DryRun, string Message);

/// <summary>
/// GitHub actions the inspector can take. The demo implementation never sends anything. Assign the real one to
/// <c>IntegrationServices.Actions</c> at startup.
/// </summary>
public interface IInspectorActions
{
    /// <summary>True when actions are only simulated (demo mode or a dry-run setting).</summary>
    bool IsDryRun { get; }

    /// <summary>Submits an Approve or Request changes review. <paramref name="body"/> may be empty for an approval.</summary>
    Task<ActionOutcome> SubmitReviewAsync(PullRequest pr, ReviewVerdict verdict, string body, CancellationToken cancellationToken);

    /// <summary>The unified diff of the pull request, used as input for Claude. May be empty when unavailable.</summary>
    Task<string> GetDiffAsync(PullRequest pr, CancellationToken cancellationToken);
}

/// <summary>Real actions on top of <see cref="GitHubWriter"/>. <paramref name="dryRun"/> builds the request but does not send it.</summary>
public sealed class GitHubInspectorActions(
    GitHubWriter writer,
    Func<PullRequest, CancellationToken, Task<string>> diff,
    bool dryRun = false) : IInspectorActions
{
    public bool IsDryRun => dryRun;

    public async Task<ActionOutcome> SubmitReviewAsync(PullRequest pr, ReviewVerdict verdict, string body, CancellationToken cancellationToken)
    {
        try
        {
            var text = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
            var result = await writer.SubmitReviewAsync(pr.Repo, pr.Number, verdict, text, dryRun, cancellationToken);
            var what = verdict == ReviewVerdict.Approve ? "Approved" : "Requested changes on";
            return result.Sent
                ? new ActionOutcome(true, false, what + " " + pr.Repo + " #" + pr.Number + ".")
                : new ActionOutcome(true, true, "Dry run: nothing was sent to GitHub.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActionOutcome(false, false, ErrorText.Describe(ex));
        }
    }

    public Task<string> GetDiffAsync(PullRequest pr, CancellationToken cancellationToken) => diff(pr, cancellationToken);
}
