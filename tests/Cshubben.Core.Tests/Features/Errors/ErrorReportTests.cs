using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class ErrorLinksTests
{
    [Fact]
    public void The_path_from_a_log_line_cannot_break_or_redirect_the_link()
    {
        Assert.Equal(
            "https://github.com/o/r/blob/HEAD/src/Proj/My%20File%231.cs#L3",
            ErrorLinks.FileUrl("o/r", "Proj/My File#1.cs", 3));
        Assert.Equal("https://github.com/o/r/blob/HEAD/src/etc/passwd", ErrorLinks.FileUrl("o/r", "../../etc/./passwd", null));
        Assert.Equal("https://github.com/o/r/blob/HEAD/src/Proj/File.cs", ErrorLinks.FileUrl("o/r", "Proj\\File.cs", null));
        Assert.Equal("https://github.com/o/r/blob/HEAD/src/Proj/File.cs", ErrorLinks.FileUrl("o/r", "/Proj/File.cs", null));
    }

    [Fact]
    public void A_file_link_points_at_the_deployed_commit_when_it_is_known_and_at_HEAD_when_not()
    {
        Assert.Equal(
            "https://github.com/o/r/blob/HEAD/src/Proj/File.cs#L40",
            ErrorLinks.FileUrl("o/r", "Proj/File.cs", 40));
        Assert.Equal(
            "https://github.com/o/r/blob/3f9c2ab/src/Proj/File.cs#L40",
            ErrorLinks.FileUrl("o/r", "Proj/File.cs", 40, "3f9c2ab"));
        Assert.Equal("https://github.com/o/r/blob/HEAD/src/Proj/File.cs", ErrorLinks.FileUrl("o/r", "Proj/File.cs", null));
    }

    [Fact]
    public void Without_a_repository_or_a_file_there_is_no_link()
    {
        Assert.Null(ErrorLinks.FileUrl(null, "Proj/File.cs", 1));
        Assert.Null(ErrorLinks.FileUrl("o/r", "../..", 1));
        Assert.Null(ErrorLinks.FileUrl("o/r", null, 1));
        Assert.Null(ErrorLinks.CommitUrl("o/r", ""));
        Assert.Equal("https://github.com/o/r/commit/abc1234", ErrorLinks.CommitUrl("o/r", "abc1234"));
    }
}

public class ErrorMarkersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Ending(Now, TimeSpan.FromMinutes(5), 288);

    [Fact]
    public void A_time_lands_in_the_bucket_that_contains_it_and_times_outside_the_chart_are_left_out()
    {
        var from = Window.Start;
        var indices = ErrorMarkers.Indices(from, Window.Step, 12, new[]
        {
            from + TimeSpan.FromMinutes(7),
            from + TimeSpan.FromMinutes(8),
            from + TimeSpan.FromMinutes(59),
            from - TimeSpan.FromMinutes(1),
            from + TimeSpan.FromMinutes(60),
        });

        Assert.Equal(new[] { 1, 11 }, indices.ToArray());
    }

    [Fact]
    public void No_times_or_no_buckets_give_no_markers()
    {
        Assert.Empty(ErrorMarkers.Indices(Window.Start, Window.Step, 12, Array.Empty<DateTimeOffset>()));
        Assert.Empty(ErrorMarkers.Indices(Window.Start, Window.Step, 0, new[] { Window.Start }));
        Assert.Empty(ErrorMarkers.Indices(Window.Start, TimeSpan.Zero, 12, new[] { Window.Start }));
    }

    [Fact]
    public void A_tail_chart_starts_where_its_first_bucket_starts()
    {
        Assert.Equal(Now - TimeSpan.FromHours(6), ErrorMarkers.TailFrom(Window, 72));
        Assert.Equal(Window.Start, ErrorMarkers.TailFrom(Window, 1000));

        // A deployment 20 minutes ago is the fourth bucket from the right of a six hour chart.
        var from = ErrorMarkers.TailFrom(Window, 72);
        Assert.Equal(new[] { 68 }, ErrorMarkers.Indices(from, Window.Step, 72, new[] { Now - TimeSpan.FromMinutes(20) }).ToArray());
    }
}

