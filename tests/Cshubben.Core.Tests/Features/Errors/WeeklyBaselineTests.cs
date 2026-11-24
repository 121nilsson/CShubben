using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class WeeklyBaselineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 3, 20, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Default(Now);

    private static AppSpike App(double nowPerBucket) =>
        new("api", new int[288], 100, nowPerBucket, 0, 1, Trend.Flat, false);

    private static LastWeekLogs Week(int perBucket, int buckets = 24) =>
        new(new[] { new ErrorSeries("api", Enumerable.Repeat(perBucket, buckets).ToArray()) }, true);

    private static LastWeekLogs Week(params ErrorSeries[] series) => new(series, true);

    [Fact]
    public void The_last_week_window_starts_where_the_latest_hour_started_a_week_ago()
    {
        var week = WeeklyBaseline.WindowFor(Window, TimeZoneInfo.Utc);
        Assert.Equal(24, week.Buckets);
        Assert.Equal(Window.End.AddHours(-1).AddDays(-7), week.Start);
    }

    [Fact]
    public void More_than_twice_last_weeks_rate_is_higher()
    {
        var c = WeeklyBaseline.Compare(App(10), Window, Week(2));
        Assert.NotNull(c);
        Assert.Equal(WeeklyVerdict.Higher, c!.Verdict);
        Assert.Equal(120, c.NowPerHour);
        Assert.Equal(24, c.LastWeekPerHour);
    }

    [Fact]
    public void A_similar_rate_is_usual_for_the_time_of_week()
    {
        Assert.Equal(WeeklyVerdict.Usual, WeeklyBaseline.Compare(App(10), Window, Week(9))!.Verdict);
    }

    [Fact]
    public void Less_than_half_is_lower()
    {
        Assert.Equal(WeeklyVerdict.Lower, WeeklyBaseline.Compare(App(2), Window, Week(10))!.Verdict);
    }

    [Fact]
    public void Only_the_hour_that_matches_now_counts_not_the_hour_after_it()
    {
        var counts = Enumerable.Repeat(1, 12).Concat(Enumerable.Repeat(100, 12)).ToArray();
        var c = WeeklyBaseline.Compare(App(1), Window, Week(new ErrorSeries("api", counts)));
        Assert.Equal(12, c!.LastWeekPerHour);
        Assert.Equal(WeeklyVerdict.Usual, c.Verdict);
    }

    [Fact]
    public void An_application_missing_last_week_had_no_errors_then_and_unreadable_data_says_nothing()
    {
        var none = WeeklyBaseline.Compare(App(5), Window, Week());
        Assert.Equal(WeeklyVerdict.Higher, none!.Verdict);
        Assert.Equal(0, none.LastWeekPerHour);
        Assert.Null(WeeklyBaseline.Compare(App(5), Window, null));
    }

    [Fact]
    public void The_application_is_found_regardless_of_case()
    {
        var c = WeeklyBaseline.Compare(App(5), Window, Week(new ErrorSeries("API", new[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 })));
        Assert.Equal(60, c!.LastWeekPerHour);
    }

    [Theory]
    [InlineData(WeeklyVerdict.Higher, 120, 24, "Higher than a week ago: 120 / h now, 24 / h at this time last week.")]
    [InlineData(WeeklyVerdict.Lower, 24, 120, "Lower than a week ago: 24 / h now, 120 / h at this time last week.")]
    [InlineData(WeeklyVerdict.Usual, 60, 55, "Usual for this time of week: 60 / h now, 55 / h a week ago.")]
    [InlineData(WeeklyVerdict.Higher, 30, 0, "Higher than a week ago: 30 / h now, none at this time last week.")]
    public void The_sentence_names_both_rates(WeeklyVerdict verdict, double now, double last, string expected)
    {
        Assert.Equal(expected, WeeklyBaseline.Line(new WeeklyComparison(verdict, now, last)));
    }

    [Fact]
    public void Two_quiet_hours_and_missing_data_give_no_sentence()
    {
        Assert.Null(WeeklyBaseline.Line(new WeeklyComparison(WeeklyVerdict.Usual, 0.4, 0.2)));
        Assert.Null(WeeklyBaseline.Line(null));
    }

    [Fact]
    public void The_copied_summary_includes_the_comparison()
    {
        var weekly = new WeeklyComparison(WeeklyVerdict.Higher, 120, 24);
        var text = ErrorReport.App(App(10), Window, Array.Empty<ErrorGroup>(), null, null, null, Now, weekly);
        Assert.Contains("- Higher than a week ago: 120 / h now, 24 / h at this time last week.", text);
    }

    [Fact]
    public void Logs_that_do_not_reach_back_give_no_comparison_instead_of_a_false_none()
    {
        Assert.Null(WeeklyBaseline.Compare(App(5), Window, new LastWeekLogs(Array.Empty<ErrorSeries>(), false)));
    }

    [Fact]
    public void The_probe_query_counts_all_levels_of_the_namespace_over_the_whole_span()
    {
        Assert.Equal(
            "sum by (appname) (count_over_time({namespace=\"default\"}[7200s]))",
            LogQl.AllLines("default", TimeSpan.FromHours(2)));
    }

    [Fact]
    public void The_probe_answer_is_read_as_one_bucket_that_spans_the_window()
    {
        var window = WeeklyBaseline.WindowFor(Window, TimeZoneInfo.Utc);
        var probe = new ErrorWindow(window.Start, window.End - window.Start, 1);
        var json = "{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[{\"metric\":{\"appname\":\"api\"},\"values\":[["
            + probe.End.ToUnixTimeSeconds() + ",\"42\"]]}]}}";
        var series = LokiParser.ParseMatrix(json, probe);
        Assert.Equal(42, Assert.Single(series).Counts.Sum());
    }

    [Fact]
    public void The_azure_probe_counts_rows_in_the_window_and_reads_the_number()
    {
        var window = WeeklyBaseline.WindowFor(Window, TimeZoneInfo.Utc);
        Assert.Contains("summarize n = count()", AzureKql.LogCount(window), StringComparison.Ordinal);
        Assert.Equal(5, AzureKql.ParseCount("{\"results\":[{\"n\":5}]}"));
        Assert.Equal(7, AzureKql.ParseCount("[{\"n\":\"7\"}]"));
        Assert.Equal(0, AzureKql.ParseCount("{\"results\":[]}"));
    }

    [Fact]
    public void A_clock_change_in_between_moves_the_window_so_it_is_the_same_time_on_the_wall()
    {
        var stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

        // Tuesday 27 October 2026 12:00 in Stockholm, two days after the clocks went back (CEST +02:00 -> CET +01:00).
        var tuesday = new ErrorWindow(
            new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(1)) - TimeSpan.FromHours(24), TimeSpan.FromMinutes(5), 288);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(1)), tuesday.End);

        var week = WeeklyBaseline.WindowFor(tuesday, stockholm);

        // The latest hour started at 11:00 CET; a week before that is 11:00 on the wall, which was still CEST.
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.FromHours(2)), week.Start);
        Assert.Equal(24, week.Buckets);
        Assert.Equal(tuesday.Step, week.Step);

        // Exactly 7 days of absolute time would have been one hour off.
        Assert.NotEqual(tuesday.End.AddHours(-1).AddDays(-7), week.Start);
    }

    [Fact]
    public void A_wall_clock_time_that_does_not_exist_a_week_earlier_moves_to_the_hour_after()
    {
        var stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

        // 2026-04-05 02:30 CEST + 7 days is 2026-03-29 02:30, which did not exist (clocks went 02:00 -> 03:00).
        var at = new DateTimeOffset(2026, 4, 5, 2, 30, 0, TimeSpan.FromHours(2));
        var earlier = WeeklyBaseline.SameLocalTimeLastWeek(at, stockholm);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2)), earlier);
    }
}
