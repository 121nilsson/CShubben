using Cshubben.Core.Model;

namespace Cshubben.Core.Data;

/// <summary>
/// Deterministic fictional data. Used for screenshots in CI and for working without a GitHub token.
/// All names are invented.
/// </summary>
public sealed class DemoData(DateTimeOffset? now = null) : IHubData
{
    private readonly DateTimeOffset _now = now ?? DateTimeOffset.UtcNow;

    public const string Viewer = "alex";

    public Task<HubSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Build());

    public HubSnapshot Build()
    {
        var authored = new List<PullRequest>
        {
            Pr("a1", PrRole.Authored, "acme/api", 412, "Add idempotency keys to payment endpoints", Viewer, h: 3, size: (212, 34, 9),
                ReviewDecision.Approved, CiState.Success, MergeState.Mergeable, comments: 4, threads: 0, "feat/idempotency"),
            Pr("a2", PrRole.Authored, "acme/web", 877, "Checkout: fix focus trap in address dialog", Viewer, h: 7, size: (48, 12, 3),
                ReviewDecision.Approved, CiState.Success, MergeState.Mergeable, comments: 2, threads: 0, "fix/focus-trap"),
            Pr("a3", PrRole.Authored, "acme/api", 409, "Migrate invoice export to streaming responses", Viewer, h: 26, size: (640, 211, 18),
                ReviewDecision.ChangesRequested, CiState.Success, MergeState.Mergeable, comments: 11, threads: 3, "feat/stream-export"),
            Pr("a4", PrRole.Authored, "acme/infra", 130, "Terraform: split state per environment", Viewer, h: 30, size: (390, 280, 14),
                ReviewDecision.ReviewRequired, CiState.Failure, MergeState.Mergeable, comments: 1, threads: 0, "chore/split-state"),
            Pr("a5", PrRole.Authored, "acme/web", 871, "Dark mode for the settings screens", Viewer, h: 52, size: (150, 40, 8),
                ReviewDecision.ReviewRequired, CiState.Success, MergeState.Conflicting, comments: 0, threads: 0, "feat/dark-settings"),
            Pr("a6", PrRole.Authored, "acme/mobile", 233, "Offline queue for field reports", Viewer, h: 70, size: (980, 120, 31),
                ReviewDecision.ReviewRequired, CiState.Pending, MergeState.Unknown, comments: 2, threads: 1, "feat/offline-queue"),
            Pr("a7", PrRole.Authored, "acme/api", 415, "Draft: rate limiting per API key", Viewer, h: 5, size: (95, 8, 4),
                ReviewDecision.None, CiState.Success, MergeState.Mergeable, comments: 0, threads: 0, "feat/rate-limit", draft: true),
            Pr("a8", PrRole.Authored, "acme/docs", 61, "Document webhook retry behaviour", Viewer, h: 100, size: (40, 2, 1),
                ReviewDecision.ReviewRequired, CiState.Success, MergeState.Mergeable, comments: 0, threads: 0, "docs/webhooks"),
        };

        var reviews = new List<PullRequest>
        {
            Pr("r1", PrRole.ReviewRequested, "acme/api", 418, "Replace homegrown retry loop with backoff library", "maria", h: 2, size: (120, 90, 6),
                ReviewDecision.ReviewRequired, CiState.Success, MergeState.Mergeable, comments: 1, threads: 0, "refactor/backoff"),
            Pr("r2", PrRole.ReviewRequested, "acme/web", 880, "Cache product listing queries", "jonas", h: 4, size: (75, 20, 4),
                ReviewDecision.ReviewRequired, CiState.Success, MergeState.Mergeable, comments: 0, threads: 0, "perf/cache-listing"),
            Pr("r3", PrRole.ReviewRequested, "acme/api", 401, "Add audit log for role changes", "priya", h: 9, size: (310, 15, 11),
                ReviewDecision.ReviewRequired, CiState.Pending, MergeState.Mergeable, comments: 6, threads: 2, "feat/audit-log"),
            Pr("r4", PrRole.ReviewRequested, "acme/mobile", 229, "Fix crash when GPS permission is revoked", "tom", h: 28, size: (30, 9, 2),
                ReviewDecision.Approved, CiState.Success, MergeState.Mergeable, comments: 3, threads: 0, "fix/gps-crash"),
            Pr("r5", PrRole.ReviewRequested, "acme/infra", 127, "Rotate CI deploy keys", "sofia", h: 33, size: (18, 18, 3),
                ReviewDecision.ChangesRequested, CiState.Success, MergeState.Mergeable, comments: 5, threads: 1, "chore/rotate-keys"),
            Pr("r6", PrRole.ReviewRequested, "acme/web", 862, "Replace moment.js with date-fns", "jonas", h: 80, size: (400, 380, 40),
                ReviewDecision.ReviewRequired, CiState.Success, MergeState.Mergeable, comments: 2, threads: 0, "chore/date-fns"),
            Pr("r7", PrRole.ReviewRequested, "acme/docs", 58, "Add onboarding guide", "lena", h: 120, size: (210, 0, 4),
                ReviewDecision.ReviewRequired, CiState.Success, MergeState.Mergeable, comments: 0, threads: 0, "docs/onboarding", draft: true),
        };

        var infos = new Dictionary<string, ReviewInfo>
        {
            ["r1"] = new("r1", true, null, Ago(2), ReviewDecision.None),
            ["r2"] = new("r2", false, null, Ago(4), ReviewDecision.None),
            ["r3"] = new("r3", true, Ago(20), Ago(9), ReviewDecision.ChangesRequested),
            ["r4"] = new("r4", true, Ago(24), Ago(30), ReviewDecision.Approved),
            ["r5"] = new("r5", true, Ago(40), Ago(50), ReviewDecision.ChangesRequested),
            ["r6"] = new("r6", true, Ago(90), Ago(80), ReviewDecision.None),
            ["r7"] = new("r7", false, null, Ago(120), ReviewDecision.None),
        };
        // r3 got new commits after the viewer's review (9 h ago vs reviewed 20 h ago).

        var feed = new List<FeedItem>
        {
            Feed("f1", FeedKind.ReviewRequested, "acme/api", "maria", 418, "Replace homegrown retry loop with backoff library", "requested your review", 2, true),
            Feed("f2", FeedKind.CiFailed, "acme/infra", "ci", 130, "Terraform: split state per environment", "plan failed on staging", 4, true),
            Feed("f3", FeedKind.Approved, "acme/api", "priya", 412, "Add idempotency keys to payment endpoints", "approved", 5, true),
            Feed("f4", FeedKind.Commented, "acme/api", "jonas", 409, "Migrate invoice export to streaming responses", "left 3 comments", 6, true),
            Feed("f5", FeedKind.Merged, "acme/web", "lena", 868, "Update footer links", "merged into main", 8, false),
            Feed("f6", FeedKind.Deployed, "acme/api", "deploy-bot", null, null, "production deploy succeeded", 9, false, bot: true),
            Feed("f7", FeedKind.Pushed, "acme/mobile", "tom", 229, "Fix crash when GPS permission is revoked", "pushed 2 commits", 12, false),
            Feed("f8", FeedKind.CiPassed, "acme/web", "ci", 877, "Checkout: fix focus trap in address dialog", "all checks passed", 13, true),
            Feed("f9", FeedKind.Opened, "acme/web", "jonas", 880, "Cache product listing queries", "opened", 14, false),
            Feed("f10", FeedKind.ChangesRequested, "acme/api", "sofia", 409, "Migrate invoice export to streaming responses", "requested changes", 27, true),
            Feed("f11", FeedKind.Merged, "acme/api", "maria", 405, "Bump serializer to 4.2", "merged into main", 30, false),
            Feed("f12", FeedKind.Pushed, "acme/api", "dependabot", 419, "Bump Npgsql from 9.0.2 to 9.0.3", "pushed 1 commit", 31, false, bot: true),
        };

        return new HubSnapshot(Viewer, authored, reviews, infos, feed, _now);
    }

    private DateTimeOffset Ago(int hours) => _now - TimeSpan.FromHours(hours);

    private PullRequest Pr(string id, PrRole role, string repo, int number, string title, string author, int h,
        (int add, int del, int files) size, ReviewDecision review, CiState ci, MergeState merge,
        int comments, int threads, string branch, bool draft = false) =>
        new(id, role, repo, number, title, $"https://github.com/{repo}/pull/{number}", author, draft,
            Ago(h + 24), Ago(h), size.add, size.del, size.files, review, ci, merge, comments, threads, branch,
            "Demo description.\n\n## Why\nThis is invented demo data used for screenshots.\n\n## Testing\n- unit tests\n- manual run");

    private FeedItem Feed(string id, FeedKind kind, string repo, string actor, int? number, string? title,
        string detail, int h, bool personal, bool bot = false) =>
        new(id, kind, repo, actor, number, title, detail, Ago(h), personal, bot);
}
