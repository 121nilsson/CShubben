using Cshubben.Core.Features.GitHub;
using Xunit;

using Cshubben.Core.Data;
namespace Cshubben.Core.Tests.GitHubTests;

public class GitHubTokenTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        name => vars.Where(v => v.Name == name).Select(v => (string?)v.Value).FirstOrDefault();

    private static FakeRunner Runner(int exit, string stdout) => new FakeRunner(() => new CommandResult(exit, stdout, ""));

    [Fact]
    public async Task GITHUB_TOKEN_wins_and_the_cli_is_not_run()
    {
        var runner = Runner(0, "from-cli");
        var token = await GitHubToken.ResolveAsync(runner, Env(("GITHUB_TOKEN", " tok1 "), ("GH_TOKEN", "tok2")));
        Assert.Equal("tok1", token);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task GH_TOKEN_is_used_when_GITHUB_TOKEN_is_missing_or_blank()
    {
        var runner = Runner(0, "from-cli");
        var token = await GitHubToken.ResolveAsync(runner, Env(("GITHUB_TOKEN", "   "), ("GH_TOKEN", "tok2")));
        Assert.Equal("tok2", token);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Falls_back_to_gh_auth_token_and_trims_the_output()
    {
        var runner = Runner(0, "gho_fromcli\n");
        var token = await GitHubToken.ResolveAsync(runner, Env());
        Assert.Equal("gho_fromcli", token);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task Failing_cli_gives_a_typed_error_that_does_not_echo_output()
    {
        var runner = Runner(1, "gho_leaky_value");
        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => GitHubToken.ResolveAsync(runner, Env()));
        Assert.DoesNotContain("gho_leaky_value", ex.Message);
    }

    [Fact]
    public async Task Missing_cli_gives_a_typed_error()
    {
        var runner = new FakeRunner(() => throw new InvalidOperationException("gh not found"));
        await Assert.ThrowsAsync<GitHubAuthException>(() => GitHubToken.ResolveAsync(runner, Env()));
    }

    [Fact]
    public async Task Empty_cli_output_is_an_error()
    {
        var runner = Runner(0, "  \n");
        await Assert.ThrowsAsync<GitHubAuthException>(() => GitHubToken.ResolveAsync(runner, Env()));
    }
}
