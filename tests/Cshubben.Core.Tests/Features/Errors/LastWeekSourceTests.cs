using System.Net;
using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class LastWeekSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 24, 12, 3, 20, TimeSpan.Zero);

    private const string EmptyMatrix = "{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[]}}";

    private static GrafanaErrorSource Source(GrafanaFake fake) =>
        new(
            new GrafanaClient(new HttpClient(fake), () => "https://grafana.example.com", () => "token"),
            new ErrorsQuery("default", false, "loki-1", Array.Empty<string>()),
            () => Noon);

    [Fact]
    public async Task Without_any_log_line_a_week_ago_nothing_is_compared_and_the_error_query_is_skipped()
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, EmptyMatrix));

        var week = await Source(fake).LoadLastWeekAsync(ErrorWindow.Default(Noon));

        Assert.False(week.LogsReachBack);
        Assert.Empty(week.Series);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task With_logs_a_week_ago_the_error_counts_of_that_hour_are_read_from_the_matching_window()
    {
        var current = ErrorWindow.Default(Noon);
        var week = WeeklyBaseline.WindowFor(current);

        var fake = new GrafanaFake(r =>
        {
            var url = r.RequestUri!.ToString();
            if (url.Contains("loglevel", StringComparison.Ordinal))
            {
                // The error counts: 4 errors in the first bucket.
                return GrafanaFake.Json(HttpStatusCode.OK,
                    "{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[{\"metric\":{\"appname\":\"binders-api\"},\"values\":[["
                    + (week.Start.ToUnixTimeSeconds() + 300) + ",\"4\"]]}]}}");
            }

            // The probe: some lines of any level over the whole two hours.
            return GrafanaFake.Json(HttpStatusCode.OK,
                "{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[{\"metric\":{\"appname\":\"binders-api\"},\"values\":[["
                + week.End.ToUnixTimeSeconds() + ",\"900\"]]}]}}");
        });

        var result = await Source(fake).LoadLastWeekAsync(current);

        Assert.True(result.LogsReachBack);
        Assert.Equal(4, Assert.Single(result.Series).Counts[0]);
        Assert.Equal(2, fake.Requests.Count);
        Assert.All(fake.Requests, q => Assert.Contains("end=" + week.End.ToUnixTimeSeconds(), q.Url, StringComparison.Ordinal));
    }

    [Fact]
    public void The_sources_say_how_many_lines_they_read_so_the_page_can_tell_when_older_ones_were_cut_off()
    {
        Assert.Equal(1000, Source(new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, "{}"))).LineLimit);
        Assert.Equal(1000, new DemoErrorSource().LineLimit);
        Assert.Equal(400, AzureLogsErrorSource.GroupRowLimit);
    }

    [Fact]
    public void The_azure_tool_is_told_to_look_back_far_enough_to_reach_last_week()
    {
        var week = WeeklyBaseline.WindowFor(ErrorWindow.Default(Noon), TimeZoneInfo.Utc);

        // The window starts 7 days and an hour before the latest whole bucket, which is 7 days, 1 hour and 3 minutes before now:
        // 170 hours rounded up, and an hour to spare.
        Assert.Equal(171, AzureKql.LookbackHours(Noon, week));

        // The normal 24 hour window needs 25 hours, which is what the tool does by default.
        Assert.Equal(26, AzureKql.LookbackHours(Noon, ErrorWindow.Default(Noon)));
    }
}
