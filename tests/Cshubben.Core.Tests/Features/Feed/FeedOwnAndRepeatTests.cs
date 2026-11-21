using Cshubben.Core.Features.Feed;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.Features.Feed;

public class FeedOwnAndRepeatTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static FeedItem Item(string id, string actor, FeedKind kind, string detail, int minutesAgo, int number = 1) =>
        new(id, kind, "a/x", actor, number, "t", detail, At.AddMinutes(-minutesAgo), false, false);

    [Fact]
    public void Own_events_are_hidden_unless_asked_for_and_counted_on_their_chip()
    {
        var items = new[] { Item("1", "peter", FeedKind.Pushed, "pushed 1 commit", 1), Item("2", "anna", FeedKind.Commented, "hi", 2) };
        var hidden = new FeedOptions { Viewer = "Peter" };
        Assert.Equal(new[] { "2" }, FeedFiltering.Apply(items, hidden).Select(i => i.Id));
        Assert.Equal(1, FeedFiltering.Count(items, hidden).Own);
        Assert.Equal(2, FeedFiltering.Apply(items, hidden with { ShowOwn = true }).Count);
        Assert.Equal(2, FeedFiltering.Apply(items, new FeedOptions()).Count);
    }

    [Fact]
    public void Repeats_of_the_same_event_collapse_but_different_text_or_late_ones_stay()
    {
        var items = new[]
        {
            Item("1", "bot", FeedKind.Commented, "please review", 1), Item("2", "bot", FeedKind.Commented, "please review", 3),
            Item("3", "bot", FeedKind.Commented, "please review", 60), Item("4", "bot", FeedKind.Commented, "other text", 4),
            Item("5", "bot", FeedKind.Commented, "please review", 2, number: 2),
        };
        var kept = FeedFiltering.RemoveRepeats(FeedFiltering.Newest(items)).Select(i => i.Id).ToList();
        Assert.Equal(new[] { "1", "5", "4", "3" }.OrderBy(x => x), kept.OrderBy(x => x));
    }
}
