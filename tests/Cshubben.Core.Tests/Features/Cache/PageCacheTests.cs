using Cshubben.Core.Features.Cache;
using Cshubben.Core.Features.Stats;
using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Features.Cache;

public class PageCacheTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static StatsInput Input() => new(
        new[] { new MergedPrRecord("a/x", 1, "Fix", "me", At.AddHours(-5), At, At.AddHours(-2), 10, 2, 1) },
        new[] { new ReviewRecord("a/y", 3, At) });

    [Fact]
    public void Page_data_survives_a_round_trip_through_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cshubben-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(PageDiskCache.TrySave(dir, "stats", "peter", "stats|peter", At, Input()));
            var hit = PageDiskCache.TryLoad<StatsInput>(dir, "stats", "PETER");
            Assert.NotNull(hit);
            Assert.Equal("stats|peter", hit!.Value.Key);
            Assert.Equal(At, hit.Value.At);
            Assert.Equal("Fix", hit.Value.Value.Merged[0].Title);
            Assert.Equal(3, hit.Value.Value.Reviews[0].Number);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Team_report_round_trips()
    {
        var team = TeamStats.Compute(new[] { new TeamPr("a/x", "anna", At, 10) });
        var back = PageDiskCache.Deserialize<TeamReport>(PageDiskCache.Serialize("p", "k", At, team), "p")!.Value;
        Assert.Equal("anna", back.Value.TopPeople[0].Author);
    }

    [Fact]
    public void Another_user_a_damaged_file_or_nothing_gives_null()
    {
        var json = PageDiskCache.Serialize("peter", "k", At, Input());
        Assert.Null(PageDiskCache.Deserialize<StatsInput>(json, "someone-else"));
        Assert.Null(PageDiskCache.Deserialize<StatsInput>("{ not json", "peter"));
        Assert.Null(PageDiskCache.Deserialize<StatsInput>("", "peter"));
        Assert.Null(PageDiskCache.TryLoad<StatsInput>(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()), "stats", "peter"));
    }

    [Fact]
    public void Seeded_entries_are_old_and_the_latest_one_is_reported()
    {
        var now = At;
        var cache = new StaleCache<string>(() => now);
        Assert.Null(cache.Latest());
        cache.Seed("k", "disk", At.AddHours(-3));
        Assert.Equal("disk", cache.Peek("k"));
        Assert.False(cache.IsFresh("k", TimeSpan.FromMinutes(30)));
        cache.Set("k2", "new");
        var latest = cache.Latest()!.Value;
        Assert.Equal(("k2", "new", At), (latest.Key, latest.Value, latest.At));
    }

    [Fact]
    public void Usage_counts_visits_fades_old_ones_and_survives_json()
    {
        var u = new PageUsage();
        for (var i = 0; i < 6; i++) u.Record("Linear");
        u.Record("Stats");
        Assert.Equal(7, u.Total);
        Assert.Equal(6.0 / 7, u.Share("linear"), 3);

        var back = PageUsage.FromJson(u.ToJson());
        Assert.Equal(6, back.Count("Linear"));
        Assert.Equal(0, PageUsage.FromJson("{ broken").Total);

        for (var i = 0; i < PageUsage.Limit; i++) u.Record("Drift");
        Assert.True(u.Total <= PageUsage.Limit);
        Assert.True(u.Count("Drift") > u.Count("Linear"));
    }

    [Fact]
    public void Favourite_pages_refresh_often_and_the_rest_rarely()
    {
        var u = new PageUsage();
        Assert.Equal(PrefetchPolicy.Warm, PrefetchPolicy.Interval(u, "Linear"));
        for (var i = 0; i < 18; i++) u.Record("Reviews");
        for (var i = 0; i < 3; i++) u.Record("Linear");
        u.Record("Stats");
        Assert.Equal(PrefetchPolicy.Hot, PrefetchPolicy.Interval(u, "Reviews"));
        Assert.Equal(PrefetchPolicy.Warm, PrefetchPolicy.Interval(u, "Linear"));
        Assert.Equal(PrefetchPolicy.Cold, PrefetchPolicy.Interval(u, "Stats"));
        Assert.Equal(PrefetchPolicy.Cold, PrefetchPolicy.Interval(u, "Drift"));
    }

    [Fact]
    public void Stable_key_is_the_same_every_time_and_differs_per_input()
    {
        Assert.Equal(StableKey.Hash("a/x,a/y"), StableKey.Hash("a/x,a/y"));
        Assert.NotEqual(StableKey.Hash("a/x"), StableKey.Hash("a/y"));
        Assert.Equal(16, StableKey.Hash("").Length);
    }
}
