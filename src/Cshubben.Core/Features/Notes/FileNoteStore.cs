using System.Text;

namespace Cshubben.Core.Features.Notes;

/// <summary>
/// Notes on disk: one "&lt;id&gt;.md" per note (front matter plus markdown) and attachments in "&lt;id&gt;.files/".
/// All tasks complete synchronously.
/// </summary>
public sealed class FileNoteStore : INoteStore
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _root;
    private readonly object _gate = new();
    private long _version;

    public FileNoteStore(string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
    }

    public string RootDirectory => _root;

    public long Version => Interlocked.Read(ref _version);

    public event Action? Changed;

    public Task<IReadOnlyList<Note>> ListAsync() => Run<IReadOnlyList<Note>>(() =>
    {
        var notes = new List<Note>();
        lock (_gate)
        {
            foreach (var file in Directory.GetFiles(_root, "*.md"))
            {
                var id = Path.GetFileNameWithoutExtension(file)!;
                if (!NoteNames.IsValidId(id)) continue;
                var note = Load(id, file);
                if (note is not null) notes.Add(note);
            }
        }
        return notes.OrderByDescending(n => n.UpdatedAt).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();
    });

    public Task<Note?> GetAsync(string id) => Run(() =>
    {
        NoteNames.RequireId(id);
        lock (_gate) return Load(id, NotePath(id));
    });

    public Task SaveAsync(Note note) => Run(() =>
    {
        NoteNames.RequireId(note.Id);
        var path = NotePath(note.Id);
        var temp = path + ".tmp";
        lock (_gate)
        {
            File.WriteAllText(temp, NoteFormat.Serialize(note), Utf8);
            File.Move(temp, path, true);
        }
        Bump();
    });

    public Task DeleteAsync(string id) => Run(() =>
    {
        NoteNames.RequireId(id);
        lock (_gate)
        {
            var path = NotePath(id);
            if (File.Exists(path)) File.Delete(path);
            var dir = FilesPath(id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        Bump();
    });

    public Task AddAttachmentAsync(string noteId, string fileName, Stream content) => Run(() =>
    {
        NoteNames.RequireId(noteId);
        NoteNames.RequireFileName(fileName);
        lock (_gate)
        {
            if (!File.Exists(NotePath(noteId))) throw new FileNotFoundException("The note does not exist.", noteId);
            var dir = FilesPath(noteId);
            Directory.CreateDirectory(dir);
            var target = SafeChild(dir, fileName);
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write);
            content.CopyTo(output);
        }
        Bump();
    });

    public Task<string> OpenAttachmentPathAsync(string noteId, string fileName) => Run(() =>
    {
        NoteNames.RequireId(noteId);
        NoteNames.RequireFileName(fileName);
        var target = SafeChild(FilesPath(noteId), fileName);
        if (!File.Exists(target)) throw new FileNotFoundException("The attachment does not exist.", fileName);
        return target;
    });

    private string NotePath(string id) => Path.Combine(_root, id + ".md");

    private string FilesPath(string id) => Path.Combine(_root, id + ".files");

    /// <summary>Combines folder and name and double-checks that the result stays inside the folder.</summary>
    private static string SafeChild(string directory, string fileName)
    {
        var dir = Path.GetFullPath(directory);
        var full = Path.GetFullPath(Path.Combine(dir, fileName));
        var prefix = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException("Invalid attachment file name.", nameof(fileName));
        return full;
    }

    private Note? Load(string id, string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path, Utf8);
        var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        var note = NoteFormat.Parse(id, text, modified);
        var dir = FilesPath(id);
        if (!Directory.Exists(dir)) return note;
        var names = Directory.GetFiles(dir)
            .Select(f => Path.GetFileName(f)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return note with { Attachments = names };
    }

    private void Bump()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }

    private static Task Run(Action action)
    {
        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static Task<T> Run<T>(Func<T> func)
    {
        try
        {
            return Task.FromResult(func());
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ex);
        }
    }
}
