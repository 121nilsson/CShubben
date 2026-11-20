using Cshubben.Core.Model;

namespace Cshubben.Core.Tests.FeedTests;

internal static class FeedTestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset Ago(double hours) => Now - TimeSpan.FromHours(hours);

    public static FeedItem Item(
        string id,
        FeedKind kind = FeedKind.Pushed,
        string repo = "acme/api",
        string actor = "maria",
        int? number = 1,
        string detail = "",
        double hoursAgo = 1,
        bool personal = false,
        bool bot = false,
        string? title = "Some PR") =>
        new(id, kind, repo, actor, number, number is null ? null : title, detail, Ago(hoursAgo), personal, bot);

    public static PullRequest Pr(string id, string repo, PrRole role = PrRole.Authored, int number = 1) =>
        new(id, role, repo, number, "T" + id, "https://github.com/" + repo + "/pull/" + number, "alex", false, Ago(10), Ago(5),
            1, 1, 1, ReviewDecision.None, CiState.Success, MergeState.Mergeable, 0, 0, "b");

    public static HubSnapshot Snapshot(IReadOnlyList<PullRequest> authored, IReadOnlyList<PullRequest> reviews, IReadOnlyList<FeedItem> feed)
    {
        var infos = reviews.ToDictionary(p => p.Id, p => new ReviewInfo(p.Id, true, null, Ago(2), ReviewDecision.None));
        return new HubSnapshot("alex", authored, reviews, infos, feed, Now);
    }
}
