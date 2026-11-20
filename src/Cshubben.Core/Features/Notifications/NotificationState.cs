using System.Text.Json;

namespace Cshubben.Core.Features.Notifications;

/// <summary>Where the "already notified" keys live. Load never throws.</summary>
public interface INotificationState
{
    IReadOnlyDictionary<string, DateTimeOffset> LoadSeen();

    void SaveSeen(IReadOnlyDictionary<string, DateTimeOffset> seen);
}

/// <summary>The seen-set in one small JSON file, written atomically (temp file, then move).</summary>
public sealed class FileNotificationState : INotificationState
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public FileNotificationState(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public static string DefaultPath(string dataDirectory) => Path.Combine(dataDirectory, "notifications-seen.json");

    public IReadOnlyDictionary<string, DateTimeOffset> LoadSeen()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, DateTimeOffset>();
            var loaded = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_path), Options);
            return loaded ?? new Dictionary<string, DateTimeOffset>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new Dictionary<string, DateTimeOffset>();
        }
    }

    public void SaveSeen(IReadOnlyDictionary<string, DateTimeOffset> seen)
    {
        ArgumentNullException.ThrowIfNull(seen);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(seen, Options));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>Keeps the seen-set in memory only. Used for demo mode and tests.</summary>
public sealed class InMemoryNotificationState : INotificationState
{
    private Dictionary<string, DateTimeOffset> _seen = new();

    public int SaveCount { get; private set; }

    public IReadOnlyDictionary<string, DateTimeOffset> LoadSeen() => new Dictionary<string, DateTimeOffset>(_seen);

    public void SaveSeen(IReadOnlyDictionary<string, DateTimeOffset> seen)
    {
        ArgumentNullException.ThrowIfNull(seen);
        _seen = new Dictionary<string, DateTimeOffset>(seen);
        SaveCount++;
    }
}

/// <summary>Keys already notified, with a size and age limit so the file stays small.</summary>
public sealed class SeenSet
{
    public const int MaxEntries = 2000;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly Dictionary<string, DateTimeOffset> _items;

    public SeenSet(IReadOnlyDictionary<string, DateTimeOffset>? initial = null)
    {
        _items = initial is null ? new() : new(initial, StringComparer.Ordinal);
    }

    public int Count => _items.Count;

    public bool Contains(string key) => _items.ContainsKey(key);

    public void Add(string key, DateTimeOffset at) => _items[key] = at;

    /// <summary>Drops entries older than <see cref="MaxAge"/>, then the oldest ones beyond <see cref="MaxEntries"/>.</summary>
    public void Prune(DateTimeOffset now)
    {
        foreach (var key in _items.Where(kv => now - kv.Value > MaxAge).Select(kv => kv.Key).ToList())
            _items.Remove(key);
        if (_items.Count <= MaxEntries) return;
        foreach (var key in _items.OrderBy(kv => kv.Value).Take(_items.Count - MaxEntries).Select(kv => kv.Key).ToList())
            _items.Remove(key);
    }

    public IReadOnlyDictionary<string, DateTimeOffset> Snapshot() => new Dictionary<string, DateTimeOffset>(_items);
}

/// <summary>Where the notification preferences that the app settings do not cover live. Load never throws.</summary>
public interface INotificationPrefsStore
{
    NotificationPrefs Load();

    void Save(NotificationPrefs prefs);
}

/// <summary>Preferences in one JSON file, written atomically.</summary>
public sealed class FileNotificationPrefsStore : INotificationPrefsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path;

    public FileNotificationPrefsStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public static string DefaultPath(string dataDirectory) => Path.Combine(dataDirectory, "notification-prefs.json");

    public NotificationPrefs Load()
    {
        try
        {
            if (!File.Exists(_path)) return new NotificationPrefs();
            var loaded = JsonSerializer.Deserialize<NotificationPrefs>(File.ReadAllText(_path), Options);
            return (loaded ?? new NotificationPrefs()).Sanitized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new NotificationPrefs();
        }
    }

    public void Save(NotificationPrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(prefs.Sanitized(), Options));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>Keeps the preferences in memory only. Used for demo mode and tests.</summary>
public sealed class InMemoryNotificationPrefsStore : INotificationPrefsStore
{
    private NotificationPrefs _current;

    public InMemoryNotificationPrefsStore(NotificationPrefs? initial = null)
    {
        _current = initial ?? new NotificationPrefs();
    }

    public NotificationPrefs Load() => _current;

    public void Save(NotificationPrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        _current = prefs.Sanitized();
    }
}
