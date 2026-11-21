namespace Cshubben.Core.Features.Notes;

/// <summary>Validation of note ids and attachment file names so nothing can escape the notes folder.</summary>
public static class NoteNames
{
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        if (IsReservedDeviceName(id)) return false;
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
        if (IsReservedDeviceName(name)) return false;
        if (name.EndsWith('.') || name.EndsWith(' ') || name.StartsWith(' ')) return false;
        if (name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0) return false;
        foreach (var c in name)
            if (c < ' ') return false;
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    /// <summary>Windows device names (CON, NUL, COM1, ...) are special with any extension; refuse them everywhere.</summary>
    public static bool IsReservedDeviceName(string name)
    {
        var dot = name.IndexOf('.');
        var stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") return true;
        return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9';
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
