namespace Cshubben.Core.Features.Review;

public enum FileKind { Source, Generated, Lockfile, Minified, Snapshot, Binary }

/// <summary>Heuristics that decide which files are collapsed by default (nobody reviews a lockfile line by line).</summary>
public static class FileKinds
{
    private static readonly HashSet<string> LockNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "bun.lockb", "bun.lock",
        "cargo.lock", "poetry.lock", "pipfile.lock", "gemfile.lock", "composer.lock", "go.sum", "flake.lock",
        "packages.lock.json", "paket.lock", "podfile.lock", "mix.lock", "pubspec.lock", "gradle.lockfile",
    };

    private static readonly string[] GeneratedSuffixes =
    {
        ".g.cs", ".g.i.cs", ".designer.cs", ".generated.cs", ".pb.go", "_pb2.py", "_pb2_grpc.py", ".pb.cc", ".pb.h",
        ".generated.ts", ".generated.js", ".d.ts.map", ".js.map", ".css.map",
    };

    private static readonly string[] GeneratedFolders = { "/generated/", "/__generated__/", "/gen/", "/dist/", "/node_modules/", "/vendor/", "/obj/" };

    public static FileKind Classify(string path, bool isBinary = false)
    {
        if (isBinary) return FileKind.Binary;
        var normalized = "/" + path.Replace('\\', '/').TrimStart('/');
        var name = normalized.Substring(normalized.LastIndexOf('/') + 1);
        var lower = name.ToLowerInvariant();

        if (LockNames.Contains(name) || lower.EndsWith(".lock", StringComparison.Ordinal)) return FileKind.Lockfile;
        if (lower.EndsWith(".min.js", StringComparison.Ordinal) || lower.EndsWith(".min.css", StringComparison.Ordinal)
            || lower.EndsWith(".bundle.js", StringComparison.Ordinal)) return FileKind.Minified;
        if (lower.EndsWith(".snap", StringComparison.Ordinal) || lower.Contains(".verified.", StringComparison.Ordinal)
            || lower.Contains(".received.", StringComparison.Ordinal)
            || normalized.Contains("/__snapshots__/", StringComparison.OrdinalIgnoreCase)) return FileKind.Snapshot;
        foreach (var suffix in GeneratedSuffixes)
            if (lower.EndsWith(suffix, StringComparison.Ordinal)) return FileKind.Generated;
        var folder = normalized.ToLowerInvariant();
        foreach (var f in GeneratedFolders)
            if (folder.Contains(f, StringComparison.Ordinal)) return FileKind.Generated;
        if (lower.Contains(".generated.", StringComparison.Ordinal)) return FileKind.Generated;
        return FileKind.Source;
    }

    public static bool CollapsedByDefault(FileKind kind) =>
        kind == FileKind.Generated || kind == FileKind.Lockfile || kind == FileKind.Minified || kind == FileKind.Snapshot;

    public static string Label(FileKind kind) => kind switch
    {
        FileKind.Generated => "Generated file",
        FileKind.Lockfile => "Lockfile",
        FileKind.Minified => "Minified file",
        FileKind.Snapshot => "Snapshot file",
        FileKind.Binary => "Binary file",
        _ => "Source file",
    };
}
