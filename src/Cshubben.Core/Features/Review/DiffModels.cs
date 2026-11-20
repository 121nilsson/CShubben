namespace Cshubben.Core.Features.Review;

public enum DiffFileStatus { Modified, Added, Deleted, Renamed }

public enum DiffLineKind { Context, Add, Remove, NoNewline }

/// <summary>Which side of a diff a line or thread sits on. Left is the old file, right is the new file.</summary>
public enum DiffSide { Left, Right }

/// <summary>One line of a hunk. Numbers are 1-based; Add lines have no old number, Remove lines no new number.</summary>
public sealed record DiffLine(DiffLineKind Kind, int? OldNumber, int? NewNumber, string Text);

/// <summary>A hunk with the numbers from its "@@ -a,b +c,d @@" header.</summary>
public sealed record DiffHunk(
    string Header,
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines)
{
    /// <summary>Text after the second "@@" (usually the enclosing function), or empty.</summary>
    public string Context
    {
        get
        {
            var first = Header.IndexOf("@@", StringComparison.Ordinal);
            if (first < 0) return string.Empty;
            var second = Header.IndexOf("@@", first + 2, StringComparison.Ordinal);
            return second < 0 ? string.Empty : Header.Substring(second + 2).Trim();
        }
    }
}

/// <summary>
/// One changed file. For binary files and files whose patch was skipped (<see cref="IsTooLarge"/>) there are no hunks,
/// but the additions and deletions are still counted.
/// </summary>
public sealed record DiffFile(
    string Path,
    string? OldPath,
    DiffFileStatus Status,
    bool IsBinary,
    bool IsTooLarge,
    int Additions,
    int Deletions,
    IReadOnlyList<DiffHunk> Hunks)
{
    public bool HasPatch => !IsBinary && !IsTooLarge && Hunks.Count > 0;

    /// <summary>File name without folders.</summary>
    public string Name
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash < 0 ? Path : Path.Substring(slash + 1);
        }
    }

    public int TotalChanges => Additions + Deletions;
}
