using Cshubben.Core.Features.Linear;
using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Features.Pipeline;

namespace Cshubben.Core.Composition;

/// <summary>
/// Turns Drift and Linear results into the plain values the notification feature compares. This is the one place that
/// knows both sides, so the features themselves stay independent of each other.
/// </summary>
public static class WatchSources
{
    /// <summary>One entry per repo that could be read. Unreadable repos are left out, so an outage never looks like a recovery or a new failure.</summary>
    public static IReadOnlyList<WatchedRepoState> FromDrift(DriftReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Rows
            .Where(r => r.Health != RepoHealth.Unavailable)
            .Select(r => new WatchedRepoState(
                r.Repo,
                r.FailingWorkflows.Count > 0,
                string.Join(",", r.FailingWorkflows.Select(w => WatchDiff.N(w.Id)).Order(StringComparer.Ordinal)),
                r.Headline,
                r.FailedDeploys.Select(d => new WatchedDeployFailure(d.Environment, WatchDiff.N(d.Id))).ToList()))
            .ToList();
    }

    /// <summary>
    /// My open issues and the out-of-sync items worth a toast: merged pull request with the issue still open, and issue done with the
    /// pull request still open. "PR without an issue" and "issue without a PR" stay on the Linear page only, they are too common to interrupt for.
    /// </summary>
    public static WatchedLinear FromLinear(IEnumerable<LinearIssue> open, IEnumerable<LinearIssue> done, IEnumerable<ReconPr> prs)
    {
        var openList = open.ToList();
        var all = openList.Concat(done).ToList();
        var sync = LinearReconciliation.Compute(prs, all)
            .Where(i => i.Kind is ReconKind.MergedButIssueOpen or ReconKind.IssueDoneButPrOpen && i.Issue is not null && i.Pr is not null)
            .Select(i => new WatchedOutOfSync(
                i.Kind + "|" + i.Issue!.Identifier + "|" + i.Pr!.Repo + "#" + WatchDiff.N(i.Pr.Number),
                i.Issue.Identifier,
                i.Text,
                i.Pr.Url))
            .ToList();
        return new WatchedLinear(openList.Select(i => new WatchedIssue(i.Identifier, i.Title, i.Url)).ToList(), sync);
    }
}
