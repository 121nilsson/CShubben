using System.Text.Json;
using Cshubben.Core.Features.Linear;
using Xunit;

namespace Cshubben.Core.Tests.Integrations;

public class LinearIssueTextTests
{
    private static LinearIssue? Parse(string json) => LinearMapper.ParseIssue(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Description_and_avatar_are_read()
    {
        var issue = Parse("""
            {"id":"i1","identifier":"ENG-1","title":"T","description":"Some **text**","url":"https://linear.app/x","priority":2,
             "updatedAt":"2026-11-20T10:00:00Z","state":{"name":"Todo","type":"unstarted"},
             "assignee":{"name":"a","displayName":"Ann","avatarUrl":"https://avatars.example.com/a.png"},"team":{"key":"ENG"}}
            """);
        Assert.NotNull(issue);
        Assert.Equal("Some **text**", issue!.Description);
        Assert.Equal("https://avatars.example.com/a.png", issue.AssigneeAvatarUrl);
    }

    [Theory]
    [InlineData("http://insecure.example.com/a.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pw@example.com/a.png")]
    [InlineData("not a url")]
    public void Unsafe_avatar_links_are_dropped(string url) => Assert.Null(LinearMapper.SafeImageUrl(url));

    [Fact]
    public void Huge_description_is_capped_and_missing_one_is_empty()
    {
        var big = new string('x', LinearMapper.MaxDescription + 500);
        var issue = Parse("{\"id\":\"i1\",\"identifier\":\"ENG-1\",\"title\":\"T\",\"description\":\"" + big + "\",\"state\":{\"name\":\"Todo\",\"type\":\"unstarted\"}}");
        Assert.True(issue!.Description.Length <= LinearMapper.MaxDescription + 5);
        Assert.Equal("", Parse("{\"id\":\"i2\",\"identifier\":\"ENG-2\",\"title\":\"T\",\"state\":{\"name\":\"Todo\",\"type\":\"unstarted\"}}")!.Description);
    }
}
