using Cshubben.Core.Data.GitHub;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.GitHubTests;

public class PrMapperTests
{
    [Fact]
    public void List_node_maps_all_cheap_fields()
    {
        var pr = PrMapper.FromListNode(Setup.Parse(Fixtures.ListNode), PrRole.Authored)!;
        Assert.Equal("PR_abc", pr.Id);
        Assert.Equal(PrRole.Authored, pr.Role);
        Assert.Equal("acme/api", pr.Repo);
        Assert.Equal(412, pr.Number);
        Assert.Equal("Add idempotency keys", pr.Title);
        Assert.Equal("https://github.com/acme/api/pull/412", pr.Url);
        Assert.Equal("alex", pr.Author);
        Assert.True(pr.IsDraft);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero), pr.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero), pr.UpdatedAt);
        Assert.Equal(212, pr.Additions);
        Assert.Equal(34, pr.Deletions);
        Assert.Equal(9, pr.ChangedFiles);
        Assert.Equal(4, pr.Comments);
        Assert.Equal("feat/idempotency", pr.HeadBranch);
        Assert.Equal("Why\nand how", pr.Description);
    }

    [Fact]
    public void List_node_starts_without_enrichment()
    {
        var pr = PrMapper.FromListNode(Setup.Parse(Fixtures.ListNode), PrRole.Authored)!;
        Assert.Equal(ReviewDecision.None, pr.Review);
        Assert.Equal(CiState.Unknown, pr.Ci);
        Assert.Equal(MergeState.Unknown, pr.Merge);
        Assert.Equal(0, pr.UnresolvedThreads);
    }

    [Fact]
    public void Missing_author_becomes_ghost()
    {
        var pr = PrMapper.FromListNode(Setup.Parse("{\"id\":\"x\",\"author\":null}"), PrRole.ReviewRequested)!;
        Assert.Equal("ghost", pr.Author);
        Assert.Equal(PrRole.ReviewRequested, pr.Role);
    }

    [Fact]
    public void Search_hits_that_are_not_pull_requests_are_skipped()
    {
        var json = "{\"search\":{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":[{},{\"id\":\"PR_1\",\"number\":1},null]}}";
        var page = PrMapper.ParseSearchPage(Setup.Parse(json), PrRole.Authored);
        Assert.Single(page.Items);
        Assert.Equal("PR_1", page.Items[0].Id);
    }

    [Fact]
    public void Search_page_reads_cursor_and_has_next()
    {
        var page = PrMapper.ParseSearchPage(Setup.Parse(Setup.Page(new[] { "a", "b" }, true, "CUR1")).GetProperty("data"), PrRole.Authored);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasNextPage);
        Assert.Equal("CUR1", page.EndCursor);
    }

    [Theory]
    [InlineData("APPROVED", ReviewDecision.Approved)]
    [InlineData("CHANGES_REQUESTED", ReviewDecision.ChangesRequested)]
    [InlineData("REVIEW_REQUIRED", ReviewDecision.ReviewRequired)]
    [InlineData(null, ReviewDecision.None)]
    [InlineData("SOMETHING_NEW", ReviewDecision.None)]
    public void Review_decision_mapping(string? value, ReviewDecision expected) =>
        Assert.Equal(expected, PrMapper.ParseReview(value));

    [Theory]
    [InlineData("SUCCESS", CiState.Success)]
    [InlineData("FAILURE", CiState.Failure)]
    [InlineData("ERROR", CiState.Failure)]
    [InlineData("PENDING", CiState.Pending)]
    [InlineData("EXPECTED", CiState.Pending)]
    [InlineData(null, CiState.Unknown)]
    [InlineData("WEIRD", CiState.Unknown)]
    public void Ci_state_mapping(string? value, CiState expected) =>
        Assert.Equal(expected, PrMapper.ParseCi(value));

    [Theory]
    [InlineData("MERGEABLE", MergeState.Mergeable)]
    [InlineData("CONFLICTING", MergeState.Conflicting)]
    [InlineData("UNKNOWN", MergeState.Unknown)]
    [InlineData(null, MergeState.Unknown)]
    public void Mergeable_mapping(string? value, MergeState expected) =>
        Assert.Equal(expected, PrMapper.ParseMerge(value));

    [Fact]
    public void Enrichment_sets_review_ci_threads_and_mergeable()
    {
        var pr = PrMapper.FromListNode(Setup.Parse(Fixtures.ListNode), PrRole.Authored)!;
        var enriched = PrMapper.ApplyEnrichment(pr, Setup.Parse(Fixtures.EnrichNode));
        Assert.Equal(ReviewDecision.ChangesRequested, enriched.Review);
        Assert.Equal(CiState.Failure, enriched.Ci);
        Assert.Equal(MergeState.Conflicting, enriched.Merge);
        Assert.Equal(2, enriched.UnresolvedThreads);
        Assert.Equal(pr.Title, enriched.Title);
    }

    [Fact]
    public void Enrichment_without_commits_or_mergeable_keeps_unknowns()
    {
        var pr = PrMapper.FromListNode(Setup.Parse(Fixtures.ListNode), PrRole.Authored)!;
        var enriched = PrMapper.ApplyEnrichment(pr, Setup.Parse("{\"id\":\"PR_abc\",\"reviewDecision\":\"APPROVED\",\"commits\":{\"nodes\":[]}}"));
        Assert.Equal(ReviewDecision.Approved, enriched.Review);
        Assert.Equal(CiState.Unknown, enriched.Ci);
        Assert.Equal(MergeState.Unknown, enriched.Merge);
    }

    [Fact]
    public void Enrichment_with_null_rollup_is_unknown_ci()
    {
        var pr = PrMapper.FromListNode(Setup.Parse(Fixtures.ListNode), PrRole.Authored)!;
        var node = Setup.Parse("{\"id\":\"PR_abc\",\"commits\":{\"nodes\":[{\"commit\":{\"statusCheckRollup\":null}}]}}");
        Assert.Equal(CiState.Unknown, PrMapper.ApplyEnrichment(pr, node).Ci);
    }

    [Fact]
    public void Node_index_skips_null_and_id_less_nodes()
    {
        var data = Setup.Parse("{\"nodes\":[null,{},{\"id\":\"A\"},{\"id\":\"B\"}]}");
        var map = PrMapper.IndexNodesById(data);
        Assert.Equal(2, map.Count);
        Assert.Contains("A", map.Keys);
    }

    [Fact]
    public void Search_strings_match_the_spec()
    {
        Assert.Equal("is:pr is:open author:@me archived:false", PrQueries.AuthoredSearch);
        Assert.Equal("is:pr is:open review-requested:@me archived:false", PrQueries.ReviewSearch);
    }

    [Fact]
    public void List_query_leaves_out_the_slow_mergeable_field()
    {
        Assert.DoesNotContain("mergeable", PrQueries.ListQuery);
        Assert.DoesNotContain("mergeable", PrQueries.EnrichQuery);
        Assert.Contains("mergeable", PrQueries.EnrichWithMergeableQuery);
    }
}
