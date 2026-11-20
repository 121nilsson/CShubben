using Cshubben.Core.Data;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>
/// Fictional Linear data that lines up with <c>DemoData</c>: a few issues linked to the demo pull requests and one example
/// of each kind of disagreement for the reconciliation. Writes only change this in-memory copy.
/// </summary>
public sealed class DemoLinearData : ILinearData
{
    private readonly object _lock = new();
    private readonly DateTimeOffset _now;
    private readonly List<LinearIssue> _issues = new();
    private readonly List<LinearIssue> _others = new();
    private readonly Dictionary<string, string> _linked = new(StringComparer.Ordinal);
    private int _nextNumber = 260;

    // Which demo PR belongs to which issue. DemoData titles carry no identifiers, so the demo keeps this map.
    private static readonly Dictionary<string, string> PrIssues = new(StringComparer.Ordinal)
    {
        ["a1"] = "API-231",
        ["a2"] = "WEB-305",
        ["a3"] = "API-224",
        ["a4"] = "INF-58",
        ["a6"] = "MOB-77",
        ["a7"] = "API-240",
        ["a8"] = "DOC-12",
        ["r1"] = "API-236",
        ["r3"] = "API-229",
    };

    private static readonly LinearTeam[] Teams =
    {
        new("team-api", "API", "Platform API"),
        new("team-web", "WEB", "Web app"),
        new("team-mob", "MOB", "Mobile"),
        new("team-inf", "INF", "Infrastructure"),
        new("team-doc", "DOC", "Documentation"),
    };

    public DemoLinearData(DateTimeOffset? now = null)
    {
        _now = now ?? DateTimeOffset.UtcNow;
        Seed();
    }

    public bool IsConnected => true;

    public bool IsDemo => true;

    private DateTimeOffset Ago(double hours) => _now - TimeSpan.FromHours(hours);

    private static LinearIssue Make(string identifier, string title, string state, LinearStateType type, string? assignee,
        int priority, string project, string[] labels, DateTimeOffset updated, DateTimeOffset? completed = null)
    {
        var team = identifier.Substring(0, identifier.IndexOf('-'));
        return new LinearIssue(
            "issue-" + identifier.ToLowerInvariant(), identifier, title, state, type, assignee, priority, team, project, labels,
            "https://linear.app/acme/issue/" + identifier, updated, completed);
    }

    private void Seed()
    {
        const string me = DemoData.Viewer;
        _issues.AddRange(new[]
        {
            Make("MOB-77", "Offline queue for field reports", "In Progress", LinearStateType.Started, me, 1, "Field app", new[] { "feature" }, Ago(3)),
            Make("API-231", "Idempotency keys for payment endpoints", "In Review", LinearStateType.Started, me, 2, "Payments", new[] { "payments" }, Ago(2)),
            Make("INF-58", "Split Terraform state per environment", "In Progress", LinearStateType.Started, me, 2, "Platform", new[] { "infra" }, Ago(5)),
            Make("API-224", "Stream the invoice export", "In Progress", LinearStateType.Started, me, 3, "Billing", new[] { "performance" }, Ago(26)),
            Make("WEB-305", "Checkout: fix focus trap in address dialog", "In Review", LinearStateType.Started, me, 3, "Checkout", new[] { "a11y" }, Ago(7)),
            Make("API-219", "Pagination off-by-one in invoice list", "In Review", LinearStateType.Started, me, 2, "Billing", new[] { "bug" }, Ago(20)),
            Make("API-245", "Webhook delivery dashboard", "In Progress", LinearStateType.Started, me, 3, "Platform", Array.Empty<string>(), Ago(70)),
            Make("API-240", "Rate limiting per API key", "Todo", LinearStateType.Unstarted, me, 3, "Platform", new[] { "security" }, Ago(40)),
            Make("WEB-312", "Dark mode for the settings screens", "Todo", LinearStateType.Unstarted, me, 3, "Design system", Array.Empty<string>(), Ago(52)),
            Make("MOB-79", "Dark mode for the field app", "Todo", LinearStateType.Unstarted, me, 4, "Field app", Array.Empty<string>(), Ago(90)),
            Make("API-251", "Evaluate the Postgres 17 upgrade", "Backlog", LinearStateType.Backlog, me, 4, "Platform", new[] { "research" }, Ago(200)),
            Make("WEB-318", "Audit unused CSS", "Backlog", LinearStateType.Backlog, me, 0, "Design system", Array.Empty<string>(), Ago(300)),
            Make("MOB-81", "Sample crash reports", "Backlog", LinearStateType.Backlog, me, 4, "Field app", Array.Empty<string>(), Ago(260)),
            Make("INF-61", "Rotate staging keys", "Backlog", LinearStateType.Backlog, me, 3, "Platform", new[] { "security" }, Ago(150)),
        });

        _others.AddRange(new[]
        {
            Make("API-236", "Replace the retry loop with a backoff library", "In Review", LinearStateType.Started, "maria", 3, "Platform", Array.Empty<string>(), Ago(2)),
            Make("API-229", "Audit log for role changes", "In Progress", LinearStateType.Started, "priya", 2, "Security", new[] { "security" }, Ago(9)),
        });

        _issues.Add(Make("DOC-12", "Document webhook retry behaviour", "Done", LinearStateType.Completed, me, 4, "Docs", Array.Empty<string>(), Ago(30), Ago(30)));
        _issues.Add(Make("API-212", "Retry budget in the HTTP client", "Done", LinearStateType.Completed, me, 3, "Platform", Array.Empty<string>(), Ago(24), Ago(24)));
        _issues.Add(Make("WEB-301", "Fix tab order on checkout", "Done", LinearStateType.Completed, me, 2, "Checkout", new[] { "a11y" }, Ago(50), Ago(50)));
        _issues.Add(Make("INF-52", "Pin Terraform provider versions", "Done", LinearStateType.Completed, me, 3, "Platform", Array.Empty<string>(), Ago(96), Ago(96)));
        _issues.Add(Make("WEB-299", "Drop the IE11 banner", "Canceled", LinearStateType.Canceled, me, 0, "Design system", Array.Empty<string>(), Ago(120), Ago(120)));
    }


