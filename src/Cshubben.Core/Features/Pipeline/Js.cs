using System.Globalization;
using System.Text.Json;

namespace Cshubben.Core.Features.Pipeline;

/// <summary>Tolerant JSON navigation: a missing or null value gives null instead of an exception.</summary>
internal static class Js
{
    public static JsonElement? Prop(JsonElement? element, string name)
    {
        if (element is null) return null;
        var e = element.Value;
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (!e.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return null;
        return value;
    }

    public static JsonElement? At(JsonElement? element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            current = Prop(current, name);
            if (current is null) return null;
        }
        return current;
    }

    public static string? Str(JsonElement? element, params string[] path)
    {
        var v = At(element, path);
        if (v is null || v.Value.ValueKind != JsonValueKind.String) return null;
        return v.Value.GetString();
    }

    public static long? Long(JsonElement? element, params string[] path)
    {
        var v = At(element, path);
        if (v is null || v.Value.ValueKind != JsonValueKind.Number) return null;
        return v.Value.TryGetInt64(out var l) ? l : null;
    }

    public static int? Int(JsonElement? element, params string[] path)
    {
        var l = Long(element, path);
        if (l is null) return null;
        if (l.Value > int.MaxValue) return int.MaxValue;
        if (l.Value < int.MinValue) return int.MinValue;
        return (int)l.Value;
    }

    public static bool Bool(JsonElement? element, params string[] path)
    {
        var v = At(element, path);
        return v is not null && v.Value.ValueKind == JsonValueKind.True;
    }

    public static DateTimeOffset? Time(JsonElement? element, params string[] path)
    {
        var s = Str(element, path);
        if (s is null) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
    }

    /// <summary>The array at <paramref name="path"/>; with no path, the element itself when it is an array.</summary>
    public static List<JsonElement> Items(JsonElement? element, params string[] path)
    {
        var result = new List<JsonElement>();
        var v = path.Length == 0 ? element : At(element, path);
        if (v is null || v.Value.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in v.Value.EnumerateArray()) result.Add(item);
        return result;
    }
}
