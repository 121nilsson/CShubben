using Cshubben.Core.Features.Feed;
using Xunit;

namespace Cshubben.Core.Tests.Features.Cache;

public class FeedLinksTests
{
    [Fact]
    public void Pull_request_and_repository_urls()
    {
        Assert.Equal("https://github.com/o/r/pull/12", FeedLinks.UrlFor("o/r", 12));
        Assert.Equal("https://github.com/o/r", FeedLinks.UrlFor("o/r", null));
        Assert.Equal("https://github.com/o/r", FeedLinks.UrlFor("o/r", 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noslash")]
    [InlineData("o/r/extra")]
    [InlineData("o/../r")]
    [InlineData("o/r?x=1")]
    [InlineData("o/r name")]
    public void Odd_names_give_no_url(string? repo) => Assert.Null(FeedLinks.UrlFor(repo, 1));
}
