using System.Globalization;

namespace Cshubben.Core.Features.Errors;

/// <summary>Pure helpers that decide what a bar chart of error counts looks like, so the UI only draws rectangles.</summary>
public static class ErrorChart
{
    /// <summary>The last <paramref name="length"/> buckets. When there are fewer, the start is padded with zeros.</summary>
    public static IReadOnlyList<int> Tail(IReadOnlyList<int> counts, int length)
    {
        if (length <= 0) return Array.Empty<int>();
        if (counts.Count >= length) return counts.Skip(counts.Count - length).ToList();
        return Enumerable.Repeat(0, length - counts.Count).Concat(counts).ToList();
    }

    /// <summary>
    /// Bar heights from 0 to <paramref name="maxHeight"/>. A bucket with errors is never shorter than
    /// <paramref name="minVisible"/>, so a single error can still be seen; an empty bucket has height 0.
    /// <paramref name="scaleMax"/> lets several charts share one scale.
    /// </summary>
    public static IReadOnlyList<double> BarHeights(IReadOnlyList<int> counts, double maxHeight, double minVisible = 2, int? scaleMax = null)
    {
        var peak = scaleMax ?? (counts.Count == 0 ? 0 : counts.Max());
        var heights = new double[counts.Count];
        if (peak <= 0) return heights;
        for (var i = 0; i < counts.Count; i++)
        {
            if (counts[i] <= 0) continue;
            heights[i] = Math.Min(maxHeight, Math.Max(minVisible, maxHeight * counts[i] / peak));
        }

        return heights;
    }
}

/// <summary>Where things that happened at a point in time (deployments) go on a bar chart.</summary>
public static class ErrorMarkers
{
    /// <summary>The start of a chart that shows the last <paramref name="shown"/> buckets of the window.</summary>
    public static DateTimeOffset TailFrom(ErrorWindow window, int shown) =>
        window.End - TimeSpan.FromTicks(window.Step.Ticks * Math.Min(shown, window.Buckets));

    /// <summary>
    /// The buckets of a chart that start at <paramref name="from"/> and have <paramref name="buckets"/> buckets of
    /// <paramref name="step"/> that contain one of the <paramref name="times"/>, sorted, without duplicates. Times outside the chart are left out.
    /// </summary>
    public static IReadOnlyList<int> Indices(DateTimeOffset from, TimeSpan step, int buckets, IEnumerable<DateTimeOffset> times)
    {
        if (buckets <= 0 || step <= TimeSpan.Zero) return Array.Empty<int>();

        var found = new SortedSet<int>();
        foreach (var time in times)
        {
            var ticks = (time - from).Ticks;
            if (ticks < 0) continue;
            var index = ticks / step.Ticks;
            if (index < buckets) found.Add((int)index);
        }

        return found.ToList();
    }
}

/// <summary>A slice of a bucket series with the times it covers.</summary>
public sealed record ChartRange(IReadOnlyList<int> Counts, DateTimeOffset From, DateTimeOffset To);

public static class ErrorZoom
{
    /// <summary>
    /// The buckets around the time an error was seen, so a short burst fills the chart instead of one bar in a day.
    /// Covers at least <paramref name="minBuckets"/> buckets, with a little margin on both sides, and never leaves the window.
    /// </summary>
    public static ChartRange Around(IReadOnlyList<int> counts, ErrorWindow window, DateTimeOffset first, DateTimeOffset last, int minBuckets = 24)
    {
        var total = Math.Min(counts.Count, window.Buckets);
        if (total == 0) return new ChartRange(Array.Empty<int>(), window.Start, window.End);

        var a = window.IndexOf(first);
        var b = window.IndexOf(last);
        if (a < 0) a = 0;
        if (b < 0) b = total - 1;
        if (b < a) (a, b) = (b, a);

        var span = b - a + 1;
        var margin = Math.Max(2, span / 5);
        var start = a - margin;
        var end = b + margin;
        var missing = minBuckets - (end - start + 1);
        if (missing > 0)
        {
            start -= missing / 2;
            end += missing - missing / 2;
        }

        if (start < 0)
        {
            end -= start;
            start = 0;
        }

        if (end > total - 1)
        {
            start -= end - (total - 1);
            end = total - 1;
        }

        start = Math.Max(0, start);
        var slice = counts.Skip(start).Take(end - start + 1).ToList();
        var from = window.Start + TimeSpan.FromTicks(window.Step.Ticks * start);
        var to = window.Start + TimeSpan.FromTicks(window.Step.Ticks * (end + 1));
        return new ChartRange(slice, from, to);
    }
}

