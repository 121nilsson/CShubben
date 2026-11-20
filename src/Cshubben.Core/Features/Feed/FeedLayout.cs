using System.Globalization;
using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Feed;

public enum FeedEntryKind
{
    /// <summary>"New since you last looked", before the unseen groups.</summary>
    NewHeader,

    /// <summary>The line between new and already seen events.</summary>
    Divider,

    DayHeader,

    Group,
}

/// <summary>One row of the rendered feed: a header, the new/seen divider, or an event group.</summary>
public sealed record FeedEntry(FeedEntryKind Kind, string Label, int Count, FeedGroup? Group);

/// <summary>The feed as the page shows it. <see cref="NewEvents"/> counts events, <see cref="NewGroups"/> counts rows.</summary>
public sealed record FeedLayout(IReadOnlyList<FeedEntry> Entries, int NewEvents, int NewGroups, int TotalEvents)
{
    public bool IsEmpty => TotalEvents == 0;

    public bool HasNew => NewEvents > 0;

    /// <summary>
    /// Builds the page model from already filtered, newest-first events. New groups come first under one header, then a divider,
    /// then the seen groups under day headers. Without new events only day headers appear.
    /// </summary>
    public static FeedLayout Build(IReadOnlyList<FeedItem> newestFirst, DateTimeOffset? lastSeen, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        var tz = zone ?? TimeZoneInfo.Local;
        var groups = FeedGrouping.Group(newestFirst, lastSeen);
        var fresh = groups.Where(g => g.IsNew).ToList();
        var seen = groups.Where(g => !g.IsNew).ToList();
        var entries = new List<FeedEntry>();

        var newEvents = fresh.Sum(g => g.Count);
        if (fresh.Count > 0)
        {
            entries.Add(new FeedEntry(FeedEntryKind.NewHeader, "New since you last looked", newEvents, null));
            foreach (var group in fresh) entries.Add(new FeedEntry(FeedEntryKind.Group, "", 0, group));
            if (seen.Count > 0) entries.Add(new FeedEntry(FeedEntryKind.Divider, "Earlier", seen.Sum(g => g.Count), null));
        }

        string? currentDay = null;
        foreach (var group in seen)
        {
            var day = DayLabel(group.At, now, tz);
            if (day != currentDay)
            {
                currentDay = day;
                var count = seen.Where(g => DayLabel(g.At, now, tz) == day).Sum(g => g.Count);
                entries.Add(new FeedEntry(FeedEntryKind.DayHeader, day, count, null));
            }

            entries.Add(new FeedEntry(FeedEntryKind.Group, "", 0, group));
        }

        return new FeedLayout(entries, newEvents, fresh.Count, newestFirst.Count);
    }

    /// <summary>"Today", "Yesterday" or "Monday 16 Nov", in the given time zone.</summary>
    public static string DayLabel(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        var a = TimeZoneInfo.ConvertTime(at, zone).Date;
        var n = TimeZoneInfo.ConvertTime(now, zone).Date;
        var days = (n - a).Days;
        return days switch
        {
            0 => "Today",
            1 => "Yesterday",
            _ => TimeZoneInfo.ConvertTime(at, zone).ToString("dddd d MMM", CultureInfo.InvariantCulture),
        };
    }
}
