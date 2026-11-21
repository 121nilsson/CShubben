using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Shared;

/// <summary>What went wrong, in terms a person can act on.</summary>
public enum FailureKind
{
    MissingCredentials,
    InvalidCredentials,
    Forbidden,
    Offline,
    Timeout,
    RateLimited,
    ServerError,
    BadResponse,
    NotFound,
    Unknown,
}

/// <summary>
/// Neutral facts about a failure. A feature turns its own exception types into this (see GitHubFailureFacts and
/// LinearFailureFacts), so the classifier does not need to know about any feature.
/// </summary>
public sealed record FailureFacts
{
    public int? HttpStatus { get; init; }

    public bool RateLimited { get; init; }

    /// <summary>When the rate limit resets, if known.</summary>
    public DateTimeOffset? ResetAt { get; init; }

    /// <summary>How long the service asked us to wait, if it said.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>No token or key is configured at all.</summary>
    public bool CredentialsMissing { get; init; }

    /// <summary>The answer could not be read (not JSON, wrong shape).</summary>
    public bool Malformed { get; init; }

    public bool Offline { get; init; }

    public bool TimedOut { get; init; }

    /// <summary>Server-provided text. Shown after redaction, shortened.</summary>
    public string? Detail { get; init; }
}

/// <summary>A classified failure with a calm message, ready for the UI. Never contains secrets.</summary>
/// <param name="Title">Short heading, for example "GitHub rate limit reached".</param>
/// <param name="Message">What happened and what to do about it.</param>
/// <param name="CanRetry">A Retry button makes sense.</param>
/// <param name="NeedsSettings">A button to Settings makes sense (token or key must be fixed).</param>
/// <param name="RetryAt">Earliest sensible time to retry, when the service said.</param>
public sealed record Failure(
    FailureKind Kind,
    string Service,
    string Title,
    string Message,
    bool CanRetry,
    bool NeedsSettings,
    DateTimeOffset? RetryAt);

/// <summary>Removes anything that looks like a token or key from text before it is shown or written to a report.</summary>
public static class SecretRedactor
{
    public const string Mask = "[hidden]";

    private static readonly Regex TokenPattern = new(
        @"(gh[pousr]_[A-Za-z0-9]{10,}|github_pat_[A-Za-z0-9_]{10,}|lin_api_[A-Za-z0-9]{10,}|lin_oauth_[A-Za-z0-9]{10,}|sk-ant-[A-Za-z0-9\-_]{10,})",
        RegexOptions.CultureInvariant);

    private static readonly Regex BearerPattern = new(
        @"(?i)\b(bearer|token)\s+[A-Za-z0-9\-_.=]{16,}",
        RegexOptions.CultureInvariant);

    public static string Scrub(string? text, IEnumerable<string?>? knownSecrets = null)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = text;
        if (knownSecrets is not null)
        {
            foreach (var secret in knownSecrets)
            {
                if (!string.IsNullOrWhiteSpace(secret) && secret.Trim().Length >= 6)
                    result = result.Replace(secret.Trim(), Mask, StringComparison.Ordinal);
            }
        }

        result = TokenPattern.Replace(result, Mask);
        result = BearerPattern.Replace(result, m => m.Groups[1].Value + " " + Mask);
        return result;
    }
}

/// <summary>
/// Pure classification of failures: token missing or invalid, offline, timeout, rate limit (with reset time), server error,
/// unreadable answer, unknown. One place decides the wording, so every page speaks the same calm language.
/// </summary>
public static class FailureClassifier
{
    private const int MaxDetail = 200;

    /// <summary>
    /// Classifies <paramref name="ex"/>. <paramref name="facts"/> are the feature-specific facts when the caller has them;
    /// otherwise the generic .NET exceptions (network, timeout, JSON) are recognised.
    /// </summary>
    public static Failure Classify(Exception ex, string service, DateTimeOffset now, FailureFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return FromFacts(facts ?? GenericFacts(ex), service, now);
    }

    /// <summary>Facts for exceptions every feature can produce: network, timeout, unreadable JSON.</summary>
    public static FailureFacts GenericFacts(Exception ex)
    {
        switch (ex)
        {
            case HttpRequestException http when http.StatusCode is { } code:
                return new FailureFacts { HttpStatus = (int)code, Detail = http.Message };
            case HttpRequestException http:
                return new FailureFacts { Offline = true, Detail = http.Message };
            case TimeoutException:
            case OperationCanceledException:
                return new FailureFacts { TimedOut = true };
            case JsonException:
                return new FailureFacts { Malformed = true };
            case AggregateException { InnerExceptions.Count: 1 } aggregate:
                return GenericFacts(aggregate.InnerExceptions[0]);
            default:
                return new FailureFacts { Detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message };
        }
    }

