using System.Text.Json;

namespace Cshubben.Core.Features.Cache;

/// <summary>How often each page is opened. Old visits fade (counts are halved when the total passes a limit) so recent habits win.</summary>
public sealed class PageUsage
{
    public const int Limit = 200;
    public const string FileName = "page-usage.json";

    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public int Total => _counts.Values.Sum();

    public int Count(string page) => _counts.TryGetValue(page, out var n) ? n : 0;

    public double Share(string page) => Total == 0 ? 0 : (double)Count(page) / Total;

    public void Record(string page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        _counts[page] = Count(page) + 1;
        if (Total > Limit)
            foreach (var key in _counts.Keys.ToList()) _counts[key] /= 2;
    }

    public string ToJson() => JsonSerializer.Serialize(_counts);

    public static PageUsage FromJson(string? json)
    {
        var usage = new PageUsage();
        if (string.IsNullOrWhiteSpace(json)) return usage;
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (read is not null)
                foreach (var (k, v) in read)
                    if (!string.IsNullOrWhiteSpace(k) && v > 0) usage._counts[k] = Math.Min(v, 100_000);
        }
        catch (JsonException)
        {
            // A damaged file starts over.
        }

        return usage;
    }

    public static PageUsage Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new PageUsage();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PageUsage();
        }
    }

    public bool TrySave(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, FileName), ToJson());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>How often a page's data is reloaded in the background: pages you use a lot often, the rest rarely.</summary>
public static class PrefetchPolicy
{
    public static readonly TimeSpan Hot = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Warm = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Cold = TimeSpan.FromMinutes(20);

    /// <summary>With fewer than five visits recorded there is no habit to go by, so every page gets the middle interval.</summary>
    public static TimeSpan Interval(PageUsage usage, string page)
    {
        if (usage.Total < 5) return Warm;
        var share = usage.Share(page);
        return share >= 0.30 ? Hot : share >= 0.08 ? Warm : Cold;
    }
}
