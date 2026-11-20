using Cshubben.Core.Shared;
namespace Cshubben.Core.Features.Linear;

/// <summary>Finds Linear issue identifiers (ENG-123) in PR titles and branch names.</summary>
public static class LinearLinks
{
    // Standards that look like identifiers. Always excluded.
    private static readonly HashSet<string> AlwaysExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "UTF", "ISO", "RFC", "CVE", "AES", "TLS", "SSL", "MD", "RSA", "WCAG", "PEP", "HTTP", "UTC", "IPV", "BASE", "ASCII", "GPT",
        // Words that often precede a plain number in branch names.
        "FIX", "BUG", "PR", "WIP", "TASK", "FEAT", "CHORE", "ISSUE", "PATCH", "TEST", "DOCS", "GH", "HOTFIX", "RELEASE", "VERSION",
    };

    // Prefixes that are only excluded when the number is a known size (sha-256, hs-512), so a real team key SHA-832 still works.
    private static readonly Dictionary<string, int[]> ExcludedWithNumber = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SHA"] = new[] { 1, 2, 3, 224, 256, 384, 512 },
        ["HS"] = new[] { 256, 384, 512 },
        ["RS"] = new[] { 256, 384, 512 },
        ["ES"] = new[] { 256, 384, 512 },
        ["PS"] = new[] { 256, 384, 512 },
    };

    /// <summary>
    /// First identifier in <paramref name="text"/>, upper-cased, or null. Upper-case matches (ENG-123) work anywhere.
    /// Lower-case matches (eng-123) only count when the text has no spaces and the prefix has at most 5 letters (a Linear team key limit), i.e. it looks like a branch name such as
    /// feat/eng-123-foo, so prose like "co-2 emissions" is left alone. Mixed case (Eng-123) is not matched.
    /// </summary>
    public static string? FindIdentifier(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var hasSpace = text.Any(char.IsWhiteSpace);
        var n = text.Length;

        for (var start = 0; start < n; start++)
        {
            if (!char.IsAsciiLetter(text[start])) continue;
            if (start > 0 && char.IsAsciiLetterOrDigit(text[start - 1])) continue;

            var i = start + 1;
            while (i < n && char.IsAsciiLetterOrDigit(text[i])) i++;
            if (i >= n || text[i] != '-') continue;

            var prefix = text.Substring(start, i - start);
            if (prefix.Length < 2 || prefix.Length > 10) continue;

            var hasUpper = prefix.Any(char.IsAsciiLetterUpper);
            var hasLower = prefix.Any(char.IsAsciiLetterLower);
            if (hasUpper && hasLower) continue;
            if (hasLower && (hasSpace || prefix.Length > 5)) continue;

            var digitsStart = i + 1;
            var j = digitsStart;
            while (j < n && char.IsAsciiDigit(text[j])) j++;
            var digitCount = j - digitsStart;
            if (digitCount < 1 || digitCount > 6) continue;
            if (j < n && char.IsAsciiLetterOrDigit(text[j])) continue;

            var number = int.Parse(text.AsSpan(digitsStart, digitCount), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            if (AlwaysExcluded.Contains(prefix)) continue;
            if (ExcludedWithNumber.TryGetValue(prefix, out var sizes) && Array.IndexOf(sizes, number) >= 0) continue;

            return prefix.ToUpperInvariant() + "-" + text.Substring(digitsStart, digitCount);
        }

        return null;
    }

    /// <summary>The identifier from the title, else from the branch name.</summary>
    public static string? FindIdentifier(string? title, string? branch) => FindIdentifier(title) ?? FindIdentifier(branch);
}
