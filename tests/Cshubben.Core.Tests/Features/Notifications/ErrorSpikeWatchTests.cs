using Cshubben.Core.Composition;
using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.Notifications;
using Xunit;

namespace Cshubben.Core.Tests.Features.Notifications;

public class ErrorSpikeWatchTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 24, 12, 0, 0, TimeSpan.FromHours(1));

    private static WatchedErrorSpike Spike(string app, string key = "") =>
        new(app, key.Length > 0 ? key : app + ":1", "181 / h now, 7.5× the usual rate");

    private static ErrorsSnapshot Snapshot(int spikeBuckets)
    {
        var window = ErrorWindow.Default(Noon);
        var counts = Enumerable.Repeat(1, window.Buckets).ToArray();
        for (var i = window.Buckets - spikeBuckets; i < window.Buckets; i++) counts[i] = 20;
        var series = new[]
        {
            new ErrorSeries("binders-api", counts),
            new ErrorSeries("quiet-api", Enumerable.Repeat(0, window.Buckets).ToArray()),
        };
        return ErrorsOverview.Build(window, series, new[] { "binders-api", "quiet-api" }, Noon);
    }

    [Fact]
    public void The_first_check_is_a_baseline_and_says_nothing()
    {
        Assert.Empty(WatchDiff.Errors(null, new[] { Spike("binders-api") }));
    }

    [Fact]
    public void An_application_that_starts_to_spike_is_reported_with_a_link_to_the_errors_page()
    {
        var e = Assert.Single(WatchDiff.Errors(Array.Empty<WatchedErrorSpike>(), new[] { Spike("binders-api", "binders-api:99") }));
        Assert.Equal(NotificationKind.ErrorSpike, e.Kind);
        Assert.Equal("error-spike:binders-api:99", e.Key);
        Assert.Equal("binders-api", e.Detail);
        Assert.Equal("binders-api", WatchDiff.AppFromErrorsUrl(e.Url));
    }

    [Fact]
    public void A_spike_that_goes_on_is_reported_once_and_other_applications_are_independent()
    {
        var before = new[] { Spike("binders-api") };
        var now = new[] { Spike("binders-api"), Spike("properties-api") };
        var e = Assert.Single(WatchDiff.Errors(before, now));
        Assert.Equal("properties-api", e.Detail);
        Assert.Empty(WatchDiff.Errors(now, now));
    }

    [Fact]
    public void An_application_name_with_odd_characters_survives_the_link()
    {
        var e = Assert.Single(WatchDiff.Errors(Array.Empty<WatchedErrorSpike>(), new[] { Spike("a b/c?d") }));
        Assert.Equal("a b/c?d", WatchDiff.AppFromErrorsUrl(e.Url));
        Assert.Null(WatchDiff.AppFromErrorsUrl("https://github.com/x/y"));
        Assert.Null(WatchDiff.AppFromErrorsUrl(WatchDiff.ErrorsScheme));
        Assert.Null(WatchDiff.AppFromErrorsUrl(null));
    }

    [Fact]
    public void Only_spiking_applications_are_watched_and_the_key_holds_when_the_spike_began()
    {
        var watched = Assert.Single(WatchSources.FromErrors(Snapshot(spikeBuckets: 12)));
        Assert.Equal("binders-api", watched.App);
        Assert.StartsWith("binders-api:", watched.Key);
        Assert.Contains("× the usual rate", watched.Text);
        Assert.Contains("/ h now", watched.Text);

        Assert.Empty(WatchSources.FromErrors(Snapshot(spikeBuckets: 0)));
    }

    [Fact]
    public void The_toast_names_the_application_and_the_rate_and_follows_the_toggle_and_the_seen_set()
    {
        var events = WatchDiff.Errors(Array.Empty<WatchedErrorSpike>(), new[] { Spike("binders-api") });

        var on = new NotificationEngine(new InMemoryNotificationState());
        var toast = Assert.Single(on.PlanEvents(events, new NotificationPrefs(), Noon).Messages);
        Assert.Equal("Errors spiking: binders-api", toast.Title);
        Assert.Equal("181 / h now, 7.5× the usual rate", toast.Body);
        Assert.Empty(on.PlanEvents(events, new NotificationPrefs(), Noon).Messages);

        var off = new NotificationEngine(new InMemoryNotificationState());
        Assert.Empty(off.PlanEvents(events, new NotificationPrefs { ErrorSpike = false }, Noon).Messages);
    }
}
