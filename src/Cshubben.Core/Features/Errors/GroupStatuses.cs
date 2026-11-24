namespace Cshubben.Core.Features.Errors;

/// <summary>Where an error group is heading.</summary>
public enum GroupStatus
{
    /// <summary>Still happening at about the same rate.</summary>
    Ongoing,

    /// <summary>First seen in the last two hours, after a quiet start of the window.</summary>
    New,

    /// <summary>At least twice as frequent in the latest hour as before it.</summary>
    Rising,

    /// <summary>At most half as frequent in the latest hour as before it.</summary>
    Fading,

    /// <summary>Not seen for an hour or more.</summary>
    Stopped,
}

/// <summary>How the groups of an application can be ordered.</summary>
public enum GroupSort
{
    /// <summary>Most errors first.</summary>
    Frequent,

    /// <summary>Seen most recently first.</summary>
    Latest,

    /// <summary>First seen most recently first.</summary>
    Newest,

    /// <summary>Rising and new errors first, the ones that stopped last.</summary>
    Rising,
}

/// <summary>
/// The log lines of one application are read up to a limit, newest first. When the limit is hit the oldest lines are missing from
/// the groups (the chart of the application, which is counted separately, is complete), so "new" cannot be told from "old".
/// </summary>
public static class GroupSampling
{
    /// <summary>The most log lines read for one application, and the most groups shown.</summary>
    public const int MaxLines = 1000;

    public const int MaxGroups = 50;

    /// <summary>True when the groups cover every error of the window, false when older lines were cut off.</summary>
    public static bool IsComplete(IReadOnlyList<ErrorGroup> groups) =>
        groups.Count < MaxGroups && groups.Sum(g => (long)g.Count) < MaxLines;
}

/// <summary>Decides the status of an error group from its counts. Pure, so every rule is tested.</summary>
public static class GroupStatuses
{
    private static readonly TimeSpan Recent = TimeSpan.FromHours(1);
    private static readonly TimeSpan Before = TimeSpan.FromHours(3);
    private static readonly TimeSpan NewWithin = TimeSpan.FromHours(2);
    private static readonly TimeSpan QuietStart = TimeSpan.FromHours(3);

    /// <summary>
    /// The status of a group. <paramref name="complete"/> is false when older log lines were cut off (see
    /// <see cref="GroupSampling"/>); then a group is never called new, because its true first appearance is not known.
    /// </summary>
    public static GroupStatus Of(ErrorGroup group, ErrorWindow window, DateTimeOffset now, bool complete = true)
    {
        if (now - group.LastSeen >= Recent) return GroupStatus.Stopped;

        if (complete && now - group.FirstSeen <= NewWithin && group.FirstSeen >= window.Start + QuietStart)
            return GroupStatus.New;

        var recentBuckets = Buckets(window, Recent);
        var beforeBuckets = Buckets(window, Before);
        var counts = group.Counts;
        var n = counts.Count;
        if (n < recentBuckets + 1) return GroupStatus.Ongoing;

        var recentSum = Sum(counts, n - recentBuckets, recentBuckets);
        var beforeLength = Math.Min(beforeBuckets, n - recentBuckets);
        var beforeSum = Sum(counts, n - recentBuckets - beforeLength, beforeLength);

        var recentRate = (double)recentSum / recentBuckets;
        var beforeRate = beforeLength == 0 ? 0 : (double)beforeSum / beforeLength;

        if (recentSum >= 3 && recentRate >= 2 * beforeRate) return GroupStatus.Rising;
        if (beforeSum >= 3 && recentRate <= beforeRate / 2) return GroupStatus.Fading;
        return GroupStatus.Ongoing;
    }

    /// <summary>How many errors the group had in the latest hour.</summary>
    public static int RecentCount(ErrorGroup group, ErrorWindow window)
    {
        var length = Math.Min(Buckets(window, Recent), group.Counts.Count);
        return length <= 0 ? 0 : Sum(group.Counts, group.Counts.Count - length, length);
    }

    public static string Label(GroupStatus status) => status switch
    {
        GroupStatus.New => "New",
        GroupStatus.Rising => "Rising",
        GroupStatus.Fading => "Fading",
        GroupStatus.Stopped => "Stopped",
        _ => "Ongoing",
    };

    private static int Buckets(ErrorWindow window, TimeSpan span) =>
        Math.Max(1, (int)Math.Round(span.TotalSeconds / window.Step.TotalSeconds));

    private static int Sum(IReadOnlyList<int> counts, int start, int length)
    {
        var sum = 0;
        for (var i = start; i < start + length; i++) sum += counts[i];
        return sum;
    }
}

/// <summary>Orders the error groups of an application.</summary>
public static class GroupOrdering
{
    public static string Label(GroupSort sort) => sort switch
    {
        GroupSort.Latest => "Latest",
        GroupSort.Newest => "Newest",
        GroupSort.Rising => "Rising",
        _ => "Most frequent",
    };

    /// <summary>The groups in the order asked for. Ties are broken the same way every time, so the list does not jump around.</summary>
    public static IReadOnlyList<ErrorGroup> Order(
        IReadOnlyList<ErrorGroup> groups, GroupSort sort, ErrorWindow window, DateTimeOffset now, bool complete = true)
    {
        IOrderedEnumerable<ErrorGroup> ordered = sort switch
        {
            GroupSort.Latest => groups.OrderByDescending(g => g.LastSeen).ThenByDescending(g => g.Count),
            GroupSort.Newest => groups.OrderByDescending(g => g.FirstSeen).ThenByDescending(g => g.Count),
            GroupSort.Rising => groups
                .OrderBy(g => Rank(GroupStatuses.Of(g, window, now, complete)))
                .ThenByDescending(g => GroupStatuses.RecentCount(g, window))
                .ThenByDescending(g => g.Count),
            _ => groups.OrderByDescending(g => g.Count).ThenByDescending(g => g.LastSeen),
        };

        return ordered.ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
    }

    private static int Rank(GroupStatus status) => status switch
    {
        GroupStatus.Rising => 0,
        GroupStatus.New => 1,
        GroupStatus.Ongoing => 2,
        GroupStatus.Fading => 3,
        _ => 4,
    };
}
