using Cshubben.Core.Model;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

/// <summary>Pure post-processing of mapped feed items: de-duplication and collapsing of bursts.</summary>
public static class FeedFilter
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Drops repeated ids and collapses bursts: items with the same kind, repo, actor and PR (or branch for pushes)
    /// that follow each other within the window leave only the newest. Result is newest first.
    /// </summary>
    public static List<FeedItem> Collapse(IEnumerable<FeedItem> items, TimeSpan? window = null)
    {
        var span = window ?? DefaultWindow;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var lastSeen = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var result = new List<FeedItem>();
        foreach (var item in items.OrderByDescending(i => i.At).ThenBy(i => i.Id, StringComparer.Ordinal))
        {
            if (!seenIds.Add(item.Id)) continue;
            var key = KeyOf(item);
            var burst = lastSeen.TryGetValue(key, out var previous) && previous - item.At <= span;
            lastSeen[key] = item.At;
            if (!burst) result.Add(item);
        }
        return result;
    }

    private static string KeyOf(FeedItem item)
    {
        var target = item.Number is not null
            ? "#" + item.Number.Value
            : "branch:" + BranchOf(item.Detail);
        return (int)item.Kind + "|" + item.Repo.ToLowerInvariant() + "|" + item.Actor.ToLowerInvariant() + "|" + target;
    }

    private static string BranchOf(string detail)
    {
        var colon = detail.IndexOf(':');
        return colon < 0 ? detail : detail.Substring(0, colon);
    }
}
