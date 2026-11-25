using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class GroupStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Ending(Now, TimeSpan.FromMinutes(5), 288);

    private static ErrorGroup Group(string key, TimeSpan firstAgo, TimeSpan lastAgo, int count, Action<int[]>? fill = null)
    {
        var counts = new int[288];
        fill?.Invoke(counts);
        return new ErrorGroup(key, key, "", null, ErrorKind.Exception, count, Now - firstAgo, Now - lastAgo, new[] { "p" }, counts, null, null);
    }

    private static void Last(int[] counts, int buckets, int value)
    {
        for (var i = counts.Length - buckets; i < counts.Length; i++) counts[i] = value;
    }

    private static void Before(int[] counts, int skip, int buckets, int value)
    {
        for (var i = counts.Length - skip - buckets; i < counts.Length - skip; i++) counts[i] = value;
    }

    [Fact]
    public void An_error_not_seen_for_an_hour_has_stopped()
    {
        var g = Group("a", TimeSpan.FromHours(10), TimeSpan.FromMinutes(90), 20, c => Before(c, 18, 36, 1));
        Assert.Equal(GroupStatus.Stopped, GroupStatuses.Of(g, Window, Now));
    }

    [Fact]
    public void An_error_first_seen_in_the_last_two_hours_is_new_when_the_lines_are_complete()
    {
        var g = Group("a", TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1), 4, c => Last(c, 6, 1));

        Assert.Equal(GroupStatus.New, GroupStatuses.Of(g, Window, Now));
        Assert.NotEqual(GroupStatus.New, GroupStatuses.Of(g, Window, Now, complete: false));
    }

    [Fact]
    public void An_error_that_was_there_at_the_start_of_the_window_is_never_new()
    {
        var g = Group("a", TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(1), 50, c => Last(c, 12, 1));
        Assert.NotEqual(GroupStatus.New, GroupStatuses.Of(g, Window, Now));
    }

    [Fact]
    public void An_error_that_doubled_in_the_latest_hour_is_rising()
    {
        var g = Group("a", TimeSpan.FromHours(10), TimeSpan.FromMinutes(1), 40, c =>
        {
            Last(c, 12, 3);
            Before(c, 12, 36, 1);
        });

        Assert.Equal(GroupStatus.Rising, GroupStatuses.Of(g, Window, Now));
    }

    [Fact]
    public void An_error_that_halved_in_the_latest_hour_is_fading()
    {
        var g = Group("a", TimeSpan.FromHours(10), TimeSpan.FromMinutes(5), 73, c =>
        {
            Before(c, 12, 36, 2);
            c[^1] = 1;
        });

        Assert.Equal(GroupStatus.Fading, GroupStatuses.Of(g, Window, Now));
    }

    [Fact]
    public void An_error_at_a_steady_rate_is_ongoing()
    {
        var g = Group("a", TimeSpan.FromHours(20), TimeSpan.FromMinutes(1), 48, c => Last(c, 48, 1));
        Assert.Equal(GroupStatus.Ongoing, GroupStatuses.Of(g, Window, Now));
    }

    [Fact]
    public void The_latest_hour_is_counted_in_buckets_of_the_window()
    {
        var g = Group("a", TimeSpan.FromHours(5), TimeSpan.FromMinutes(1), 100, c => Last(c, 20, 1));
        Assert.Equal(12, GroupStatuses.RecentCount(g, Window));
    }

    [Fact]
    public void Short_counts_do_not_throw()
    {
        var g = new ErrorGroup("k", "t", "", null, ErrorKind.Message, 1, Now - TimeSpan.FromMinutes(30), Now, Array.Empty<string>(), new[] { 1, 2 }, null, null);
        Assert.Equal(GroupStatus.New, GroupStatuses.Of(g, Window, Now));
        Assert.Equal(3, GroupStatuses.RecentCount(g, Window));
    }

    [Theory]
    [InlineData(GroupStatus.New, "New")]
    [InlineData(GroupStatus.Rising, "Rising")]
    [InlineData(GroupStatus.Fading, "Fading")]
    [InlineData(GroupStatus.Stopped, "Stopped")]
    [InlineData(GroupStatus.Ongoing, "Ongoing")]
    public void Every_status_has_a_label(GroupStatus status, string label) =>
        Assert.Equal(label, GroupStatuses.Label(status));

    [Fact]
    public void The_groups_can_be_ordered_four_ways_and_the_order_is_stable()
    {
        var rising = Group("rising", TimeSpan.FromHours(20), TimeSpan.FromMinutes(5), 50, c => Last(c, 12, 3));
        var fresh = Group("fresh", TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1), 10, c => Last(c, 6, 1));
        var gone = Group("gone", TimeSpan.FromHours(20), TimeSpan.FromHours(3), 30, c => Before(c, 40, 20, 1));
        var groups = new[] { gone, fresh, rising };

        string Keys(GroupSort sort) =>
            string.Join(",", GroupOrdering.Order(groups, sort, Window, Now).Select(g => g.Key));

        Assert.Equal("rising,gone,fresh", Keys(GroupSort.Frequent));
        Assert.Equal("fresh,rising,gone", Keys(GroupSort.Latest));
        Assert.Equal("fresh,rising,gone", Keys(GroupSort.Newest));
        Assert.Equal("rising,fresh,gone", Keys(GroupSort.Rising));
        Assert.Equal(Keys(GroupSort.Rising), string.Join(",", GroupOrdering.Order(new[] { rising, fresh, gone }, GroupSort.Rising, Window, Now).Select(g => g.Key)));
    }

    [Fact]
    public void Groups_are_complete_only_when_no_log_lines_were_cut_off()
    {
        ErrorGroup G(int count) => Group("g" + count, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), count);

        Assert.True(GroupSampling.IsComplete(new[] { G(10), G(20) }));
        Assert.False(GroupSampling.IsComplete(new[] { G(600), G(400) }));
        Assert.False(GroupSampling.IsComplete(Enumerable.Range(0, GroupSampling.MaxGroups).Select(_ => G(1)).ToArray()));
        Assert.True(GroupSampling.IsComplete(Array.Empty<ErrorGroup>()));
    }

    [Fact]
    public void A_source_that_reads_fewer_lines_is_cut_off_sooner()
    {
        ErrorGroup G(int count) => Group("g" + count, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), count);

        // 400 lines in total is plenty for Loki (1000) but all that Azure reads (400): older lines are missing.
        var groups = new[] { G(300), G(100) };
        Assert.True(GroupSampling.IsComplete(groups));
        Assert.False(GroupSampling.IsComplete(groups, AzureLogsErrorSource.GroupRowLimit));
        Assert.True(GroupSampling.IsComplete(new[] { G(300), G(99) }, AzureLogsErrorSource.GroupRowLimit));
    }

    [Theory]
    [InlineData(GroupSort.Frequent, "Most frequent")]
    [InlineData(GroupSort.Latest, "Last seen")]
    [InlineData(GroupSort.Newest, "First seen")]
    [InlineData(GroupSort.Rising, "Trending up")]
    public void Every_sort_has_a_label(GroupSort sort, string label) =>
        Assert.Equal(label, GroupOrdering.Label(sort));
}
