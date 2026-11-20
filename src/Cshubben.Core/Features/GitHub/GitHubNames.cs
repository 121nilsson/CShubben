using System.Globalization;

using Cshubben.Core.Data;
namespace Cshubben.Core.Features.GitHub;

/// <summary>Validation of names that end up in URLs and GraphQL strings.</summary>
public static class GitHubNames
{
    /// <summary>Owner and repo names are [A-Za-z0-9._-]; names made only of dots are path tricks.</summary>
    public static bool IsSafeName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 100) return false;
        var allDots = true;
        foreach (var c in name)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
            if (!ok) return false;
            if (c != '.') allDots = false;
        }
        return !allDots;
    }

    /// <summary>Splits "owner/name"; exactly one slash and two safe names are required.</summary>
    public static bool TrySplitRepo(string? repo, out string owner, out string name)
    {
        owner = string.Empty;
        name = string.Empty;
        if (string.IsNullOrEmpty(repo)) return false;
        var parts = repo.Split('/');
        if (parts.Length != 2) return false;
        if (!IsSafeName(parts[0]) || !IsSafeName(parts[1])) return false;
        owner = parts[0];
        name = parts[1];
        return true;
    }

    public static (string Owner, string Name) SplitRepo(string? repo)
    {
        if (!TrySplitRepo(repo, out var owner, out var name))
        {
            throw new ArgumentException("Invalid repository name: " + (repo ?? "(null)"), nameof(repo));
        }
        return (owner, name);
    }

    public static void CheckNumber(int number)
    {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number), "Invalid pull request number: " + number.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>7 to 64 hexadecimal characters.</summary>
    public static bool IsCommitSha(string? sha)
    {
        if (string.IsNullOrEmpty(sha) || sha.Length < 7 || sha.Length > 64) return false;
        foreach (var c in sha)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>GraphQL node ids: letters, digits, '_', '-', '='.</summary>
    public static bool IsNodeId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 100) return false;
        foreach (var c in id)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '=';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>Case-insensitive key for a PR, used for the feed "personal" test and title cache.</summary>
    public static string PrKey(string repo, int number) =>
        repo.ToLowerInvariant() + "#" + number.ToString(CultureInfo.InvariantCulture);
}
