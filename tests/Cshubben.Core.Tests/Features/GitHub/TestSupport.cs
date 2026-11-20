using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cshubben.Core.Features.GitHub;

using Cshubben.Core.Data;
namespace Cshubben.Core.Tests.GitHubTests;

public sealed record Recorded(HttpMethod Method, string Url, string Body, string? Authorization, string? UserAgent, string? IfNoneMatch)
{
    /// <summary>The "variables" object of a GraphQL body.</summary>
    public JsonElement Variables => JsonDocument.Parse(Body).RootElement.GetProperty("variables").Clone();

    public string Query => JsonDocument.Parse(Body).RootElement.GetProperty("query").GetString() ?? "";
}

public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<Recorded, HttpResponseMessage> _responder;
    private readonly object _lock = new();
    private readonly List<Recorded> _requests = new();

    public FakeHandler(Func<Recorded, HttpResponseMessage> responder) => _responder = responder;

    public IReadOnlyList<Recorded> Requests
    {
        get { lock (_lock) { return _requests.ToList(); } }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var rec = new Recorded(
            request.Method,
            request.RequestUri!.ToString(),
            body,
            request.Headers.Authorization?.ToString(),
            request.Headers.UserAgent.ToString(),
            request.Headers.TryGetValues("If-None-Match", out var v) ? v.FirstOrDefault() : null);
        lock (_lock) { _requests.Add(rec); }
        return _responder(rec);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string? etag = null)
    {
        var resp = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (etag is not null) resp.Headers.ETag = new EntityTagHeaderValue(etag);
        return resp;
    }

    public static HttpResponseMessage Ok(string json, string? etag = null) => Json(HttpStatusCode.OK, json, etag);

    public static HttpResponseMessage Status(HttpStatusCode status) => Json(status, "{\"message\":\"boom\"}");
}

public static class Setup
{
    public static GitHubRest Rest(FakeHandler handler, string token = "ghp_secret_token_value") =>
        new GitHubRest(new HttpClient(handler), token, (_, _) => Task.CompletedTask);

    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>A search page with the given PR ids, a cursor and hasNextPage.</summary>
    public static string Page(string[] ids, bool hasNext, string? cursor)
    {
        var nodes = string.Join(",", ids.Select(id => "{\"id\":\"" + id + "\",\"number\":1,\"title\":\"t\",\"url\":\"u\",\"repository\":{\"nameWithOwner\":\"a/b\"}}"));
        var cur = cursor is null ? "null" : "\"" + cursor + "\"";
        return "{\"data\":{\"search\":{\"pageInfo\":{\"hasNextPage\":" + (hasNext ? "true" : "false") + ",\"endCursor\":" + cur + "},\"nodes\":[" + nodes + "]}}}";
    }
}

public sealed class FakeRunner : ICommandRunner
{
    private readonly Func<CommandResult> _result;

    public FakeRunner(Func<CommandResult> result) => _result = result;

    public int Calls { get; private set; }

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(_result());
    }
}
