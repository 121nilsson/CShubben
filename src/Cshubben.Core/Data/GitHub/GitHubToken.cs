namespace Cshubben.Core.Data.GitHub;

/// <summary>Finds a GitHub token. Order: GITHUB_TOKEN, GH_TOKEN, then `gh auth token`. Never logs or stores it.</summary>
public static class GitHubToken
{
    public static readonly IReadOnlyList<string> EnvironmentVariables = new[] { "GITHUB_TOKEN", "GH_TOKEN" };

    public static async Task<string> ResolveAsync(
        ICommandRunner? runner = null,
        Func<string, string?>? getEnvironmentVariable = null,
        CancellationToken cancellationToken = default)
    {
        getEnvironmentVariable ??= static name => Environment.GetEnvironmentVariable(name);

        foreach (var name in EnvironmentVariables)
        {
            var value = getEnvironmentVariable(name)?.Trim();
            if (!string.IsNullOrEmpty(value)) return value;
        }

        runner ??= new ProcessCommandRunner();
        CommandResult result;
        try
        {
            result = await runner.RunAsync("gh", new[] { "auth", "token" }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GitHubAuthException(
                "No GitHub token found: set GITHUB_TOKEN or GH_TOKEN, or install the GitHub CLI and run `gh auth login`.", ex);
        }

        var token = result.StdOut.Trim();
        if (result.ExitCode != 0 || token.Length == 0)
        {
            throw new GitHubAuthException(
                "No GitHub token found: set GITHUB_TOKEN or GH_TOKEN, or run `gh auth login`.");
        }
        return token;
    }
}