/// <summary>The short texts of the Errors page. Pure, so the wording is tested.</summary>
public static class ErrorsText
{
    /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago".</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromMinutes(60)) return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min ago";
        if (span < TimeSpan.FromHours(48)) return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + " h ago";
        return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + " d ago";
    }

    /// <summary>"5 min", "2 h 5 min", "1 d": a length of time in the units of <see cref="Ago"/>.</summary>
    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "less than a minute";
        if (span < TimeSpan.FromMinutes(60)) return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min";
        if (span < TimeSpan.FromHours(48))
        {
            var minutes = (int)span.TotalMinutes % 60;
            return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + " h"
                + (minutes > 0 && span < TimeSpan.FromHours(6) ? " " + minutes.ToString(CultureInfo.InvariantCulture) + " min" : "");
        }

        return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + " d";
    }

    /// <summary>"12 min after deploy 3f9c2ab by anna from main", or "right at deploy ..." within the first minute.</summary>
    public static string AfterDeploy(DeployLink link)
    {
        var d = link.Deploy;
        var sha = d.Sha.Length > 7 ? d.Sha[..7] : d.Sha;
        var after = link.After < TimeSpan.FromMinutes(1) ? "right at" : Span(link.After) + " after";
        return after + " deploy " + sha
            + (d.Creator.Length > 0 ? " by " + d.Creator : "")
            + (d.Ref.Length > 0 ? " from " + d.Ref : "");
    }

    /// <summary>"First seen 12 min after deploy 3f9c2ab by anna from main".</summary>
    public static string DeployLine(string subject, DeployLink link) => subject + " " + AfterDeploy(link);

    /// <summary>"Started 51 min ago", with the deployment it followed when there is one: "Started 51 min ago, 12 min after deploy ...".</summary>
    public static string StartLine(DateTimeOffset started, DateTimeOffset now, DeployLink? link) =>
        "Started " + Ago(started, now) + (link is null ? "" : ", " + AfterDeploy(link));

    /// <summary>"Errors per 5 min over the last 6 hours: 124 in total, peak 18 at 14:35." For screen readers; the clock is in <paramref name="zone"/>.</summary>
    public static string ChartSummary(IReadOnlyList<int> counts, ErrorWindow window, int shown, TimeZoneInfo? zone = null)
    {
        var tail = ErrorChart.Tail(counts, Math.Min(shown, window.Buckets));
        var span = TimeSpan.FromTicks(window.Step.Ticks * tail.Count);
        var step = Span(window.Step);
        var length = span < TimeSpan.FromHours(2)
            ? ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " minutes"
            : ((int)Math.Round(span.TotalHours)).ToString(CultureInfo.InvariantCulture) + " hours";
        var total = tail.Sum();
        if (total == 0) return "No errors in the last " + length + ".";

        var peak = tail.Max();
        var peakIndex = 0;
        for (var i = 0; i < tail.Count; i++)
        {
            if (tail[i] != peak) continue;
            peakIndex = i;
            break;
        }

        var peakAt = ErrorMarkers.TailFrom(window, tail.Count) + TimeSpan.FromTicks(window.Step.Ticks * peakIndex);
        var local = TimeZoneInfo.ConvertTime(peakAt, zone ?? TimeZoneInfo.Local);
        return "Errors per " + step + " over the last " + length + ": " + total.ToString(CultureInfo.InvariantCulture)
            + " in total, peak " + peak.ToString(CultureInfo.InvariantCulture) + " at " + local.ToString("HH:mm", CultureInfo.InvariantCulture) + ".";
    }

    /// <summary>"4.2×" below ten, "12×" from ten.</summary>
    public static string Factor(double factor) =>
        factor >= 10
            ? ((int)Math.Round(factor)).ToString(CultureInfo.InvariantCulture) + "×"
            : factor.ToString("0.#", CultureInfo.InvariantCulture) + "×";

    public static string TrendGlyph(Trend trend) => trend switch
    {
        Trend.Up => "↑",
        Trend.Down => "↓",
        _ => "→",
    };

    /// <summary>The trend in words, for screen readers.</summary>
    public static string TrendWord(Trend trend) => trend switch
    {
        Trend.Up => "rising",
        Trend.Down => "falling",
        _ => "steady",
    };

    /// <summary>"3 apps with errors · 1 spiking", or "No errors in the last 24 hours".</summary>
    public static string Summary(ErrorsSnapshot snapshot)
    {
        if (snapshot.WithErrors == 0) return "No errors in the last 24 hours";
        var of = snapshot.Apps.Count > snapshot.WithErrors ? " of " + snapshot.Apps.Count.ToString(CultureInfo.InvariantCulture) : "";
        var apps = snapshot.WithErrors.ToString(CultureInfo.InvariantCulture) + of + (snapshot.WithErrors == 1 && of.Length == 0 ? " app" : " apps") + " with errors";
        return snapshot.Spiking == 0 ? apps : apps + " · " + snapshot.Spiking.ToString(CultureInfo.InvariantCulture) + " spiking";
    }

    /// <summary>Errors per hour in the latest hour, for the app list ("48 / h").</summary>
    public static string PerHour(AppSpike app, ErrorWindow window)
    {
        var perHour = app.Now * TimeSpan.FromHours(1).TotalSeconds / window.Step.TotalSeconds;
        return Math.Round(perHour).ToString(CultureInfo.InvariantCulture) + " / h";
    }
}
