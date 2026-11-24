using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class DeployCorrelationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Ending(Now, TimeSpan.FromMinutes(5), 288);

    private static DeployedVersion Deploy(int minutesAgo, string sha = "3f9c2ab1234567890") =>
        new("prod", sha, "main", "anna", Now - TimeSpan.FromMinutes(minutesAgo));

    private static ErrorGroup Group(DateTimeOffset first, DateTimeOffset last) =>
        new("k", "t", "d", null, ErrorKind.Exception, 5, first, last, new[] { "p" }, new int[288], null, null);

    [Fact]
    public void Before_is_the_newest_deployment_at_or_before_the_time()
    {
        var deploys = new[] { Deploy(600, "a"), Deploy(120, "b"), Deploy(30, "c") };

        Assert.Equal("b", DeployCorrelation.Before(deploys, Now - TimeSpan.FromMinutes(60))?.Sha);
        Assert.Equal("b", DeployCorrelation.Before(deploys, Now - TimeSpan.FromMinutes(120))?.Sha);
        Assert.Equal("c", DeployCorrelation.Before(deploys, Now)?.Sha);
        Assert.Null(DeployCorrelation.Before(deploys, Now - TimeSpan.FromMinutes(700)));
        Assert.Null(DeployCorrelation.Before(Array.Empty<DeployedVersion>(), Now));
    }

    [Fact]
    public void A_deployment_shortly_before_the_start_is_the_probable_cause()
    {
        var link = DeployCorrelation.Link(new[] { Deploy(60) }, Now - TimeSpan.FromMinutes(48), DeployCorrelation.SpikeWindow);

        Assert.NotNull(link);
        Assert.Equal(TimeSpan.FromMinutes(12), link!.After);
        Assert.Equal("3f9c2ab1234567890", link.Deploy.Sha);
    }

    [Fact]
    public void An_old_deployment_is_not_blamed()
    {
        Assert.Null(DeployCorrelation.Link(new[] { Deploy(300) }, Now - TimeSpan.FromMinutes(48), DeployCorrelation.SpikeWindow));
    }

    [Fact]
    public void A_start_in_the_same_bucket_as_the_deployment_counts_as_right_after_it()
    {
        // The bucket starts at 11:05 and the deployment was at 11:08.
        var started = new DateTimeOffset(2026, 11, 24, 11, 5, 0, TimeSpan.Zero);
        var deploy = new DeployedVersion("prod", "abc1234", "main", "anna", new DateTimeOffset(2026, 11, 24, 11, 8, 0, TimeSpan.Zero));

        Assert.Null(DeployCorrelation.Link(new[] { deploy }, started, DeployCorrelation.SpikeWindow));

        var link = DeployCorrelation.Link(new[] { deploy }, started, DeployCorrelation.SpikeWindow, Window.Step);
        Assert.NotNull(link);
        Assert.Equal(TimeSpan.Zero, link!.After);
    }

    [Fact]
    public void A_new_error_is_tied_to_the_deployment_before_it_first_appeared()
    {
        var first = Now - TimeSpan.FromHours(3);
        var link = DeployCorrelation.FirstSeenLink(new[] { Deploy(190) }, Group(first, Now), Window);

        Assert.NotNull(link);
        Assert.Equal(TimeSpan.FromMinutes(10), link!.After);
    }

    [Fact]
    public void A_first_sighting_is_not_tied_to_a_deployment_when_older_log_lines_were_cut_off()
    {
        var first = Now - TimeSpan.FromHours(3);
        Assert.NotNull(DeployCorrelation.FirstSeenLink(new[] { Deploy(190) }, Group(first, Now), Window, complete: true));
        Assert.Null(DeployCorrelation.FirstSeenLink(new[] { Deploy(190) }, Group(first, Now), Window, complete: false));
    }

    [Fact]
    public void An_error_that_was_there_when_the_window_began_did_not_start_with_a_deployment()
    {
        var first = Window.Start + TimeSpan.FromMinutes(3);
        Assert.Null(DeployCorrelation.FirstSeenLink(new[] { Deploy(60 * 24 - 1) }, Group(first, Now), Window));
    }

    private static int[] Flat(int value, int length = 288)
    {
        var counts = new int[length];
        Array.Fill(counts, value);
        return counts;
    }

    [Fact]
    public void A_spike_starts_where_the_run_of_elevated_buckets_that_reaches_now_starts()
    {
        var counts = Flat(2);
        for (var i = 279; i < 288; i++) counts[i] = 20;

        Assert.Equal(Window.Start + TimeSpan.FromMinutes(5 * 279), SpikeOnset.Find(counts, Window));
    }

    [Fact]
    public void A_quiet_bucket_or_two_inside_a_spike_does_not_split_it()
    {
        var counts = Flat(2);
        for (var i = 270; i < 288; i++) counts[i] = 20;
        counts[275] = 0;
        counts[276] = 1;

        Assert.Equal(Window.Start + TimeSpan.FromMinutes(5 * 270), SpikeOnset.Find(counts, Window));
    }

    [Fact]
    public void A_burst_in_a_silent_history_starts_at_the_first_bucket_of_the_burst()
    {
        var counts = Flat(0);
        counts[286] = 3;
        counts[287] = 4;

        Assert.Equal(Window.Start + TimeSpan.FromMinutes(5 * 286), SpikeOnset.Find(counts, Window));
    }

    [Fact]
    public void A_steady_application_has_no_onset()
    {
        Assert.Null(SpikeOnset.Find(Flat(2), Window));
        Assert.Null(SpikeOnset.Find(Flat(0), Window));
        Assert.Null(SpikeOnset.Find(Array.Empty<int>(), Window));
        Assert.Null(SpikeOnset.Find(new[] { 5, 5 }, Window));
    }

    [Fact]
    public void A_spike_that_is_over_has_no_onset()
    {
        var counts = Flat(2);
        for (var i = 200; i < 240; i++) counts[i] = 30;

        Assert.Null(SpikeOnset.Find(counts, Window));
    }
}
