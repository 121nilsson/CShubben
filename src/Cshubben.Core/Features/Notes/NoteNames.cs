namespace Cshubben.Core.Features.Notes;

/// <summary>Validation of note ids and attachment file names so nothing can escape the notes folder.</summary>
public static class NoteNames
{
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        foreach (var c in id)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    public static bool IsValidFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (name.EndsWith('.') || name.EndsWith(' ') || name.StartsWith(' ')) return false;
        if (name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0) return false;
        foreach (var c in name)
            if (c < ' ') return false;
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public static string RequireId(string? id)
    {
        if (!IsValidId(id)) throw new ArgumentException("Invalid note id.", nameof(id));
        return id!;
    }

    public static string RequireFileName(string? name)
    {
        if (!IsValidFileName(name)) throw new ArgumentException("Invalid attachment file name.", nameof(name));
        return name!;
    }
}
