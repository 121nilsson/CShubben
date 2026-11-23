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
