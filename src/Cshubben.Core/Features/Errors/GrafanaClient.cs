using System.Globalization;
using System.Net;

namespace Cshubben.Core.Features.Errors;

/// <summary>
/// Reads Loki through Grafana's data source proxy (<c>/api/datasources/proxy/uid/{uid}/loki/api/v1/...</c>). It only reads.
/// The token goes in the Authorization header and never appears in an exception.
/// </summary>
public sealed class GrafanaClient
{
    private readonly HttpClient _http;
    private readonly Func<string?> _baseUrl;
    private readonly Func<string?> _token;

    public GrafanaClient(HttpClient http, Func<string?> baseUrl, Func<string?> token)
    {
        _http = http;
        _baseUrl = baseUrl;
        _token = token;
    }

    /// <summary>
    /// The Loki data source to read. A configured uid is used as it is (no lookup, so a token that may not list data sources
    /// works too); without one the first Loki data source in Grafana is used.
    /// </summary>
    public async Task<LokiDatasource> ResolveAsync(string? configuredUid, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredUid))
        {
            var uid = configuredUid.Trim();
            return new LokiDatasource(uid, uid);
        }

        var text = await GetAsync("/api/datasources", cancellationToken).ConfigureAwait(false);
        var found = LokiParser.PickLoki(LokiParser.ParseDatasources(text), null);
        return found ?? throw new GrafanaException("Grafana has no Loki data source this token can see. Enter its UID in Settings.");
    }

    /// <summary>Error counts per application over the window, one query for all applications.</summary>
    public async Task<IReadOnlyList<ErrorSeries>> GetErrorSeriesAsync(
        LokiDatasource source,
        ErrorWindow window,
        string ns,
        bool includeWarnings,
        CancellationToken cancellationToken = default)
    {
        // A sample at time t counts (t - step, t], so the first sample is one step after the start of the window.
        var query = LogQl.ErrorCounts(ns, includeWarnings, window.Step);
        var path = Loki(source, "query_range")
                   + "?query=" + Uri.EscapeDataString(query)
                   + "&start=" + Seconds(window.Start + window.Step)
                   + "&end=" + Seconds(window.End)
                   + "&step=" + Seconds(window.Step);
        var text = await GetAsync(path, cancellationToken).ConfigureAwait(false);
        return LokiParser.ParseMatrix(text, window);
    }

    /// <summary>The newest error lines of one application in the window, oldest first.</summary>
    public async Task<IReadOnlyList<LogEntry>> GetErrorLinesAsync(
        LokiDatasource source,
        ErrorWindow window,
        string ns,
        string app,
        bool includeWarnings,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var query = LogQl.Lines(ns, app, includeWarnings);
        var path = Loki(source, "query_range")
                   + "?query=" + Uri.EscapeDataString(query)
                   + "&start=" + Seconds(window.Start)
                   + "&end=" + Seconds(window.End)
                   + "&limit=" + Math.Clamp(limit, 1, 5000).ToString(CultureInfo.InvariantCulture)
                   + "&direction=backward";
        var text = await GetAsync(path, cancellationToken).ConfigureAwait(false);
        return LokiParser.ParseStreams(text);
    }

    /// <summary>A cheap check for the Test connection button. Returns what was found.</summary>
    public async Task<string> TestAsync(string? configuredUid, CancellationToken cancellationToken = default)
    {
        var source = await ResolveAsync(configuredUid, cancellationToken).ConfigureAwait(false);
        var text = await GetAsync(Loki(source, "labels"), cancellationToken).ConfigureAwait(false);
        var labels = LokiParser.ParseStringList(text);
        var missing = new[] { LogQl.AppLabel, LogQl.NamespaceLabel, LogQl.LevelLabel }
            .Where(l => !labels.Contains(l, StringComparer.Ordinal))
            .ToList();
        var name = source.Name == source.Uid ? "UID " + source.Uid : "'" + source.Name + "'";
        return missing.Count == 0
            ? "Connected to the Loki data source " + name + "."
            : "Connected to the Loki data source " + name + ", but it has no " + string.Join(", ", missing) + " label.";
    }

    private static string Loki(LokiDatasource source, string endpoint) =>
        "/api/datasources/proxy/uid/" + Uri.EscapeDataString(source.Uid) + "/loki/api/v1/" + endpoint;

    private static string Seconds(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static string Seconds(TimeSpan span) => ((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        var baseUrl = _baseUrl()?.Trim().TrimEnd('/');
        var token = _token()?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
            throw new GrafanaAuthException("No Grafana address or token is configured.", missing: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + path);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "Cshubben");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GrafanaException("Could not reach Grafana: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GrafanaException("The request to Grafana timed out.", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return text;

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new GrafanaAuthException("Grafana rejected the token, or the token may not read this data source.", missing: false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new GrafanaHttpException(status, "the data source or address was not found. Check the Grafana address and the data source UID");
            if (response.StatusCode == HttpStatusCode.BadRequest)
                throw new GrafanaHttpException(status, "Loki rejected the query. " + LokiParser.Shorten(text));
            throw new GrafanaHttpException(status);
        }
    }
}
