using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class ErrorIssueTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static ErrorGroup Group(string title, int count) => new(
        "k", title, "", null, ErrorKind.Exception, count, Now.AddHours(-2), Now.AddMinutes(-3),
        new[] { "pod-1" }, new[] { count }, null, null);

    [Fact]
    public void The_title_names_the_application_and_the_error()
    {
        var draft = ErrorIssue.Draft("binders-api", Group("NullReferenceException", 5), GroupStatus.Ongoing, null, null, null, Now);
        Assert.Equal("binders-api: NullReferenceException", draft.Title);
    }

    [Fact]
    public void Sensitive_values_never_reach_the_title_or_the_description()
    {
        var draft = ErrorIssue.Draft("api", Group("Failed for anna@example.com", 5), GroupStatus.Ongoing, null, null, null, Now);
        Assert.DoesNotContain("anna@example.com", draft.Title);
        Assert.DoesNotContain("anna@example.com", draft.Description);
        Assert.Contains("<email>", draft.Title);
    }

    [Fact]
    public void A_long_title_is_cut_and_stays_on_one_line()
    {
        var draft = ErrorIssue.Draft("api", Group(new string('x', 300) + "\nsecond line", 5), GroupStatus.Ongoing, null, null, null, Now);
        Assert.Equal(120, draft.Title.Length);
        Assert.EndsWith("…", draft.Title);
        Assert.DoesNotContain('\n', draft.Title);
    }

    [Theory]
    [InlineData(GroupStatus.New, 20, 2)]
    [InlineData(GroupStatus.Rising, 500, 2)]
    [InlineData(GroupStatus.New, 19, 3)]
    [InlineData(GroupStatus.Ongoing, 500, 3)]
    [InlineData(GroupStatus.Fading, 500, 4)]
    [InlineData(GroupStatus.Stopped, 1, 4)]
    public void Priority_follows_status_and_volume(GroupStatus status, int count, int expected)
    {
        Assert.Equal(expected, ErrorIssue.PriorityOf(Group("e", count), status));
    }

    [Fact]
    public void The_description_carries_the_group_summary_and_says_where_it_came_from()
    {
        var draft = ErrorIssue.Draft("api", Group("Boom", 7), GroupStatus.Ongoing, null, null, null, Now);
        Assert.Contains("**Boom** in api", draft.Description);
        Assert.EndsWith("Created from the Errors page in Cshubben.", draft.Description);
    }

    [Fact]
    public void A_title_is_not_cut_in_the_middle_of_an_emoji()
    {
        // "a: " is 3 chars, so the emoji below starts at index 118: its two halves straddle the cut at 119.
        var draft = ErrorIssue.Draft("a", Group(new string('x', 115) + "😀" + new string('y', 10), 5), GroupStatus.Ongoing, null, null, null, Now);
        Assert.EndsWith("…", draft.Title);
        Assert.False(char.IsHighSurrogate(draft.Title[^2]));
        Assert.Equal(119, draft.Title.Length);
    }
}
