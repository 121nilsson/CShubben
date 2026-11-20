using Cshubben.Core.Features.Stats;
using Xunit;

namespace Cshubben.Core.Tests.Features.Stats;

public class StatsTests
{
    // Friday 2026-11-20 12:00 UTC. Its week starts Monday 2026-11-16.
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static MergedPrRecord Pr(double cycleHours, double? reviewAfterHours = null, int lines = 100, double mergedHoursAgo = 1, string repo = "acme/api")
    {
        var merged = Now - TimeSpan.FromHours(mergedHoursAgo);
        var opened = merged - TimeSpan.FromHours(cycleHours);
        DateTimeOffset? review = reviewAfterHours is null ? null : opened + TimeSpan.FromHours(reviewAfterHours.Value);
        return new MergedPrRecord(repo, 1, "t", "alex", opened, merged, review, lines, 0, 1);
    }

    // ---- percentile and median --------------------------------------------------------------------------

    [Fact]
    public void Median_of_empty_is_zero() => Assert.Equal(0, StatsCalculator.Median(Array.Empty<double>()));

    [Fact]
    public void Median_of_one_value_is_that_value() => Assert.Equal(7, StatsCalculator.Median(new[] { 7.0 }));

    [Fact]
    public void Median_of_odd_count_is_the_middle_value() => Assert.Equal(3, StatsCalculator.Median(new[] { 5.0, 1.0, 3.0 }));

    [Fact]
    public void Median_of_even_count_interpolates() => Assert.Equal(2.5, StatsCalculator.Median(new[] { 4.0, 1.0, 2.0, 3.0 }));

    [Fact]
    public void P90_interpolates_between_ranks()
    {
        // 1..10: position 8.1 -> 9 + 0.1 * (10 - 9)
        var values = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();
        Assert.Equal(9.1, StatsCalculator.Percentile(values, 90), 6);
    }

    [Fact]
    public void Percentile_is_clamped_to_zero_and_hundred()
    {
        var values = new[] { 1.0, 2.0, 3.0 };
        Assert.Equal(1, StatsCalculator.Percentile(values, -5));
        Assert.Equal(3, StatsCalculator.Percentile(values, 250));
    }

    [Fact]
    public void Summarize_empty_is_the_empty_spread() => Assert.Equal(Spread.Empty, StatsCalculator.Summarize(Array.Empty<double>()));

    [Fact]
    public void Summarize_reports_count_mean_and_max()
    {
        var spread = StatsCalculator.Summarize(new[] { 2.0, 4.0, 6.0 });
        Assert.Equal(3, spread.Count);
        Assert.Equal(4, spread.Mean);
        Assert.Equal(6, spread.Max);
        Assert.Equal(4, spread.Median);
    }

    // ---- records ----------------------------------------------------------------------------------------

    [Fact]
    public void Cycle_time_is_open_to_merge() => Assert.Equal(TimeSpan.FromHours(30), Pr(30).CycleTime);

    [Fact]
    public void Cycle_time_is_never_negative()
    {
        var pr = Pr(5) with { OpenedAt = Now + TimeSpan.FromDays(1) };
        Assert.Equal(TimeSpan.Zero, pr.CycleTime);
    }

    [Fact]
    public void Time_to_first_review_is_open_to_review() => Assert.Equal(TimeSpan.FromHours(2), Pr(10, 2).TimeToFirstReview);

    [Fact]
    public void Time_to_first_review_is_null_without_review() => Assert.Null(Pr(10).TimeToFirstReview);

    [Fact]
    public void Time_to_first_review_after_merge_is_treated_as_bad_data() => Assert.Null(Pr(10, 12).TimeToFirstReview);

    [Fact]
    public void Time_to_first_review_before_opening_is_zero() => Assert.Equal(TimeSpan.Zero, Pr(10, -3).TimeToFirstReview);

    // ---- weeks ------------------------------------------------------------------------------------------

