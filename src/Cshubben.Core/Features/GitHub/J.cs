using System.Globalization;
using System.Text.Json;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

/// <summary>Tolerant JSON navigation: missing or null values give defaults instead of exceptions.</summary>
internal static class J
{
    public static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.Undefined) return null;
        return v;
    }

    public static JsonElement? At(JsonElement? e, params string[] path)
    {
        var cur = e;
        foreach (var name in path)
        {
            if (cur is null) return null;
            cur = Prop(cur.Value, name);
        }
        return cur;
    }

    public static string? StrOrNull(JsonElement? e, params string[] path)
    {
        var v = At(e, path);
        if (v is null) return null;
        if (v.Value.ValueKind != JsonValueKind.String) return null;
        return v.Value.GetString();
    }

    public static string Str(JsonElement? e, params string[] path) => StrOrNull(e, path) ?? string.Empty;

    public static int? IntOrNull(JsonElement? e, params string[] path)
    {
        var v = At(e, path);
        if (v is null) return null;
        if (v.Value.ValueKind != JsonValueKind.Number) return null;
        if (!v.Value.TryGetInt64(out var l)) return null;
        if (l > int.MaxValue) return int.MaxValue;
        if (l < int.MinValue) return int.MinValue;
        return (int)l;
    }

    public static int Int(JsonElement? e, params string[] path) => IntOrNull(e, path) ?? 0;

    public static bool Bool(JsonElement? e, params string[] path)
    {
        var v = At(e, path);
        if (v is null) return false;
        return v.Value.ValueKind == JsonValueKind.True;
    }

    public static DateTimeOffset? Time(JsonElement? e, params string[] path)
    {
        var s = StrOrNull(e, path);
        if (s is null) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)) return t;
        return null;
    }

    public static List<JsonElement> Items(JsonElement? e, params string[] path)
    {
        var result = new List<JsonElement>();
        var v = At(e, path);
        if (v is null) return result;
        if (v.Value.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in v.Value.EnumerateArray()) result.Add(item);
        return result;
    }
}
