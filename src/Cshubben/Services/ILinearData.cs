using Cshubben.Core.Features.Linear;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>
/// What the Linear page shows. <paramref name="Issues"/> are my open issues, <paramref name="RecentlyDone"/> the ones I finished
/// lately, <paramref name="Prs"/> my open and recently merged pull requests as the reconciliation sees them.
/// </summary>
public sealed record LinearBoard(
    IReadOnlyList<LinearIssue> Issues,
    IReadOnlyList<LinearIssue> RecentlyDone,
    IReadOnlyList<ReconPr> Prs);

/// <summary>
/// The Linear data and write actions used by the Linear page and the inspector. The real implementation wraps
/// <see cref="LinearClient"/>; <see cref="DemoLinearData"/> is an in-memory one. Assign it to
/// <c>IntegrationServices.Linear</c> at startup.
/// </summary>
public interface ILinearData
{
    /// <summary>False when no API key is set. The page then shows the connect-your-account state.</summary>
    bool IsConnected { get; }

    bool IsDemo { get; }

    /// <summary>My issues, recently done issues and the PRs to reconcile them with. <paramref name="snapshot"/> holds my open PRs.</summary>
    Task<LinearBoard> LoadAsync(HubSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>The issue identifier of a pull request (from its title or branch), or null. Real implementations use <see cref="LinearLinks"/>.</summary>
    string? IdentifierFor(PullRequest pr);

    /// <summary>Null when the issue does not exist.</summary>
    Task<LinearIssue?> GetIssueAsync(string identifier, CancellationToken cancellationToken);

    Task<IReadOnlyList<LinearTeam>> GetTeamsAsync(CancellationToken cancellationToken);

    Task<LinearIssue> CreateIssueAsync(string teamId, string title, string? description, int? priority, CancellationToken cancellationToken);

    /// <summary>Attaches a GitHub pull request to an issue (https://github.com/ links only).</summary>
    Task LinkPullRequestAsync(string issueId, string url, string title, CancellationToken cancellationToken);

    /// <summary>Moves the issue to its team's completed state.</summary>
    Task MarkDoneAsync(LinearIssue issue, CancellationToken cancellationToken);
}
