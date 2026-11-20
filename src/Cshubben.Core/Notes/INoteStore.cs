namespace Cshubben.Core.Notes;

/// <summary>
/// Storage for notes. Implementations in this repo complete their tasks synchronously (local disk or memory),
/// which lets UI code read the result of an already completed task without blocking.
/// </summary>
public interface INoteStore
{
    /// <summary>Increases on every change. Cheap to poll from the UI.</summary>
    long Version { get; }

    /// <summary>Raised after every change, possibly on any thread.</summary>
    event Action? Changed;

    /// <summary>All notes, most recently updated first.</summary>
    Task<IReadOnlyList<Note>> ListAsync();

    Task<Note?> GetAsync(string id);

    /// <summary>Creates or replaces a note. Attachments are managed only through AddAttachmentAsync.</summary>
    Task SaveAsync(Note note);

    Task DeleteAsync(string id);

    /// <summary>Stores a file next to the note. The file name must be a plain name without folders.</summary>
    Task AddAttachmentAsync(string noteId, string fileName, Stream content);

    /// <summary>A local file path for the attachment, suitable for opening in the default app.</summary>
    Task<string> OpenAttachmentPathAsync(string noteId, string fileName);
}
