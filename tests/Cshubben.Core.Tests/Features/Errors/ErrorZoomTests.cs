using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class ErrorZoomTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_short_burst_gets_a_chart_of_at_least_the_minimum_length_around_it()
    {
        var window = ErrorWindow.Default(Now);
        var counts = new int[window.Buckets];
        counts[200] = 7;
        var at = window.Start + TimeSpan.FromTicks(window.Step.Ticks * 200);

        var zoom = ErrorZoom.Around(counts, window, at, at, 24);

        Assert.True(zoom.Counts.Count >= 24);
        Assert.Contains(7, zoom.Counts);
        Assert.True(zoom.From <= at && zoom.To > at);
    }

    [Fact]
    public void The_slice_never_leaves_the_window()
    {
        var window = ErrorWindow.Default(Now);
        var counts = new int[window.Buckets];
        counts[window.Buckets - 1] = 1;
        var at = window.End - window.Step;

        var zoom = ErrorZoom.Around(counts, window, at, at, 24);

        Assert.Equal(window.End, zoom.To);
        Assert.True(zoom.From >= window.Start);
        Assert.Equal(1, zoom.Counts[^1]);
    }

    [Fact]
    public void Summary_says_how_many_of_all_applications_have_errors()
    {
        var window = ErrorWindow.Default(Now);
        var snapshot = new ErrorsSnapshot(window, new[]
        {
            new AppSpike("a", new int[288], 10, 4, 1, 4, Trend.Up, true),
            new AppSpike("b", new int[288], 0, 0, 0, 0, Trend.Flat, false),
        }, Now);

        Assert.Equal("1 of 2 apps with errors · 1 spiking", ErrorsText.Summary(snapshot));
    }
}
