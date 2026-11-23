using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cshubben.Core.Features.Errors;

/// <summary>An access token with its refresh token and expiry.</summary>
public sealed record McpToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    /// <summary>True when the token is good for at least another minute.</summary>
    public bool IsValidAt(DateTimeOffset now) => ExpiresAt - now > TimeSpan.FromSeconds(60);
}

/// <summary>Where to sign in, found from the server's discovery documents.</summary>
public sealed record McpAuthMetadata(string AuthorizeEndpoint, string TokenEndpoint, string? RegistrationEndpoint, string Scope);

/// <summary>
/// The pure parts of OAuth for an MCP server: authorization code with PKCE (S256), a public client, discovery documents.
/// Everything that talks to the network or the browser is in <see cref="McpOAuthSession"/>.
/// </summary>
public static class McpOAuth
{
    public const string ClientName = "Cshubben";

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A random PKCE verifier (43 characters).</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>A random value that ties the browser's answer to our request.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    /// <summary>The S256 challenge of a verifier.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static Uri BuildAuthorizeUrl(string endpoint, string clientId, string redirectUri, string scope, string state, string challenge)
    {
        var query = string.Join(
            "&",
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(clientId),
            "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            "scope=" + Uri.EscapeDataString(scope),
            "state=" + Uri.EscapeDataString(state),
            "code_challenge=" + Uri.EscapeDataString(challenge),
            "code_challenge_method=S256");
        return new Uri(endpoint + (endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query);
    }

    /// <summary>The authorization code from the redirect, or an exception that says what the server answered instead.</summary>
    public static string ParseCallback(Uri callback, string expectedState)
    {
        var values = ParseQuery(callback.Query);
        if (values.TryGetValue("error", out var error))
        {
            values.TryGetValue("error_description", out var description);
            throw new LogsAuthException(
                "Sign-in was refused: " + (string.IsNullOrWhiteSpace(description) ? error : LokiParser.Shorten(description)), true);
        }

        if (!values.TryGetValue("state", out var state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
            throw new LogsAuthException("The sign-in answer did not match the request. Try again.", true);
        if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new LogsAuthException("The sign-in answer had no code. Try again.", true);
        return code;
    }

    public static IReadOnlyDictionary<string, string> Query(string query) => ParseQuery(query);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString((eq < 0 ? pair : pair.Substring(0, eq)).Replace('+', ' '));
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            result[name] = value;
        }

        return result;
    }

    /// <summary>Reads a token response. <paramref name="previousRefresh"/> is kept when the server does not send a new refresh token.</summary>
    public static McpToken ParseToken(string json, DateTimeOffset now, string? previousRefresh = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new LogsException(Unreadable);
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                var code = error.GetString() ?? "";
                var text = root.TryGetProperty("error_description", out var d) && d.ValueKind == JsonValueKind.String ? LokiParser.Shorten(d.GetString()) : code;
                throw new LogsAuthException("The sign-in was refused: " + text, true);
            }

            var access = Str(root, "access_token");
            if (string.IsNullOrWhiteSpace(access)) throw new LogsException(Unreadable);
            var refresh = Str(root, "refresh_token");
            var seconds = 3600.0;
            if (root.TryGetProperty("expires_in", out var expires))
            {
                if (expires.ValueKind == JsonValueKind.Number) seconds = expires.GetDouble();
                else if (expires.ValueKind == JsonValueKind.String
                         && double.TryParse(expires.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) seconds = parsed;
            }

            return new McpToken(access, string.IsNullOrWhiteSpace(refresh) ? previousRefresh : refresh, now + TimeSpan.FromSeconds(Math.Max(seconds, 0)));
        }
        catch (JsonException)
        {
            throw new LogsException(Unreadable);
        }
    }

    /// <summary>Reads the protected resource document and the authorization server's configuration.</summary>
    public static McpAuthMetadata ParseMetadata(string resourceJson, string serverJson)
    {
        try
        {
            using var resource = JsonDocument.Parse(resourceJson);
            using var server = JsonDocument.Parse(serverJson);
            var authorize = Str(server.RootElement, "authorization_endpoint");
            var token = Str(server.RootElement, "token_endpoint");
            if (string.IsNullOrWhiteSpace(authorize) || string.IsNullOrWhiteSpace(token)) throw new LogsException(Unreadable);

            var scopes = new List<string>();
            if (resource.RootElement.ValueKind == JsonValueKind.Object
                && resource.RootElement.TryGetProperty("scopes_supported", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) scopes.Add(item.GetString()!);
            }

            return new McpAuthMetadata(authorize, token, Str(server.RootElement, "registration_endpoint"), string.Join(' ', scopes));
        }
        catch (JsonException)
        {
            throw new LogsException(Unreadable);
        }
    }

    /// <summary>The first authorization server named by the protected resource document.</summary>
    public static string? AuthorizationServer(string resourceJson)
    {
        try
        {
            using var resource = JsonDocument.Parse(resourceJson);
            if (resource.RootElement.ValueKind == JsonValueKind.Object
                && resource.RootElement.TryGetProperty("authorization_servers", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) return item.GetString();
            }
        }
        catch (JsonException)
        {
            // Treated as "not found" below.
        }

        return null;
    }

    private const string Unreadable = "The log service returned an answer that could not be read.";

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
