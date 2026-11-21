using System.Globalization;
using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Model;

namespace Cshubben.Services;

/// <summary>
/// Fictional, deterministic pipeline, conversation and drift data that lines up with <c>DemoData</c>. The shape never changes
/// between runs (only the clock moves), so screenshots look the same. Writes are simulated and never leave the process.
/// </summary>
public sealed class DemoPipelineData : IPipelineData
{
    private readonly Func<DateTimeOffset> _clock;

    public DemoPipelineData(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsDemo => true;

    public bool IsDryRun => true;

    // ------------------------------------------------------------------ checks

    public async Task<IReadOnlyList<CheckRun>> GetChecksAsync(PullRequest pr, CancellationToken cancellationToken)
    {
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        return ChecksFor(pr, _clock());
    }

    /// <summary>The checks of a demo pull request. Failing PRs fail in the tests, pending ones are still running.</summary>
    public static IReadOnlyList<CheckRun> ChecksFor(PullRequest pr, DateTimeOffset now)
    {
        var runId = 880000L + pr.Number;
        var start = pr.Ci == CiState.Pending ? now - TimeSpan.FromMinutes(6) : pr.UpdatedAt + TimeSpan.FromMinutes(2);
        var infra = pr.Repo.EndsWith("/infra", StringComparison.Ordinal);
        var list = new List<CheckRun>();
        long nextId = pr.Number * 100L;

        CheckRun Make(string name, RunState state, int offsetSeconds, int seconds, string? step = null, string? excerpt = null)
        {
            nextId++;
            var started = start + TimeSpan.FromSeconds(offsetSeconds);
            var finished = state is RunState.Running or RunState.Queued or RunState.Skipped ? (DateTimeOffset?)null : started + TimeSpan.FromSeconds(seconds);
            return new CheckRun(
                nextId, name, state, state is RunState.Queued or RunState.Skipped ? null : started, finished,
                "https://github.com/" + pr.Repo + "/actions/runs/" + runId.ToString(CultureInfo.InvariantCulture) + "/job/" + nextId.ToString(CultureInfo.InvariantCulture),
                runId, step, excerpt);
        }

        if (pr.Ci == CiState.Unknown) return list;

        if (infra)
        {
            list.Add(Make("terraform fmt", RunState.Success, 0, 11));
            list.Add(Make("terraform validate", RunState.Success, 14, 29));
            if (pr.Ci == CiState.Failure)
            {
                list.Add(Make("terraform plan (staging)", RunState.Failure, 45, 63, "Plan",
                    "Error: Error acquiring the state lock\n\nLock Info:\n  ID:        6f1c2e9a-48d0-3b57-91aa-0c2f7be1d4aa\n  Path:      acme-tfstate/staging/terraform.tfstate\n  Operation: OperationTypePlan\n\nTerraform acquires a state lock to protect the state from being written by multiple users at the same time."));
                list.Add(Make("terraform plan (production)", RunState.Skipped, 0, 0));
            }
            else
            {
                list.Add(Make("terraform plan (staging)", pr.Ci == CiState.Pending ? RunState.Running : RunState.Success, 45, 63));
                list.Add(Make("terraform plan (production)", pr.Ci == CiState.Pending ? RunState.Queued : RunState.Success, 120, 71));
            }

            return list;
        }

        var flaky = pr.Ci == CiState.Success && pr.Number % 2 == 0;
        list.Add(Make("Lint", RunState.Success, 0, 24));
        list.Add(Make("Build (ubuntu-latest)", RunState.Success, 0, 102));
        list.Add(Make("Build (windows-latest)", RunState.Success, 0, 190));
        list.Add(Make("CodeQL", pr.Ci == CiState.Pending ? RunState.Running : RunState.Success, 0, 211));

        if (flaky) list.Add(Make("Unit tests", RunState.Failure, 105, 96, "Run unit tests", "Failed Retry_backoff_is_jittered [31 ms]\n  Assert.InRange() Failure: Value 0 not in range 1 to 4"));
        var tests = pr.Ci == CiState.Pending ? RunState.Running : RunState.Success;
        list.Add(Make("Unit tests", tests, flaky ? 215 : 105, 154));

        if (pr.Ci == CiState.Failure)
        {
            list.Add(Make("Integration tests", RunState.Failure, 195, 262, "Run integration tests",
                "Failed IdempotencyTests.Duplicate_key_returns_original_response [412 ms]\n  Expected: 200\n  Actual:   409\n  at Acme.Api.Tests.IdempotencyTests.Duplicate_key_returns_original_response() in /_/tests/IdempotencyTests.cs:line 88\n\nFailed!  - Failed: 1, Passed: 318, Skipped: 0, Total: 319"));
            list.Add(Make("Deploy preview", RunState.Skipped, 0, 0));
        }
        else
        {
            list.Add(Make("Integration tests", pr.Ci == CiState.Pending ? RunState.Queued : RunState.Success, 195, 262));
            list.Add(Make("Deploy preview", pr.Ci == CiState.Pending ? RunState.Queued : RunState.Success, 470, 48));
        }

        return list;
    }

    // ------------------------------------------------------------------ conversation

    public async Task<PrConversation> GetConversationAsync(PullRequest pr, CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        return ConversationFor(pr);
    }

    /// <summary>A believable timeline for a demo pull request, derived from its counts so the tabs agree with the badges.</summary>
    public static PrConversation ConversationFor(PullRequest pr)
    {
        var span = pr.UpdatedAt - pr.CreatedAt;
        DateTimeOffset At(double fraction) => pr.CreatedAt + TimeSpan.FromTicks((long)(span.Ticks * fraction));
        var items = new List<ConversationItem>();
        var author = pr.Author;
        var sha = ShaOf(pr);
        var n = pr.Number;

        ConversationItem Commit(string key, string title, double at, string who) =>
            new("commit-" + key + n, ConversationKind.Commit, who, false, At(at), string.Empty,
                "https://github.com/" + pr.Repo + "/commit/" + sha)
            { Sha = sha, Title = title };

        ConversationItem Event(string key, string who, string text, double at, bool bot = false) =>
            new("event-" + key + n, ConversationKind.Event, who, bot, At(at), string.Empty, string.Empty) { Title = text };

        ConversationItem Comment(string key, string who, string body, double at, bool bot = false) =>
            new("comment-" + key + n, ConversationKind.Comment, who, bot, At(at), body,
                pr.Url + "#issuecomment-" + key) ;

        ConversationItem Review(string key, string who, ReviewStateKind state, string body, double at) =>
            new("review-" + key + n, ConversationKind.Review, who, false, At(at), body, pr.Url + "#pullrequestreview-" + key)
            { Review = state };

        ConversationItem Thread(string key, string who, string path, int line, string body, double at, bool resolved, params ThreadReply[] replies) =>
            new("thread-" + key + n, ConversationKind.Thread, who, false, At(at), body, pr.Url + "#discussion-" + key)
            { Path = path, Line = line, Resolved = resolved, Replies = replies };

        items.Add(Commit("a", "Add the core change", 0.01, author));
        items.Add(Commit("b", "Wire it into the request path", 0.03, author));
        items.Add(Event("label", author, "added the label " + (pr.Repo.EndsWith("/infra", StringComparison.Ordinal) ? "infrastructure" : "feature"), 0.04));
        items.Add(Event("req", author, "requested a review from priya", 0.05));
        items.Add(Comment("bot", "codecov[bot]", "**Codecov Report**\n\nCoverage is `91.3%` (+0.4%) with 98.2% of changed lines covered.", 0.12, bot: true));

        if (pr.Comments > 0)
        {
            items.Add(Comment("q1", "maria", "Could we also log the key together with the request id? It would make support tickets much easier to trace.", 0.2));
            items.Add(Comment("a1", author, "Good idea, added in the next commit.", 0.24));
        }

        var paths = new[]
        {
            ("src/Payments/PaymentsController.cs", 88, "This swallows the timeout case. Should it surface as a 503 instead of a retry?"),
            ("src/Payments/Idempotency/IdempotencyStore.cs", 41, "Is the 24 hour key lifetime documented anywhere? I could not find it."),
            ("tests/IdempotencyTests.cs", 120, "Nit: this test would be clearer with two explicit requests instead of the loop."),
        };
        for (var i = 0; i < pr.UnresolvedThreads; i++)
        {
            var p = paths[i % paths.Length];
            items.Add(Thread("open" + i, i % 2 == 0 ? "jonas" : "maria", p.Item1, p.Item2, p.Item3, 0.3 + i * 0.05, false));
        }

        if (pr.Review == ReviewDecision.Approved)
        {
            items.Add(Thread("done", "priya", "src/Payments/PaymentsController.cs", 54,
                "Should this be `const`? The value never changes.", 0.4, true,
                new ThreadReply(author, At(0.42), "Yes, fixed.")));
            items.Add(Commit("c", "Address review comments", 0.5, author));
            items.Add(Review("p", "priya", ReviewStateKind.Approved, "Small and clear. Approving.", 0.62));
            items.Add(Review("j", "jonas", ReviewStateKind.Approved, "", 0.7));
        }
        else if (pr.Review == ReviewDecision.ChangesRequested)
        {
            items.Add(Review("s", "sofia", ReviewStateKind.ChangesRequested, "The streaming path needs a back-pressure test before this can go in. Details in the threads.", 0.45));
            items.Add(Commit("c", "Start on back-pressure handling", 0.8, author));
        }
        else if (pr.Role == PrRole.Authored && !pr.IsDraft)
        {
            items.Add(Review("m", "maria", ReviewStateKind.Commented, "First pass: the approach looks right. I will do a proper review tomorrow.", 0.5));
        }

        if (pr.Merge == MergeState.Conflicting)
            items.Add(Comment("conf", "github-actions[bot]", "This branch has conflicts that must be resolved.", 0.9, bot: true));

        return ConversationBuilder.Build(items, sha);
    }

    /// <summary>A stable, valid 40-character commit id for a demo pull request.</summary>
    public static string ShaOf(PullRequest pr) =>
        (pr.Number.ToString("x4", CultureInfo.InvariantCulture) + "9c3e51f0b7d24a86c1e0f93d5b7a2c481e6f0d93").Substring(0, 40);

    // ------------------------------------------------------------------ drift

    public async Task<IReadOnlyList<RepoActivity>> GetDriftAsync(IReadOnlyList<string> repos, CancellationToken cancellationToken)
    {
        await Task.Delay(350, cancellationToken).ConfigureAwait(false);
        var now = _clock();
        return repos.Select(r => ActivityFor(r, now)).ToList();
    }

    /// <summary>Scripted activity for the demo repositories; any other repository is quiet and green.</summary>
    public static RepoActivity ActivityFor(string repo, DateTimeOffset now)
    {
        WorkflowRun Run(long id, string name, RunState state, double hoursAgo, string ev = "push") =>
            new(id, name, state, "main", ShaFor(repo, id), ev,
                "https://github.com/" + repo + "/actions/runs/" + id.ToString(CultureInfo.InvariantCulture),
                now - TimeSpan.FromHours(hoursAgo), now - TimeSpan.FromHours(hoursAgo) + TimeSpan.FromMinutes(4));

        Deployment Deploy(long id, string env, DeployState state, double hoursAgo, string who = "deploy-bot") =>
            new(env, id, ShaFor(repo, id), "main", who, now - TimeSpan.FromHours(hoursAgo), state,
                "https://github.com/" + repo + "/actions/runs/" + (id + 5000).ToString(CultureInfo.InvariantCulture));

        switch (repo)
        {
            case "acme/api":
                return new RepoActivity(repo, "main",
                    new[]
                    {
                        Run(1101, "API Build and Test", RunState.Success, 3),
                        Run(1102, "Deploy API", RunState.Success, 9),
                        Run(1103, "CodeQL", RunState.Success, 30),
                    },
                    new[]
                    {
                        Deploy(2101, "production", DeployState.Success, 9),
                        Deploy(2102, "staging", DeployState.Success, 3),
                    },
                    new ReleaseInfo("v2.8.0", now - TimeSpan.FromDays(6)), 14, 3, now);
            case "acme/web":
                return new RepoActivity(repo, "main",
                    new[]
                    {
                        Run(1201, "Web CI", RunState.Failure, 0.7),
                        Run(1202, "Lighthouse", RunState.Success, 5),
                        Run(1203, "Deploy Web", RunState.Success, 50),
                    },
                    new[]
                    {
                        Deploy(2201, "production", DeployState.Success, 50),
                        Deploy(2202, "preview", DeployState.Success, 6),
                    },
                    new ReleaseInfo("v5.14.2", now - TimeSpan.FromDays(11)), 21, 9, now);
            case "acme/infra":
                return new RepoActivity(repo, "main",
                    new[]
                    {
                        Run(1301, "Terraform plan", RunState.Failure, 4),
                        Run(1302, "Terraform fmt", RunState.Success, 4),
                    },
                    new[]
                    {
                        Deploy(2301, "staging", DeployState.Failure, 4, "ci"),
                        Deploy(2302, "production", DeployState.Success, 24 * 6),
                    },
                    null, null, 5, now);
            case "acme/mobile":
                return new RepoActivity(repo, "main",
                    new[]
                    {
                        Run(1401, "Mobile CI", RunState.Running, 0.1),
                        Run(1402, "Release build", RunState.Queued, 0.05),
                        Run(1403, "UI tests", RunState.Success, 20),
                    },
                    new[] { Deploy(2401, "testflight", DeployState.Success, 24 * 5) },
                    new ReleaseInfo("v1.22.0", now - TimeSpan.FromDays(19)), 37, 12, now);
            case "acme/docs":
                return new RepoActivity(repo, "main",
                    new[] { Run(1501, "Docs Publish", RunState.Success, 24 * 3) },
                    new[] { Deploy(2501, "github-pages", DeployState.Success, 24 * 3) },
                    null, null, 0, now);
            default:
                return new RepoActivity(repo, "main",
                    new[] { Run(1901, "CI", RunState.Success, 8) },
                    Array.Empty<Deployment>(), null, null, null, now);
        }
    }

    private static string ShaFor(string repo, long id)
    {
        var seed = (repo.Length * 7919 + (int)id).ToString("x8", CultureInfo.InvariantCulture);
        return (seed + "4e0b19c7a3d8f26510be9a4c7d3f02e8b6a1").Substring(0, 40);
    }

    // ------------------------------------------------------------------ writes

    public async Task<ActionOutcome> RerunFailedJobsAsync(string repo, IReadOnlyList<long> runIds, CancellationToken cancellationToken)
    {
        await Task.Delay(700, cancellationToken).ConfigureAwait(false);
        return runIds.Count == 0
            ? new ActionOutcome(false, false, "There are no failed workflow runs to re-run.")
            : new ActionOutcome(true, true, "Demo mode: nothing was sent to GitHub.");
    }

    public async Task<ActionOutcome> ReplyAsync(PullRequest pr, string body, CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body)
            ? new ActionOutcome(false, false, "Write some text first.")
            : new ActionOutcome(true, true, "Demo mode: nothing was sent to GitHub.");
    }
}

/// <summary>A stand-in for merging that waits a moment and never merges anything.</summary>
public sealed class DemoMergeActions : IMergeActions
{
    public bool IsDryRun => true;

    public async Task<ActionOutcome> MergeAsync(PullRequest pr, MergeMethodChoice method, CancellationToken cancellationToken, string? expectedHeadSha = null)
    {
        await Task.Delay(900, cancellationToken).ConfigureAwait(false);
        return new ActionOutcome(true, true, "Demo mode: " + pr.Repo + " #" + pr.Number + " was not merged. Nothing was sent to GitHub.");
    }
}
