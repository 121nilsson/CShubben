using System.Globalization;
using System.Text.Json;

namespace Cshubben.Core.Integrations.Linear;

/// <summary>Pure JSON-to-model mapping for Linear responses.</summary>
public static class LinearMapper
{
    /// <summary>
    /// Returns the "data" object of a GraphQL response, or throws a typed exception when the response holds errors.
    /// </summary>
    public static JsonElement Unwrap(JsonElement root, TimeSpan? retryAfter = null)
    {
        if (JsonRead.Arr(root, "errors") is JsonElement errors && errors.GetArrayLength() > 0)
        {
            var messages = new List<string>();
            var auth = false;
            var rateLimited = false;
            var notFound = false;
            foreach (var error in errors.EnumerateArray())
            {
                var message = JsonRead.Str(error, "message") ?? "Unknown error";
                messages.Add(message);
                var lower = message.ToLowerInvariant();
                var type = "";
                var code = "";
                if (JsonRead.Obj(error, "extensions") is JsonElement ext)
                {
                    type = (JsonRead.Str(ext, "type") ?? "").ToLowerInvariant();
                    code = (JsonRead.Str(ext, "code") ?? "").ToUpperInvariant();
                }

                if (type.Contains("authentication") || code.Contains("AUTHENTICATION") || lower.Contains("not authenticated")
                    || lower.Contains("authentication required") || lower.Contains("unauthorized"))
                {
                    auth = true;
                }

                if (code == "RATELIMITED" || type.Contains("ratelimit") || lower.Contains("rate limit"))
                {
                    rateLimited = true;
                }

                if (lower.Contains("not found")) notFound = true;
            }

            if (auth) throw new LinearAuthException("Linear rejected the API key.");
            if (rateLimited) throw new LinearRateLimitException("Linear rate limit reached.", retryAfter);
            throw new LinearApiException("Linear returned errors: " + string.Join("; ", messages), messages, notFound);
        }

        if (JsonRead.Obj(root, "data") is JsonElement data) return data;
        throw new LinearApiException("Linear returned an empty response.", Array.Empty<string>(), false);
    }

    public static LinearViewer ParseViewer(JsonElement data)
    {
        var viewer = JsonRead.Obj(data, "viewer");
        if (viewer is not JsonElement v) throw new LinearApiException("Linear returned no viewer.", Array.Empty<string>(), false);
        var id = JsonRead.Str(v, "id");
        if (string.IsNullOrEmpty(id)) throw new LinearApiException("Linear returned no viewer id.", Array.Empty<string>(), false);
        var name = JsonRead.Str(v, "name");
        var email = JsonRead.Str(v, "email");
        return new LinearViewer(id, string.IsNullOrWhiteSpace(name) ? (email ?? "") : name, email);
    }

    /// <summary>Maps one issue node. Null when the node has no id or identifier.</summary>
    public static LinearIssue? ParseIssue(JsonElement node)
    {
        var id = JsonRead.Str(node, "id");
        var identifier = JsonRead.Str(node, "identifier");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(identifier)) return null;

        var stateName = "";
        var stateType = LinearStateType.Unknown;
        if (JsonRead.Obj(node, "state") is JsonElement state)
        {
            stateName = JsonRead.Str(state, "name") ?? "";
            stateType = ParseStateType(JsonRead.Str(state, "type"));
        }

        string? assignee = null;
        if (JsonRead.Obj(node, "assignee") is JsonElement who)
        {
            assignee = JsonRead.Str(who, "displayName");
            if (string.IsNullOrWhiteSpace(assignee)) assignee = JsonRead.Str(who, "name");
        }

        var teamKey = "";
        if (JsonRead.Obj(node, "team") is JsonElement team) teamKey = JsonRead.Str(team, "key") ?? "";

        string? project = null;
        if (JsonRead.Obj(node, "project") is JsonElement proj) project = JsonRead.Str(proj, "name");

        var labels = new List<string>();
        if (JsonRead.Path(node, "labels") is JsonElement labelConnection && JsonRead.Arr(labelConnection, "nodes") is JsonElement labelNodes)
        {
            foreach (var label in labelNodes.EnumerateArray())
            {
                var name = JsonRead.Str(label, "name");
                if (!string.IsNullOrWhiteSpace(name)) labels.Add(name);
            }
        }

        var priority = (int)(JsonRead.Num(node, "priority") ?? 0);
        if (priority < 0 || priority > 4) priority = 0;

        return new LinearIssue(
            id,
            identifier,
            JsonRead.Str(node, "title") ?? "",
            stateName,
            stateType,
            assignee,
            priority,
            teamKey,
            project,
            labels,
            JsonRead.Str(node, "url") ?? "",
            ParseDate(JsonRead.Str(node, "updatedAt")) ?? DateTimeOffset.UnixEpoch,
            ParseDate(JsonRead.Str(node, "completedAt")));
    }

    /// <summary>Maps a connection object ({ nodes, pageInfo }) of issues.</summary>
    public static LinearPage<LinearIssue> ParseIssuePage(JsonElement? connection)
    {
        var items = new List<LinearIssue>();
        if (connection is not JsonElement c) return new LinearPage<LinearIssue>(items, false, null);
        if (JsonRead.Arr(c, "nodes") is JsonElement nodes)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                var issue = ParseIssue(node);
                if (issue is not null) items.Add(issue);
            }
        }

        var (hasNext, cursor) = ParsePageInfo(c);
        return new LinearPage<LinearIssue>(items, hasNext, cursor);
    }

    public static LinearPage<LinearTeam> ParseTeamPage(JsonElement? connection)
    {
        var items = new List<LinearTeam>();
        if (connection is not JsonElement c) return new LinearPage<LinearTeam>(items, false, null);
        if (JsonRead.Arr(c, "nodes") is JsonElement nodes)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                var id = JsonRead.Str(node, "id");
                var key = JsonRead.Str(node, "key");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key)) continue;
                items.Add(new LinearTeam(id, key, JsonRead.Str(node, "name") ?? key));
            }
        }

        var (hasNext, cursor) = ParsePageInfo(c);
        return new LinearPage<LinearTeam>(items, hasNext, cursor);
    }

    public static IReadOnlyList<LinearLabel> ParseLabels(JsonElement? connection)
    {
        var items = new List<LinearLabel>();
        if (connection is JsonElement c && JsonRead.Arr(c, "nodes") is JsonElement nodes)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                var id = JsonRead.Str(node, "id");
                var name = JsonRead.Str(node, "name");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) items.Add(new LinearLabel(id, name));
            }
        }

        return items;
    }

    public static LinearStateType ParseStateType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "triage" => LinearStateType.Triage,
        "backlog" => LinearStateType.Backlog,
        "unstarted" => LinearStateType.Unstarted,
        "started" => LinearStateType.Started,
        "completed" => LinearStateType.Completed,
        "canceled" => LinearStateType.Canceled,
        _ => LinearStateType.Unknown,
    };

    private static (bool HasNext, string? Cursor) ParsePageInfo(JsonElement connection)
    {
        if (JsonRead.Obj(connection, "pageInfo") is JsonElement info)
        {
            var hasNext = JsonRead.Bool(info, "hasNextPage") == true;
            return (hasNext, JsonRead.Str(info, "endCursor"));
        }

        return (false, null);
    }

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }
}
