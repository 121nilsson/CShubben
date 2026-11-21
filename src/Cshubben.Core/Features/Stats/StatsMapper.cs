using System.Globalization;
using System.Text.Json;

namespace Cshubben.Core.Features.Stats;

public sealed record StatsPageInfo(bool HasNextPage, string? EndCursor);

/// <summary>Pure: turns GraphQL search pages into <see cref="MergedPrRecord"/> and <see cref="ReviewRecord"/>. Tolerant of missing fields.</summary>
public static class StatsMapper
{
    public static (StatsPageInfo Page, IReadOnlyList<MergedPrRecord> Items) ParseMerged(JsonElement data)
    {
        var search = Prop(data, "search");
        var items = new List<MergedPrRecord>();
        foreach (var node in Nodes(search))
        {
            var number = Int(node, "number");
            var repo = Str(Prop(node, "repository"), "nameWithOwner");
            var opened = Time(node, "createdAt");
            var merged = Time(node, "mergedAt");
            if (number == 0 || repo.Length == 0 || opened is null || merged is null) continue;

            var author = Str(Prop(node, "author"), "login");
            DateTimeOffset? firstReview = null;
            foreach (var review in Nodes(Prop(node, "reviews")))
            {
                if (Str(review, "state") == "PENDING") continue;
                if (author.Length > 0 && string.Equals(Str(Prop(review, "author"), "login"), author, StringComparison.OrdinalIgnoreCase)) continue;
                var at = Time(review, "submittedAt");
                if (at is not null && (firstReview is null || at < firstReview)) firstReview = at;
            }

            items.Add(new MergedPrRecord(repo, number, Str(node, "title"), author, opened.Value, merged.Value, firstReview,
                Int(node, "additions"), Int(node, "deletions"), Int(node, "changedFiles")));
        }
        return (ParsePage(search), items);
    }

    public static (StatsPageInfo Page, IReadOnlyList<ReviewRecord> Items) ParseReviewed(JsonElement data)
    {
        var search = Prop(data, "search");
        var items = new List<ReviewRecord>();
        foreach (var node in Nodes(search))
        {
            var number = Int(node, "number");
            var repo = Str(Prop(node, "repository"), "nameWithOwner");
            if (number == 0 || repo.Length == 0) continue;
            foreach (var review in Nodes(Prop(node, "reviews")))
            {
                if (Str(review, "state") == "PENDING") continue;
                if (Time(review, "submittedAt") is { } at) items.Add(new ReviewRecord(repo, number, at));
            }
        }
        return (ParsePage(search), items);
    }

    private static StatsPageInfo ParsePage(JsonElement? search)
    {
        var info = Prop(search, "pageInfo");
        var next = info is { } i && i.TryGetProperty("hasNextPage", out var h) && h.ValueKind == JsonValueKind.True;
        var cursor = Str(info, "endCursor");
        return new StatsPageInfo(next, cursor.Length == 0 ? null : cursor);
    }

    private static JsonElement? Prop(JsonElement? e, string name)
    {
        if (e is not { ValueKind: JsonValueKind.Object } obj) return null;
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return v;
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement? parent)
    {
        if (Prop(parent, "nodes") is { ValueKind: JsonValueKind.Array } arr)
            foreach (var n in arr.EnumerateArray())
                if (n.ValueKind == JsonValueKind.Object) yield return n;
    }

    private static string Str(JsonElement? e, string name) =>
        Prop(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";

    private static int Int(JsonElement? e, string name) =>
        Prop(e, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : 0;

    private static DateTimeOffset? Time(JsonElement? e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
