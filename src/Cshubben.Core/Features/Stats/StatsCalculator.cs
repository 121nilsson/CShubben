using System.Globalization;

namespace Cshubben.Core.Features.Stats;

/// <summary>Pure statistics over merged pull requests and given reviews. Deterministic: <c>now</c> is always passed in.</summary>
public static class StatsCalculator
{
    public const int DefaultWeeks = 12;

    private static readonly (string Label, string Range, int MaxLines)[] SizeRanges =
    {
        ("XS", "up to 50 lines", 50),
        ("S", "51 to 200", 200),
        ("M", "201 to 500", 500),
        ("L", "501 to 1000", 1000),
        ("XL", "over 1000", int.MaxValue),
    };

    /// <summary>Percentile with linear interpolation between ranks. <paramref name="percent"/> is 0 to 100. Empty input gives 0.</summary>
    public static double Percentile(IEnumerable<double> values, double percent)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        var p = Math.Clamp(percent, 0, 100);
        var position = (sorted.Length - 1) * p / 100.0;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
    }

    public static double Median(IEnumerable<double> values) => Percentile(values, 50);

    public static Spread Summarize(IEnumerable<double> hours)
    {
        var list = hours.ToList();
        if (list.Count == 0) return Spread.Empty;
        return new Spread(list.Count, Median(list), Percentile(list, 90), list.Average(), list.Max());
    }

    /// <summary>Monday 00:00 UTC of the week that contains <paramref name="at"/>.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset at)
    {
        var date = at.UtcDateTime.Date;
        var back = ((int)date.DayOfWeek + 6) % 7;
        return new DateTimeOffset(DateTime.SpecifyKind(date.AddDays(-back), DateTimeKind.Utc));
    }

    /// <summary>The Monday starts of the last <paramref name="weeks"/> weeks, oldest first, ending with the week of <paramref name="now"/>.</summary>
    public static IReadOnlyList<DateTimeOffset> WeekStarts(DateTimeOffset now, int weeks)
    {
        var count = Math.Max(1, weeks);
        var current = WeekStart(now);
        var result = new List<DateTimeOffset>(count);
        for (var i = count - 1; i >= 0; i--) result.Add(current.AddDays(-7 * i));
        return result;
    }

    /// <summary>Counts events per week for the last weeks, zero-filled. Events after <paramref name="now"/> or before the first week are ignored.</summary>
    public static IReadOnlyList<WeekBucket> WeeklyCounts(IEnumerable<DateTimeOffset> times, DateTimeOffset now, int weeks)
    {
        var starts = WeekStarts(now, weeks);
        var counts = new Dictionary<DateTimeOffset, int>();
        foreach (var start in starts) counts[start] = 0;
        foreach (var time in times)
        {
            if (time > now) continue;
            var key = WeekStart(time);
            if (counts.ContainsKey(key)) counts[key]++;
        }

        return starts.Select(s => new WeekBucket(s, counts[s])).ToList();
    }

    /// <summary>Merged pull requests per week.</summary>
    public static IReadOnlyList<WeekBucket> Throughput(IEnumerable<MergedPrRecord> merged, DateTimeOffset now, int weeks) =>
        WeeklyCounts(merged.Select(m => m.MergedAt), now, weeks);

    /// <summary>Reviews given per week.</summary>
    public static IReadOnlyList<WeekBucket> ReviewLoad(IEnumerable<ReviewRecord> reviews, DateTimeOffset now, int weeks) =>
        WeeklyCounts(reviews.Select(r => r.At), now, weeks);

    /// <summary>Median cycle time (hours) of the PRs merged in each of the last weeks. Weeks without merges have Count 0 and median 0.</summary>
    public static IReadOnlyList<WeekValue> CycleTrend(IEnumerable<MergedPrRecord> merged, DateTimeOffset now, int weeks)
    {
        var starts = WeekStarts(now, weeks);
        var byWeek = new Dictionary<DateTimeOffset, List<double>>();
        foreach (var start in starts) byWeek[start] = new List<double>();
        foreach (var pr in merged)
        {
            if (pr.MergedAt > now) continue;
            if (byWeek.TryGetValue(WeekStart(pr.MergedAt), out var list)) list.Add(pr.CycleTime.TotalHours);
        }

        return starts.Select(s => new WeekValue(s, byWeek[s].Count, Median(byWeek[s]))).ToList();
    }

    /// <summary>The size label for a number of changed lines: XS, S, M, L or XL.</summary>
    public static string SizeLabel(int lines)
    {
        foreach (var range in SizeRanges)
            if (lines <= range.MaxLines) return range.Label;
        return "XL";
    }

    public static IReadOnlyList<SizeBucket> SizeDistribution(IEnumerable<MergedPrRecord> merged)
    {
        var list = merged.ToList();
        var counts = new Dictionary<string, int>();
        foreach (var range in SizeRanges) counts[range.Label] = 0;
        foreach (var pr in list) counts[SizeLabel(pr.Lines)]++;
        var total = list.Count;
        return SizeRanges
            .Select(r => new SizeBucket(r.Label, r.Range, counts[r.Label], total == 0 ? 0 : (double)counts[r.Label] / total))
            .ToList();
    }

    public static IReadOnlyList<RepoCount> TopRepos(IEnumerable<MergedPrRecord> merged, int take = 5) =>
        merged.GroupBy(m => m.Repo, StringComparer.Ordinal)
            .Select(g => new RepoCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Repo, StringComparer.Ordinal)
            .Take(Math.Max(0, take))
            .ToList();

    /// <summary>
    /// Everything for the Stats page. Only pull requests merged and reviews given inside the window
    /// (the last <paramref name="weeks"/> calendar weeks up to <paramref name="now"/>) are counted.
    /// </summary>
    public static StatsReport Compute(StatsInput input, DateTimeOffset now, int weeks = DefaultWeeks)
    {
        var count = Math.Max(1, weeks);
        var since = WeekStarts(now, count)[0];
        var merged = input.Merged.Where(m => m.MergedAt >= since && m.MergedAt <= now).ToList();
        var reviews = input.Reviews.Where(r => r.At >= since && r.At <= now).ToList();

        var firstReview = merged
            .Select(m => m.TimeToFirstReview)
            .Where(t => t.HasValue)
            .Select(t => t!.Value.TotalHours);

        return new StatsReport(
            count,
            merged.Count,
            reviews.Count,
            Summarize(merged.Select(m => m.CycleTime.TotalHours)),
            Summarize(firstReview),
            Throughput(merged, now, count),
            ReviewLoad(reviews, now, count),
            CycleTrend(merged, now, count),
            SizeDistribution(merged),
            TopRepos(merged));
    }
}

/// <summary>Short human text for durations.</summary>
public static class StatsFormat
{
    /// <summary>"25 min", "5.2 h", "30 h", "2.3 d". Under a minute is "&lt;1 min". Invalid or negative input is an en dash.</summary>
    public static string Duration(double hours)
    {
        if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0) return "–";
        if (hours < 1)
        {
            var minutes = (int)Math.Round(hours * 60);
            if (minutes < 1) return "<1 min";
            if (minutes >= 60) return "1 h";
            return minutes.ToString(CultureInfo.InvariantCulture) + " min";
        }

        if (hours < 48) return OneDecimal(hours) + " h";
        return OneDecimal(hours / 24) + " d";
    }

    private static string OneDecimal(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
}
