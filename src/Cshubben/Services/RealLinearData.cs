using System.Globalization;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Model;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>
/// The real <see cref="ILinearData"/> on top of <see cref="LinearClient"/>. The API key is resolved on every request from the
/// environment or the secret store, so a key saved in Settings works without a restart. Recently merged pull requests come from a
/// GitHub search (<c>is:pr is:merged author:@me merged:&gt;=date</c>) through the live GitHub connection, when there is one.
/// </summary>
public sealed class RealLinearData : ILinearData
{
    /// <summary>How far back "recently done" and "recently merged" reach.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(14);

    private const int MaxMergedPrs = 30;

    private readonly ISecretStore _secrets;
    private readonly LinearClient _client;
    private readonly Func<GitHubGraphQl?> _graphQl;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _linked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _identifiersById = new(StringComparer.Ordinal);
    private IReadOnlyList<LinearTeam>? _teams;

    public RealLinearData(ISecretStore secrets, HttpClient http, Func<GitHubGraphQl?> graphQl, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(graphQl);
        _secrets = secrets;
        _graphQl = graphQl;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _client = new LinearClient(http, () => LinearClient.ResolveApiKey(_secrets));
    }

    public bool IsConnected => LinearClient.ResolveApiKey(_secrets) is not null;

    public bool IsDemo => false;

    /// <summary>The GitHub search for pull requests I merged since <paramref name="since"/>. Pure, so it is easy to check.</summary>
    public static string MergedSearch(DateTimeOffset since) =>
        "is:pr is:merged author:@me archived:false merged:>=" + since.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<LinearBoard> LoadAsync(HubSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var since = _clock() - RecentWindow;
        var issuesTask = _client.GetMyIssuesAsync(100, cancellationToken);
        var doneTask = _client.GetRecentlyCompletedAsync(since, 50, cancellationToken);
        var mergedTask = LoadMergedAsync(since, cancellationToken);
        await Task.WhenAll(issuesTask, doneTask, mergedTask).ConfigureAwait(false);

        var issues = await issuesTask.ConfigureAwait(false);
        var done = await doneTask.ConfigureAwait(false);
        Remember(issues);
        Remember(done);

        var prs = new List<ReconPr>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pr in snapshot.Authored)
        {
            if (seen.Add(pr.Repo + "#" + pr.Number.ToString(CultureInfo.InvariantCulture))) prs.Add(ReconPr.FromOpen(pr));
        }

        foreach (var pr in await mergedTask.ConfigureAwait(false))
        {
            if (seen.Add(pr.Repo + "#" + pr.Number.ToString(CultureInfo.InvariantCulture))) prs.Add(pr);
        }

        return new LinearBoard(issues, done, prs);
    }

    /// <summary>Merged PRs are a nice-to-have: any failure gives an empty list instead of failing the whole Linear page.</summary>
    private async Task<IReadOnlyList<ReconPr>> LoadMergedAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var graphQl = _graphQl();
        if (graphQl is null) return Array.Empty<ReconPr>();
        try
        {
            var merged = await new GitHubPullRequests(graphQl)
                .SearchAsync(MergedSearch(since), PrRole.Authored, MaxMergedPrs, 1, cancellationToken)
                .ConfigureAwait(false);
            // A merge touches the pull request last, so its update time stands in for the merge time.
            return merged.Select(pr => new ReconPr(pr.Repo, pr.Number, pr.Title, pr.Url, pr.HeadBranch, true, pr.UpdatedAt)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Array.Empty<ReconPr>();
        }
    }

    public string? IdentifierFor(PullRequest pr)
    {
        lock (_lock)
        {
            if (_linked.TryGetValue(pr.Url, out var linked)) return linked;
        }

        return LinearLinks.FindIdentifier(pr.Title, pr.HeadBranch);
    }

    public async Task<LinearIssue?> GetIssueAsync(string identifier, CancellationToken cancellationToken)
    {
        var issue = await _client.GetIssueByIdentifierAsync(identifier, cancellationToken).ConfigureAwait(false);
        if (issue is not null) Remember(new[] { issue });
        return issue;
    }

    public async Task<IReadOnlyList<LinearTeam>> GetTeamsAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_teams is not null) return _teams;
        }

        var teams = await _client.GetTeamsAsync(cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            _teams = teams;
        }

        return teams;
    }

    public async Task<LinearIssue> CreateIssueAsync(string teamId, string title, string? description, int? priority, CancellationToken cancellationToken)
    {
        var issue = await _client.CreateIssueAsync(teamId, title, description, priority, null, cancellationToken).ConfigureAwait(false);
        Remember(new[] { issue });
        return issue;
    }

    public async Task LinkPullRequestAsync(string issueId, string url, string title, CancellationToken cancellationToken)
    {
        await _client.LinkPullRequestAsync(issueId, url, title, cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            if (_identifiersById.TryGetValue(issueId, out var identifier)) _linked[url] = identifier;
        }
    }

    /// <summary>Moves the issue to the completed state of its team (the one Linear lists first, normally "Done").</summary>
    public async Task MarkDoneAsync(LinearIssue issue, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var teams = await GetTeamsAsync(cancellationToken).ConfigureAwait(false);
        var team = teams.FirstOrDefault(t => string.Equals(t.Key, issue.TeamKey, StringComparison.OrdinalIgnoreCase));
        if (team is null) throw new LinearException("Could not find the Linear team " + issue.TeamKey + ".");

        var states = await _client.GetTeamStatesAsync(team.Id, cancellationToken).ConfigureAwait(false);
        var target = LinearMapper.PickDoneState(states);
        if (target is null) throw new LinearException("The Linear team " + team.Key + " has no completed state.");

        await _client.UpdateIssueStateAsync(issue.Id, target.Id, cancellationToken).ConfigureAwait(false);
    }

    private void Remember(IEnumerable<LinearIssue> issues)
    {
        lock (_lock)
        {
            foreach (var issue in issues) _identifiersById[issue.Id] = issue.Identifier;
        }
    }
}
