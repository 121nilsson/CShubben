namespace Cshubben.Core.Features.Review;

public enum TreeRowKind { Dir, File }

/// <summary>
/// One row of the flattened file tree. For directories FileCount and the +/- numbers are aggregates over every
/// file below that passes the filter. FileIndex points into the file list that was given to the builder.
/// </summary>
public sealed record TreeRow(
    TreeRowKind Kind,
    string Name,
    string Path,
    int Depth,
    int FileIndex,
    int Additions,
    int Deletions,
    int FileCount,
    bool Collapsed,
    DiffFileStatus Status);

/// <summary>Builds the file tree: folders with a single folder child are merged ("src/app"), folders first, then files.</summary>
public static class FileTreeBuilder
{
    public static IReadOnlyList<TreeRow> Build(
        IReadOnlyList<DiffFile> files,
        string? filter = null,
        IReadOnlyCollection<string>? collapsedDirs = null)
    {
        var root = new Node(string.Empty, string.Empty);
        var needle = (filter ?? string.Empty).Trim();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (needle.Length > 0 && file.Path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var parts = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var node = root;
            for (var d = 0; d < parts.Length - 1; d++)
            {
                var name = parts[d];
                if (!node.Dirs.TryGetValue(name, out var child))
                {
                    var path = node.Path.Length == 0 ? name : node.Path + "/" + name;
                    child = new Node(name, path);
                    node.Dirs[name] = child;
                }
                node = child;
            }
            node.Files.Add(i);
        }

        ComputeTotals(root, files);
        var rows = new List<TreeRow>();
        var collapsed = collapsedDirs ?? Array.Empty<string>();
        Emit(root, 0, files, collapsed, rows);
        return rows;
    }

    /// <summary>Indices of files that are visible with the given collapsed folders, in tree order.</summary>
    public static IReadOnlyList<int> VisibleFileOrder(IReadOnlyList<TreeRow> rows) =>
        rows.Where(r => r.Kind == TreeRowKind.File).Select(r => r.FileIndex).ToList();

    /// <summary>The order files appear in the tree without filter or collapsing. j/k navigation uses this.</summary>
    public static IReadOnlyList<int> TreeOrder(IReadOnlyList<DiffFile> files) =>
        VisibleFileOrder(Build(files));

    private static void ComputeTotals(Node node, IReadOnlyList<DiffFile> files)
    {
        node.Adds = 0;
        node.Dels = 0;
        node.Count = 0;
        foreach (var index in node.Files)
        {
            node.Adds += files[index].Additions;
            node.Dels += files[index].Deletions;
            node.Count++;
        }
        foreach (var dir in node.Dirs.Values)
        {
            ComputeTotals(dir, files);
            node.Adds += dir.Adds;
            node.Dels += dir.Dels;
            node.Count += dir.Count;
        }
    }

    private static void Emit(Node node, int depth, IReadOnlyList<DiffFile> files, IReadOnlyCollection<string> collapsed, List<TreeRow> rows)
    {
        foreach (var dir in node.Dirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var current = dir;
            var name = dir.Name;
            while (current.Files.Count == 0 && current.Dirs.Count == 1)
            {
                current = current.Dirs.Values.First();
                name += "/" + current.Name;
            }

            var isCollapsed = collapsed.Contains(current.Path);
            rows.Add(new TreeRow(TreeRowKind.Dir, name, current.Path, depth, -1, current.Adds, current.Dels, current.Count, isCollapsed, DiffFileStatus.Modified));
            if (!isCollapsed) Emit(current, depth + 1, files, collapsed, rows);
        }

        foreach (var index in node.Files.OrderBy(i => files[i].Name, StringComparer.OrdinalIgnoreCase))
        {
            var f = files[index];
            rows.Add(new TreeRow(TreeRowKind.File, f.Name, f.Path, depth, index, f.Additions, f.Deletions, 1, false, f.Status));
        }
    }

    private sealed class Node
    {
        public Node(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }
        public string Path { get; }
        public Dictionary<string, Node> Dirs { get; } = new(StringComparer.Ordinal);
        public List<int> Files { get; } = new();
        public int Adds { get; set; }
        public int Dels { get; set; }
        public int Count { get; set; }
    }
}
