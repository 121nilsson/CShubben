using System.Globalization;
using System.Text.Json;

namespace Cshubben.Core.Features.Digest;

public sealed record DigestPageInfo(bool HasNextPage, string? EndCursor);

/// <summary>Pure: turns a GraphQL search page into merged pull requests. Tolerant of missing fields.</summary>
public static class DigestMapper
{
    public static (DigestPageInfo Page, IReadOnlyList<MergedPr> Items) Parse(JsonElement data)
    {
        var search = Prop(data, "search");
        var items = new List<MergedPr>();
        if (search is { } s && s.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;
                var number = Int(node, "number");
                var repo = Str(Prop(node, "repository"), "nameWithOwner");
                var merged = Time(node, "mergedAt");
                if (number == 0 || repo.Length == 0 || merged is null) continue;
                items.Add(new MergedPr(
                    repo, number, Str(node, "title"), Str(Prop(node, "author"), "login"), merged.Value,
                    Int(node, "additions"), Int(node, "deletions"), Int(node, "changedFiles"), Str(node, "url")));
            }
        }

        var info = Prop(search, "pageInfo");
        var next = info is { } i && i.TryGetProperty("hasNextPage", out var h) && h.ValueKind == JsonValueKind.True;
        var cursor = info is { } c && c.TryGetProperty("endCursor", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return (new DigestPageInfo(next, cursor), items);
    }

    private static JsonElement? Prop(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string Str(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
}
