using System.Net;
using System.Net.Sockets;
using System.Text;
using Cshubben.Core.Features.Errors;

namespace Cshubben.Services;

/// <summary>
/// Opens the sign-in page in the user's browser and waits for the browser to be sent back to a port on this machine
/// (127.0.0.1, a free port chosen when this object is created). A plain TCP listener, so no URL reservation or admin rights are needed.
/// </summary>
sealed class LoopbackAuthorization : IAuthorizationCallback, IDisposable
{
    private readonly TcpListener _listener;
    private readonly int _port;

    public LoopbackAuthorization()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(4);
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        RedirectUri = "http://127.0.0.1:" + _port + "/callback";
    }

    public string RedirectUri { get; }

    public async Task<Uri> AuthorizeAsync(Uri authorizeUrl, CancellationToken cancellationToken)
    {
        if (!Shell.OpenUrl(authorizeUrl.AbsoluteUri))
            throw new LogsException("Could not open the browser for signing in.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                var target = await ReadTargetAsync(stream, timeout.Token).ConfigureAwait(false);
                if (target is null || !target.StartsWith("/callback", StringComparison.Ordinal))
                {
                    await RespondAsync(stream, "404 Not Found", "", timeout.Token).ConfigureAwait(false);
                    continue;
                }

                await RespondAsync(
                    stream,
                    "200 OK",
                    "<html><body style=\"font-family:sans-serif\"><h3>You can close this tab</h3><p>Return to Cshubben.</p></body></html>",
                    timeout.Token).ConfigureAwait(false);
                return new Uri("http://127.0.0.1:" + _port + target);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LogsException("Signing in took too long. Try again.");
        }
    }

    public void Dispose() => _listener.Stop();

    /// <summary>The request target of the first line ("GET /callback?code=... HTTP/1.1"), or null when it cannot be read.</summary>
    private static async Task<string?> ReadTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            var end = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (end < 0) continue;
            var parts = text.Substring(0, end).Split(' ');
            return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
        }

        return null;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string html, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 " + status + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
