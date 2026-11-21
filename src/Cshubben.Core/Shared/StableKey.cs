namespace Cshubben.Core.Shared;

/// <summary>A hash that is the same in every run (string.GetHashCode is not), for cache keys that are saved to disk.</summary>
public static class StableKey
{
    public static string Hash(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var c in text)
        {
            h ^= c;
            h *= 1099511628211UL;
        }

        return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
