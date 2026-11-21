using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cshubben.Core.Features.Cache;

/// <summary>
/// The last result of a slow page (Linear, Drift, Stats) on disk, so the page can paint at once after a restart. One file per page,
/// holding what the page already shows (never tokens). A file from another version or another user, a damaged file or a type that
/// cannot be read back is ignored, never an error.
/// </summary>
public static class PageDiskCache
{
    public const int FormatVersion = 1;

    private sealed record Envelope<T>(int Version, string Viewer, string Key, DateTimeOffset At, T? Value);

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public static string FileName(string name) => "page-" + name + ".json";

    public static string Serialize<T>(string viewer, string key, DateTimeOffset at, T value) =>
        JsonSerializer.Serialize(new Envelope<T>(FormatVersion, viewer, key, at, value), Options);

    public static (string Key, DateTimeOffset At, T Value)? Deserialize<T>(string? json, string viewer) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var e = JsonSerializer.Deserialize<Envelope<T>>(json, Options);
            if (e is null || e.Version != FormatVersion || e.Value is null || e.Key.Length == 0) return null;
            if (!string.Equals(e.Viewer, viewer, StringComparison.OrdinalIgnoreCase)) return null;
            return (e.Key, e.At, e.Value);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public static (string Key, DateTimeOffset At, T Value)? TryLoad<T>(string directory, string name, string viewer) where T : class
    {
        try
        {
            var path = Path.Combine(directory, FileName(name));
            return File.Exists(path) ? Deserialize<T>(File.ReadAllText(path), viewer) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool TrySave<T>(string directory, string name, string viewer, string key, DateTimeOffset at, T value)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName(name));
            var temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(viewer, key, at, value));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }
}
