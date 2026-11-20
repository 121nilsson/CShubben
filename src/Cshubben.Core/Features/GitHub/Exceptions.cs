using System.Text.Json;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

/// <summary>Base type for everything this slice throws on purpose.</summary>
public class GitHubException : Exception
{
    public GitHubException(string message) : base(message) { }

    public GitHubException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>No token could be found.</summary>
public sealed class GitHubAuthException : GitHubException
{
    public GitHubAuthException(string message) : base(message) { }

    public GitHubAuthException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>A non-success HTTP answer from GitHub.</summary>
public sealed class GitHubApiException : GitHubException
{
    public GitHubApiException(int statusCode, string message, bool isRateLimited = false) : base(message)
    {
        StatusCode = statusCode;
        IsRateLimited = isRateLimited;
    }

    public int StatusCode { get; }

    public bool IsRateLimited { get; }
}

/// <summary>GraphQL answered 200 but reported errors.</summary>
public sealed class GitHubGraphQlException : GitHubException
{
    public GitHubGraphQlException(IReadOnlyList<string> errors, bool isRateLimited)
        : base("GitHub GraphQL error: " + string.Join("; ", errors))
    {
        Errors = errors;
        IsRateLimited = isRateLimited;
    }

    public IReadOnlyList<string> Errors { get; }

    public bool IsRateLimited { get; }
}

internal static class Recoverable
{
    /// <summary>Errors that should degrade a load (warning) instead of failing it.</summary>
    public static bool Is(Exception ex, CancellationToken ct) =>
        ex is GitHubException
        || ex is HttpRequestException
        || ex is JsonException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);
}
