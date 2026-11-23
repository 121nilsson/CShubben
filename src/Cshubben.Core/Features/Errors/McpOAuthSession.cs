using System.Net;
using System.Text;
using System.Text.Json;
using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.Errors;

/// <summary>
/// The browser side of signing in: a listener on the loopback address and the user's browser. Behind an interface because it is I/O.
/// </summary>
public interface IAuthorizationCallback
{
    /// <summary>The address the identity provider sends the user back to, for example http://127.0.0.1:53211/callback.</summary>
    string RedirectUri { get; }

    /// <summary>Opens the sign-in page and waits until the browser has been sent back. Returns the address it was sent back to.</summary>
    Task<Uri> AuthorizeAsync(Uri authorizeUrl, CancellationToken cancellationToken);
}

/// <summary>
/// Signs in to an MCP server that uses the standard MCP authorization: discovery, dynamic client registration, authorization
/// code with PKCE, refresh tokens. The client is public (no secret). Only the refresh token and client id are saved, in the
/// credential store; the access token lives in memory.
/// </summary>
public sealed class McpOAuthSession
{
    /// <summary>Name of the secret that holds the client id and refresh token.</summary>
    public const string SecretKey = "logs.oauth";

    private readonly HttpClient _http;
    private readonly string _serverUrl;
    private readonly ISecretStore _secrets;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpToken? _token;
    private McpAuthMetadata? _metadata;

    public McpOAuthSession(HttpClient http, string serverUrl, ISecretStore secrets, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _serverUrl = serverUrl.Trim().TrimEnd('/');
        _secrets = secrets;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>True when a sign-in for this server is saved (it may still have expired).</summary>
    public bool HasSignedIn => LoadStored() is not null;

    /// <summary>A valid access token. Refreshes it when needed; never opens a browser.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is { } current && current.IsValidAt(_clock())) return current.AccessToken;

            var stored = LoadStored();
            if (stored is null || string.IsNullOrEmpty(stored.RefreshToken))
                throw new LogsAuthException("Sign in to the log service in Settings > Errors.", true);

            var metadata = await MetadataAsync(cancellationToken).ConfigureAwait(false);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = stored.RefreshToken,
                ["client_id"] = stored.ClientId,
                ["scope"] = metadata.Scope,
            };

            try
            {
                var response = await PostFormAsync(metadata.TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
                _token = McpOAuth.ParseToken(response, _clock(), stored.RefreshToken);
            }
            catch (LogsAuthException)
            {
                // The refresh token is no longer good: forget it so the page asks for a new sign-in.
                SignOutCore();
                throw new LogsAuthException("The sign-in to the log service has expired. Sign in again in Settings > Errors.", true);
            }

            if (_token.RefreshToken != stored.RefreshToken) Save(new Stored(_serverUrl, stored.ClientId, _token.RefreshToken ?? ""));
            return _token.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Registers this app (once per sign-in), sends the user to the browser and trades the answer for tokens.</summary>
    public async Task SignInAsync(IAuthorizationCallback callback, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var metadata = await MetadataAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(metadata.RegistrationEndpoint))
                throw new LogsException("The log service does not allow apps to register themselves.");

            var clientId = await RegisterAsync(metadata.RegistrationEndpoint, callback.RedirectUri, metadata.Scope, cancellationToken).ConfigureAwait(false);
            var verifier = McpOAuth.NewVerifier();
            var state = McpOAuth.NewState();
            var url = McpOAuth.BuildAuthorizeUrl(metadata.AuthorizeEndpoint, clientId, callback.RedirectUri, metadata.Scope, state, McpOAuth.Challenge(verifier));

            var returned = await callback.AuthorizeAsync(url, cancellationToken).ConfigureAwait(false);
            var code = McpOAuth.ParseCallback(returned, state);

            var response = await PostFormAsync(
                metadata.TokenEndpoint,
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = callback.RedirectUri,
                    ["client_id"] = clientId,
                    ["code_verifier"] = verifier,
                },
                cancellationToken).ConfigureAwait(false);

            _token = McpOAuth.ParseToken(response, _clock());
            Save(new Stored(_serverUrl, clientId, _token.RefreshToken ?? ""));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the saved sign-in.</summary>
    public void SignOut()
    {
        SignOutCore();
    }

    private void SignOutCore()
    {
        _token = null;
        _secrets.Delete(SecretKey);
    }

    private async Task<McpAuthMetadata> MetadataAsync(CancellationToken cancellationToken)
    {
        if (_metadata is not null) return _metadata;

        var resource = await GetTextAsync(_serverUrl + "/.well-known/oauth-protected-resource", cancellationToken).ConfigureAwait(false);
        var server = McpOAuth.AuthorizationServer(resource)
                     ?? throw new LogsException("The log service did not say where to sign in.");
        var config = await GetTextAsync(server.TrimEnd('/') + "/.well-known/openid-configuration", cancellationToken).ConfigureAwait(false);
        _metadata = McpOAuth.ParseMetadata(resource, config);
        return _metadata;
    }

    private async Task<string> RegisterAsync(string endpoint, string redirectUri, string scope, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["client_name"] = McpOAuth.ClientName,
                ["redirect_uris"] = new[] { redirectUri },
                ["grant_types"] = new[] { "authorization_code", "refresh_token" },
                ["response_types"] = new[] { "code" },
                ["token_endpoint_auth_method"] = "none",
                ["scope"] = scope,
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        var text = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("client_id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString()))
                return id.GetString()!;
        }
        catch (JsonException)
        {
            // Falls through to the error below.
        }

        throw new LogsException("The log service returned an answer that could not be read.");
    }

    private async Task<string> PostFormAsync(string endpoint, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
        return await SendAsync(request, cancellationToken, tokenEndpoint: true).ConfigureAwait(false);
    }

    private async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool tokenEndpoint = false)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", McpOAuth.ClientName);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LogsException("Could not reach the log service: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LogsException("The request to the log service timed out.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return text;

            // A token endpoint explains a refusal in JSON ("invalid_grant"); let the token parser turn that into an auth error.
            if (tokenEndpoint && (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized))
            {
                McpOAuth.ParseToken(text, _clock());
                throw new LogsAuthException("The sign-in was refused.", true);
            }

            throw new LogsHttpException((int)response.StatusCode, LokiParser.Shorten(text));
        }
    }

    private sealed record Stored(string Server, string ClientId, string RefreshToken);

    private Stored? LoadStored()
    {
        var text = _secrets.Get(SecretKey);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(text);
            return stored is not null
                   && string.Equals(stored.Server, _serverUrl, StringComparison.Ordinal)
                   && !string.IsNullOrEmpty(stored.ClientId)
                ? stored
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Save(Stored stored) => _secrets.Set(SecretKey, JsonSerializer.Serialize(stored));
}
