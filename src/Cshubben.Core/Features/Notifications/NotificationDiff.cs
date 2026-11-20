using System.Globalization;
using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Notifications;

/// <summary>
/// Finds what changed between two snapshots and turns it into events worth a notification.
/// Pure: no clock, no I/O. The first snapshot after start (previous is null) is only a baseline and never notifies.
/// </summary>
public static class NotificationDiff
{
    public static IReadOnlyList<NotificationEvent> Compute(HubSnapshot? previous, HubSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null) return Array.Empty<NotificationEvent>();

        var events = new List<NotificationEvent>();
        var oldAuthored = Index(previous.Authored);
        var oldReviews = Index(previous.Reviews);
        var oldFeedIds = new HashSet<string>(previous.Feed.Select(f => f.Id), StringComparer.Ordinal);
        var newFeed = current.Feed.Where(f => !oldFeedIds.Contains(f.Id)).ToList();

        var mentioned = Mentions(current, newFeed, events);
        AuthoredChanges(current, oldAuthored, mentioned, events);
        Merges(current, oldAuthored, newFeed, events);
        ReviewChanges(previous, current, oldAuthored, oldReviews, events);

        // Stable sort: priority first, snapshot order kept inside a kind.
        return events.OrderBy(e => (int)e.Kind).ToList();
    }

    /// <summary>True when the PR can be merged right now.</summary>
    public static bool IsReadyToMerge(PullRequest pr) =>
        !pr.IsDraft && pr.Review == ReviewDecision.Approved && pr.Ci == CiState.Success && pr.Merge == MergeState.Mergeable;

    private static void AuthoredChanges(
        HubSnapshot current,
        Dictionary<string, PullRequest> oldAuthored,
        HashSet<string> mentioned,
        List<NotificationEvent> events)
    {
        foreach (var pr in current.Authored)
        {
            if (!oldAuthored.TryGetValue(pr.Id, out var old)) continue;
            var stamp = pr.UpdatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);

            if (old.Ci != CiState.Failure && pr.Ci == CiState.Failure)
                events.Add(Make("ci-failed:" + pr.Id + ":" + stamp, NotificationKind.CiFailed, pr));

            if (old.Review != ReviewDecision.ChangesRequested && pr.Review == ReviewDecision.ChangesRequested)
                events.Add(Make("changes:" + pr.Id + ":" + stamp, NotificationKind.ChangesRequested, pr));

            if (IsReadyToMerge(pr) && !IsReadyToMerge(old))
                events.Add(Make("ready:" + pr.Id + ":" + stamp, NotificationKind.ReadyToMerge, pr));

            if (pr.Comments > old.Comments && !mentioned.Contains(RepoKey(pr.Repo, pr.Number)))
            {
                var delta = pr.Comments - old.Comments;
                var detail = delta == 1 ? "1 new comment" : delta.ToString(CultureInfo.InvariantCulture) + " new comments";
                events.Add(Make("comment:" + pr.Id + ":" + pr.Comments.ToString(CultureInfo.InvariantCulture),
                    NotificationKind.Comment, pr, detail));
            }
        }
    }

    private static void Merges(
        HubSnapshot current,
        Dictionary<string, PullRequest> oldAuthored,
        List<FeedItem> newFeed,
        List<NotificationEvent> events)
    {
        var stillOpen = new HashSet<string>(current.Authored.Select(p => p.Id), StringComparer.Ordinal);
        foreach (var old in oldAuthored.Values)
        {
            if (stillOpen.Contains(old.Id)) continue;
            var merged = newFeed.Any(f => f.Kind == FeedKind.Merged
                                          && f.Number == old.Number
                                          && string.Equals(f.Repo, old.Repo, StringComparison.OrdinalIgnoreCase));
            if (merged) events.Add(Make("merged:" + old.Id, NotificationKind.Merged, old));
        }
    }

    private static void ReviewChanges(
        HubSnapshot previous,
        HubSnapshot current,
        Dictionary<string, PullRequest> oldAuthored,
        Dictionary<string, PullRequest> oldReviews,
        List<NotificationEvent> events)
    {
        foreach (var pr in current.Reviews)
        {
            var info = current.ReviewInfos.GetValueOrDefault(pr.Id);
            if (!oldReviews.ContainsKey(pr.Id))
            {
                if (oldAuthored.ContainsKey(pr.Id) || pr.IsDraft) continue;
                if (info is not null && info.MyReviewAt is not null)
                {
                    // Already reviewed once and back in the queue: the author asked again.
                    var stamp = (info.LastCommitAt.UtcTicks).ToString(CultureInfo.InvariantCulture);
                    events.Add(Make("rereview:" + pr.Id + ":" + stamp, NotificationKind.ReReviewRequested, pr, pr.Author));
                }
                else
                {
                    events.Add(Make("review-requested:" + pr.Id, NotificationKind.ReviewRequested, pr, pr.Author));
                }

                continue;
            }

            var oldInfo = previous.ReviewInfos.GetValueOrDefault(pr.Id);
            if (!UpdatedSinceReview(oldInfo) && UpdatedSinceReview(info) && info is not null)
            {
                var stamp = info.LastCommitAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
                events.Add(Make("rereview:" + pr.Id + ":" + stamp, NotificationKind.ReReviewRequested, pr, pr.Author));
            }
        }
    }

    private static HashSet<string> Mentions(HubSnapshot current, List<FeedItem> newFeed, List<NotificationEvent> events)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(current.Viewer)) return result;
        foreach (var item in newFeed)
        {
            if (item.Kind != FeedKind.Commented || item.Number is not { } number || item.IsBot) continue;
            if (string.Equals(item.Actor, current.Viewer, StringComparison.OrdinalIgnoreCase)) continue;
            if (!MentionsUser(item.Detail, current.Viewer)) continue;

            var pr = current.All.FirstOrDefault(p => p.Number == number
                                                     && string.Equals(p.Repo, item.Repo, StringComparison.OrdinalIgnoreCase));
            var title = pr?.Title ?? item.Title ?? "";
            var url = pr?.Url ?? "https://github.com/" + item.Repo + "/pull/" + number.ToString(CultureInfo.InvariantCulture);
            var id = pr?.Id ?? item.Repo + "#" + number.ToString(CultureInfo.InvariantCulture);
            events.Add(new NotificationEvent("mention:" + item.Id, NotificationKind.Mention, id, item.Repo, number, title, url, item.Actor));
            result.Add(RepoKey(item.Repo, number));
        }

        return result;
    }

    /// <summary>True when the text contains "@login" as a whole word (case-insensitive).</summary>
    public static bool MentionsUser(string? text, string login)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(login)) return false;
        var needle = "@" + login.Trim();
        var start = 0;
        while (start < text.Length)
        {
            var i = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            var end = i + needle.Length;
            var boundary = end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '-' || text[end] == '_');
            var leftOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            if (boundary && leftOk) return true;
            start = i + 1;
        }

        return false;
    }

    private static bool UpdatedSinceReview(ReviewInfo? info) =>
        info?.MyReviewAt is { } reviewed && info.LastCommitAt > reviewed;

    private static string RepoKey(string repo, int number) =>
        repo.ToLowerInvariant() + "#" + number.ToString(CultureInfo.InvariantCulture);

    private static NotificationEvent Make(string key, NotificationKind kind, PullRequest pr, string detail = "") =>
        new(key, kind, pr.Id, pr.Repo, pr.Number, pr.Title, pr.Url, detail);

    private static Dictionary<string, PullRequest> Index(IEnumerable<PullRequest> prs)
    {
        var map = new Dictionary<string, PullRequest>(StringComparer.Ordinal);
        foreach (var pr in prs) map.TryAdd(pr.Id, pr);
        return map;
    }
}
