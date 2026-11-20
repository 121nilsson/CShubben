using System.ComponentModel;
using Cshubben.Core.Integrations;
using Cshubben.Core.Integrations.Claude;
using Xunit;

namespace Cshubben.Core.Tests.Integrations;

public class ClaudeRunnerTests
{
    private const string Ok =
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":1234,\"result\":\"Hello\"," +
        "\"total_cost_usd\":0.0123,\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":5,\"cache_read_input_tokens\":100,\"output_tokens\":20}," +
        "\"modelUsage\":{\"claude-sonnet-4-5\":{}}}";

    private static ClaudeRunner Runner(FakeProcessRunner fake, string? exe = "/bin/claude", string? token = null) =>
        new(fake, () => exe, () => token);

    private static bool HasPair(IReadOnlyList<string> args, string first, string second)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == first && args[i + 1] == second) return true;
        }

        return false;
    }

    private static ProcessResult Out(string stdout, int code = 0, string stderr = "") => new(code, stdout, stderr);

    [Fact]
    public async Task Runs_headless_with_no_tools_and_prompt_on_stdin()
    {
        var fake = new FakeProcessRunner(Out(Ok));
        var result = await Runner(fake).RunAsync(new ClaudeRequest("SYSTEM TEXT", "USER TEXT"));

        var call = Assert.Single(fake.Calls);
        Assert.Equal("/bin/claude", call.FileName);
        Assert.Contains("-p", call.Arguments);
        Assert.True(HasPair(call.Arguments, "--output-format", "json"));
        Assert.True(HasPair(call.Arguments, "--tools", ""));
        Assert.Contains("--no-session-persistence", call.Arguments);
        Assert.Contains("--disable-slash-commands", call.Arguments);
        Assert.Contains("--strict-mcp-config", call.Arguments);
        Assert.True(HasPair(call.Arguments, "--system-prompt", "SYSTEM TEXT"));
        Assert.True(HasPair(call.Arguments, "--model", "sonnet"));
        Assert.Equal("USER TEXT", call.StdIn);
        Assert.DoesNotContain("USER TEXT", call.Arguments);

        Assert.True(result.Success);
        Assert.Equal("Hello", result.Text);
        Assert.Equal(0.0123, result.CostUsd, 4);
        Assert.Equal("claude-sonnet-4-5", result.Model);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), result.Duration);
        Assert.Equal(115, result.InputTokens);
        Assert.Equal(20, result.OutputTokens);
        Assert.Equal(ClaudeErrorKind.None, result.Error);
    }

    [Fact]
    public async Task Token_goes_in_environment_never_in_arguments()
    {
        var fake = new FakeProcessRunner(Out(Ok));
        await Runner(fake, token: " sk-ant-oat01-secret ").RunAsync(new ClaudeRequest("s", "u"));

        var call = Assert.Single(fake.Calls);
        Assert.NotNull(call.Env);
        Assert.Equal("sk-ant-oat01-secret", call.Env![ClaudeAuth.TokenEnvVar]);
        Assert.DoesNotContain(call.Arguments, a => a.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_token_means_no_environment_override()
    {
        var fake = new FakeProcessRunner(Out(Ok));
        await Runner(fake).RunAsync(new ClaudeRequest("s", "u"));
        Assert.Null(Assert.Single(fake.Calls).Env);
    }

    [Fact]
    public async Task Valid_effort_and_model_are_passed_and_invalid_ones_ignored()
    {
        var fake = new FakeProcessRunner(Out(Ok));
        await Runner(fake).RunAsync(new ClaudeRequest("s", "u") { Model = "opus", Effort = "high" });
        await Runner(fake).RunAsync(new ClaudeRequest("s", "u") { Model = "gpt-5; rm -rf", Effort = "turbo" });

        Assert.True(HasPair(fake.Calls[0].Arguments, "--model", "opus"));
        Assert.True(HasPair(fake.Calls[0].Arguments, "--effort", "high"));
        Assert.True(HasPair(fake.Calls[1].Arguments, "--model", "sonnet"));
        Assert.DoesNotContain("--effort", fake.Calls[1].Arguments);
    }

    [Fact]
    public async Task Full_model_id_is_accepted()
    {
        var fake = new FakeProcessRunner(Out(Ok));
        await Runner(fake).RunAsync(new ClaudeRequest("s", "u") { Model = "claude-opus-4-1" });
        Assert.True(HasPair(Assert.Single(fake.Calls).Arguments, "--model", "claude-opus-4-1"));
    }

    [Fact]
    public async Task Missing_binary_is_NotInstalled_and_nothing_runs()
    {
        var fake = new FakeProcessRunner(Out(Ok));
        var result = await Runner(fake, exe: null).RunAsync(new ClaudeRequest("s", "u"));
        Assert.False(result.Success);
        Assert.Equal(ClaudeErrorKind.NotInstalled, result.Error);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Start_failure_is_NotInstalled()
    {
        var fake = new FakeProcessRunner(_ => throw new Win32Exception("not found"));
        var result = await Runner(fake).RunAsync(new ClaudeRequest("s", "u"));
        Assert.Equal(ClaudeErrorKind.NotInstalled, result.Error);
    }

    [Fact]
    public async Task Timeout_is_reported()
    {
        var fake = new FakeProcessRunner(new ProcessResult(-1, "", "", true));
        var result = await Runner(fake).RunAsync(new ClaudeRequest("s", "u") { Timeout = TimeSpan.FromSeconds(5) });
        Assert.Equal(ClaudeErrorKind.Timeout, result.Error);
        Assert.Contains("5 s", result.ErrorMessage);
    }

    [Fact]
    public async Task Error_flag_with_auth_text_is_AuthFailed_with_hint()
    {
        var json = "{\"is_error\":true,\"result\":\"Failed to authenticate: this login reached its time limit.\"}";
        var result = await Runner(new FakeProcessRunner(Out(json))).RunAsync(new ClaudeRequest("s", "u"));
        Assert.Equal(ClaudeErrorKind.AuthFailed, result.Error);
        Assert.Contains("setup-token", result.ErrorMessage);
    }

    [Fact]
    public async Task Nonzero_exit_with_login_message_is_AuthFailed()
    {
        var fake = new FakeProcessRunner(Out("", 1, "Not logged in. Please run /login"));
        var result = await Runner(fake).RunAsync(new ClaudeRequest("s", "u"));
        Assert.Equal(ClaudeErrorKind.AuthFailed, result.Error);
    }

    [Fact]
    public async Task Nonzero_exit_otherwise_is_Failed()
    {
        var fake = new FakeProcessRunner(Out("", 2, "boom"));
        var result = await Runner(fake).RunAsync(new ClaudeRequest("s", "u"));
        Assert.Equal(ClaudeErrorKind.Failed, result.Error);
        Assert.Contains("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task Unreadable_output_with_exit_zero_is_Failed()
    {
        var result = await Runner(new FakeProcessRunner(Out("not json at all"))).RunAsync(new ClaudeRequest("s", "u"));
        Assert.Equal(ClaudeErrorKind.Failed, result.Error);
    }

    [Fact]
    public async Task Links_that_are_not_github_are_removed_from_the_answer()
    {
        var json = "{\"is_error\":false,\"result\":\"See https://evil.example/x and https://github.com/acme/api/pull/1 now\"}";
        var result = await Runner(new FakeProcessRunner(Out(json))).RunAsync(new ClaudeRequest("s", "u"));
        Assert.DoesNotContain("evil.example", result.Text);
        Assert.Contains("https://github.com/acme/api/pull/1", result.Text);
        Assert.Contains(LinkFilter.Removed, result.Text);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var fake = new FakeProcessRunner(_ => throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Runner(fake).RunAsync(new ClaudeRequest("s", "u")));
    }

    [Fact]
    public void Token_resolution_prefers_the_secret_store_when_env_is_empty()
    {
        var store = new InMemorySecretStore();
        store.Set(SecretKeys.ClaudeOAuthToken, " stored ");
        var old = Environment.GetEnvironmentVariable(ClaudeAuth.TokenEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ClaudeAuth.TokenEnvVar, null);
            Assert.Equal("stored", ClaudeRunner.ResolveToken(store));
            Assert.Null(ClaudeRunner.ResolveToken(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClaudeAuth.TokenEnvVar, old);
        }
    }
}

public class ClaudeOutputParserTests
{
    [Fact]
    public void Reads_a_result_object()
    {
        var parsed = ClaudeOutputParser.Parse("{\"is_error\":false,\"result\":\"hi\",\"total_cost_usd\":0.5}");
        Assert.NotNull(parsed);
        Assert.Equal("hi", parsed!.Text);
        Assert.Equal(0.5, parsed.CostUsd);
        Assert.False(parsed.IsError);
    }

    [Fact]
    public void Reads_the_result_event_from_an_array()
    {
        var raw = "[{\"type\":\"system\"},{\"type\":\"assistant\"},{\"type\":\"result\",\"result\":\"done\",\"is_error\":false}]";
        Assert.Equal("done", ClaudeOutputParser.Parse(raw)!.Text);
    }

    [Fact]
    public void Finds_the_object_inside_surrounding_text()
    {
        var parsed = ClaudeOutputParser.Parse("warning: update available\n{\"result\":\"ok\",\"is_error\":false}\n");
        Assert.Equal("ok", parsed!.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain text")]
    [InlineData("{\"is_error\":false}")]
    [InlineData("[1,2,3]")]
    public void Unusable_output_gives_null(string raw)
    {
        Assert.Null(ClaudeOutputParser.Parse(raw));
    }

    [Fact]
    public void Negative_or_missing_cost_becomes_zero()
    {
        Assert.Equal(0.0, ClaudeOutputParser.Parse("{\"result\":\"x\",\"total_cost_usd\":-3}")!.CostUsd);
        Assert.Equal(0.0, ClaudeOutputParser.Parse("{\"result\":\"x\"}")!.CostUsd);
    }

    [Theory]
    [InlineData("Failed to authenticate", true)]
    [InlineData("Please run /login", true)]
    [InlineData("HTTP 401 Unauthorized", true)]
    [InlineData("listening on port 4010", false)]
    [InlineData("", false)]
    public void Auth_errors_are_recognised(string message, bool expected)
    {
        Assert.Equal(expected, ClaudeAuth.IsAuthError(message));
    }
}
