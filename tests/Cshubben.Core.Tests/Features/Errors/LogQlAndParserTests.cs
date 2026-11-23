using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class LogQlTests
{
    [Fact]
    public void Quote_escapes_quotes_backslashes_and_line_breaks()
    {
        Assert.Equal("\"plain\"", LogQl.Quote("plain"));
        Assert.Equal("\"a\\\"b\\\\c\"", LogQl.Quote("a\"b\\c"));
        Assert.Equal("\"line\\none\\ttab\"", LogQl.Quote("line\none\ttab"));
    }

    [Fact]
    public void Selector_covers_namespace_app_and_error_levels()
    {
        Assert.Equal(
            "{namespace=\"default\", loglevel=~\"(?i)error|critical|fatal\"}",
            LogQl.Selector("default", null, false));
        Assert.Equal(
            "{namespace=\"default\", appname=\"binders-api\", loglevel=~\"(?i)error|critical|fatal\"}",
            LogQl.Selector("default", "binders-api", false));
        Assert.Contains("warn", LogQl.Selector("default", null, true), StringComparison.Ordinal);
    }

    [Fact]
    public void A_hostile_app_name_stays_inside_its_quotes()
    {
        var selector = LogQl.Selector("default", "x\"} |= \"y", false);
        Assert.Equal(
            "{namespace=\"default\", appname=\"x\\\"} |= \\\"y\", loglevel=~\"(?i)error|critical|fatal\"}",
            selector);
    }

    [Fact]
    public void Error_counts_are_one_query_for_all_apps_over_one_step()
    {
        Assert.Equal(
            "sum by (appname) (count_over_time({namespace=\"default\", loglevel=~\"(?i)error|critical|fatal\"}[300s]))",
            LogQl.ErrorCounts("default", false, TimeSpan.FromMinutes(5)));
    }
}

public class ErrorWindowTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_ends_at_the_last_whole_step_and_covers_the_buckets()
    {
        var window = ErrorWindow.Ending(Noon.AddMinutes(3).AddSeconds(14), TimeSpan.FromMinutes(5), 12);
        Assert.Equal(Noon, window.End);
        Assert.Equal(Noon.AddHours(-1), window.Start);
        Assert.Equal(12, window.Buckets);
    }

    [Fact]
    public void Default_is_the_last_24_hours_in_five_minute_buckets()
    {
        var window = ErrorWindow.Default(Noon);
        Assert.Equal(288, window.Buckets);
        Assert.Equal(TimeSpan.FromMinutes(5), window.Step);
        Assert.Equal(Noon.AddHours(-24), window.Start);
    }

    [Fact]
    public void IndexOf_gives_the_bucket_or_minus_one_outside()
    {
        var window = ErrorWindow.Ending(Noon, TimeSpan.FromMinutes(5), 12);
        Assert.Equal(0, window.IndexOf(window.Start));
        Assert.Equal(0, window.IndexOf(window.Start.AddMinutes(4).AddSeconds(59)));
        Assert.Equal(1, window.IndexOf(window.Start.AddMinutes(5)));
        Assert.Equal(11, window.IndexOf(Noon.AddSeconds(-1)));
        Assert.Equal(-1, window.IndexOf(Noon));
        Assert.Equal(-1, window.IndexOf(window.Start.AddSeconds(-1)));
    }

    [Fact]
    public void A_loki_sample_belongs_to_the_bucket_it_counted()
    {
        var window = ErrorWindow.Ending(Noon, TimeSpan.FromMinutes(5), 12);
        var start = window.Start.ToUnixTimeSeconds();
        Assert.Equal(0, window.IndexOfSampleEnd(start + 300));
        Assert.Equal(1, window.IndexOfSampleEnd(start + 600));
        Assert.Equal(11, window.IndexOfSampleEnd(start + 3600));
        Assert.Equal(-1, window.IndexOfSampleEnd(start));
        Assert.Equal(-1, window.IndexOfSampleEnd(start + 3900));
    }

    [Fact]
    public void A_window_needs_a_step_and_buckets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ErrorWindow.Ending(Noon, TimeSpan.Zero, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => ErrorWindow.Ending(Noon, TimeSpan.FromMinutes(5), 0));
    }
}

public class LokiParserTests
{
    private static readonly ErrorWindow Window = ErrorWindow.Ending(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(5), 12);

    private static string Sample(int number, string value) =>
        "[" + (Window.Start.ToUnixTimeSeconds() + 300L * number) + ".000,\"" + value + "\"]";

    private static string Series(string app, params string[] samples) =>
        "{\"metric\":{\"appname\":\"" + app + "\"},\"values\":[" + string.Join(",", samples) + "]}";

    private static string Matrix(params string[] series) =>
        "{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[" + string.Join(",", series) + "]}}";

