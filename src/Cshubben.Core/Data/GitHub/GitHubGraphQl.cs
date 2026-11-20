using System.Text.Json;

namespace Cshubben.Core.Data.GitHub;

/// <summary>Thin GraphQL client: POST, typed errors, rate-limit info. Retry of 502/503 happens in <see cref="GitHubRest"/>.</summary>
public sealed class GitHubGraphQl
{
    public const string Endpoint = "https://api.github.com/graphql";

    private readonly GitHubRest _rest;

    public GitHubGraphQl(GitHubRest rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        _rest = rest;
    }

    public RateLimitInfo? LastRateLimit { get; private set; }

    /// <summary>
    /// Runs a query and returns the "data" object. With allowPartial, errors next to usable data
    /// (for example PRs that were deleted meanwhile) are ignored.
    /// </summary>
    public async Task<JsonElement> QueryAsync(
        string query,
        IReadOnlyDictionary<string, object?>? variables = null,
        bool allowPartial = false,
        CancellationToken cancellationToken = default)
    {
        var body = PrQueries.BuildBody(query, variables);
        var response = await _rest.SendAsync(HttpMethod.Post, Endpoint, body, true, cancellationToken).ConfigureAwait(false);
        if (response.Json is null) throw new GitHubGraphQlException(new[] { "Empty response from GitHub." }, false);
        var data = ExtractData(response.Json.Value, allowPartial);
        LastRateLimit = RateLimitInfo.FromGraphQl(data) ?? _rest.LastRateLimit;
        return data;
    }

    /// <summary>Pure: turns a full GraphQL response into its data object or throws <see cref="GitHubGraphQlException"/>.</summary>
    public static JsonElement ExtractData(JsonElement root, bool allowPartial)
    {
        var data = J.At(root, "data");
        var errors = J.Items(root, "errors");
        if (errors.Count > 0 && !(allowPartial && data is not null))
        {
            var messages = new List<string>();
            var limited = false;
            foreach (var e in errors)
            {
                var message = J.Str(e, "message");
                messages.Add(message.Length == 0 ? "Unknown error" : message);
                if (J.Str(e, "type") == "RATE_LIMITED" || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)) limited = true;
            }
            throw new GitHubGraphQlException(messages, limited);
        }
        if (data is null) throw new GitHubGraphQlException(new[] { "GitHub returned no data." }, false);
        return data.Value;
    }
}
