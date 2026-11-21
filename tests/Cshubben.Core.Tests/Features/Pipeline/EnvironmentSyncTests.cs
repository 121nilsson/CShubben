using Cshubben.Core.Features.Pipeline;
using Xunit;

namespace Cshubben.Core.Tests.Features.Pipeline;

public class EnvironmentSyncTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static Deployment Dep(string env, string sha, int minutesAgo = 0, long id = 1) =>
        new(env, id, sha, "main", "bot", At.AddMinutes(-minutesAgo), DeployState.Success, "");

    [Fact]
    public void Same_commit_everywhere_is_quiet()
    {
        var info = EnvironmentSync.Describe(new[] { Dep("test", "1a2b3c4d5e"), Dep("prod", "1a2b3c4d5e") })!;
        Assert.False(info.Differs);
        Assert.StartsWith("All 2 environments at ", info.Text);
    }

    [Fact]
    public void Different_commits_are_reported_per_environment()
    {
        var info = EnvironmentSync.Describe(new[] { Dep("test", "aaaaaaaa"), Dep("prod", "bbbbbbbb") })!;
        Assert.True(info.Differs);
        Assert.Contains("prod ", info.Text);
        Assert.Contains("test ", info.Text);
    }

    [Fact]
    public void Only_the_latest_deployment_of_an_environment_counts_and_one_environment_gives_nothing()
    {
        var info = EnvironmentSync.Describe(new[] { Dep("test", "old11111", 60, 1), Dep("test", "new22222", 5, 2), Dep("prod", "new22222", 30, 3) })!;
        Assert.False(info.Differs);
        Assert.Null(EnvironmentSync.Describe(new[] { Dep("prod", "aaaaaaaa") }));
        Assert.Null(EnvironmentSync.Describe(Array.Empty<Deployment>()));
    }
}
