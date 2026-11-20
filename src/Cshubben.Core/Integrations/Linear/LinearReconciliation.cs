using Cshubben.Core.Model;

namespace Cshubben.Core.Integrations.Linear;

/// <summary>A pull request as seen by the reconciliation: open or merged.</summary>
public sealed record ReconPr(string Repo, int Number, string Title, string Url, string Branch, bool Merged, DateTimeOffset? MergedAt = null)
{
    public static ReconPr FromOpen(PullRequest pr) => new(pr.Repo, pr.Number, pr.Title, pr.Url, pr.HeadBranch, false);
}

public enum ReconKind
{
    /// <summary>The PR is merged but the issue is still open.</summary>
    MergedButIssueOpen,

    /// <summary>The issue is done (or canceled) but the PR is still open.</summary>
    IssueDoneButPrOpen,

    /// <summary>An open PR with no Linear identifier in its title or branch.</summary>
    PrWithoutIssue,

    /// <summary>The issue is in progress but no PR mentions it.</summary>
    InProgressWithoutPr,
}

public sealed record ReconItem(ReconKind Kind, ReconPr? Pr, LinearIssue? Issue, string Text);

/// <summary>Finds PRs and Linear issues that disagree. Pure.</summary>
public static class LinearReconciliation
{
    /// <summary>
    /// <paramref name="prs"/> are the open and recently merged PRs; <paramref name="issues"/> are the known issues
    /// (open ones assigned to me plus any fetched by identifier). A PR whose identifier is not among the issues is not flagged,
    /// because the issue may simply not have been loaded.
    /// </summary>
    public static IReadOnlyList<ReconItem> Compute(IEnumerable<ReconPr> prs, IEnumerable<LinearIssue> issues)
    {
        var prList = prs.ToList();
        var byIdentifier = new Dictionary<string, LinearIssue>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues) byIdentifier[issue.Identifier] = issue;

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(int Order, int Number, ReconItem Item)>();

        foreach (var pr in prList)
        {
            var identifier = LinearLinks.FindIdentifier(pr.Title, pr.Branch);
            if (identifier is null)
            {
                if (!pr.Merged)
                {
                    result.Add((2, pr.Number, new ReconItem(
                        ReconKind.PrWithoutIssue,
                        pr,
                        null,
                        pr.Repo + "#" + pr.Number + " has no Linear issue in its title or branch name.")));
                }

                continue;
            }

            referenced.Add(identifier);
            if (!byIdentifier.TryGetValue(identifier, out var linked)) continue;

            if (pr.Merged && !linked.IsDone)
            {
                result.Add((0, pr.Number, new ReconItem(
                    ReconKind.MergedButIssueOpen,
                    pr,
                    linked,
                    "PR #" + pr.Number + " is merged but " + linked.Identifier + " is still \"" + linked.StateName + "\".")));
            }
            else if (!pr.Merged && linked.IsDone)
            {
                result.Add((1, pr.Number, new ReconItem(
                    ReconKind.IssueDoneButPrOpen,
                    pr,
                    linked,
                    "PR #" + pr.Number + " is still open but " + linked.Identifier + " is \"" + linked.StateName + "\".")));
            }
        }

        foreach (var issue in byIdentifier.Values)
        {
            if (issue.IsInProgress && !referenced.Contains(issue.Identifier))
            {
                result.Add((3, 0, new ReconItem(
                    ReconKind.InProgressWithoutPr,
                    null,
                    issue,
                    issue.Identifier + " is \"" + issue.StateName + "\" but no pull request mentions it.")));
            }
        }

        return result
            .OrderBy(r => r.Order)
            .ThenByDescending(r => r.Number)
            .ThenBy(r => r.Item.Issue?.Identifier, StringComparer.Ordinal)
            .Select(r => r.Item)
            .ToList();
    }
}
