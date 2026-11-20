using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cshubben.Core.Data.GitHub;

/// <summary>Rate-limit numbers from response headers or the GraphQL rateLimit field.</summary>
public sealed record RateLimitInfo(int? Limit, int? Remaining, DateTimeOffset? ResetAt, int? Cost)
{
    public static RateLimitInfo? FromHeaders(HttpResponseHeaders headers)
    {
        var remaining = HeaderInt(headers, "x-ratelimit-remaining");
        if (remaining is null) return null;
        var limit = HeaderInt(headers, "x-ratelimit-limit");
        var reset = HeaderInt(headers, "x-ratelimit-reset");
        DateTimeOffset? resetAt = null;
        if (reset is not null) resetAt = DateTimeOffset.FromUnixTimeSeconds(reset.Value);
        return new RateLimitInfo(limit, remaining, resetAt, null);
    }

    public static RateLimitInfo? FromGraphQl(JsonElement data)
    {
        var rl = J.At(data, "rateLimit");
        if (rl is null) return null;
        return new RateLimitInfo(
            J.IntOrNull(rl, "limit"),
            J.IntOrNull(rl, "remaining"),
            J.Time(rl, "resetAt"),
            J.IntOrNull(rl, "cost"));
    }

    private static int? HeaderInt(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values)) return null;
        var first = values.FirstOrDefault();
        if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
        return null;
    }
}

/// <param name="Status">HTTP status code.</param>
/// <param name="Json">Parsed body, or null when empty or on 304.</param>
/// <param name="ETag">Entity tag of the response (on 304, the one that was sent).</param>
public sealed record RestResponse(int Status, JsonElement? Json, string? ETag)
{
    public bool NotModified => Status == 304;
}

/// <summary>
/// The one place that talks HTTP to GitHub: auth, headers, 502/503 retry, errors, rate-limit headers.
/// The token is only ever put in the Authorization header and never appears in messages.
/// </summary>
public sealed class GitHubRest
{
    public const string ApiBase = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public GitHubRest(HttpClient http, string token, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("A GitHub token is required.", nameof(token));
        _http = http;
        _token = token.Trim();
        _delay = delay ?? ((span, ct) => Task.Delay(span, ct));
    }

    /// <summary>Backoff before the single retry of a 502/503.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public RateLimitInfo? LastRateLimit { get; private set; }

    /// <summary>GET with an optional ETag. Retries one 502/503.</summary>
    public Task<RestResponse> GetAsync(string url, string? etag = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(HttpMethod.Get, url, null, etag, true, cancellationToken);

    /// <summary>Write call. Set retry to false for anything that must not be repeated.</summary>
    public Task<RestResponse> SendAsync(HttpMethod method, string url, string? jsonBody, bool retry, CancellationToken cancellationToken = default) =>
        ExecuteAsync(method, url, jsonBody, null, retry, cancellationToken);

    private async Task<RestResponse> ExecuteAsync(HttpMethod method, string url, string? jsonBody, string? etag, bool retry, CancellationToken ct)
    {
        if (!url.StartsWith(ApiBase + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException("URL must be under " + ApiBase, nameof(url));
        }

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.UserAgent.ParseAdd("Cshubben");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            if (jsonBody is not null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var rate = RateLimitInfo.FromHeaders(response.Headers);
            if (rate is not null) LastRateLimit = rate;

            if (retry && attempt == 0 && (status == 502 || status == 503))
            {
                await _delay(RetryDelay, ct).ConfigureAwait(false);
                continue;
            }

            if (status == 304) return new RestResponse(304, null, etag);

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (status < 200 || status > 299) throw BuildError(status, text, rate);

            var newEtag = response.Headers.ETag?.ToString();
            if (string.IsNullOrWhiteSpace(text)) return new RestResponse(status, null, newEtag);
            try
            {
                using var doc = JsonDocument.Parse(text);
                return new RestResponse(status, doc.RootElement.Clone(), newEtag);
            }
            catch (JsonException)
            {
                throw new GitHubApiException(status, "GitHub returned a response that is not valid JSON.");
            }
        }
    }

    private static GitHubApiException BuildError(int status, string text, RateLimitInfo? rate)
    {
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            message = J.StrOrNull(doc.RootElement, "message");
        }
        catch (JsonException)
        {
        }

        if (string.IsNullOrWhiteSpace(message)) message = "GitHub answered HTTP " + status.ToString(CultureInfo.InvariantCulture);
        if (message.Length > 300) message = message.Substring(0, 300);

        var limited = status == 429
            || (status == 403 && rate?.Remaining == 0)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
        return new GitHubApiException(status, message, limited);
    }
}
