using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class ErrorFingerprintTests
{
    private const string Header = "[2026-11-23T13:11:49.1234567Z Error] binders-api-bbc6c45f6-fp46m - Acme.BuildingBlocks.Core.Authorization.Runtime.TenantBasedRuntimeService - 0a1b2c3d4e5f6a7b";

    private static string NullReference(string resource) => string.Join("\n",
        Header,
        "Could not get the user's permissions on resource: 'Id: \"" + resource + "\"'",
        "System.NullReferenceException: Object reference not set to an instance of an object.",
        "   at Acme.BuildingBlocks.Infrastructure.Authorization.PolicyServer.PolicyServerRuntimeRepository.GetGroups(IEnumerable`1 tenantPath) in /_/src/Acme.BuildingBlocks.Infrastructure.Authorization/PolicyServer/PolicyServerRuntimeRepository.cs:line 154",
        "   at Acme.BuildingBlocks.Core.Authorization.Runtime.TenantBasedRuntimeService`4.GetPermissionsToResource(TResource resource) in /_/src/Acme.BuildingBlocks.Core/Authorization/Runtime/TenantBasedRuntimeService.cs:line 109");

    [Fact]
    public void An_exception_is_identified_by_type_logger_and_top_application_frame()
    {
        var f = ErrorFingerprints.Of(NullReference("OKND9LTTC"));

        Assert.Equal(ErrorKind.Exception, f.Kind);
        Assert.Equal("System.NullReferenceException", f.ExceptionType);
        Assert.Equal("Acme.BuildingBlocks.Infrastructure.Authorization.PolicyServer.PolicyServerRuntimeRepository.GetGroups", f.Method);
        Assert.Equal("Acme.BuildingBlocks.Infrastructure.Authorization/PolicyServer/PolicyServerRuntimeRepository.cs", f.File);
        Assert.Equal(154, f.Line);
        Assert.Equal("NullReferenceException · PolicyServerRuntimeRepository.cs:154", f.Title);
        Assert.Contains("permissions", f.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Ids_in_the_message_do_not_split_a_problem()
    {
        Assert.Equal(
            ErrorFingerprints.Of(NullReference("OKND9LTTC")).Key,
            ErrorFingerprints.Of(NullReference("ZZQX1AB77")).Key);
    }

    [Fact]
    public void Another_line_or_exception_is_another_problem()
    {
        var other = NullReference("A").Replace("line 154", "line 155", StringComparison.Ordinal);
        Assert.NotEqual(ErrorFingerprints.Of(NullReference("A")).Key, ErrorFingerprints.Of(other).Key);

        var argument = NullReference("A").Replace("System.NullReferenceException", "System.ArgumentNullException", StringComparison.Ordinal);
        Assert.NotEqual(ErrorFingerprints.Of(NullReference("A")).Key, ErrorFingerprints.Of(argument).Key);
    }

    [Fact]
    public void Framework_frames_without_a_file_are_skipped_for_the_top_frame()
    {
        var f = ErrorFingerprints.Of(string.Join("\n",
            "System.ArgumentNullException: Value cannot be null. (Parameter 'source')",
            "   at System.ThrowHelper.ThrowArgumentNullException(ExceptionArgument argument)",
            "   at Acme.Notifications.Handler.Process(EventEnvelope e) in /src/Notifications/Handler.cs:line 58"));

        Assert.Equal("Acme.Notifications.Handler.Process", f.Method);
        Assert.Equal("Notifications/Handler.cs", f.File);
        Assert.Equal(58, f.Line);
    }

    [Fact]
    public void Without_source_info_the_first_frame_is_used()
    {
        var f = ErrorFingerprints.Of("System.TimeoutException: timed out\n   at Acme.Repo.Load(Guid id)");
        Assert.Equal("Acme.Repo.Load", f.Method);
        Assert.Null(f.File);
        Assert.Null(f.Line);
        Assert.Equal("TimeoutException · Repo.Load", f.Title);
    }

    [Fact]
    public void A_missing_backend_role_is_its_own_kind_with_a_hint()
    {
        var f = ErrorFingerprints.Of(
            "[2026-11-23T13:11:49Z Warning] pod - Acme.Auth.Handler\nUser with clientId '\"3f0c1e7a-0000-4000-8000-000000000001\"' does not have backend_role '\"binders_get_participants\"' required to fulfill machine requirement.");

        Assert.Equal(ErrorKind.MissingBackendRole, f.Kind);
        Assert.Equal("Missing backend_role binders_get_participants", f.Title);
        Assert.Contains("PolicyServer", f.Hint, StringComparison.Ordinal);
        Assert.Contains("binders_get_participants", f.Hint, StringComparison.Ordinal);
        Assert.Contains("3f0c1e7a-0000-4000-8000-000000000001", f.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_message_is_grouped_by_its_normalised_text()
    {
        const string logger = "[2026-11-23T13:11:49Z Error] pod - Acme.Activities.Api.Jobs.CleanupJob - t1\n";
        var a = ErrorFingerprints.Of(logger + "Cleanup of 12 expired items failed, retrying in 5 minutes.");
        var b = ErrorFingerprints.Of(logger + "Cleanup of 87 expired items failed, retrying in 5 minutes.");
        var c = ErrorFingerprints.Of(logger + "Something else failed.");

        Assert.Equal(ErrorKind.Message, a.Kind);
        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, c.Key);
        Assert.Equal("Cleanup of <n> expired items failed, retrying in <n> minutes.", a.Title);
        Assert.Equal("CleanupJob", a.Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Empty_input_does_not_throw(string? message)
    {
        var f = ErrorFingerprints.Of(message);
        Assert.Equal(ErrorKind.Message, f.Kind);
        Assert.Equal("Error without a message", f.Title);
    }

    [Fact]
    public void NormalizeMessage_replaces_guids_hashes_codes_and_numbers()
    {
        Assert.Equal(
            "user <id> hash <id> code <id> took <n> ms",
            ErrorFingerprints.NormalizeMessage("user 3f0c1e7a-0000-4000-8000-000000000001 hash 0a1b2c3d4e5f6a7b code OKND9LTTC took 1500 ms"));
        Assert.Equal("a b", ErrorFingerprints.NormalizeMessage("  a \t b  "));
        Assert.Equal("HTTP", ErrorFingerprints.NormalizeMessage("HTTP"));
    }

    [Fact]
    public void CleanMethod_and_NormalizePath()
    {
        Assert.Equal("A.B.Run", ErrorFingerprints.CleanMethod("A.B`4.Run(IEnumerable`1 x, Int32 y)"));
        Assert.Equal("A.B.Run", ErrorFingerprints.CleanMethod("A.B.Run[T](T x)"));
        Assert.Equal("Proj/File.cs", ErrorFingerprints.NormalizePath("/_/src/Proj/File.cs"));
        Assert.Equal("Proj/File.cs", ErrorFingerprints.NormalizePath("/src/Proj/File.cs"));
        Assert.Equal("Proj/File.cs", ErrorFingerprints.NormalizePath("/_\\src\\Proj\\File.cs".Replace("/_\\", "/_/")));
        Assert.Equal("other/File.cs", ErrorFingerprints.NormalizePath("other/File.cs"));
    }
}

public class ErrorGroupingTests
{
    private static readonly ErrorWindow Window = ErrorWindow.Ending(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(5), 12);

    private static LogEntry Entry(int minutesIntoWindow, string pod, string message) =>
        new(Window.Start.AddMinutes(minutesIntoWindow), "binders-api", pod, "Error", message);

    private static string Throw(string type, int line) =>
        type + ": boom\n   at Acme.Svc.Run() in /src/Svc/Run.cs:line " + line;

    [Fact]
    public void Same_problem_is_one_group_with_counts_times_and_pods()
    {
        var groups = ErrorGrouping.Group(new[]
        {
            Entry(2, "pod-b", Throw("System.InvalidOperationException", 10)),
            Entry(7, "pod-a", Throw("System.InvalidOperationException", 10)),
            Entry(8, "pod-a", Throw("System.InvalidOperationException", 10)),
        }, Window);

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Count);
        Assert.Equal(Window.Start.AddMinutes(2), group.FirstSeen);
        Assert.Equal(Window.Start.AddMinutes(8), group.LastSeen);
        Assert.Equal(new[] { "pod-a", "pod-b" }, group.Pods.ToArray());
        Assert.Equal(12, group.Counts.Count);
        Assert.Equal(1, group.Counts[0]);
        Assert.Equal(2, group.Counts[1]);
        Assert.Equal(3, group.Counts.Sum());
        Assert.Equal("Svc/Run.cs", group.File);
        Assert.Equal("Acme.Svc.Run", group.Method);
    }

    [Fact]
    public void Groups_are_ordered_by_count_then_recency_and_capped()
    {
        var entries = new List<LogEntry>
        {
            Entry(1, "p", Throw("System.AException", 1)),
            Entry(2, "p", Throw("System.BException", 2)),
            Entry(3, "p", Throw("System.BException", 2)),
            Entry(9, "p", Throw("System.CException", 3)),
        };

        var groups = ErrorGrouping.Group(entries, Window);
        Assert.Equal(new[] { "BException", "CException", "AException" }, groups.Select(g => g.Title.Split(' ')[0]).ToArray());
        Assert.Single(ErrorGrouping.Group(entries, Window, max: 1));
    }

    [Fact]
    public void No_entries_no_groups()
    {
        Assert.Empty(ErrorGrouping.Group(Array.Empty<LogEntry>(), Window));
    }

    [Fact]
    public void Entries_outside_the_window_count_but_do_not_land_in_a_bucket()
    {
        var group = Assert.Single(ErrorGrouping.Group(new[]
        {
            new LogEntry(Window.Start.AddHours(-3), "binders-api", "p", "Error", Throw("System.XException", 5)),
        }, Window));
        Assert.Equal(1, group.Count);
        Assert.Equal(0, group.Counts.Sum());
    }
}

public class SpikeDetectorTests
{
    private static ErrorSeries Series(string app, int length, int baseline, int now, int nowBuckets = 12)
    {
        var counts = new int[length];
        for (var i = 0; i < length; i++) counts[i] = i >= length - nowBuckets ? now : baseline;
        return new ErrorSeries(app, counts);
    }

    [Fact]
    public void A_jump_over_the_baseline_is_a_spike_with_a_factor_and_rising_trend()
    {
        var spike = SpikeDetector.Analyze(Series("binders-api", 288, 2, 20));
        Assert.True(spike.IsSpike);
        Assert.Equal(20, spike.Now, 3);
        Assert.Equal(2, spike.Baseline, 3);
        Assert.Equal(10, spike.Factor, 3);
        Assert.Equal(Trend.Up, spike.Trend);
        Assert.Equal(2 * 276 + 20 * 12, spike.Total);
    }

    [Fact]
    public void Steady_noise_is_not_a_spike()
    {
        var counts = Enumerable.Range(0, 288).Select(i => 3 + (i % 3)).ToArray();
        var result = SpikeDetector.Analyze(new ErrorSeries("properties-api", counts));
        Assert.False(result.IsSpike);
        Assert.Equal(Trend.Flat, result.Trend);
    }

    [Fact]
    public void Almost_nothing_never_alarms()
    {
        var counts = new int[288];
        counts[^1] = 1; // mean over the last hour is far below one per bucket
        Assert.False(SpikeDetector.Analyze(new ErrorSeries("activities-api", counts)).IsSpike);
        Assert.False(SpikeDetector.Analyze(new ErrorSeries("contracts-api", new int[288])).IsSpike);
    }

    [Fact]
    public void A_new_problem_in_a_silent_app_is_a_spike()
    {
        var spike = SpikeDetector.Analyze(Series("new-api", 288, 0, 3));
        Assert.True(spike.IsSpike);
        Assert.Equal(3, spike.Factor, 3);
    }

    [Fact]
    public void Falling_after_a_burst_is_a_down_trend_without_a_spike()
    {
        var counts = new int[288];
        for (var i = 0; i < 288; i++) counts[i] = i >= 288 - 12 ? 1 : i >= 288 - 84 ? 20 : 2;
        var result = SpikeDetector.Analyze(new ErrorSeries("a-api", counts));
        Assert.False(result.IsSpike);
        Assert.Equal(Trend.Down, result.Trend);
    }

    [Fact]
    public void Short_or_empty_series_do_not_throw()
    {
        var empty = SpikeDetector.Analyze(new ErrorSeries("e", Array.Empty<int>()));
        Assert.False(empty.IsSpike);
        Assert.Equal(0, empty.Total);

        var tiny = SpikeDetector.Analyze(new ErrorSeries("t", new[] { 5, 5 }));
        Assert.False(tiny.IsSpike);
        Assert.Equal(10, tiny.Total);
    }

    [Fact]
    public void Apps_are_ordered_spiking_first_then_by_errors_then_by_name()
    {
        var list = SpikeDetector.Analyze(new[]
        {
            Series("zeta-api", 288, 5, 5),
            Series("alpha-api", 288, 0, 0),
            Series("big-spike", 288, 1, 30),
            Series("small-spike", 288, 1, 8),
            Series("mid-api", 288, 2, 2),
        });

        Assert.Equal(new[] { "big-spike", "small-spike", "zeta-api", "mid-api", "alpha-api" }, list.Select(a => a.App).ToArray());
    }

    [Fact]
    public void Overview_adds_quiet_known_apps_once_and_counts_spikes()
    {
        var window = ErrorWindow.Default(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
        var snapshot = ErrorsOverview.Build(
            window,
            new[] { Series("binders-api", 288, 2, 20) },
            new[] { "binders-api", "Contracts-API", "contracts-api", "", "activities-api" },
            window.End);

        Assert.Equal(new[] { "binders-api", "activities-api", "Contracts-API" }, snapshot.Apps.Select(a => a.App).ToArray());
        Assert.Equal(1, snapshot.Spiking);
        Assert.Equal(1, snapshot.WithErrors);
    }
}

public class ErrorChartTests
{
    [Fact]
    public void Tail_takes_the_last_buckets_and_pads_short_series_with_zeros()
    {
        Assert.Equal(new[] { 3, 4, 5 }, ErrorChart.Tail(new[] { 1, 2, 3, 4, 5 }, 3).ToArray());
        Assert.Equal(new[] { 0, 0, 1, 2 }, ErrorChart.Tail(new[] { 1, 2 }, 4).ToArray());
        Assert.Empty(ErrorChart.Tail(new[] { 1 }, 0));
    }

    [Fact]
    public void Bars_scale_to_the_peak_and_a_single_error_stays_visible()
    {
        var heights = ErrorChart.BarHeights(new[] { 0, 1, 50, 100 }, 40);
        Assert.Equal(0, heights[0]);
        Assert.Equal(2, heights[1]);
        Assert.Equal(20, heights[2], 3);
        Assert.Equal(40, heights[3], 3);
    }

    [Fact]
    public void Bars_can_share_a_scale_and_never_exceed_the_maximum()
    {
        var heights = ErrorChart.BarHeights(new[] { 10, 200 }, 30, scaleMax: 100);
        Assert.Equal(3, heights[0], 3);
        Assert.Equal(30, heights[1], 3);
        Assert.All(ErrorChart.BarHeights(new int[4], 30), h => Assert.Equal(0, h));
        Assert.Empty(ErrorChart.BarHeights(Array.Empty<int>(), 30));
    }
}

public class ErrorsTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(300, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(47 * 3600, "47 h ago")]
    [InlineData(3 * 86400, "3 d ago")]
    public void Ago_uses_the_largest_sensible_unit(int seconds, string expected)
    {
        Assert.Equal(expected, ErrorsText.Ago(Now.AddSeconds(-seconds), Now));
    }

    [Theory]
    [InlineData(4.2, "4.2×")]
    [InlineData(3.0, "3×")]
    [InlineData(12.4, "12×")]
    public void Factor_is_short(double value, string expected) => Assert.Equal(expected, ErrorsText.Factor(value));

    [Fact]
    public void Trend_glyphs_and_words()
    {
        Assert.Equal("↑", ErrorsText.TrendGlyph(Trend.Up));
        Assert.Equal("→", ErrorsText.TrendGlyph(Trend.Flat));
        Assert.Equal("↓", ErrorsText.TrendGlyph(Trend.Down));
        Assert.Equal("rising", ErrorsText.TrendWord(Trend.Up));
        Assert.Equal("falling", ErrorsText.TrendWord(Trend.Down));
        Assert.Equal("steady", ErrorsText.TrendWord(Trend.Flat));
    }

    [Fact]
    public void Summary_and_per_hour()
    {
        var window = ErrorWindow.Default(Now);
        var quiet = new ErrorsSnapshot(window, new[] { new AppSpike("a", new int[288], 0, 0, 0, 0, Trend.Flat, false) }, Now);
        Assert.Equal("No errors in the last 24 hours", ErrorsText.Summary(quiet));

        var busy = new ErrorsSnapshot(window, new[]
        {
            new AppSpike("a", new int[288], 10, 4, 1, 4, Trend.Up, true),
            new AppSpike("b", new int[288], 5, 1, 1, 1, Trend.Flat, false),
        }, Now);
        Assert.Equal("2 apps with errors · 1 spiking", ErrorsText.Summary(busy));
        Assert.Equal("48 / h", ErrorsText.PerHour(busy.Apps[0], window));
    }
}
