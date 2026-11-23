using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cshubben.Core.Features.Errors;

/// <summary>What to read from the Azure Monitor workspace. The caller builds it from the settings.</summary>
/// <param name="Workspace">Name or id of the Log Analytics workspace.</param>
/// <param name="ResourceGroup">Resource group of the workspace.</param>
/// <param name="IncludeWarnings">Also read warnings, not only errors.</param>
/// <param name="KnownApps">Applications to show even when they had no errors.</param>
public sealed record AzureLogsQuery(string Workspace, string ResourceGroup, bool IncludeWarnings, IReadOnlyList<string> KnownApps);

/// <summary>
/// KQL for Application Insights data in a Log Analytics workspace, and the reading of the rows that come back.
/// Pure, so the queries and the parsing are tested without a network. The application name is <c>AppRoleName</c>;
/// errors are <c>AppExceptions</c> plus <c>AppTraces</c> at severity Error (3) or higher (Warning is 2).
/// </summary>
public static class AzureKql
{
    public const string ToolName = "monitor_workspace_log_query";

    public static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "\"";

    private static string Time(DateTimeOffset at) =>
        "datetime(" + at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture) + ")";

    private static int MinSeverity(bool includeWarnings) => includeWarnings ? 2 : 3;

    /// <summary>Error counts per application and bucket for the whole window, one query for all applications.</summary>
    public static string ErrorCounts(ErrorWindow window, bool includeWarnings) =>
        "union (AppTraces | where SeverityLevel >= " + MinSeverity(includeWarnings).ToString(CultureInfo.InvariantCulture)
        + " | project TimeGenerated, AppRoleName), (AppExceptions | project TimeGenerated, AppRoleName)"
        + " | where TimeGenerated >= " + Time(window.Start) + " and TimeGenerated < " + Time(window.End)
        + " and isnotempty(AppRoleName)"
        + " | summarize n = count() by AppRoleName, bucket = bin(TimeGenerated, "
        + ((long)window.Step.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s)";

    /// <summary>The newest error rows of one application, with the exception's stack as JSON for <see cref="ParseLines"/>.</summary>
    public static string ErrorLines(ErrorWindow window, string app, bool includeWarnings, int limit) =>
        "union"
        + " (AppExceptions | where AppRoleName == " + Quote(app)
        + " | project TimeGenerated, Pod = AppRoleInstance, Level = \"Error\", ExceptionType, Message = OuterMessage, Stack = tostring(Details)),"
        + " (AppTraces | where AppRoleName == " + Quote(app) + " and SeverityLevel >= " + MinSeverity(includeWarnings).ToString(CultureInfo.InvariantCulture)
        + " | project TimeGenerated, Pod = AppRoleInstance, Level = iff(SeverityLevel >= 3, \"Error\", \"Warning\"), ExceptionType = \"\", Message, Stack = \"\")"
        + " | where TimeGenerated >= " + Time(window.Start) + " and TimeGenerated < " + Time(window.End)
        + " | top " + Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture) + " by TimeGenerated desc";

    /// <summary>The rows of a tool answer: <c>{"results":[...]}</c> or a bare array.</summary>
    public static IReadOnlyList<JsonElement> Rows(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                    ? results
                    : default;
            if (array.ValueKind != JsonValueKind.Array)
            {
                // The service reports failures as {"status":4xx,"message":...}.
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var status)
                    && status.ValueKind == JsonValueKind.Number && status.GetInt32() >= 400)
                    throw new LogsHttpException(status.GetInt32(), root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? LokiParser.Shorten(m.GetString()) : null);
                throw new LogsException(Unreadable);
            }

            return array.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch (JsonException)
        {
            throw new LogsException(Unreadable);
        }
    }

    private const string Unreadable = "The log service returned an answer that could not be read.";

    /// <summary>Dense counts per application from the rows of <see cref="ErrorCounts"/>.</summary>
    public static IReadOnlyList<ErrorSeries> ParseCounts(string text, ErrorWindow window)
    {
        var perApp = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var row in Rows(text))
        {
            var app = Str(row, "AppRoleName");
            if (string.IsNullOrWhiteSpace(app)) continue;
            if (!TryTime(Str(row, "bucket"), out var at)) continue;
            var index = window.IndexOf(at);
            if (index < 0) continue;
            if (!TryNumber(row, "n", out var n) || n <= 0) continue;

            if (!perApp.TryGetValue(app, out var counts))
            {
                counts = new int[window.Buckets];
                perApp[app] = counts;
            }

            counts[index] += (int)Math.Min(n, int.MaxValue);
        }

        return perApp
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new ErrorSeries(p.Key, p.Value))
            .ToList();
    }

    /// <summary>Log lines in the shape <see cref="ErrorFingerprints"/> reads, oldest first.</summary>
    public static IReadOnlyList<LogEntry> ParseLines(string text, string app)
    {
        var entries = new List<LogEntry>();
        foreach (var row in Rows(text))
        {
            if (!TryTime(Str(row, "TimeGenerated"), out var at)) continue;
            var message = Compose(Str(row, "ExceptionType"), Str(row, "Message"), Str(row, "Stack"));
            entries.Add(new LogEntry(at, app, Str(row, "Pod") ?? "", Str(row, "Level") ?? "Error", message));
        }

        return entries.OrderBy(e => e.At).ToList();
    }

    /// <summary>
    /// The text of one error as a .NET log would print it: the message, then the exception line and one "at" line per stack frame.
    /// Application Insights keeps the stack as JSON (<c>[{"parsedStack":[{"method":..,"fileName":..,"line":..}]}]</c>).
    /// </summary>
    public static string Compose(string? exceptionType, string? message, string? stackJson)
    {
        var text = new StringBuilder();
        var outer = (message ?? "").Trim();
        if (string.IsNullOrEmpty(exceptionType)) return outer;

        if (outer.Length > 0) text.Append(outer).Append('\n');
        text.Append(exceptionType).Append(": ").Append(outer);

        if (!string.IsNullOrWhiteSpace(stackJson))
        {
            try
            {
                using var document = JsonDocument.Parse(stackJson);
                var details = document.RootElement;
                var first = details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0 ? details[0] : details;
                if (first.ValueKind == JsonValueKind.Object
                    && first.TryGetProperty("parsedStack", out var frames)
                    && frames.ValueKind == JsonValueKind.Array)
                {
                    foreach (var frame in frames.EnumerateArray())
                    {
                        var method = Str(frame, "method");
                        if (string.IsNullOrWhiteSpace(method)) continue;
                        text.Append("\n   at ").Append(method);
                        var file = Str(frame, "fileName");
                        if (!string.IsNullOrWhiteSpace(file) && TryNumber(frame, "line", out var line) && line > 0)
                            text.Append(" in ").Append(file).Append(":line ").Append(((long)line).ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (JsonException)
            {
                // A stack that cannot be read leaves the message and exception type, which still group fine.
            }
        }

        return text.ToString();
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Null ? null : value.ToString()
            : null;

    private static bool TryNumber(JsonElement element, string name, out double number)
    {
        number = 0;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetDouble(out number);
        return value.ValueKind == JsonValueKind.String
               && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryTime(string? text, out DateTimeOffset at) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at);
}

/// <summary>Reads errors from Azure Monitor (Log Analytics) through the MCP server that fronts it.</summary>
public sealed class AzureLogsErrorSource : IErrorSource
{
    private readonly McpClient _client;
    private readonly AzureLogsQuery _query;
    private readonly Func<DateTimeOffset> _clock;

    public AzureLogsErrorSource(McpClient client, AzureLogsQuery query, Func<DateTimeOffset>? clock = null)
    {
        _client = client;
        _query = query;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsDemo => false;

    public async Task<ErrorsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock();
        var window = ErrorWindow.Default(now);
        var text = await RunAsync("AppTraces", AzureKql.ErrorCounts(window, _query.IncludeWarnings), 5000, cancellationToken).ConfigureAwait(false);
        return ErrorsOverview.Build(window, AzureKql.ParseCounts(text, window), _query.KnownApps, now);
    }

    public async Task<IReadOnlyList<ErrorGroup>> LoadGroupsAsync(string app, CancellationToken cancellationToken = default)
    {
        var window = ErrorWindow.Default(_clock());
        var text = await RunAsync("AppExceptions", AzureKql.ErrorLines(window, app, _query.IncludeWarnings, 400), 400, cancellationToken).ConfigureAwait(false);
        return ErrorGrouping.Group(AzureKql.ParseLines(text, app), window);
    }

    /// <summary>A cheap check for the Test connection button: counts the traces of the last hour.</summary>
    public async Task<string> TestAsync(CancellationToken cancellationToken = default)
    {
        var text = await RunAsync("AppTraces", "AppTraces | where TimeGenerated > ago(1h) | summarize n = count()", 5, cancellationToken, 1).ConfigureAwait(false);
        var rows = AzureKql.Rows(text);
        var count = rows.Count > 0 && rows[0].TryGetProperty("n", out var n)
            ? n.ValueKind == JsonValueKind.Number ? n.GetInt64().ToString(CultureInfo.InvariantCulture) : n.ToString()
            : "0";
        return "Connected to the workspace " + _query.Workspace + ": " + count + " traces in the last hour.";
    }

    private Task<string> RunAsync(string table, string kql, int limit, CancellationToken cancellationToken, int hours = 25) =>
        _client.CallToolAsync(
            AzureKql.ToolName,
            new Dictionary<string, object>
            {
                ["workspace"] = _query.Workspace,
                ["resource-group"] = _query.ResourceGroup,
                ["table"] = table,
                ["query"] = kql,
                ["hours"] = hours,
                ["limit"] = limit,
            },
            cancellationToken);
}