    public static Failure FromFacts(FailureFacts facts, string service, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var name = string.IsNullOrWhiteSpace(service) ? "The service" : service;
        var fix = CredentialHint(name);

        if (facts.CredentialsMissing)
        {
            return new Failure(FailureKind.MissingCredentials, name, "No " + name + " credentials",
                "No " + name + " token or key was found. " + fix, true, true, null);
        }

        var status = facts.HttpStatus;
        if (facts.RateLimited || status == 429)
        {
            var resetAt = facts.ResetAt ?? (facts.RetryAfter is { } wait ? now + wait : (DateTimeOffset?)null);
            var when = resetAt is { } reset
                ? "It resets " + Relative(reset - now) + "."
                : "It usually resets within the hour.";
            return new Failure(FailureKind.RateLimited, name, name + " rate limit reached",
                name + " is asking us to slow down. " + when + " Cshubben keeps the data it already has; retry after the reset.",
                true, false, resetAt);
        }

        if (status == 401)
        {
            return new Failure(FailureKind.InvalidCredentials, name, name + " rejected the credentials",
                name + " answered 401: the token or key is wrong, expired or revoked. " + fix, true, true, null);
        }

        if (status == 403)
        {
            return new Failure(FailureKind.Forbidden, name, name + " refused the request",
                name + " answered 403: the token may lack a permission, or the organisation requires single sign-on authorisation for it. "
                + "Check the token's scopes, or create a new one. " + fix, true, true, null);
        }

        if (status == 404)
        {
            return new Failure(FailureKind.NotFound, name, name + " could not find it",
                name + " answered 404. The item may have been deleted or renamed, or the token cannot see it.", true, false, null);
        }

        if (status is >= 500 and <= 599)
        {
            return new Failure(FailureKind.ServerError, name, name + " is having trouble",
                name + " answered " + status.Value.ToString(CultureInfo.InvariantCulture)
                + ". This is on their side, not yours. Wait a minute and retry.", true, false, null);
        }

        if (facts.Offline)
        {
            return new Failure(FailureKind.Offline, name, "Cannot reach " + name,
                "The network request failed. Check your internet connection, VPN or proxy, then retry.", true, false, null);
        }

        if (facts.TimedOut)
        {
            return new Failure(FailureKind.Timeout, name, name + " took too long",
                "The request timed out. The connection may be slow or " + name + " may be busy. Retry in a moment.", true, false, null);
        }

        if (facts.Malformed)
        {
            return new Failure(FailureKind.BadResponse, name, name + " sent an unreadable answer",
                "The answer was not in the expected format. This is usually temporary; retry. If it keeps happening, copy the diagnostics report from Settings.",
                true, false, null);
        }

        var detail = Shorten(SecretRedactor.Scrub(facts.Detail));
        return new Failure(FailureKind.Unknown, name, "Something went wrong",
            detail.Length == 0 ? "An unexpected error occurred. Retry; if it persists, run the checks in Settings." : detail + " Retry; if it persists, run the checks in Settings.",
            true, false, null);
    }

    /// <summary>"in 3 min", "in about 2 h", "now". Never negative.</summary>
    public static string Relative(TimeSpan span)
    {
        if (span <= TimeSpan.FromSeconds(5)) return "now";
        if (span < TimeSpan.FromSeconds(90)) return "in under a minute";
        if (span < TimeSpan.FromMinutes(90))
            return "in " + Math.Round(span.TotalMinutes).ToString("0", CultureInfo.InvariantCulture) + " min";
        return "in about " + Math.Round(span.TotalHours).ToString("0", CultureInfo.InvariantCulture) + " h";
    }

    private static string CredentialHint(string service) => service switch
    {
        "GitHub" => "Set GITHUB_TOKEN, or run `gh auth login`, then retry.",
        "Linear" => "Paste a new API key under Linear in Settings.",
        "Claude" => "Sign in with the Claude CLI or add a token under Claude in Settings.",
        _ => "Check the credentials in Settings.",
    };

    private static string Shorten(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= MaxDetail) return trimmed;
        return trimmed.Substring(0, MaxDetail).TrimEnd() + "...";
    }
}
