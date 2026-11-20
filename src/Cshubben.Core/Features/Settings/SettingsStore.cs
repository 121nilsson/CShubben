using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cshubben.Core.Features.Settings;

/// <summary>Where the app settings live. Load never throws: a missing or damaged file gives the defaults.</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>Settings in one JSON file, written atomically (temp file, then move).</summary>
public sealed class FileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public FileSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public static string DefaultPath(string dataDirectory) => Path.Combine(dataDirectory, "settings.json");

    public string FilePath => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            return (loaded ?? new AppSettings()).Sanitized();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
        catch (NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Sanitized(), Options));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>Keeps settings in memory only. Used for demo mode and tests.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private AppSettings _current;

    public InMemorySettingsStore(AppSettings? initial = null)
    {
        _current = initial ?? new AppSettings();
    }

    public AppSettings Load() => _current;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _current = settings.Sanitized();
    }
}
