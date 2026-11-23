using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.Errors;

/// <summary>Something went wrong talking to the log service (the MCP in front of Azure Monitor). Messages never contain tokens.</summary>
public class LogsException : Exception
{
    public LogsException(string message)
        : base(message)
    {
    }

    public LogsException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>Not signed in, the sign-in expired, or the account is not allowed to use the log service.</summary>
public sealed class LogsAuthException : LogsException
{
    public LogsAuthException(string message, bool needsSignIn)
        : base(message)
    {
        NeedsSignIn = needsSignIn;
    }

    /// <summary>True when signing in (again) would help, false when the account has no access.</summary>
    public bool NeedsSignIn { get; }
}

/// <summary>The log service answered with an HTTP error status.</summary>
public sealed class LogsHttpException : LogsException
{
    public LogsHttpException(int statusCode, string? detail = null)
        : base("The log service answered with HTTP " + statusCode + (string.IsNullOrWhiteSpace(detail) ? "." : ": " + detail))
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

/// <summary>Maps this slice's exceptions to neutral <see cref="FailureFacts"/> for the shared classifier.</summary>
public static class LogsFailureFacts
{
    /// <summary>Null when the exception is not one of ours (the generic rules then apply).</summary>
    public static FailureFacts? From(Exception ex)
    {
        switch (ex)
        {
            case LogsAuthException auth:
                return auth.NeedsSignIn
                    ? new FailureFacts { CredentialsMissing = true, Detail = auth.Message }
                    : new FailureFacts { HttpStatus = 403, Detail = auth.Message };
            case LogsHttpException http:
                return new FailureFacts { HttpStatus = http.StatusCode, RateLimited = http.StatusCode == 429, Detail = http.Message };
            case LogsException { InnerException: { } inner }:
                return FailureClassifier.GenericFacts(inner) with { Detail = ex.Message };
            case LogsException plain:
                return new FailureFacts
                {
                    Malformed = plain.Message.Contains("could not be read", StringComparison.Ordinal),
                    Detail = plain.Message,
                };
            default:
                return null;
        }
    }
}
