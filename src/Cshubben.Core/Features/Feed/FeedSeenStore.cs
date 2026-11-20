using System.Globalization;
using System.Text.Json;

namespace Cshubben.Core.Features.Feed;

/// <summary>Remembers when the user last looked at the feed. Load never throws; Save never throws into the UI.</summary>
public interface IFeedSeenStore
{
    /// <summary>The last-seen timestamp, or null when the feed was never marked as seen.</summary>
    DateTimeOffset? Load();

    void Save(DateTimeOffset lastSeen);
}

/// <summary>Keeps the timestamp in memory only. Used for demo mode and tests.</summary>
public sealed class InMemoryFeedSeenStore : IFeedSeenStore
{
    private DateTimeOffset? _value;

    public InMemoryFeedSeenStore(DateTimeOffset? initial = null)
    {
        _value = initial;
    }

    public DateTimeOffset? Load() => _value;

    public void Save(DateTimeOffset lastSeen)
    {
        _value = lastSeen;
    }
}

/// <summary>One small JSON file, written atomically. The value is cached after the first read so the UI can call Load on every render.</summary>
public sealed class FileFeedSeenStore : IFeedSeenStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private bool _loaded;
    private DateTimeOffset? _cached;

    public FileFeedSeenStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public static string DefaultPath(string dataDirectory) => Path.Combine(dataDirectory, "feed-seen.json");

    public string FilePath => _path;

    public DateTimeOffset? Load()
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                _cached = ReadFile();
                _loaded = true;
            }

            return _cached;
        }
    }

    public void Save(DateTimeOffset lastSeen)
    {
        lock (_lock)
        {
            _cached = lastSeen;
            _loaded = true;
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temp = _path + ".tmp";
                var text = lastSeen.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
                File.WriteAllText(temp, JsonSerializer.Serialize(new Dictionary<string, string> { ["lastSeen"] = text }));
                File.Move(temp, _path, overwrite: true);
            }
            catch (IOException)
            {
                // The value stays in memory for this run; losing it only means items show as new again next start.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private DateTimeOffset? ReadFile()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("lastSeen", out var value) || value.ValueKind != JsonValueKind.String) return null;
            var text = value.GetString();
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
