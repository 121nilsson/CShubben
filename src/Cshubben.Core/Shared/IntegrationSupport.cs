using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Shared;

/// <summary>Storage for tokens and API keys. A Windows Credential Manager implementation comes later.</summary>
public interface ISecretStore
{
    string? Get(string key);

    void Set(string key, string value);

    void Delete(string key);
}

/// <summary>Well-known secret names.</summary>
public static class SecretKeys
{
    public const string LinearApiKey = "linear.api-key";
    public const string ClaudeOAuthToken = "claude.oauth-token";
    public const string GrafanaToken = "grafana.token";
}

/// <summary>Keeps secrets in memory only. Used by tests and as a fallback.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _items = new(StringComparer.Ordinal);

    public string? Get(string key) => _items.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        _items[key] = value;
    }

    public void Delete(string key)
    {
        _items.TryRemove(key, out _);
    }
}

/// <summary>Small helpers for reading JSON without throwing on missing or mistyped fields.</summary>
public static class JsonRead
{
    public static string? Str(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    public static JsonElement? Obj(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return null;
    }

    public static JsonElement? Arr(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        return null;
    }

    public static double? Num(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number))
        {
            return number;
        }

        return null;
    }

    public static bool? Bool(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }

        return null;
    }

    /// <summary>Follows nested objects. Null when any step is missing or not an object.</summary>
    public static JsonElement? Path(JsonElement element, params string[] names)
    {
        var current = element;
        foreach (var name in names)
        {
            var next = Obj(current, name);
            if (next is null) return null;
            current = next.Value;
        }

        return current;
    }
}

/// <summary>Finds a JSON object inside text that may have markdown fences or prose around it.</summary>
public static class JsonText
{
    public static JsonElement? ParseObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var searchFrom = 0;
        while (searchFrom < text.Length)
        {
            var start = text.IndexOf('{', searchFrom);
            if (start < 0) return null;
            var end = FindObjectEnd(text, start);
            if (end > start)
            {
                try
                {
                    using var doc = JsonDocument.Parse(text.Substring(start, end - start + 1));
                    if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // Not valid JSON from this brace; try the next one.
                }
            }

            searchFrom = start + 1;
        }

        return null;
    }

    private static int FindObjectEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }
}

/// <summary>Only https://github.com/ links may survive in text that came from a model.</summary>
public static class LinkFilter
{
    public const string Removed = "[link removed]";

    private static readonly Regex UrlPattern = new(
        @"\b(?:[a-z][a-z0-9+.\-]*://|(?:javascript|data|vbscript|mailto|file|tel):|www\.)[^\s)\]>""'<]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsGitHubUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    public static string StripNonGitHubLinks(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return UrlPattern.Replace(text, m => IsGitHubUrl(m.Value) ? m.Value : Removed);
    }
}
