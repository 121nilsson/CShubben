using System.Net;
using System.Text.Json;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Shared;

public class FailureClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static Failure Gh(Exception ex, DateTimeOffset? reset = null) =>
        FailureClassifier.Classify(ex, "GitHub", Now, GitHubFailureFacts.From(ex, reset));

    private static Failure Lin(Exception ex) =>
        FailureClassifier.Classify(ex, "Linear", Now, LinearFailureFacts.From(ex));

    [Fact]
    public void Missing_github_token_points_to_settings()
    {
        var f = Gh(new GitHubAuthException("No GitHub token found"));
        Assert.Equal(FailureKind.MissingCredentials, f.Kind);
        Assert.True(f.NeedsSettings);
        Assert.True(f.CanRetry);
        Assert.Contains("GITHUB_TOKEN", f.Message);
    }

    [Fact]
    public void Http_401_is_invalid_credentials()
    {
        var f = Gh(new GitHubApiException(401, "Bad credentials"));
        Assert.Equal(FailureKind.InvalidCredentials, f.Kind);
        Assert.True(f.NeedsSettings);
    }

    [Fact]
    public void Http_403_without_rate_limit_is_forbidden()
    {
        var f = Gh(new GitHubApiException(403, "Resource protected by organization SAML enforcement"));
        Assert.Equal(FailureKind.Forbidden, f.Kind);
        Assert.Contains("single sign-on", f.Message);
    }

    [Fact]
    public void Rate_limit_reports_reset_time()
    {
        var f = Gh(new GitHubApiException(403, "API rate limit exceeded", isRateLimited: true), Now.AddMinutes(12));
        Assert.Equal(FailureKind.RateLimited, f.Kind);
        Assert.Equal(Now.AddMinutes(12), f.RetryAt);
        Assert.Contains("in 12 min", f.Message);
        Assert.False(f.NeedsSettings);
    }

    [Fact]
    public void Http_429_is_rate_limited_even_without_flag_or_reset()
    {
        var f = Gh(new GitHubApiException(429, "Too many requests"));
        Assert.Equal(FailureKind.RateLimited, f.Kind);
        Assert.Null(f.RetryAt);
        Assert.Contains("within the hour", f.Message);
    }

    [Fact]
    public void Graphql_rate_limit_and_bad_credentials()
    {
        Assert.Equal(FailureKind.RateLimited, Gh(new GitHubGraphQlException(new[] { "limit" }, true)).Kind);
        Assert.Equal(FailureKind.InvalidCredentials, Gh(new GitHubGraphQlException(new[] { "Bad credentials" }, false)).Kind);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void Server_errors(int status)
    {
        var f = Gh(new GitHubApiException(status, "boom"));
        Assert.Equal(FailureKind.ServerError, f.Kind);
        Assert.Contains(status.ToString(), f.Message);
    }

    [Fact]
    public void Invalid_json_from_github_is_a_bad_response()
    {
        var f = Gh(new GitHubApiException(200, "GitHub returned a response that is not valid JSON."));
        Assert.Equal(FailureKind.BadResponse, f.Kind);
        Assert.Equal(FailureKind.BadResponse, FailureClassifier.Classify(new JsonException("x"), "GitHub", Now).Kind);
    }

    [Fact]
    public void Network_down_is_offline()
    {
        var f = Gh(new HttpRequestException("No such host is known"));
        Assert.Equal(FailureKind.Offline, f.Kind);
        Assert.Contains("internet", f.Message);
    }

    [Fact]
    public void Timeouts()
    {
        Assert.Equal(FailureKind.Timeout, Gh(new TaskCanceledException()).Kind);
        Assert.Equal(FailureKind.Timeout, Gh(new TimeoutException()).Kind);
    }

    [Fact]
    public void Http_request_exception_with_status_uses_the_status()
    {
        var f = FailureClassifier.Classify(new HttpRequestException("x", null, HttpStatusCode.BadGateway), "GitHub", Now);
        Assert.Equal(FailureKind.ServerError, f.Kind);
    }

    [Fact]
    public void Unknown_error_keeps_a_short_detail_and_hides_tokens()
    {
        var f = Gh(new InvalidOperationException("odd failure with ghp_" + "abcdefghijklmnopqrstuvwxyz0123456789 inside"));
        Assert.Equal(FailureKind.Unknown, f.Kind);
        Assert.Contains("odd failure", f.Message);
        Assert.DoesNotContain("ghp_abc", f.Message);
        Assert.Contains(SecretRedactor.Mask, f.Message);
    }

    [Fact]
    public void Unknown_error_detail_is_shortened()
    {
        var f = Gh(new InvalidOperationException(new string('x', 1000)));
        Assert.True(f.Message.Length < 400);
    }

    [Fact]
    public void Linear_missing_and_rejected_key()
    {
        Assert.Equal(FailureKind.MissingCredentials, Lin(new LinearAuthException("No Linear API key is configured.")).Kind);
        var rejected = Lin(new LinearAuthException("Linear rejected the API key."));
        Assert.Equal(FailureKind.InvalidCredentials, rejected.Kind);
        Assert.Contains("Settings", rejected.Message);
    }

    [Fact]
    public void Linear_rate_limit_uses_retry_after()
    {
        var f = Lin(new LinearRateLimitException("limit", TimeSpan.FromMinutes(3)));
        Assert.Equal(FailureKind.RateLimited, f.Kind);
        Assert.Equal(Now.AddMinutes(3), f.RetryAt);
        Assert.Contains("in 3 min", f.Message);
    }

    [Fact]
    public void Linear_http_unreadable_and_wrapped_network_errors()
    {
        Assert.Equal(FailureKind.ServerError, Lin(new LinearHttpException(503)).Kind);
        Assert.Equal(FailureKind.BadResponse,
            Lin(new LinearApiException("Linear returned a response that could not be read.", Array.Empty<string>(), false)).Kind);
        Assert.Equal(FailureKind.Offline,
            Lin(new LinearException("Could not reach Linear: x", new HttpRequestException("x"))).Kind);
        Assert.Equal(FailureKind.Timeout,
            Lin(new LinearException("The request to Linear timed out.", new TaskCanceledException())).Kind);
    }

    [Theory]
    [InlineData(5, "now")]
    [InlineData(45, "in under a minute")]
    [InlineData(600, "in 10 min")]
    [InlineData(7200, "in about 2 h")]
    public void Relative_phrases(int seconds, string expected) =>
        Assert.Equal(expected, FailureClassifier.Relative(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Redactor_hides_known_and_pattern_secrets()
    {
        var text = "key=my-very-secret-value and lin_api_" + "abcdefghijklmnop1234 and Bearer abcdefghijklmnopqrstuvwxyz";
        var clean = SecretRedactor.Scrub(text, new[] { "my-very-secret-value" });
        Assert.DoesNotContain("my-very-secret-value", clean);
        Assert.DoesNotContain("lin_api_abc", clean);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", clean);
    }
}
