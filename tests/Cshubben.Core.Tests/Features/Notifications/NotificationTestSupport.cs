using Cshubben.Core.Model;

namespace Cshubben.Core.Tests.Features.Notifications;

internal static class N
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    public static PullRequest Pr(
        string id = "p1",
        string repo = "acme/api",
        int number = 1,
        PrRole role = PrRole.Authored,
        ReviewDecision review = ReviewDecision.None,
        CiState ci = CiState.Pending,
        MergeState merge = MergeState.Mergeable,
        int comments = 0,
        bool draft = false,
        int updatedMinutes = 0,
        string author = "alice",
        string title = "Add things") =>
        new(id, role, repo, number, title, "https://github.com/" + repo + "/pull/" + number, author, draft,
            T0.AddDays(-2), T0.AddMinutes(updatedMinutes), 10, 2, 3, review, ci, merge, comments, 0, "feature");

    public static FeedItem Feed(string id, FeedKind kind, string repo = "acme/api", string actor = "bob", int? number = 1,
        string detail = "", bool bot = false) =>
        new(id, kind, repo, actor, number, "t", detail, T0, true, bot);

    public static HubSnapshot Snap(
        IEnumerable<PullRequest>? authored = null,
        IEnumerable<PullRequest>? reviews = null,
        IEnumerable<FeedItem>? feed = null,
        IReadOnlyDictionary<string, ReviewInfo>? infos = null,
        string viewer = "me") =>
        new(viewer,
            (authored ?? Array.Empty<PullRequest>()).ToList(),
            (reviews ?? Array.Empty<PullRequest>()).ToList(),
            infos ?? new Dictionary<string, ReviewInfo>(),
            (feed ?? Array.Empty<FeedItem>()).ToList(),
            T0);

    public static PullRequest Review(string id = "r1", int number = 9, string repo = "acme/web", bool draft = false) =>
        Pr(id, repo, number, PrRole.ReviewRequested, draft: draft, author: "carol", title: "Review me");

    public static ReviewInfo Info(string prId, DateTimeOffset? myReviewAt, DateTimeOffset lastCommit) =>
        new(prId, true, myReviewAt, lastCommit, ReviewDecision.None);
}
