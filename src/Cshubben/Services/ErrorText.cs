using Cshubben.Core.Features.GitHub;

namespace Cshubben.Services;

/// <summary>Turns exceptions into one short, human sentence. Never includes tokens (our exceptions do not carry them).</summary>
static class ErrorText
{
    public static string Describe(Exception ex) => ex switch
    {
        GitHubApiException { IsRateLimited: true } => "GitHub rate limit reached. It resets shortly; try again in a few minutes.",
        GitHubGraphQlException { IsRateLimited: true } => "GitHub rate limit reached. It resets shortly; try again in a few minutes.",
        GitHubApiException { StatusCode: 401 } => "GitHub rejected the token (401). Check GITHUB_TOKEN or run `gh auth login`.",
        HttpRequestException => "Network problem: " + ex.Message,
        TaskCanceledException => "The request timed out.",
        _ => string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message,
    };
}
