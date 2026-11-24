using System.Globalization;

namespace Cshubben.Core.Features.Errors;

/// <summary>
/// What was read for the hour a week ago. <paramref name="LogsReachBack"/> is false when the log service holds no log lines at all
/// from then (retention is shorter than a week), so "no errors" there cannot be told from "no logs" and nothing is compared.
/// </summary>
public sealed record LastWeekLogs(IReadOnlyList<ErrorSeries> Series, bool LogsReachBack);

public enum WeeklyVerdict { Usual, Higher, Lower }

/// <summary>The latest hour of an application next to the same hour one week earlier. Rates are errors per hour.</summary>
public sealed record WeeklyComparison(WeeklyVerdict Verdict, double NowPerHour, double LastWeekPerHour);

/// <summary>
/// Puts "now" next to the same time of week one week ago, so a spike that is just Monday morning can be told from one that is new.
/// The comparison is informational: it does not change what counts as a spike. Pure, so every rule is tested.
/// </summary>
public static class WeeklyBaseline
{
    /// <summary>Buckets in an hour at the default step; the first hour of the last-week window lines up with "now".</summary>
    public const int HourBuckets = 12;

    /// <summary>A rate must be at least this many times (or at most one over this) the other to count as higher or lower.</summary>
    public const double Ratio = 2.0;

    /// <summary>
    /// The window to read for last week: two hours that start where the current latest hour started a week ago. Buckets 0 to 11
    /// are the hour that matches "now"; the hour after it is context for later use.
    /// </summary>
    public static ErrorWindow WindowFor(DateTimeOffset now) =>
        ErrorWindow.Ending(now.AddDays(-7).AddHours(1), ErrorWindow.DefaultStep, 2 * HourBuckets);

    /// <summary>Shown instead of a comparison when the logs do not reach back a week.</summary>
    public const string Unavailable = "No comparison with last week: the logs do not reach back that far.";

    /// <summary>
    /// Null when last week could not be read or the logs do not reach back to it. Otherwise an application without a series had
    /// no errors then.
    /// </summary>
    public static WeeklyComparison? Compare(AppSpike app, ErrorWindow window, LastWeekLogs? lastWeek)
    {
        if (lastWeek is not { LogsReachBack: true }) return null;

        var series = lastWeek.Series.FirstOrDefault(s => string.Equals(s.App, app.App, StringComparison.OrdinalIgnoreCase));
        var counts = series?.Counts ?? Array.Empty<int>();
        var take = Math.Min(HourBuckets, counts.Count);
        double sum = 0;
        for (var i = 0; i < take; i++) sum += counts[i];

        var perBucket = HourBuckets;
        var scale = TimeSpan.FromHours(1).TotalSeconds / window.Step.TotalSeconds;
        var last = take == 0 ? 0 : sum / perBucket * scale;
        var now = app.Now * scale;

        var ratio = (now + 1) / (last + 1);
        var verdict = ratio >= Ratio ? WeeklyVerdict.Higher : ratio <= 1 / Ratio ? WeeklyVerdict.Lower : WeeklyVerdict.Usual;
        return new WeeklyComparison(verdict, now, last);
    }

    /// <summary>One short sentence, or null when both hours were quiet and there is nothing to say.</summary>
    public static string? Line(WeeklyComparison? comparison)
    {
        if (comparison is null || (comparison.NowPerHour < 1 && comparison.LastWeekPerHour < 1)) return null;

        var now = Rate(comparison.NowPerHour);
        var then = comparison.LastWeekPerHour < 0.5 ? "none" : Rate(comparison.LastWeekPerHour);
        return comparison.Verdict switch
        {
            WeeklyVerdict.Higher => "Higher than a week ago: " + now + " now, " + then + " at this time last week.",
            WeeklyVerdict.Lower => "Lower than a week ago: " + now + " now, " + then + " at this time last week.",
            _ => "Usual for this time of week: " + now + " now, " + then + " a week ago.",
        };
    }

    private static string Rate(double perHour) =>
        (perHour < 1 ? "<1" : Math.Round(perHour).ToString(CultureInfo.InvariantCulture)) + " / h";
}
