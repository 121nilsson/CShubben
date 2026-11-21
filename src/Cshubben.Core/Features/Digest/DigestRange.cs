using System.Globalization;

namespace Cshubben.Core.Features.Digest;

/// <summary>The time span and name of a summary period. Days are calendar days in the offset of <c>now</c> (the user's local time).</summary>
public static class DigestRange
{
    public static (DateTimeOffset From, DateTimeOffset To) For(DigestPeriod period, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return period switch
        {
            DigestPeriod.Yesterday => (today.AddDays(-1), today),
            DigestPeriod.Week => (today.AddDays(-6), now),
            DigestPeriod.Month => (today.AddDays(-29), now),
            _ => (new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset), now),
        };
    }

    public static string Label(DigestPeriod period, DateTimeOffset now)
    {
        var (from, to) = For(period, now);
        string D(DateTimeOffset d) => d.ToString("d MMM", CultureInfo.InvariantCulture);
        return period switch
        {
            DigestPeriod.Yesterday => "Yesterday, " + D(from),
            DigestPeriod.Week => "Last 7 days, " + D(from) + " to " + D(to),
            DigestPeriod.Month => "Last 30 days, " + D(from) + " to " + D(to),
            _ => from.Year.ToString(CultureInfo.InvariantCulture) + " so far, " + D(from) + " to " + D(to),
        };
    }

    public static string ShortLabel(DigestPeriod period) => period switch
    {
        DigestPeriod.Yesterday => "Yesterday",
        DigestPeriod.Week => "Last 7 days",
        DigestPeriod.Month => "Last 30 days",
        _ => "This year",
    };
}
