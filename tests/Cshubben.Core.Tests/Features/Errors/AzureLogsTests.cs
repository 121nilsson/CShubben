using System.Net;
using System.Text;
using Cshubben.Core.Features.Errors;
using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class AzureKqlTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Ending(Noon, TimeSpan.FromMinutes(5), 12);

    [Fact]
    public void Counts_cover_exceptions_and_error_traces_in_the_window()
    {
        var kql = AzureKql.ErrorCounts(Window, false);
        Assert.Contains("AppTraces | where SeverityLevel >= 3", kql, StringComparison.Ordinal);
        Assert.Contains("AppExceptions", kql, StringComparison.Ordinal);
        Assert.Contains("datetime(2026-11-23T11:00:00.0000000Z)", kql, StringComparison.Ordinal);
        Assert.Contains("TimeGenerated < datetime(2026-11-23T12:00:00.0000000Z)", kql, StringComparison.Ordinal);
        Assert.Contains("bin(TimeGenerated, 300s)", kql, StringComparison.Ordinal);
        Assert.Contains("SeverityLevel >= 2", AzureKql.ErrorCounts(Window, true), StringComparison.Ordinal);
    }

    [Fact]
    public void A_hostile_app_name_stays_inside_its_quotes()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", AzureKql.Quote("a\"b\\c"));
        var kql = AzureKql.ErrorLines(Window, "x\" or 1==1 //", false, 50);
        Assert.Contains("AppRoleName == \"x\\\" or 1==1 //\"", kql, StringComparison.Ordinal);
        Assert.Contains("top 50 by TimeGenerated desc", kql, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_become_dense_series_per_app()
    {
        var bucket = Window.Start.AddMinutes(10).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var json = "{\"status\":200,\"message\":\"Success\",\"results\":["
            + "{\"AppRoleName\":\"binders-api\",\"bucket\":\"" + bucket + "\",\"n\":\"7\"},"
            + "{\"AppRoleName\":\"binders-api\",\"bucket\":\"" + bucket + "\",\"n\":3},"
            + "{\"AppRoleName\":\"\",\"bucket\":\"" + bucket + "\",\"n\":\"9\"},"
            + "{\"AppRoleName\":\"old-api\",\"bucket\":\"2020-01-01T00:00:00Z\",\"n\":\"9\"}]}";

        var series = AzureKql.ParseCounts(json, Window);

        var binders = Assert.Single(series);
        Assert.Equal("binders-api", binders.App);
        Assert.Equal(12, binders.Counts.Count);
        Assert.Equal(10, binders.Counts[2]);
        Assert.Equal(10, binders.Counts.Sum());
    }

    [Fact]
    public void A_failure_answer_and_garbage_are_reported()
    {
        var http = Assert.Throws<LogsHttpException>(() => AzureKql.Rows("{\"status\":400,\"message\":\"Bad query\"}"));
        Assert.Equal(400, http.StatusCode);
        Assert.Throws<LogsException>(() => AzureKql.Rows("not json"));
        Assert.Throws<LogsException>(() => AzureKql.Rows("{\"status\":200}"));
        Assert.Empty(AzureKql.Rows("{\"results\":[]}"));
    }

    [Fact]
    public void An_exception_row_becomes_a_log_text_the_fingerprint_understands()
    {
        const string stack = "[{\"parsedStack\":[{\"level\":0,\"method\":\"Acme.Repo.GetGroups\",\"fileName\":\"/_/src/Acme.Repo/Repo.cs\",\"line\":154},{\"level\":1,\"method\":\"System.Linq.Enumerable.First\",\"fileName\":\"\",\"line\":0}]}]";
        var text = AzureKql.Compose("System.NullReferenceException", "Object reference not set", stack);

        Assert.StartsWith("Object reference not set\nSystem.NullReferenceException: Object reference not set", text, StringComparison.Ordinal);
        Assert.Contains("   at Acme.Repo.GetGroups in /_/src/Acme.Repo/Repo.cs:line 154", text, StringComparison.Ordinal);
        Assert.Contains("   at System.Linq.Enumerable.First", text, StringComparison.Ordinal);

        var fingerprint = ErrorFingerprints.Of(text);
        Assert.Equal(ErrorKind.Exception, fingerprint.Kind);
        Assert.Equal("Acme.Repo.GetGroups", fingerprint.Method);
        Assert.Equal("Acme.Repo/Repo.cs", fingerprint.File);
        Assert.Equal(154, fingerprint.Line);
    }

    [Fact]
    public void A_trace_row_stays_a_message_and_a_broken_stack_does_not_throw()
    {
        Assert.Equal("Something failed", AzureKql.Compose("", "Something failed", ""));
        Assert.Contains("System.XException: boom", AzureKql.Compose("System.XException", "boom", "{not json"), StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_are_parsed_oldest_first()
    {
        var json = "{\"results\":[" +
            "{\"TimeGenerated\":\"2026-11-23T11:50:00Z\",\"Pod\":\"p2\",\"Level\":\"Warning\",\"ExceptionType\":\"\",\"Message\":\"later\",\"Stack\":\"\"}," +
            "{\"TimeGenerated\":\"2026-11-23T11:40:00Z\",\"Pod\":\"p1\",\"Level\":\"Error\",\"ExceptionType\":\"\",\"Message\":\"earlier\",\"Stack\":\"\"}]}";
        var lines = AzureKql.ParseLines(json, "binders-api");
        Assert.Equal(new[] { "earlier", "later" }, lines.Select(l => l.Message).ToArray());
        Assert.Equal("p1", lines[0].Pod);
        Assert.Equal("binders-api", lines[0].App);
    }
}

public class McpOAuthTests
{
    [Fact]
    public void The_challenge_is_the_s256_of_the_verifier()
    {
        // The example of RFC 7636, appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", McpOAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        Assert.Equal(43, McpOAuth.NewVerifier().Length);
        Assert.NotEqual(McpOAuth.NewState(), McpOAuth.NewState());
    }

    [Fact]
    public void The_authorize_url_carries_pkce_state_and_the_redirect()
    {
        var url = McpOAuth.BuildAuthorizeUrl("https://x.example/oauth/authorize", "client 1", "http://localhost:5000/callback", "a/b offline_access", "st", "ch");
        var query = McpOAuth.Query(url.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client 1", query["client_id"]);
        Assert.Equal("http://localhost:5000/callback", query["redirect_uri"]);
        Assert.Equal("a/b offline_access", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("ch", query["code_challenge"]);
        Assert.Equal("st", query["state"]);
    }

    [Fact]
    public void The_callback_gives_the_code_only_for_the_right_state()
    {
        Assert.Equal("abc", McpOAuth.ParseCallback(new Uri("http://127.0.0.1:5/callback?code=abc&state=s1"), "s1"));
        Assert.Throws<LogsAuthException>(() => McpOAuth.ParseCallback(new Uri("http://127.0.0.1:5/callback?code=abc&state=other"), "s1"));
        Assert.Throws<LogsAuthException>(() => McpOAuth.ParseCallback(new Uri("http://127.0.0.1:5/callback?state=s1"), "s1"));
        var refused = Assert.Throws<LogsAuthException>(() => McpOAuth.ParseCallback(new Uri("http://127.0.0.1:5/callback?error=access_denied&error_description=No+way&state=s1"), "s1"));
        Assert.Contains("No way", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tokens_are_read_with_expiry_and_a_kept_refresh_token()
    {
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var token = McpOAuth.ParseToken("{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":3600}", now);
        Assert.Equal("a", token.AccessToken);
        Assert.Equal("r", token.RefreshToken);
        Assert.Equal(now.AddHours(1), token.ExpiresAt);
        Assert.True(token.IsValidAt(now));
        Assert.False(token.IsValidAt(now.AddMinutes(59.5)));

        var kept = McpOAuth.ParseToken("{\"access_token\":\"a2\",\"expires_in\":\"60\"}", now, "old");
        Assert.Equal("old", kept.RefreshToken);

        var refused = Assert.Throws<LogsAuthException>(() => McpOAuth.ParseToken("{\"error\":\"invalid_grant\",\"error_description\":\"expired\"}", now));
        Assert.True(refused.NeedsSignIn);
        Assert.Throws<LogsException>(() => McpOAuth.ParseToken("{}", now));
        Assert.Throws<LogsException>(() => McpOAuth.ParseToken("nope", now));
    }

    [Fact]
    public void Metadata_is_read_from_the_discovery_documents()
    {
        const string resource = "{\"resource\":\"https://s\",\"authorization_servers\":[\"https://s/oauth\"],\"scopes_supported\":[\"api/Mcp.Tools.ReadWrite\",\"offline_access\"]}";
        const string server = "{\"authorization_endpoint\":\"https://s/oauth/authorize\",\"token_endpoint\":\"https://s/oauth/token\",\"registration_endpoint\":\"https://s/oauth/register\"}";
        var meta = McpOAuth.ParseMetadata(resource, server);
        Assert.Equal("https://s/oauth/authorize", meta.AuthorizeEndpoint);
        Assert.Equal("https://s/oauth/token", meta.TokenEndpoint);
        Assert.Equal("https://s/oauth/register", meta.RegistrationEndpoint);
        Assert.Equal("api/Mcp.Tools.ReadWrite offline_access", meta.Scope);
        Assert.Equal("https://s/oauth", McpOAuth.AuthorizationServer(resource));
        Assert.Null(McpOAuth.AuthorizationServer("{}"));
        Assert.Throws<LogsException>(() => McpOAuth.ParseMetadata(resource, "{}"));
    }
}

internal sealed class RouteFake : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;

    public RouteFake(Func<HttpRequestMessage, string, HttpResponseMessage> respond) => _respond = respond;

    public List<(string Method, string Url, string Body, string? Authorization)> Requests { get; } = new();

    public static HttpResponseMessage Json(HttpStatusCode status, string body, string type = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, type) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        string? authorization = null;
        if (request.Headers.TryGetValues("Authorization", out var values)) authorization = values.FirstOrDefault();
        Requests.Add((request.Method.Method, request.RequestUri!.ToString(), body, authorization));
        return _respond(request, body);
    }
}

internal sealed class FakeCallback : IAuthorizationCallback
{
    public string RedirectUri => "http://127.0.0.1:5555/callback";

    public Uri? Seen { get; private set; }

    public Task<Uri> AuthorizeAsync(Uri authorizeUrl, CancellationToken cancellationToken)
    {
        Seen = authorizeUrl;
        var state = McpOAuth.Query(authorizeUrl.Query)["state"];
        return Task.FromResult(new Uri("http://127.0.0.1:5555/callback?code=the-code&state=" + Uri.EscapeDataString(state)));
    }
}

public class McpOAuthSessionTests
{
    private const string Server = "https://logs.example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage Route(HttpRequestMessage request, string body, Func<string, HttpResponseMessage>? token = null)
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url.EndsWith("/.well-known/oauth-protected-resource", StringComparison.Ordinal))
            return RouteFake.Json(HttpStatusCode.OK, "{\"authorization_servers\":[\"" + Server + "/oauth\"],\"scopes_supported\":[\"api/x\",\"offline_access\"]}");
        if (url.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            return RouteFake.Json(HttpStatusCode.OK, "{\"authorization_endpoint\":\"" + Server + "/oauth/authorize\",\"token_endpoint\":\"" + Server + "/oauth/token\",\"registration_endpoint\":\"" + Server + "/oauth/register\"}");
        if (url.EndsWith("/oauth/register", StringComparison.Ordinal))
            return RouteFake.Json(HttpStatusCode.Created, "{\"client_id\":\"client-1\"}");
        if (url.EndsWith("/oauth/token", StringComparison.Ordinal))
            return (token ?? (_ => RouteFake.Json(HttpStatusCode.OK, "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\",\"expires_in\":3600}")))(body);
        return RouteFake.Json(HttpStatusCode.NotFound, "{}");
    }

    [Fact]
    public async Task Signing_in_registers_exchanges_the_code_and_saves_only_the_refresh_token()
    {
        var fake = new RouteFake((r, b) => Route(r, b));
        var secrets = new InMemorySecretStore();
        var session = new McpOAuthSession(new HttpClient(fake), Server + "/", secrets, () => Now);
        var callback = new FakeCallback();

        Assert.False(session.HasSignedIn);
        await session.SignInAsync(callback);

        Assert.True(session.HasSignedIn);
        Assert.Equal("access-1", await session.GetAccessTokenAsync());
        Assert.Contains("refresh-1", secrets.Get(McpOAuthSession.SecretKey), StringComparison.Ordinal);
        Assert.DoesNotContain("access-1", secrets.Get(McpOAuthSession.SecretKey), StringComparison.Ordinal);

        var register = fake.Requests.Single(r => r.Url.EndsWith("/oauth/register", StringComparison.Ordinal));
        Assert.Contains("http://127.0.0.1:5555/callback", register.Body, StringComparison.Ordinal);
        Assert.Contains("\"token_endpoint_auth_method\":\"none\"", register.Body, StringComparison.Ordinal);

        var token = fake.Requests.Single(r => r.Url.EndsWith("/oauth/token", StringComparison.Ordinal));
        Assert.Contains("grant_type=authorization_code", token.Body, StringComparison.Ordinal);
        Assert.Contains("code=the-code", token.Body, StringComparison.Ordinal);
        Assert.Contains("code_verifier=", token.Body, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", callback.Seen!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_access_token_is_refreshed_with_the_saved_refresh_token()
    {
        var clock = Now;
        var fake = new RouteFake((r, b) => Route(r, b, body =>
            body.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                ? RouteFake.Json(HttpStatusCode.OK, "{\"access_token\":\"access-2\",\"expires_in\":3600}")
                : RouteFake.Json(HttpStatusCode.OK, "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\",\"expires_in\":600}")));
        var session = new McpOAuthSession(new HttpClient(fake), Server, new InMemorySecretStore(), () => clock);
        await session.SignInAsync(new FakeCallback());

        clock = Now.AddMinutes(30);
        Assert.Equal("access-2", await session.GetAccessTokenAsync());
        Assert.Contains("refresh_token=refresh-1", fake.Requests.Last().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_refresh_token_is_forgotten_and_asks_for_a_new_sign_in()
    {
        var clock = Now;
        var refuse = false;
        var fake = new RouteFake((r, b) => Route(r, b, _ => refuse
            ? RouteFake.Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}")
            : RouteFake.Json(HttpStatusCode.OK, "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":60}")));
        var secrets = new InMemorySecretStore();
        var session = new McpOAuthSession(new HttpClient(fake), Server, secrets, () => clock);
        await session.SignInAsync(new FakeCallback());

        clock = Now.AddHours(1);
        refuse = true;
        var ex = await Assert.ThrowsAsync<LogsAuthException>(() => session.GetAccessTokenAsync());
        Assert.True(ex.NeedsSignIn);
        Assert.Null(secrets.Get(McpOAuthSession.SecretKey));
    }

    [Fact]
    public async Task Without_a_sign_in_no_token_is_available_and_nothing_is_sent()
    {
        var fake = new RouteFake((r, b) => Route(r, b));
        var session = new McpOAuthSession(new HttpClient(fake), Server, new InMemorySecretStore(), () => Now);
        var ex = await Assert.ThrowsAsync<LogsAuthException>(() => session.GetAccessTokenAsync());
        Assert.True(ex.NeedsSignIn);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task A_sign_in_for_another_server_does_not_count_and_sign_out_forgets_it()
    {
        var secrets = new InMemorySecretStore();
        var fake = new RouteFake((r, b) => Route(r, b));
        var first = new McpOAuthSession(new HttpClient(fake), Server, secrets, () => Now);
        await first.SignInAsync(new FakeCallback());

        Assert.False(new McpOAuthSession(new HttpClient(fake), "https://other.example.com", secrets, () => Now).HasSignedIn);
        first.SignOut();
        Assert.False(first.HasSignedIn);
    }
}

public class McpClientTests
{
    private static string Rpc(int id, string result) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}";

    private static HttpResponseMessage Answer(string body, string? session = null, bool eventStream = false)
    {
        var response = eventStream
            ? RouteFake.Json(HttpStatusCode.OK, "event: message\r\ndata: " + body + "\r\n\r\n", "text/event-stream")
            : RouteFake.Json(HttpStatusCode.OK, body);
        if (session is not null) response.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        return response;
    }

    private static string IdOf(string body) => System.Text.Json.JsonDocument.Parse(body).RootElement.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : "";

    private static McpClient Client(RouteFake fake) =>
        new(new HttpClient(fake), "https://logs.example.com", _ => Task.FromResult("tok"));

    [Fact]
    public async Task A_tool_call_initializes_once_sends_the_token_and_session_and_returns_the_text()
    {
        var fake = new RouteFake((r, body) =>
        {
            if (body.Contains("\"initialize\"", StringComparison.Ordinal)) return Answer(Rpc(int.Parse(IdOf(body)), "{\"protocolVersion\":\"2025-06-18\"}"), "sess-1");
            if (body.Contains("notifications/initialized", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.Accepted);
            return Answer(Rpc(int.Parse(IdOf(body)), "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"results\\\":[]}\"}]}"));
        });
        var client = Client(fake);
        var args = new Dictionary<string, object> { ["workspace"] = "w", ["hours"] = 25, ["query"] = "q \"x\"" };

        var text = await client.CallToolAsync("monitor_workspace_log_query", args);
        await client.CallToolAsync("monitor_workspace_log_query", args);

        Assert.Equal("{\"results\":[]}", text);
        Assert.Equal(1, fake.Requests.Count(r => r.Body.Contains("\"initialize\"", StringComparison.Ordinal)));
        var call = fake.Requests.Last();
        Assert.Equal("Bearer tok", call.Authorization);
        Assert.Contains("\"name\":\"monitor_workspace_log_query\"", call.Body, StringComparison.Ordinal);
        Assert.Contains("\"hours\":25", call.Body, StringComparison.Ordinal);
        Assert.Contains("\"query\":\"q \\u0022x\\u0022\"", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_event_stream_answer_is_read()
    {
        var fake = new RouteFake((r, body) =>
        {
            var id = IdOf(body);
            if (body.Contains("\"initialize\"", StringComparison.Ordinal)) return Answer(Rpc(int.Parse(id), "{}"), eventStream: true);
            if (id.Length == 0) return new HttpResponseMessage(HttpStatusCode.Accepted);
            return Answer(Rpc(int.Parse(id), "{\"content\":[{\"type\":\"text\",\"text\":\"hello\"}]}"), eventStream: true);
        });
        Assert.Equal("hello", await Client(fake).CallToolAsync("t", new Dictionary<string, object>()));
    }

    [Fact]
    public async Task A_tool_error_and_a_json_rpc_error_are_reported()
    {
        var toolError = new RouteFake((r, body) =>
        {
            var id = IdOf(body);
            if (id.Length == 0) return new HttpResponseMessage(HttpStatusCode.Accepted);
            return body.Contains("\"initialize\"", StringComparison.Ordinal)
                ? Answer(Rpc(int.Parse(id), "{}"))
                : Answer(Rpc(int.Parse(id), "{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"bad query\"}]}"));
        });
        var ex = await Assert.ThrowsAsync<LogsException>(() => Client(toolError).CallToolAsync("t", new Dictionary<string, object>()));
        Assert.Contains("bad query", ex.Message, StringComparison.Ordinal);

        var rpcError = new RouteFake((r, body) =>
        {
            var id = IdOf(body);
            if (id.Length == 0) return new HttpResponseMessage(HttpStatusCode.Accepted);
            return body.Contains("\"initialize\"", StringComparison.Ordinal)
                ? Answer(Rpc(int.Parse(id), "{}"))
                : Answer("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32602,\"message\":\"unknown tool\"}}");
        });
        var ex2 = await Assert.ThrowsAsync<LogsException>(() => Client(rpcError).CallToolAsync("t", new Dictionary<string, object>()));
        Assert.Contains("unknown tool", ex2.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Refused_calls_are_auth_failures_that_do_not_leak_the_token(HttpStatusCode status, bool needsSignIn)
    {
        var fake = new RouteFake((_, _) => RouteFake.Json(status, "{}"));
        var ex = await Assert.ThrowsAsync<LogsAuthException>(() => Client(fake).CallToolAsync("t", new Dictionary<string, object>()));
        Assert.Equal(needsSignIn, ex.NeedsSignIn);
        Assert.DoesNotContain("tok", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forgotten_session_is_started_again_once()
    {
        var initializes = 0;
        var fake = new RouteFake((r, body) =>
        {
            var id = IdOf(body);
            if (id.Length == 0) return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (body.Contains("\"initialize\"", StringComparison.Ordinal))
            {
                initializes++;
                return Answer(Rpc(int.Parse(id), "{}"), "sess-" + initializes);
            }

            return initializes == 1
                ? RouteFake.Json(HttpStatusCode.NotFound, "{}")
                : Answer(Rpc(int.Parse(id), "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}"));
        });
        Assert.Equal("ok", await Client(fake).CallToolAsync("t", new Dictionary<string, object>()));
        Assert.Equal(2, initializes);
    }
}

public class AzureLogsErrorSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static AzureLogsErrorSource Source(Func<string, string> tool, out List<string> bodies)
    {
        var seen = new List<string>();
        bodies = seen;
        var fake = new RouteFake((r, body) =>
        {
            string id = System.Text.Json.JsonDocument.Parse(body).RootElement.TryGetProperty("id", out var i) ? i.GetInt32().ToString() : "";
            if (id.Length == 0) return new HttpResponseMessage(HttpStatusCode.Accepted);
            if (body.Contains("\"initialize\"", StringComparison.Ordinal))
                return RouteFake.Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{}}");
            seen.Add(body);
            var text = System.Text.Json.JsonSerializer.Serialize(tool(body));
            return RouteFake.Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + text + "}]}}");
        });
        var client = new McpClient(new HttpClient(fake), "https://logs.example.com", _ => Task.FromResult("tok"));
        return new AzureLogsErrorSource(client, new AzureLogsQuery("ws", "rg", false, new[] { "binders-api", "contracts-api" }), () => Noon);
    }

    [Fact]
    public async Task Load_sends_the_workspace_and_resource_group_and_adds_quiet_known_apps()
    {
        var window = ErrorWindow.Default(Noon);
        var bucket = window.End.AddMinutes(-5).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var source = Source(_ => "{\"results\":[{\"AppRoleName\":\"binders-api\",\"bucket\":\"" + bucket + "\",\"n\":\"30\"}]}", out var bodies);

        var snapshot = await source.LoadAsync();

        Assert.Equal(new[] { "binders-api", "contracts-api" }, snapshot.Apps.Select(a => a.App).ToArray());
        Assert.Equal(30, snapshot.Apps[0].Total);
        var body = Assert.Single(bodies);
        Assert.Contains("\"workspace\":\"ws\"", body, StringComparison.Ordinal);
        Assert.Contains("\"resource-group\":\"rg\"", body, StringComparison.Ordinal);
        Assert.Contains("\"table\":\"AppTraces\"", body, StringComparison.Ordinal);
        Assert.False(source.IsDemo);
    }

    [Fact]
    public async Task Groups_come_from_exception_rows()
    {
        var at = Noon.AddMinutes(-20).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var stack = "[{\\\"parsedStack\\\":[{\\\"method\\\":\\\"A.B.Run\\\",\\\"fileName\\\":\\\"/src/A/B.cs\\\",\\\"line\\\":7}]}]";
        var source = Source(_ =>
            "{\"results\":[{\"TimeGenerated\":\"" + at + "\",\"Pod\":\"p\",\"Level\":\"Error\",\"ExceptionType\":\"System.InvalidOperationException\",\"Message\":\"boom\",\"Stack\":\"" + stack + "\"}]}",
            out _);

        var group = Assert.Single(await source.LoadGroupsAsync("binders-api"));

        Assert.Equal(1, group.Count);
        Assert.Equal("A.B.Run", group.Method);
        Assert.Equal("A/B.cs", group.File);
    }

    [Fact]
    public async Task Test_reports_the_trace_count()
    {
        var source = Source(_ => "{\"results\":[{\"n\":\"2328\"}]}", out _);
        Assert.Equal("Connected to the workspace ws: 2328 traces in the last hour.", await source.TestAsync());
    }

    [Fact]
    public void Failures_map_to_calm_facts()
    {
        Assert.True(LogsFailureFacts.From(new LogsAuthException("x", true))!.CredentialsMissing);
        Assert.Equal(403, LogsFailureFacts.From(new LogsAuthException("x", false))!.HttpStatus);
        Assert.Equal(500, LogsFailureFacts.From(new LogsHttpException(500))!.HttpStatus);
        Assert.NotNull(LogsFailureFacts.From(new LogsException("x")));
        Assert.Null(LogsFailureFacts.From(new InvalidOperationException()));
    }
}
