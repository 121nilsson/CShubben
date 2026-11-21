using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.GitHub;

/// <summary>Maps this slice's exceptions to neutral <see cref="FailureFacts"/> for the shared classifier.</summary>
public static class GitHubFailureFacts
{
    /// <summary>Null when the exception is not a GitHub one (the generic rules then apply).</summary>
    public static FailureFacts? From(Exception ex, DateTimeOffset? rateLimitResetAt = null)
    {
        switch (ex)
        {
            case GitHubAuthException:
                return new FailureFacts { CredentialsMissing = true };
            case GitHubApiException api:
                return new FailureFacts
                {
                    HttpStatus = api.StatusCode is >= 200 and <= 299 ? null : api.StatusCode,
                    RateLimited = api.IsRateLimited,
                    ResetAt = api.IsRateLimited ? rateLimitResetAt : null,
                    Malformed = api.StatusCode is >= 200 and <= 299,
                    Detail = api.Message,
                };
            case GitHubGraphQlException gql:
                var bad = gql.Errors.Any(e => e.Contains("bad credentials", StringComparison.OrdinalIgnoreCase));
                return new FailureFacts
                {
                    HttpStatus = bad ? 401 : null,
                    RateLimited = gql.IsRateLimited,
                    ResetAt = gql.IsRateLimited ? rateLimitResetAt : null,
                    Detail = gql.Message,
                };
            default:
                return null;
        }
    }
}
