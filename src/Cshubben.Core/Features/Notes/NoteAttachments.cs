using System.Text;

namespace Cshubben.Core.Features.Notes;

/// <summary>Outcome of attaching a file to a note.</summary>
/// <param name="Ok">True when the file was stored.</param>
/// <param name="FileName">The name it was stored under (may differ from the original to avoid overwriting).</param>
/// <param name="Message">A short, user-facing sentence for success or failure.</param>
public sealed record AttachResult(bool Ok, string FileName, string Message);

/// <summary>Rules for attaching files to notes: safe names, no overwrites, a size limit with a clear message.</summary>
public static class NoteAttachments
{
    /// <summary>Largest file we accept. Attachments are copied into the notes folder, so this keeps it from filling up by accident.</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB";
    }

    /// <summary>
    /// A safe plain file name from a path or name that came from outside (drag and drop, a picker): folders are dropped,
    /// invalid characters become "_", and the result is never empty, hidden-dots-only or too long.
    /// </summary>
    public static string SanitizeName(string? original)
    {
        var name = original ?? "";
        var cut = name.LastIndexOfAny(new[] { '/', '\\' });
        if (cut >= 0) name = name.Substring(cut + 1);

        var sb = new StringBuilder(name.Length);
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { ':', '*', '?', '"', '<', '>', '|' };
        foreach (var c in name)
            sb.Append(c < ' ' || invalid.Contains(c) ? '_' : c);

        var clean = sb.ToString().Replace("..", "_").Trim().TrimEnd('.', ' ');
        if (clean.Length == 0) clean = "file";
        if (clean.Length > 150)
        {
            var ext = Path.GetExtension(clean);
            if (ext.Length > 20) ext = "";
            clean = clean.Substring(0, 150 - ext.Length) + ext;
        }
        return NoteNames.IsValidFileName(clean) ? clean : "file";
    }

    /// <summary>
    /// A name that does not collide (ignoring case) with <paramref name="existing"/>:
    /// "report.pdf" becomes "report (1).pdf", then "report (2).pdf".
    /// </summary>
    public static string UniqueName(string name, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        var ext = Path.GetExtension(name);
        var stem = name.Substring(0, name.Length - ext.Length);
        for (var i = 1; ; i++)
        {
            var candidate = stem + " (" + i + ")" + ext;
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".pif",
        ".lnk", ".url", ".hta", ".cpl", ".reg", ".jar", ".dll", ".appx", ".msix", ".application", ".gadget",
    };

    /// <summary>True for file types that run code when opened. Attachments of these types can be stored but are never launched from a note.</summary>
    public static bool IsExecutable(string fileName) => ExecutableExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>The refusal message for a file of this size, or null when it is fine.</summary>
    public static string? CheckSize(string displayName, long bytes) =>
        bytes > MaxBytes
            ? $"\"{displayName}\" is {FormatSize(bytes)}, which is over the {FormatSize(MaxBytes)} limit for attachments."
            : null;

    /// <summary>
    /// Copies a file into the note's attachments. Never overwrites: a clashing name gets " (1)". Never throws for expected
    /// problems (too big, empty name, note missing); it returns a failed result with a message instead.
    /// </summary>
    /// <param name="knownLength">Size from the file system, when known, so a huge file is refused without reading it.</param>
    public static async Task<AttachResult> AddAsync(INoteStore store, string noteId, string originalName, Stream content, long? knownLength = null)
    {
        var name = SanitizeName(originalName);
        if (knownLength is long known && CheckSize(name, known) is { } tooBigEarly)
            return new AttachResult(false, name, tooBigEarly);

        try
        {
            var note = await store.GetAsync(noteId).ConfigureAwait(false);
            if (note is null) return new AttachResult(false, name, "The note no longer exists.");

            // Read at most the limit plus one byte, so a stream with a wrong or unknown length cannot fill memory or disk.
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await content.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxBytes) return new AttachResult(false, name, $"\"{name}\" is over the {FormatSize(MaxBytes)} limit for attachments.");
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;

            var stored = UniqueName(name, note.Attachments);
            await store.AddAttachmentAsync(noteId, stored, buffer).ConfigureAwait(false);
            return new AttachResult(true, stored, stored == name ? $"Attached {stored}." : $"Attached as {stored} (a file named {name} already exists).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new AttachResult(false, name, "Could not attach the file: " + ex.Message);
        }
    }

    /// <summary>
    /// Attaches several local files (from the picker or a drop). Folders and missing files are reported, not thrown.
    /// </summary>
    public static async Task<AttachBatchResult> AddFilesAsync(INoteStore store, string noteId, IEnumerable<string> paths)
    {
        var added = new List<string>();
        var problems = new List<string>();
        foreach (var path in paths)
        {
            var shown = Path.GetFileName(path ?? "");
            if (shown.Length == 0) shown = path ?? "";
            try
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (Directory.Exists(path)) { problems.Add($"\"{shown}\" is a folder. Attach files, not folders."); continue; }
                var info = new FileInfo(path);
                if (!info.Exists) { problems.Add($"\"{shown}\" could not be found."); continue; }
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var result = await AddAsync(store, noteId, info.Name, stream, info.Length).ConfigureAwait(false);
                if (result.Ok) added.Add(result.FileName);
                else problems.Add(result.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                problems.Add($"Could not read \"{shown}\": {ex.Message}");
            }
        }
        return new AttachBatchResult(added, problems);
    }
}

/// <summary>The outcome of attaching several files.</summary>
public sealed record AttachBatchResult(IReadOnlyList<string> Added, IReadOnlyList<string> Problems)
{
    public bool HasProblems => Problems.Count > 0;

    /// <summary>One user-facing sentence or two, problems first.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>(Problems);
            if (Added.Count == 1) parts.Add($"Attached {Added[0]}.");
            else if (Added.Count > 1) parts.Add($"Attached {Added.Count} files.");
            return string.Join(" ", parts);
        }
    }
}
