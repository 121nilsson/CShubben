namespace Cshubben.Core.Integrations.Claude;

/// <summary>File-system probe so the locator can be tested without touching the disk.</summary>
public interface ILocatorFileSystem
{
    bool FileExists(string path);

    /// <summary>Full paths of the direct sub-directories of <paramref name="path"/> (empty when missing).</summary>
    IReadOnlyList<string> Directories(string path);
}

public sealed class RealLocatorFileSystem : ILocatorFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<string> Directories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>The environment facts the locator needs, so tests can supply their own.</summary>
public sealed record LocatorEnvironment(
    bool IsWindows,
    string? OverridePath,
    string? PathVariable,
    string? LocalAppData,
    string? HomeDirectory)
{
    public const string OverrideVariable = "CSHUBBEN_CLAUDE_PATH";

    public static LocatorEnvironment FromProcess()
    {
        var over = Environment.GetEnvironmentVariable(OverrideVariable);
        if (string.IsNullOrWhiteSpace(over)) over = Environment.GetEnvironmentVariable("HUB_CLAUDE_PATH");
        return new LocatorEnvironment(
            OperatingSystem.IsWindows(),
            over,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
}

/// <summary>
/// Finds the <c>claude</c> binary: explicit override, then PATH, then the copies bundled with Claude Desktop
/// and the VS Code extension (newest version first).
/// </summary>
public static class ClaudeLocator
{
    public static string? Find() => Find(LocatorEnvironment.FromProcess(), new RealLocatorFileSystem());

    /// <summary>
    /// Returns the path, or null when nothing was found. An override that points at a missing file also
    /// gives null: an explicit setting that is wrong should fail visibly rather than fall through.
    /// </summary>
    public static string? Find(LocatorEnvironment env, ILocatorFileSystem fs)
    {
        var over = env.OverridePath;
        if (!string.IsNullOrWhiteSpace(over))
        {
            return fs.FileExists(over) ? over : null;
        }

        var names = env.IsWindows ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };
        var bundledName = env.IsWindows ? "claude.exe" : "claude";

        var pathVariable = env.PathVariable;
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (var rawDir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = rawDir.Trim().Trim('"');
                if (dir.Length == 0) continue;
                foreach (var name in names)
                {
                    var candidate = Path.Combine(dir, name);
                    if (fs.FileExists(candidate)) return candidate;
                }
            }
        }

        var found = new List<(int[] Version, string Exe)>();

        // Claude Desktop: <LocalAppData>/Claude-Data/<app>/claude-code/<version>/<hash>/claude.exe
        if (!string.IsNullOrEmpty(env.LocalAppData))
        {
            foreach (var app in fs.Directories(Path.Combine(env.LocalAppData, "Claude-Data")))
            {
                foreach (var versionDir in fs.Directories(Path.Combine(app, "claude-code")))
                {
                    foreach (var hashDir in fs.Directories(versionDir))
                    {
                        var exe = Path.Combine(hashDir, bundledName);
                        if (fs.FileExists(exe)) found.Add((ParseVersion(Path.GetFileName(versionDir)), exe));
                    }
                }
            }
        }

        // VS Code: ~/.vscode*/extensions/anthropic.claude-code-<version>-*/resources/native-binary/claude.exe
        if (!string.IsNullOrEmpty(env.HomeDirectory))
        {
            const string prefix = "anthropic.claude-code-";
            foreach (var vscode in fs.Directories(env.HomeDirectory))
            {
                if (!Path.GetFileName(vscode).StartsWith(".vscode", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var extension in fs.Directories(Path.Combine(vscode, "extensions")))
                {
                    var name = Path.GetFileName(extension);
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var exe = Path.Combine(extension, "resources", "native-binary", bundledName);
                    if (fs.FileExists(exe)) found.Add((ParseVersion(name.Substring(prefix.Length)), exe));
                }
            }
        }

        string? best = null;
        int[]? bestVersion = null;
        foreach (var item in found)
        {
            if (bestVersion is null || CompareVersions(item.Version, bestVersion) > 0)
            {
                best = item.Exe;
                bestVersion = item.Version;
            }
        }

        return best;
    }

    /// <summary>The first three numbers found in the text, e.g. "2.0.5-win32-x64" gives 2, 0, 5.</summary>
    public static int[] ParseVersion(string text)
    {
        var parts = new List<int>();
        var current = -1;
        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch))
            {
                current = (current < 0 ? 0 : current) * 10 + (ch - '0');
                if (current > 100_000_000) current = 100_000_000;
            }
            else if (current >= 0)
            {
                parts.Add(current);
                current = -1;
            }
        }

        if (current >= 0) parts.Add(current);
        return parts.Take(3).ToArray();
    }

    public static int CompareVersions(int[] a, int[] b)
    {
        var length = Math.Max(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var left = i < a.Length ? a[i] : 0;
            var right = i < b.Length ? b[i] : 0;
            if (left != right) return left.CompareTo(right);
        }

        return 0;
    }
}