public class ErrorTextsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Ending(Now, TimeSpan.FromMinutes(5), 288);

    private static DeployLink Link(TimeSpan after, string creator = "anna", string reference = "main") =>
        new(new DeployedVersion("prod", "3f9c2ab1234567890", reference, creator, Now - after), after);

    [Theory]
    [InlineData(30, "less than a minute")]
    [InlineData(12 * 60, "12 min")]
    [InlineData(125 * 60, "2 h 5 min")]
    [InlineData(7 * 3600, "7 h")]
    [InlineData(36 * 3600, "36 h")]
    [InlineData(3 * 24 * 3600, "3 d")]
    public void A_length_of_time_is_written_in_the_largest_sensible_unit(int seconds, string expected) =>
        Assert.Equal(expected, ErrorsText.Span(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_deploy_line_names_the_commit_the_person_and_the_branch()
    {
        Assert.Equal(
            "First seen 12 min after deploy 3f9c2ab by anna from main",
            ErrorsText.DeployLine("First seen", Link(TimeSpan.FromMinutes(12))));
        Assert.Equal(
            "First seen right at deploy 3f9c2ab by anna from main",
            ErrorsText.DeployLine("First seen", Link(TimeSpan.FromSeconds(20))));
        Assert.Equal(
            "Started 5 min after deploy 3f9c2ab",
            ErrorsText.DeployLine("Started", Link(TimeSpan.FromMinutes(5), creator: "", reference: "")));
    }

    [Fact]
    public void The_start_line_says_when_and_after_which_deployment()
    {
        var started = Now - TimeSpan.FromMinutes(51);
        Assert.Equal("Started 51 min ago", ErrorsText.StartLine(started, Now, null));
        Assert.Equal(
            "Started 51 min ago, 12 min after deploy 3f9c2ab by anna from main",
            ErrorsText.StartLine(started, Now, Link(TimeSpan.FromMinutes(12))));
    }

    [Fact]
    public void A_chart_is_described_in_words_for_screen_readers()
    {
        var counts = new int[288];
        counts[280] = 18;
        counts[200] = 3; // outside the last six hours

        Assert.Equal(
            "Errors per 5 min over the last 6 hours: 18 in total, peak 18 at 11:20.",
            ErrorsText.ChartSummary(counts, Window, 72, TimeZoneInfo.Utc));
        Assert.Equal("No errors in the last 6 hours.", ErrorsText.ChartSummary(new int[288], Window, 72, TimeZoneInfo.Utc));
        Assert.Equal("No errors in the last 60 minutes.", ErrorsText.ChartSummary(new int[288], Window, 12, TimeZoneInfo.Utc));
    }
}

public class ErrorReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Ending(Now, TimeSpan.FromMinutes(5), 288);
    private static readonly DeployedVersion Deployed = new("prod", "3f9c2ab1234567890", "main", "anna", Now - TimeSpan.FromMinutes(60));

    private static ErrorGroup Group(string title, int count, string? message = null) =>
        new("k", title, "", null, ErrorKind.Exception, count, Now - TimeSpan.FromHours(2), Now - TimeSpan.FromMinutes(3),
            new[] { "pod-1", "pod-2" }, new int[288], "Acme.Svc.Run", "Proj/File.cs", 40,
            message is null ? null : new[] { new LogEntry(Now, "app", "pod-1", "Error", message) });

    [Fact]
    public void An_application_summary_has_the_rate_the_start_the_deployment_and_the_top_errors()
    {
        var spike = new AppSpike("binders-api", new int[288], 123, 4.0, 1.0, 9.0, Trend.Up, true);
        var link = new DeployLink(Deployed, TimeSpan.FromMinutes(12));
        var groups = new[] { Group("NullReferenceException · File.cs:40", 100), Group("TimeoutException · Repo.cs:8", 23) };

        var text = ErrorReport.App(spike, Window, groups, Now - TimeSpan.FromMinutes(48), link, "o/r", Now);

        Assert.StartsWith("### binders-api: errors spiking 9×", text, StringComparison.Ordinal);
        Assert.Contains("- 48 / h now, 123 errors in the last 24 hours, rising", text, StringComparison.Ordinal);
        Assert.Contains("- Started 48 min ago, 12 min after deploy 3f9c2ab by anna from main", text, StringComparison.Ordinal);
        Assert.Contains("- Deployed commit: https://github.com/o/r/commit/3f9c2ab1234567890", text, StringComparison.Ordinal);
        Assert.Contains("- Repository: o/r", text, StringComparison.Ordinal);
        Assert.Contains("1. NullReferenceException · File.cs:40: 100 errors, first 2 h ago, last 3 min ago", text, StringComparison.Ordinal);
        Assert.Contains("2. TimeoutException · Repo.cs:8: 23 errors", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quiet_application_and_a_long_list_are_summarised_briefly()
    {
        var quiet = new AppSpike("contracts-api", new int[288], 0, 0, 0, 0, Trend.Flat, false);
        Assert.Equal("### contracts-api: no errors", ErrorReport.App(quiet, Window, Array.Empty<ErrorGroup>(), null, null, null, Now));

        var busy = new AppSpike("a", new int[288], 7, 1, 1, 1, Trend.Flat, false);
        var many = Enumerable.Range(1, 8).Select(i => Group("E" + i, i)).ToArray();
        var text = ErrorReport.App(busy, Window, many, null, null, null, Now);
        Assert.Contains("5. E5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("6. E6", text, StringComparison.Ordinal);
        Assert.Contains("(and 3 more)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_summary_links_the_code_at_the_deployed_commit_and_quotes_the_message_without_sensitive_values()
    {
        var group = Group("NullReferenceException · File.cs:40", 12,
            "[2026-11-24T11:57:00Z Error] pod-1 - Acme.Logger - t1\nCould not load user anna@example.com\nSystem.NullReferenceException: boom");
        var link = new DeployLink(Deployed, TimeSpan.FromMinutes(8));

        var text = ErrorReport.Group("binders-api", group, GroupStatus.New, "o/r", Deployed, link, Now);

        Assert.StartsWith("**NullReferenceException · File.cs:40** in binders-api", text, StringComparison.Ordinal);
        Assert.Contains("- 12 errors in 24 hours, first 2 h ago, last 3 min ago, on 2 pods", text, StringComparison.Ordinal);
        Assert.Contains("- Status: new", text, StringComparison.Ordinal);
        Assert.Contains("- First seen 8 min after deploy 3f9c2ab by anna from main", text, StringComparison.Ordinal);
        Assert.Contains("- Code: `Acme.Svc.Run` at [Proj/File.cs:40](https://github.com/o/r/blob/3f9c2ab1234567890/src/Proj/File.cs#L40)", text, StringComparison.Ordinal);
        Assert.Contains("> Could not load user <email>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("anna@example.com", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ongoing_error_without_a_repository_has_no_status_line_and_no_link()
    {
        var text = ErrorReport.Group("app", Group("E", 1), GroupStatus.Ongoing, null, null, null, Now);

        Assert.DoesNotContain("Status:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("](", text, StringComparison.Ordinal);
        Assert.Contains("- Code: `Acme.Svc.Run`", text, StringComparison.Ordinal);
        Assert.Contains("- 1 error in 24 hours", text, StringComparison.Ordinal);
    }
}
