using System.Globalization;
using System.Text.Json;

namespace Cshubben.Core.Features.Errors;

/// <summary>A Loki data source in Grafana. <see cref="Uid"/> is what the proxy address needs.</summary>
public sealed record LokiDatasource(string Uid, string Name);

/// <summary>
/// Reads Loki's JSON answers (through Grafana's data source proxy) into plain values. Pure, so it is tested with inline JSON.
/// Anything that does not look like Loki's answer throws a <see cref="GrafanaException"/> with a calm message.
/// </summary>
public static class LokiParser
{
    private const string Unreadable = "Grafana returned a response that could not be read.";

    /// <summary>
    /// A metric (matrix) answer to the per-application count query, as dense counts per bucket of the window.
    /// Applications that have no samples at all are not in the result.
    /// </summary>
    public static IReadOnlyList<ErrorSeries> ParseMatrix(string json, ErrorWindow window)
    {
        var data = ReadData(json);
        var totals = new Dictionary<string, int[]>(StringComparer.Ordinal);

        foreach (var item in Results(data))
        {
            if (!item.TryGetProperty("metric", out var metric)) continue;
            var app = Label(metric, LogQl.AppLabel);
            if (app.Length == 0) continue;
            if (!item.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) continue;

            if (!totals.TryGetValue(app, out var counts))
            {
                counts = new int[window.Buckets];
                totals[app] = counts;
            }

            foreach (var point in values.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2) continue;
                if (!TryNumber(point[0], out var time) || !TryNumber(point[1], out var value)) continue;
                var index = window.IndexOfSampleEnd(time);
                if (index < 0 || double.IsInfinity(value) || double.IsNaN(value)) continue;
                counts[index] += (int)Math.Max(0, Math.Round(value));
            }
        }

        return totals
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ErrorSeries(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>A log (streams) answer: the lines with their application, pod and level, oldest first.</summary>
    public static IReadOnlyList<LogEntry> ParseStreams(string json)
    {
        var data = ReadData(json);
        var entries = new List<LogEntry>();

        foreach (var item in Results(data))
        {
            var stream = item.TryGetProperty("stream", out var s) ? s : default;
            var app = Label(stream, LogQl.AppLabel);
            var pod = Label(stream, "pod");
            var level = Label(stream, LogQl.LevelLabel);
            if (level.Length == 0) level = Label(stream, "level");

            if (!item.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) continue;
            foreach (var pair in values.EnumerateArray())
            {
                // [ "<unix nanoseconds>", "<line>" ], newer Loki versions add a third element with metadata.
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) continue;
                if (pair[0].ValueKind != JsonValueKind.String || pair[1].ValueKind != JsonValueKind.String) continue;
                if (!long.TryParse(pair[0].GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nanos)) continue;
                entries.Add(new LogEntry(
                    DateTimeOffset.FromUnixTimeMilliseconds(nanos / 1_000_000),
                    app,
                    pod,
                    level,
                    pair[1].GetString() ?? ""));
            }
        }

        return entries.OrderBy(e => e.At).ToList();
    }

    /// <summary>The strings of a labels or label values answer.</summary>
    public static IReadOnlyList<string> ParseStringList(string json)
    {
        var data = ReadData(json);
        if (data.ValueKind != JsonValueKind.Array) throw new GrafanaException(Unreadable);
        return data.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>The Loki data sources in Grafana's <c>/api/datasources</c> answer.</summary>
    public static IReadOnlyList<LokiDatasource> ParseDatasources(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new GrafanaException(Unreadable);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new GrafanaException(Unreadable);
            var result = new List<LokiDatasource>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(Label(item, "type"), "loki", StringComparison.OrdinalIgnoreCase)) continue;
                var uid = Label(item, "uid");
                if (uid.Length == 0) continue;
                var name = Label(item, "name");
                result.Add(new LokiDatasource(uid, name.Length == 0 ? uid : name));
            }

            return result;
        }
    }

    /// <summary>The data source with the configured uid or name, otherwise the first Loki data source. Null when there is none.</summary>
    public static LokiDatasource? PickLoki(IReadOnlyList<LokiDatasource> sources, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var wanted = configured.Trim();
            return sources.FirstOrDefault(s => string.Equals(s.Uid, wanted, StringComparison.OrdinalIgnoreCase))
                   ?? sources.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }

        return sources.Count > 0 ? sources[0] : null;
    }

    /// <summary>One short line of server text for an error message: no line breaks, at most 200 characters.</summary>
    public static string Shorten(string? text)
    {
        var line = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= 200 ? line : line.Substring(0, 199) + "…";
    }

    private static JsonElement ReadData(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new GrafanaException(Unreadable);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new GrafanaException(Unreadable);

            if (root.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && !string.Equals(status.GetString(), "success", StringComparison.Ordinal))
            {
                var detail = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                    ? Shorten(error.GetString())
                    : "";
                throw new GrafanaException(detail.Length == 0 ? "Loki reported an error." : "Loki reported an error: " + detail);
            }

            if (!root.TryGetProperty("data", out var data)) throw new GrafanaException(Unreadable);
            return data.Clone();
        }
    }

    private static IEnumerable<JsonElement> Results(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array)
        {
            throw new GrafanaException(Unreadable);
        }

        return result.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();
    }

    private static string Label(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool TryNumber(JsonElement element, out double value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value);
            case JsonValueKind.String:
                return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                value = 0;
                return false;
        }
    }
}
