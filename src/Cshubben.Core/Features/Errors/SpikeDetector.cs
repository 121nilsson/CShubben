namespace Cshubben.Core.Features.Errors;

/// <summary>
/// How a spike is recognised. The defaults suit 5 minute buckets: "now" is the latest hour, the history before it is the baseline.
/// </summary>
/// <param name="NowBuckets">Buckets that count as "now".</param>
/// <param name="K">How many robust standard deviations above the baseline now must be.</param>
/// <param name="MinNow">Fewest errors per bucket (mean over now) that can be a spike, so 0 to 2 errors never alarm.</param>
/// <param name="PriorBuckets">Buckets just before "now" that the trend arrow compares with.</param>
public sealed record SpikeOptions(int NowBuckets = 12, double K = 4.0, double MinNow = 1.0, int PriorBuckets = 72);

/// <summary>Compares the latest hour of an application's errors with its own history. Pure, so every rule is tested.</summary>
public static class SpikeDetector
{
    /// <summary>1.4826 turns a median absolute deviation into a standard deviation for normally distributed data.</summary>
    private const double MadToSigma = 1.4826;

    public static AppSpike Analyze(ErrorSeries series, SpikeOptions? options = null)
    {
        var o = options ?? new SpikeOptions();
        var counts = series.Counts;
        var n = counts.Count;
        var total = counts.Sum();
        if (n == 0) return new AppSpike(series.App, counts, total, 0, 0, 0, Trend.Flat, false);

        var nowBuckets = Math.Clamp(o.NowBuckets, 1, n);
        var historyLength = n - nowBuckets;
        var now = Mean(counts, historyLength, nowBuckets);

        var baseline = 0.0;
        var deviation = 1.0;
        if (historyLength > 0)
        {
            var history = counts.Take(historyLength).Select(c => (double)c).ToList();
            baseline = Median(history);
            var mad = Median(history.Select(h => Math.Abs(h - baseline)).ToList());
            deviation = Math.Max(mad * MadToSigma, 1.0);
        }

        var isSpike = now >= o.MinNow && now > baseline + o.K * deviation;
        var factor = now / Math.Max(baseline, 1.0);

        var priorLength = Math.Min(Math.Max(o.PriorBuckets, 1), historyLength);
        var trend = Trend.Flat;
        if (priorLength > 0)
        {
            var prior = Mean(counts, historyLength - priorLength, priorLength);
            var ratio = (now + 0.5) / (prior + 0.5);
            trend = ratio >= 1.5 ? Trend.Up : ratio <= 1.0 / 1.5 ? Trend.Down : Trend.Flat;
        }

        return new AppSpike(series.App, counts, total, now, baseline, factor, trend, isSpike);
    }

    /// <summary>
    /// All applications, the spiking ones first (largest factor first), then the others with errors (most errors first),
    /// then the quiet ones by name.
    /// </summary>
    public static IReadOnlyList<AppSpike> Analyze(IEnumerable<ErrorSeries> series, SpikeOptions? options = null) =>
        series
            .Select(s => Analyze(s, options))
            .OrderByDescending(a => a.IsSpike)
            .ThenByDescending(a => a.IsSpike ? a.Factor : a.Total)
            .ThenBy(a => a.App, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static double Mean(IReadOnlyList<int> counts, int start, int length)
    {
        long sum = 0;
        for (var i = start; i < start + length; i++) sum += counts[i];
        return (double)sum / length;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}

/// <summary>Builds the snapshot the page shows from the counts per application.</summary>
public static class ErrorsOverview
{
    /// <summary>
    /// Every application with errors, analysed, plus the known applications (the ones bound to a repository) that had none,
    /// as quiet entries at the end, so a healthy app is visible too.
    /// </summary>
    public static ErrorsSnapshot Build(
        ErrorWindow window,
        IEnumerable<ErrorSeries> series,
        IEnumerable<string> knownApps,
        DateTimeOffset at,
        SpikeOptions? options = null)
    {
        var apps = SpikeDetector.Analyze(series, options).ToList();
        var quiet = knownApps
            .Where(k => !string.IsNullOrWhiteSpace(k) && !apps.Any(a => string.Equals(a.App, k, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k => SpikeDetector.Analyze(new ErrorSeries(k, new int[window.Buckets]), options));
        apps.AddRange(quiet);
        return new ErrorsSnapshot(window, apps, at);
    }
}