    [Fact]
    public void Week_starts_on_monday_utc()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), StatsCalculator.WeekStart(Now));
        // Sunday belongs to the week that began six days earlier.
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), StatsCalculator.WeekStart(new DateTimeOffset(2026, 11, 22, 23, 59, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero), StatsCalculator.WeekStart(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Week_start_uses_the_utc_day_for_other_offsets()
    {
        // Monday 00:30 at +02:00 is still Sunday 22:30 UTC, so it belongs to the previous week.
        var at = new DateTimeOffset(2026, 11, 16, 0, 30, 0, TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), StatsCalculator.WeekStart(at));
    }

    [Fact]
    public void Week_starts_are_oldest_first_and_end_with_the_current_week()
    {
        var starts = StatsCalculator.WeekStarts(Now, 4);
        Assert.Equal(4, starts.Count);
        Assert.Equal(StatsCalculator.WeekStart(Now), starts[3]);
        Assert.Equal(starts[0].AddDays(21), starts[3]);
    }

    [Fact]
    public void Weekly_counts_are_zero_filled()
    {
        var counts = StatsCalculator.WeeklyCounts(new[] { Now - TimeSpan.FromHours(1) }, Now, 3);
        Assert.Equal(new[] { 0, 0, 1 }, counts.Select(c => c.Count).ToArray());
    }

    [Fact]
    public void Weekly_counts_ignore_the_future_and_the_distant_past()
    {
        var times = new[] { Now + TimeSpan.FromHours(1), Now - TimeSpan.FromDays(200), Now - TimeSpan.FromDays(8) };
        var counts = StatsCalculator.WeeklyCounts(times, Now, 3);
        Assert.Equal(new[] { 0, 1, 0 }, counts.Select(c => c.Count).ToArray());
    }

    [Fact]
    public void Throughput_counts_merges_per_week()
    {
        var prs = new[] { Pr(5, mergedHoursAgo: 1), Pr(5, mergedHoursAgo: 2), Pr(5, mergedHoursAgo: 24 * 8) };
        var counts = StatsCalculator.Throughput(prs, Now, 2);
        Assert.Equal(new[] { 1, 2 }, counts.Select(c => c.Count).ToArray());
    }

    [Fact]
    public void Review_load_counts_reviews_per_week()
    {
        var reviews = new[]
        {
            new ReviewRecord("acme/api", 1, Now - TimeSpan.FromHours(2)),
            new ReviewRecord("acme/api", 2, Now - TimeSpan.FromHours(3)),
            new ReviewRecord("acme/web", 3, Now - TimeSpan.FromDays(9)),
        };
        Assert.Equal(new[] { 1, 2 }, StatsCalculator.ReviewLoad(reviews, Now, 2).Select(c => c.Count).ToArray());
    }

    [Fact]
    public void Cycle_trend_gives_the_median_per_week_and_zero_for_empty_weeks()
    {
        var prs = new[] { Pr(10, mergedHoursAgo: 1), Pr(30, mergedHoursAgo: 2), Pr(50, mergedHoursAgo: 3) };
        var trend = StatsCalculator.CycleTrend(prs, Now, 2);
        Assert.Equal(0, trend[0].Count);
        Assert.Equal(0, trend[0].MedianHours);
        Assert.Equal(3, trend[1].Count);
        Assert.Equal(30, trend[1].MedianHours);
    }

    // ---- sizes ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "XS")]
    [InlineData(50, "XS")]
    [InlineData(51, "S")]
    [InlineData(200, "S")]
    [InlineData(201, "M")]
    [InlineData(500, "M")]
    [InlineData(501, "L")]
    [InlineData(1000, "L")]
    [InlineData(1001, "XL")]
    public void Size_label_boundaries(int lines, string expected) => Assert.Equal(expected, StatsCalculator.SizeLabel(lines));

    [Fact]
    public void Size_distribution_counts_and_fractions()
    {
        var prs = new[] { Pr(1, lines: 10), Pr(1, lines: 20), Pr(1, lines: 30), Pr(1, lines: 5000) };
        var sizes = StatsCalculator.SizeDistribution(prs);
        Assert.Equal(new[] { "XS", "S", "M", "L", "XL" }, sizes.Select(s => s.Label).ToArray());
        Assert.Equal(new[] { 3, 0, 0, 0, 1 }, sizes.Select(s => s.Count).ToArray());
        Assert.Equal(0.75, sizes[0].Fraction, 6);
        Assert.Equal(1.0, sizes.Sum(s => s.Fraction), 6);
    }

    [Fact]
    public void Size_distribution_of_nothing_has_zero_fractions()
    {
        var sizes = StatsCalculator.SizeDistribution(Array.Empty<MergedPrRecord>());
        Assert.Equal(5, sizes.Count);
        Assert.All(sizes, s => Assert.Equal(0, s.Fraction));
    }

    // ---- repos ------------------------------------------------------------------------------------------

    [Fact]
    public void Top_repos_sort_by_count_then_name_and_are_limited()
    {
        var prs = new[] { Pr(1, repo: "b"), Pr(1, repo: "a"), Pr(1, repo: "c"), Pr(1, repo: "c") };
        var top = StatsCalculator.TopRepos(prs, 2);
        Assert.Equal(new[] { "c", "a" }, top.Select(r => r.Repo).ToArray());
        Assert.Equal(2, top[0].Count);
    }

    // ---- report -----------------------------------------------------------------------------------------

    [Fact]
    public void Compute_with_no_data_is_all_zero_but_still_has_weeks()
    {
        var report = StatsCalculator.Compute(new StatsInput(Array.Empty<MergedPrRecord>(), Array.Empty<ReviewRecord>()), Now, 6);
        Assert.Equal(0, report.MergedCount);
        Assert.Equal(Spread.Empty, report.CycleTime);
        Assert.Equal(Spread.Empty, report.FirstReview);
        Assert.Equal(6, report.Throughput.Count);
        Assert.Equal(6, report.ReviewLoad.Count);
    }

    [Fact]
    public void Compute_only_counts_the_window()
    {
        var inside = Pr(10, mergedHoursAgo: 5);
        var outside = Pr(500, mergedHoursAgo: 24 * 60);
        var future = Pr(10, mergedHoursAgo: -5);
        var report = StatsCalculator.Compute(new StatsInput(new[] { inside, outside, future }, Array.Empty<ReviewRecord>()), Now, 4);
        Assert.Equal(1, report.MergedCount);
        Assert.Equal(10, report.CycleTime.Median);
    }

    [Fact]
    public void Compute_first_review_skips_prs_without_review()
    {
        var prs = new[] { Pr(10, 1), Pr(10, 3), Pr(10) };
        var report = StatsCalculator.Compute(new StatsInput(prs, Array.Empty<ReviewRecord>()), Now, 4);
        Assert.Equal(3, report.CycleTime.Count);
        Assert.Equal(2, report.FirstReview.Count);
        Assert.Equal(2, report.FirstReview.Median);
    }

    // ---- formatting -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, "<1 min")]
    [InlineData(0.25, "15 min")]
    [InlineData(0.999, "1 h")]
    [InlineData(1.0, "1 h")]
    [InlineData(5.25, "5.3 h")]
    [InlineData(30.0, "30 h")]
    [InlineData(48.0, "2 d")]
    [InlineData(78.0, "3.3 d")]
    [InlineData(-1.0, "–")]
    public void Duration_format(double hours, string expected) => Assert.Equal(expected, StatsFormat.Duration(hours));

    [Fact]
    public void Duration_format_handles_nan() => Assert.Equal("–", StatsFormat.Duration(double.NaN));

    // ---- demo data --------------------------------------------------------------------------------------

    [Fact]
    public void Demo_data_is_deterministic()
    {
        var a = StatsCalculator.Compute(DemoStats.Build(Now), Now);
        var b = StatsCalculator.Compute(DemoStats.Build(Now), Now);
        Assert.Equal(a.MergedCount, b.MergedCount);
        Assert.Equal(a.CycleTime, b.CycleTime);
        Assert.Equal(a.Throughput.Select(t => t.Count), b.Throughput.Select(t => t.Count));
    }

    [Fact]
    public void Demo_data_fills_most_of_the_window_with_sane_values()
    {
        var report = StatsCalculator.Compute(DemoStats.Build(Now), Now);
        Assert.True(report.MergedCount >= 40);
        Assert.True(report.ReviewCount >= 50);
        Assert.True(report.CycleTime.Median > 0);
        Assert.True(report.CycleTime.P90 >= report.CycleTime.Median);
        Assert.True(report.FirstReview.Median < report.CycleTime.Median);
        Assert.Contains(report.Throughput, t => t.Count > 0);
    }
}
