using System.Text.Json;
using Cshubben.Core.Features.Stats;
using Xunit;

namespace Cshubben.Core.Tests.Features.Stats;

public class StatsMapperTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Merged_uses_first_review_by_someone_else_and_skips_pending()
    {
        var data = Parse("""
        {"search":{"pageInfo":{"hasNextPage":true,"endCursor":"c1"},"nodes":[
          {"number":7,"title":"Fix","createdAt":"2026-11-10T08:00:00Z","mergedAt":"2026-11-11T08:00:00Z","additions":10,"deletions":5,"changedFiles":2,
           "author":{"login":"me"},"repository":{"nameWithOwner":"o/r"},
           "reviews":{"nodes":[
             {"submittedAt":"2026-11-10T09:00:00Z","state":"COMMENTED","author":{"login":"me"}},
             {"submittedAt":"2026-11-10T12:00:00Z","state":"PENDING","author":{"login":"you"}},
             {"submittedAt":"2026-11-10T14:00:00Z","state":"APPROVED","author":{"login":"you"}}]}},
          {"number":0,"title":"bad"}]}}
        """);

        var (page, items) = StatsMapper.ParseMerged(data);

        Assert.True(page.HasNextPage);
        Assert.Equal("c1", page.EndCursor);
        var pr = Assert.Single(items);
        Assert.Equal("o/r", pr.Repo);
        Assert.Equal(15, pr.Lines);
        Assert.Equal(TimeSpan.FromHours(6), pr.TimeToFirstReview);
        Assert.Equal(TimeSpan.FromHours(24), pr.CycleTime);
    }

    [Fact]
    public void Merged_without_reviews_has_no_first_review_and_tolerates_missing_fields()
    {
        var data = Parse("""{"search":{"nodes":[{"number":1,"createdAt":"2026-11-10T08:00:00Z","mergedAt":"2026-11-10T09:00:00Z","repository":{"nameWithOwner":"o/r"}}]}}""");
        var (page, items) = StatsMapper.ParseMerged(data);
        Assert.False(page.HasNextPage);
        Assert.Null(page.EndCursor);
        Assert.Null(Assert.Single(items).FirstReviewAt);
    }

    [Fact]
    public void Reviewed_gives_one_record_per_submitted_review()
    {
        var data = Parse("""
        {"search":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
          {"number":3,"repository":{"nameWithOwner":"o/r"},"reviews":{"nodes":[
            {"submittedAt":"2026-11-10T09:00:00Z","state":"COMMENTED"},
            {"submittedAt":"2026-11-11T09:00:00Z","state":"APPROVED"},
            {"submittedAt":"2026-11-12T09:00:00Z","state":"PENDING"}]}}]}}
        """);
        var (_, items) = StatsMapper.ParseReviewed(data);
        Assert.Equal(2, items.Count);
        Assert.All(items, r => Assert.Equal(3, r.Number));
    }

    [Fact]
    public void Search_strings_use_utc_day_and_exclude_own_prs_for_reviews()
    {
        var since = new DateTimeOffset(2026, 9, 1, 23, 30, 0, TimeSpan.FromHours(2));
        Assert.Contains("merged:>=2026-09-01", StatsQueries.MergedSearch(since));
        var reviewed = StatsQueries.ReviewedSearch(since);
        Assert.Contains("reviewed-by:@me", reviewed);
        Assert.Contains("-author:@me", reviewed);
    }
}
