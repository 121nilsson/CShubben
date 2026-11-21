using System.Text.Json;
using System.Text.Json.Serialization;
using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Cache;

/// <summary>
/// The last loaded <see cref="HubSnapshot"/> on disk, so the app can show it at once on start while the real load runs.
/// Holds what the lists already show (titles, descriptions, names). Never tokens. A file from another version, another user
/// or a damaged file is ignored, never an error.
/// </summary>
public static class SnapshotCache
{
    public const int FormatVersion = 1;

    public const string FileName = "snapshot-cache.json";

    private sealed record Envelope(int Version, string Viewer, HubSnapshot? Snapshot);

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public static string Serialize(HubSnapshot snapshot) =>
        JsonSerializer.Serialize(new Envelope(FormatVersion, snapshot.Viewer, snapshot), Options);

    /// <summary>The snapshot, or null when the text is empty, damaged, from another format version or from another user.</summary>
    public static HubSnapshot? Deserialize(string? json, string? expectedViewer = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(json, Options);
            if (envelope is null || envelope.Version != FormatVersion || envelope.Snapshot is null) return null;
            if (expectedViewer is { Length: > 0 } && !string.Equals(envelope.Viewer, expectedViewer, StringComparison.OrdinalIgnoreCase)) return null;
            return envelope.Snapshot;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public static HubSnapshot? TryLoad(string directory, string? expectedViewer = null)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            return File.Exists(path) ? Deserialize(File.ReadAllText(path), expectedViewer) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes through a temporary file so a crash never leaves half a file. Returns false when the disk refused.</summary>
    public static bool TrySave(string directory, HubSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(snapshot));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
