using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.Linear;

/// <summary>Maps this slice's exceptions to neutral <see cref="FailureFacts"/> for the shared classifier.</summary>
public static class LinearFailureFacts
{
    /// <summary>Null when the exception is not a Linear one (the generic rules then apply).</summary>
    public static FailureFacts? From(Exception ex)
    {
        switch (ex)
        {
            case LinearAuthException auth:
                return auth.Message.Contains("No Linear API key", StringComparison.Ordinal)
                    ? new FailureFacts { CredentialsMissing = true }
                    : new FailureFacts { HttpStatus = 401 };
            case LinearRateLimitException limited:
                return new FailureFacts { RateLimited = true, RetryAfter = limited.RetryAfter };
            case LinearHttpException http:
                return new FailureFacts { HttpStatus = http.StatusCode };
            case LinearApiException api:
                return new FailureFacts
                {
                    Malformed = api.Messages.Count == 0 && api.Message.Contains("could not be read", StringComparison.Ordinal),
                    Detail = api.Messages.Count > 0 ? string.Join("; ", api.Messages) : api.Message,
                };
            case LinearException { InnerException: { } inner }:
                var generic = FailureClassifier.GenericFacts(inner);
                return generic with { Detail = ex.Message };
            case LinearException plain:
                return new FailureFacts { Detail = plain.Message };
            default:
                return null;
        }
    }
}
