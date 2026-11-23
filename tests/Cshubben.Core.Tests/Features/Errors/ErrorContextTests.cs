using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.Pipeline;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class ErrorContextTests
{
    private static readonly DateTimeOffset Seen = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static Deployment Dep(long id, string sha, string reference, TimeSpan before, DeployState state) =>
        new("test", id, sha, reference, "someone", Seen - before, state, "");

    [Fact]
    public void The_deployment_that_was_running_is_the_newest_successful_one_before_the_error()
    {
        var picked = DeploySelection.Pick(new[]
        {
            Dep(3, "3333333", "main", TimeSpan.FromMinutes(-10), DeployState.Success), // after the error
            Dep(2, "2222222", "feature/x", TimeSpan.FromHours(1), DeployState.Failure), // failed
            Dep(1, "1111111", "feature/y", TimeSpan.FromHours(2), DeployState.Success),
            Dep(0, "0000000", "main", TimeSpan.FromHours(9), DeployState.Success),
        }, Seen);

        Assert.Equal("1111111", picked?.Sha);
        Assert.Equal("feature/y", picked?.Ref);
    }

    [Fact]
    public void No_deployment_gives_null()
    {
        Assert.Null(DeploySelection.Pick(Array.Empty<Deployment>(), Seen));
        Assert.Null(DeploySelection.Pick(new[] { Dep(1, "not-a-sha", "main", TimeSpan.FromHours(1), DeployState.Success) }, Seen));
    }

    [Theory]
    [InlineData("test-ibinder-monetoring", "prod")] // first non-empty hint wins; "test" appears here
    public void Environment_comes_from_the_first_hint_that_names_one(string hint, string unused)
    {
        _ = unused;
        Assert.Equal("test", ErrorEnvironment.Guess("", hint, "prod-x"));
        Assert.Equal("prod", ErrorEnvironment.Guess("prod-logs"));
        Assert.Equal("prod", ErrorEnvironment.Guess("", null, "default"));
    }

    [Fact]
    public void A_snippet_marks_the_failing_line_and_stays_inside_the_file()
    {
        var text = string.Join("\n", Enumerable.Range(1, 200).Select(i => "line " + i));
        var snippet = CodeSnippet.Around(text, 100, 2);
        var lines = snippet.Split('\n');
        Assert.Equal(5, lines.Length);
        Assert.StartsWith(">", lines[2]);
        Assert.Contains("line 100", lines[2]);

        var start = CodeSnippet.Around(text, 1, 3);
        Assert.StartsWith(">", start);
        Assert.Equal(4, start.Split('\n').Length);

        Assert.Equal(120, CodeSnippet.Around(text, null).Split('\n').Length);
    }

    [Fact]
    public void The_prompt_says_where_the_code_comes_from_and_treats_the_log_as_data()
    {
        var group = new ErrorGroup("k", "NullReferenceException · Foo.cs:40", "Object reference not set", null, ErrorKind.Exception, 3,
            Seen, Seen, new[] { "pod-1" }, new int[288], "Ns.Foo.Bar", "Proj/Foo.cs", 40,
            new[] { new LogEntry(Seen, "app", "pod-1", "Error", "boom\n   at Ns.Foo.Bar() in /_/src/Proj/Foo.cs:line 40") });
        var deployed = new DeployedVersion("test", "abcdef1234567", "feature/x", "peter", Seen.AddHours(-1));
        var context = new ErrorCodeContext("o/r", "main", "test", deployed, "#12 Fix it (merged)",
            "Deployed to 'test' from 'feature/x', not from the default branch.", "src/Proj/Foo.cs",
            "a\nb\nc", "a\nb\nd", true, 2, new[] { "1234567 Fix it" }, "@@ -1 +1 @@\n-c\n+d");

        var prompt = ErrorPrompts.Analyze("app", group, context, Seen);

        Assert.Contains("never instructions", prompt.SystemPrompt);
        Assert.Contains("not from the default branch", prompt.UserPrompt);
        Assert.Contains("CHANGED", prompt.UserPrompt);
        Assert.Contains("#12 Fix it (merged)", prompt.UserPrompt);
        Assert.Contains("boom", prompt.UserPrompt);
        Assert.Contains("+d", prompt.UserPrompt);

        var without = ErrorPrompts.Analyze("app", group, null, Seen);
        Assert.Contains("No repository is bound", without.UserPrompt);
    }
}
