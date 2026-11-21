using Cshubben.Core.Features.Cache;
using Cshubben.Core.Model;
using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Features.Cache;

public class CacheTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static HubSnapshot Sample(string viewer = "me") => new(
        viewer,
        new[] { new PullRequest("id1", PrRole.Authored, "o/r", 1, "Title", "https://github.com/o/r/pull/1", "me", false, T0, T0,
            10, 2, 3, ReviewDecision.Approved, CiState.Success, MergeState.Mergeable, 1, 0, "feat/x", "Body") },
        Array.Empty<PullRequest>(),
        new Dictionary<string, ReviewInfo> { ["id2"] = new("id2", true, T0, T0, ReviewDecision.None) },
        new[] { new FeedItem("f1", FeedKind.Merged, "o/r", "you", 1, "Title", "merged", T0, true, false) },
        T0);

    [Fact]
    public void Snapshot_round_trips_with_enums_dates_and_dictionary()
    {
        var back = SnapshotCache.Deserialize(SnapshotCache.Serialize(Sample()), "me");
        Assert.NotNull(back);
        Assert.Equal("Title", back!.Authored[0].Title);
        Assert.Equal(CiState.Success, back.Authored[0].Ci);
        Assert.Equal(T0, back.At);
        Assert.Equal(ReviewDecision.None, back.ReviewInfos["id2"].MyDecision);
        Assert.Equal(FeedKind.Merged, back.Feed[0].Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Version\":99,\"Viewer\":\"me\",\"Snapshot\":null}")]
    public void Damaged_or_foreign_text_gives_null(string? text) => Assert.Null(SnapshotCache.Deserialize(text, "me"));

    [Fact]
    public void Other_users_cache_is_ignored()
    {
        var json = SnapshotCache.Serialize(Sample("someone-else"));
        Assert.Null(SnapshotCache.Deserialize(json, "me"));
        Assert.NotNull(SnapshotCache.Deserialize(json, null));
    }

    [Fact]
    public void File_round_trip_and_missing_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cshubben-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(SnapshotCache.TryLoad(dir));
            Assert.True(SnapshotCache.TrySave(dir, Sample()));
            Assert.Equal("me", SnapshotCache.TryLoad(dir, "ME")!.Viewer);
            Assert.False(File.Exists(Path.Combine(dir, SnapshotCache.FileName + ".tmp")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task StaleCache_loads_once_within_ttl_and_again_after()
    {
        var now = T0;
        var cache = new StaleCache<string>(() => now);
        var calls = 0;
        Task<string> Load() { calls++; return Task.FromResult("v" + calls); }

        Assert.Null(cache.Peek("k"));
        Assert.Equal("v1", await cache.GetAsync("k", TimeSpan.FromMinutes(2), Load));
        Assert.Equal("v1", await cache.GetAsync("k", TimeSpan.FromMinutes(2), Load));
        Assert.Equal("v1", cache.Peek("k"));

        now = T0.AddMinutes(3);
        Assert.False(cache.IsFresh("k", TimeSpan.FromMinutes(2)));
        Assert.Equal("v1", cache.Peek("k"));
        Assert.Equal("v2", await cache.GetAsync("k", TimeSpan.FromMinutes(2), Load));
    }

    [Fact]
    public async Task StaleCache_keeps_old_value_when_load_fails_and_shares_concurrent_loads()
    {
        var cache = new StaleCache<string>(() => T0);
        cache.Set("k", "old");
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("k", TimeSpan.Zero, () => throw new InvalidOperationException()));
        Assert.Equal("old", cache.Peek("k"));

        var gate = new TaskCompletionSource<string>();
        var calls = 0;
        Task<string> Slow() { Interlocked.Increment(ref calls); return gate.Task; }
        var a = cache.GetAsync("n", TimeSpan.FromMinutes(1), Slow);
        var b = cache.GetAsync("n", TimeSpan.FromMinutes(1), Slow);
        gate.SetResult("x");
        Assert.Equal("x", await a);
        Assert.Equal("x", await b);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StaleCache_remove_forces_a_new_load()
    {
        var cache = new StaleCache<string>(() => T0);
        var n = 0;
        await cache.GetAsync("k", TimeSpan.FromHours(1), () => Task.FromResult("a" + ++n));
        cache.Remove("k");
        Assert.Equal("a2", await cache.GetAsync("k", TimeSpan.FromHours(1), () => Task.FromResult("a" + ++n)));
    }
}