    public Task<LinearBoard> LoadAsync(HubSnapshot snapshot, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var open = _issues.Where(i => !i.IsDone).ToList();
            var done = _issues.Where(i => i.IsDone).OrderByDescending(i => i.CompletedAt).ToList();

            var prs = new List<ReconPr>();
            foreach (var pr in snapshot.Authored)
            {
                var identifier = IdentifierFor(pr);
                // The reconciliation reads identifiers from the branch name, so put the demo link there.
                var branch = identifier is null ? pr.HeadBranch : pr.HeadBranch + "/" + identifier.ToLowerInvariant();
                prs.Add(new ReconPr(pr.Repo, pr.Number, pr.Title, pr.Url, branch, false));
            }

            prs.Add(new ReconPr("acme/api", 398, "Fix pagination off-by-one in invoice list", "https://github.com/acme/api/pull/398",
                "fix/pagination/api-219", true, Ago(20)));
            prs.Add(new ReconPr("acme/api", 392, "Retry budget in the HTTP client", "https://github.com/acme/api/pull/392",
                "feat/retry-budget/api-212", true, Ago(26)));

            return Task.FromResult(new LinearBoard(open, done, prs));
        }
    }

    public string? IdentifierFor(PullRequest pr)
    {
        lock (_lock)
        {
            if (_linked.TryGetValue(pr.Url, out var linked)) return linked;
        }

        if (PrIssues.TryGetValue(pr.Id, out var identifier)) return identifier;
        return LinearLinks.FindIdentifier(pr.Title, pr.HeadBranch);
    }

    public Task<LinearIssue?> GetIssueAsync(string identifier, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var found = _issues.Concat(_others).FirstOrDefault(i => string.Equals(i.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(found);
        }
    }

    public Task<IReadOnlyList<LinearTeam>> GetTeamsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LinearTeam>>(Teams);

    public async Task<LinearIssue> CreateIssueAsync(string teamId, string title, string? description, int? priority, CancellationToken cancellationToken)
    {
        await Task.Delay(400, cancellationToken);
        var team = Teams.FirstOrDefault(t => t.Id == teamId) ?? Teams[0];
        lock (_lock)
        {
            var identifier = team.Key + "-" + _nextNumber++;
            var issue = Make(identifier, title.Trim(), "Todo", LinearStateType.Unstarted, DemoData.Viewer, priority ?? 0, "", Array.Empty<string>(), _now);
            _issues.Add(issue);
            return issue;
        }
    }

    public async Task LinkPullRequestAsync(string issueId, string url, string title, CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        lock (_lock)
        {
            var issue = _issues.Concat(_others).FirstOrDefault(i => i.Id == issueId);
            if (issue is not null) _linked[url] = issue.Identifier;
        }
    }

    public async Task MarkDoneAsync(LinearIssue issue, CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken);
        lock (_lock)
        {
            var index = _issues.FindIndex(i => i.Id == issue.Id);
            if (index < 0) return;
            _issues[index] = _issues[index] with
            {
                StateName = "Done",
                StateType = LinearStateType.Completed,
                CompletedAt = _now,
                UpdatedAt = _now,
            };
        }
    }
}
