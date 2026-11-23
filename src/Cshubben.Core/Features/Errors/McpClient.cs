using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Cshubben.Core.Features.Errors;

/// <summary>
/// A minimal MCP client over Streamable HTTP: initialize, then call tools. It only does what the Errors page needs.
/// The access token comes from a callback so refreshing is somebody else's job, and it never appears in an exception.
/// </summary>
public sealed class McpClient
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly HttpClient _http;
    private readonly string _url;
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _session;
    private bool _initialized;
    private int _nextId = 1;

    public McpClient(HttpClient http, string url, Func<CancellationToken, Task<string>> accessToken)
    {
        _http = http;
        _url = url.Trim();
        _accessToken = accessToken;
    }

    /// <summary>Calls a tool and returns the text it answered with.</summary>
    public async Task<string> CallToolAsync(string tool, IReadOnlyDictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
                var id = _nextId++;
                var reply = await PostAsync(Request(id, "tools/call", w =>
                {
                    w.WriteString("name", tool);
                    w.WritePropertyName("arguments");
                    WriteArguments(w, arguments);
                }), cancellationToken).ConfigureAwait(false);

                // The server forgot our session (it restarted): start over once.
                if (reply.Status == (int)HttpStatusCode.NotFound && attempt == 0)
                {
                    _initialized = false;
                    _session = null;
                    continue;
                }

                return ReadToolResult(reply, id);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        var id = _nextId++;
        var reply = await PostAsync(Request(id, "initialize", w =>
        {
            w.WriteString("protocolVersion", ProtocolVersion);
            w.WriteStartObject("capabilities");
            w.WriteEndObject();
            w.WriteStartObject("clientInfo");
            w.WriteString("name", McpOAuth.ClientName);
            w.WriteString("version", "1.0");
            w.WriteEndObject();
        }), cancellationToken).ConfigureAwait(false);
        ThrowIfFailed(reply);
        _ = ReadResult(reply, id);

        _session = reply.SessionId;
        await PostAsync(Notification("notifications/initialized"), cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    private static string Request(int id, string method, Action<Utf8JsonWriter> parameters)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteNumber("id", id);
            w.WriteString("method", method);
            w.WriteStartObject("params");
            parameters(w);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Notification(string method) =>
        "{\"jsonrpc\":\"2.0\",\"method\":\"" + method + "\"}";

    private static void WriteArguments(Utf8JsonWriter w, IReadOnlyDictionary<string, object> arguments)
    {
        w.WriteStartObject();
        foreach (var (name, value) in arguments)
        {
            switch (value)
            {
                case int i:
                    w.WriteNumber(name, i);
                    break;
                case bool b:
                    w.WriteBoolean(name, b);
                    break;
                default:
                    w.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        w.WriteEndObject();
    }

    private sealed record Reply(int Status, string ContentType, string Text, string? SessionId);

    private async Task<Reply> PostAsync(string body, CancellationToken cancellationToken)
    {
        var token = await _accessToken(cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, _url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        request.Headers.TryAddWithoutValidation("User-Agent", McpOAuth.ClientName);
        if (_session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _session);

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
            string? session = null;
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var values)) session = values.FirstOrDefault();
            return new Reply((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType ?? "", text, session);
        }
    }

    private static void ThrowIfFailed(Reply reply)
    {
        if (reply.Status is >= 200 and < 300) return;
        if (reply.Status == (int)HttpStatusCode.Unauthorized)
            throw new LogsAuthException("The log service did not accept the sign-in. Sign in again in Settings > Errors.", true);
        if (reply.Status == (int)HttpStatusCode.Forbidden)
            throw new LogsAuthException("Your account is not allowed to use the log service. Ask its owner for access.", false);
        throw new LogsHttpException(reply.Status, LokiParser.Shorten(reply.Text));
    }

    private static string ReadToolResult(Reply reply, int id)
    {
        ThrowIfFailed(reply);
        var result = ReadResult(reply, id);
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        var text = new StringBuilder();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("text", out var part)
                    && part.ValueKind == JsonValueKind.String)
                {
                    if (text.Length > 0) text.Append('\n');
                    text.Append(part.GetString());
                }
            }
        }

        var isError = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (isError) throw new LogsException("The log service reported an error: " + LokiParser.Shorten(text.ToString()));
        return text.ToString();
    }

    /// <summary>The "result" of the JSON-RPC answer with this id, from a plain JSON body or an event stream.</summary>
    private static string ReadResult(Reply reply, int id)
    {
        foreach (var payload in Payloads(reply))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (!root.TryGetProperty("id", out var answered) || answered.ValueKind != JsonValueKind.Number || answered.GetInt32() != id) continue;

                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : "unknown error";
                    throw new LogsException("The log service reported an error: " + LokiParser.Shorten(message));
                }

                if (root.TryGetProperty("result", out var result)) return result.GetRawText();
            }
            catch (JsonException)
            {
                // Not JSON (a comment line or a keep-alive): look at the next payload.
            }
        }

        throw new LogsException("The log service returned an answer that could not be read.");
    }

    private static IEnumerable<string> Payloads(Reply reply)
    {
        if (!reply.ContentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
        {
            yield return reply.Text;
            yield break;
        }

        var data = new StringBuilder();
        foreach (var raw in reply.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.Length == 0)
            {
                if (data.Length > 0) yield return data.ToString();
                data.Clear();
                continue;
            }

            if (!raw.StartsWith("data:", StringComparison.Ordinal)) continue;
            if (data.Length > 0) data.Append('\n');
            data.Append(raw.AsSpan(5).TrimStart(' '));
        }

        if (data.Length > 0) yield return data.ToString();
    }
}
