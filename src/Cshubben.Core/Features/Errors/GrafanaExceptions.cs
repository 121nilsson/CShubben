using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.Errors;

/// <summary>Something went wrong talking to Grafana or reading what Loki answered. The message never contains the token.</summary>
public class GrafanaException : Exception
{
    public GrafanaException(string message)
        : base(message)
    {
    }

    public GrafanaException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>No address or token, or Grafana refused the token.</summary>
public sealed class GrafanaAuthException : GrafanaException
{
    public GrafanaAuthException(string message, bool missing)
        : base(message)
    {
        Missing = missing;
    }

    /// <summary>True when nothing is configured yet, false when Grafana rejected what is.</summary>
    public bool Missing { get; }
}

/// <summary>Grafana answered with an HTTP error status.</summary>
public sealed class GrafanaHttpException : GrafanaException
{
    public GrafanaHttpException(int statusCode, string? detail = null)
        : base("Grafana answered with HTTP " + statusCode + (string.IsNullOrWhiteSpace(detail) ? "." : ": " + detail))
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

/// <summary>Maps this slice's exceptions to neutral <see cref="FailureFacts"/> for the shared classifier.</summary>
public static class GrafanaFailureFacts
{
    /// <summary>Null when the exception is not a Grafana one (the generic rules then apply).</summary>
    public static FailureFacts? From(Exception ex)
    {
        switch (ex)
        {
            case GrafanaAuthException auth:
                return auth.Missing
                    ? new FailureFacts { CredentialsMissing = true }
                    : new FailureFacts { HttpStatus = 401 };
            case GrafanaHttpException http:
                return new FailureFacts { HttpStatus = http.StatusCode, RateLimited = http.StatusCode == 429, Detail = http.Message };
            case GrafanaException { InnerException: { } inner }:
                return FailureClassifier.GenericFacts(inner) with { Detail = ex.Message };
            case GrafanaException plain:
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
