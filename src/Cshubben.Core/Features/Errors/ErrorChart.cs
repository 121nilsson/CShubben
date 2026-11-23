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
        var apps = snapshot.WithErrors.ToString(CultureInfo.InvariantCulture) + (snapshot.WithErrors == 1 ? " app" : " apps") + " with errors";
        return snapshot.Spiking == 0 ? apps : apps + " · " + snapshot.Spiking.ToString(CultureInfo.InvariantCulture) + " spiking";
    }

    /// <summary>Errors per hour in the latest hour, for the app list ("48 / h").</summary>
    public static string PerHour(AppSpike app, ErrorWindow window)
    {
        var perHour = app.Now * TimeSpan.FromHours(1).TotalSeconds / window.Step.TotalSeconds;
        return Math.Round(perHour).ToString(CultureInfo.InvariantCulture) + " / h";
    }
}
