using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cshubben.Core.Integrations.Linear;

/// <summary>
/// Linear GraphQL client. The personal API key goes in the Authorization header as is (no "Bearer").
/// The key never appears in exceptions. A 5xx answer is retried once.
/// </summary>
public sealed class LinearClient
{
    public const string Endpoint = "https://api.linear.app/graphql";

    private const int PageSize = 50;

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private LinearRateLimit? _lastRateLimit;

    public LinearClient(HttpClient http, Func<string?> apiKeyProvider, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _apiKey = apiKeyProvider;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    public LinearClient(HttpClient http, string apiKey, Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(http, () => apiKey, delay)
    {
    }

    /// <summary>The API key from the LINEAR_API_KEY environment variable, else from the secret store.</summary>
    public static string? ResolveApiKey(ISecretStore? secrets)
    {
        var fromEnv = Environment.GetEnvironmentVariable("LINEAR_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
        var stored = secrets?.Get(SecretKeys.LinearApiKey);
        return string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
    }

    /// <summary>The rate-limit headers from the most recent response, if Linear sent them.</summary>
    public LinearRateLimit? LastRateLimit => _lastRateLimit;

    public async Task<LinearViewer> GetViewerAsync(CancellationToken cancellationToken = default)
    {
        var data = await PostAsync(LinearQueries.Viewer(), cancellationToken).ConfigureAwait(false);
        return LinearMapper.ParseViewer(data);
    }

    /// <summary>Open issues assigned to me, newest update first, fetched page by page up to <paramref name="maxIssues"/>.</summary>
    public async Task<IReadOnlyList<LinearIssue>> GetMyIssuesAsync(int maxIssues = 100, CancellationToken cancellationToken = default)
    {
        var all = new List<LinearIssue>();
        string? after = null;
        while (all.Count < maxIssues)
        {
            var size = Math.Min(PageSize, maxIssues - all.Count);
            var data = await PostAsync(LinearQueries.MyIssues(size, after), cancellationToken).ConfigureAwait(false);
            var page = LinearMapper.ParseIssuePage(JsonRead.Path(data, "viewer", "assignedIssues"));
            all.AddRange(page.Items);
            if (!page.HasNextPage || page.EndCursor is null) break;
            after = page.EndCursor;
        }

        return all;
    }

    /// <summary>Null when the issue does not exist.</summary>
    public async Task<LinearIssue?> GetIssueByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await PostAsync(LinearQueries.IssueByIdentifier(identifier), cancellationToken).ConfigureAwait(false);
            return JsonRead.Obj(data, "issue") is JsonElement node ? LinearMapper.ParseIssue(node) : null;
        }
        catch (LinearApiException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<LinearIssue>> GetRecentlyCompletedAsync(DateTimeOffset since, int max = 50, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync(LinearQueries.RecentlyCompleted(since, Math.Min(max, 100)), cancellationToken).ConfigureAwait(false);
        return LinearMapper.ParseIssuePage(JsonRead.Path(data, "viewer", "assignedIssues")).Items;
    }

    public async Task<IReadOnlyList<LinearTeam>> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<LinearTeam>();
        string? after = null;
        for (var i = 0; i < 10; i++)
        {
            var data = await PostAsync(LinearQueries.Teams(PageSize, after), cancellationToken).ConfigureAwait(false);
            var page = LinearMapper.ParseTeamPage(JsonRead.Obj(data, "teams"));
            all.AddRange(page.Items);
            if (!page.HasNextPage || page.EndCursor is null) break;
            after = page.EndCursor;
        }

        return all;
    }

    public async Task<IReadOnlyList<LinearLabel>> GetTeamLabelsAsync(string teamId, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync(LinearQueries.TeamLabels(teamId), cancellationToken).ConfigureAwait(false);
        return LinearMapper.ParseLabels(JsonRead.Path(data, "team", "labels"));
    }

    public async Task<LinearIssue> CreateIssueAsync(
        string teamId,
        string title,
        string? description = null,
        int? priority = null,
        IReadOnlyList<string>? labelIds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamId)) throw new ArgumentException("A team id is required.", nameof(teamId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A title is required.", nameof(title));
        if (priority is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be 0 to 4.");

        var data = await PostAsync(
            LinearQueries.CreateIssue(teamId, title.Trim(), description, priority, labelIds),
            cancellationToken).ConfigureAwait(false);
        var payload = JsonRead.Obj(data, "issueCreate");
        if (payload is JsonElement p && JsonRead.Bool(p, "success") == true && JsonRead.Obj(p, "issue") is JsonElement node)
        {
            var issue = LinearMapper.ParseIssue(node);
            if (issue is not null) return issue;
        }

        throw new LinearApiException("Linear did not create the issue.", Array.Empty<string>(), false);
    }

    /// <summary>Attaches a GitHub pull request to an issue.</summary>
    public async Task LinkPullRequestAsync(string issueId, string url, string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(issueId)) throw new ArgumentException("An issue id is required.", nameof(issueId));
        if (!LinkFilter.IsGitHubUrl(url)) throw new ArgumentException("Only https://github.com/ links can be attached.", nameof(url));

        var data = await PostAsync(LinearQueries.LinkPullRequest(issueId, url, title), cancellationToken).ConfigureAwait(false);
        var payload = JsonRead.Obj(data, "attachmentCreate");
        if (payload is JsonElement p && JsonRead.Bool(p, "success") == true) return;
        throw new LinearApiException("Linear did not attach the pull request.", Array.Empty<string>(), false);
    }

    private async Task<JsonElement> PostAsync(LinearRequest request, CancellationToken cancellationToken)
    {
        var key = _apiKey()?.Trim();
        if (string.IsNullOrEmpty(key)) throw new LinearAuthException("No Linear API key is configured.");

        var payload = new Dictionary<string, object?>
        {
            ["query"] = request.Query,
            ["variables"] = request.Variables,
        };
        var body = JsonSerializer.Serialize(payload);

        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.TryAddWithoutValidation("Authorization", key);
            message.Headers.TryAddWithoutValidation("User-Agent", "Cshubben");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new LinearException("Could not reach Linear: " + ex.Message, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LinearException("The request to Linear timed out.", ex);
            }

            using (response)
            {
                var rateLimit = ReadRateLimit(response);
                if (rateLimit is not null) _lastRateLimit = rateLimit;
                var retryAfter = RetryAfter(response, rateLimit);
                var status = (int)response.StatusCode;
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (status >= 500)
                {
                    if (attempt == 0)
                    {
                        await _delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new LinearHttpException(status);
                }

                if (status == 429) throw new LinearRateLimitException("Linear rate limit reached.", retryAfter);
                if (status == 401 || status == 403) throw new LinearAuthException("Linear rejected the API key.");

                var root = TryParse(text);
                if (root is null)
                {
                    if (!response.IsSuccessStatusCode) throw new LinearHttpException(status);
                    throw new LinearApiException("Linear returned a response that could not be read.", Array.Empty<string>(), false);
                }

                var data = LinearMapper.Unwrap(root.Value, retryAfter);
                if (!response.IsSuccessStatusCode) throw new LinearHttpException(status);
                return data;
            }
        }
    }

    private static JsonElement? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LinearRateLimit? ReadRateLimit(HttpResponseMessage response)
    {
        int? remaining = null;
        DateTimeOffset? resetsAt = null;
        if (response.Headers.TryGetValues("X-RateLimit-Requests-Remaining", out var remainingValues))
        {
            var first = remainingValues.FirstOrDefault();
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) remaining = parsed;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Requests-Reset", out var resetValues))
        {
            var first = resetValues.FirstOrDefault();
            if (long.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0 && ms < 253_402_300_000_000)
            {
                resetsAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }
        }

        return remaining is null && resetsAt is null ? null : new LinearRateLimit(remaining, resetsAt);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response, LinearRateLimit? rateLimit)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is TimeSpan d) return d;
        if (rateLimit?.ResetsAt is DateTimeOffset reset)
        {
            var wait = reset - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