    [Fact]
    public void Matrix_becomes_dense_counts_per_bucket()
    {
        var json = Matrix(
            Series("binders-api", Sample(1, "3"), Sample(2, "4"), Sample(12, "9")),
            Series("properties-api", Sample(6, "1")));

        var result = LokiParser.ParseMatrix(json, Window);

        Assert.Equal(new[] { "binders-api", "properties-api" }, result.Select(s => s.App).ToArray());
        var binders = result[0].Counts;
        Assert.Equal(12, binders.Count);
        Assert.Equal(3, binders[0]);
        Assert.Equal(4, binders[1]);
        Assert.Equal(9, binders[11]);
        Assert.Equal(16, binders.Sum());
        Assert.Equal(1, result[1].Counts[5]);
    }

    [Fact]
    public void Samples_outside_the_window_and_bad_values_are_ignored()
    {
        var json = Matrix(Series("a-api", Sample(0, "7"), Sample(13, "7"), Sample(2, "NaN"), Sample(3, "oops"), Sample(4, "2")));
        var counts = LokiParser.ParseMatrix(json, Window)[0].Counts;
        Assert.Equal(2, counts.Sum());
        Assert.Equal(2, counts[3]);
    }

    [Fact]
    public void Two_series_of_the_same_app_are_added_and_series_without_an_app_are_skipped()
    {
        var json = Matrix(
            Series("a-api", Sample(1, "2")),
            Series("a-api", Sample(1, "3")),
            "{\"metric\":{},\"values\":[" + Sample(1, "5") + "]}");
        var result = LokiParser.ParseMatrix(json, Window);
        Assert.Equal("a-api", Assert.Single(result).App);
        Assert.Equal(5, result[0].Counts[0]);
    }

    [Fact]
    public void A_loki_error_becomes_a_calm_exception()
    {
        var ex = Assert.Throws<GrafanaException>(() => LokiParser.ParseMatrix("{\"status\":\"error\",\"error\":\"parse error at line 1\"}", Window));
        Assert.Contains("parse error at line 1", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"status\":\"success\"}")]
    [InlineData("{\"status\":\"success\",\"data\":{\"result\":42}}")]
    public void Unreadable_answers_throw(string json)
    {
        var ex = Assert.Throws<GrafanaException>(() => LokiParser.ParseMatrix(json, Window));
        Assert.Contains("could not be read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Streams_become_lines_oldest_first_with_labels()
    {
        const string json = """
            { "status": "success", "data": { "resultType": "streams", "result": [
              { "stream": { "appname": "binders-api", "pod": "binders-api-1", "loglevel": "Error" },
                "values": [ ["1764331909735000000", "newer"], ["1764331900000000000", "older", {"structured":"x"}] ] },
              { "stream": { "appname": "binders-api", "pod": "binders-api-2", "level": "fatal" },
                "values": [ ["1764331905000000000", "middle"], ["not a number", "skipped"] ] }
            ] } }
            """;

        var lines = LokiParser.ParseStreams(json);

        Assert.Equal(new[] { "older", "middle", "newer" }, lines.Select(l => l.Message).ToArray());
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1764331909735), lines[2].At);
        Assert.Equal("binders-api-1", lines[2].Pod);
        Assert.Equal("Error", lines[2].Level);
        Assert.Equal("fatal", lines[1].Level);
        Assert.Equal("binders-api", lines[1].App);
    }

    [Fact]
    public void String_lists_and_datasources_are_read()
    {
        Assert.Equal(new[] { "appname", "namespace" }, LokiParser.ParseStringList("{\"status\":\"success\",\"data\":[\"appname\",\"\",\"namespace\",1]}").ToArray());

        const string json = """
            [ { "uid": "prom1", "name": "Prometheus", "type": "prometheus" },
              { "uid": "loki1", "name": "Loki prod", "type": "loki" },
              { "uid": "loki2", "name": "Loki test", "type": "Loki" },
              { "name": "no uid", "type": "loki" } ]
            """;
        var sources = LokiParser.ParseDatasources(json);
        Assert.Equal(new[] { "loki1", "loki2" }, sources.Select(s => s.Uid).ToArray());
        Assert.Equal("Loki prod", sources[0].Name);
    }

    [Fact]
    public void PickLoki_prefers_the_configured_uid_or_name_else_the_first()
    {
        var sources = new[] { new LokiDatasource("loki1", "Loki prod"), new LokiDatasource("loki2", "Loki test") };
        Assert.Equal("loki1", LokiParser.PickLoki(sources, null)!.Uid);
        Assert.Equal("loki2", LokiParser.PickLoki(sources, " LOKI2 ")!.Uid);
        Assert.Equal("loki2", LokiParser.PickLoki(sources, "loki test")!.Uid);
        Assert.Null(LokiParser.PickLoki(sources, "other"));
        Assert.Null(LokiParser.PickLoki(Array.Empty<LokiDatasource>(), null));
    }

    [Fact]
    public void Shorten_keeps_one_short_line()
    {
        Assert.Equal("a b", LokiParser.Shorten("a\r\nb"));
        var shortened = LokiParser.Shorten(new string('x', 500));
        Assert.Equal(200, shortened.Length);
        Assert.EndsWith("…", shortened, StringComparison.Ordinal);
    }
}
