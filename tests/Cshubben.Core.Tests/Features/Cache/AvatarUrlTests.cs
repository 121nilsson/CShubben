using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Features.Cache;

public class AvatarUrlTests
{
    [Fact]
    public void Plain_login_gets_a_sized_url() =>
        Assert.Equal("https://github.com/octo-cat.png?size=56", AvatarUrl.ForGitHub("octo-cat", 28));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dependabot[bot]")]
    [InlineData("ghost")]
    [InlineData("bad/../login")]
    [InlineData("-leading")]
    [InlineData("white space")]
    [InlineData("a?b=c")]
    public void Anything_else_gets_no_url(string? login) => Assert.Null(AvatarUrl.ForGitHub(login));

    [Fact]
    public void Size_is_clamped() =>
        Assert.Equal("https://github.com/me.png?size=256", AvatarUrl.ForGitHub("me", 5000));
}
