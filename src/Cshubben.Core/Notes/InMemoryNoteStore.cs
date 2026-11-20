namespace Cshubben.Core.Notes;

/// <summary>Notes held in memory. Used for tests, demos and screenshots.</summary>
public sealed class InMemoryNoteStore : INoteStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Note> _notes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private long _version;

    public InMemoryNoteStore(IEnumerable<Note>? notes = null)
    {
        if (notes is null) return;
        foreach (var n in notes) _notes[n.Id] = n;
    }

    public long Version => Interlocked.Read(ref _version);

    public event Action? Changed;

    public Task<IReadOnlyList<Note>> ListAsync()
    {
        lock (_gate)
        {
            IReadOnlyList<Note> list = _notes.Values
                .OrderByDescending(n => n.UpdatedAt)
                .ThenBy(n => n.Id, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<Note?> GetAsync(string id)
    {
        lock (_gate)
        {
            return Task.FromResult<Note?>(_notes.TryGetValue(id, out var note) ? note : null);
        }
    }

    public Task SaveAsync(Note note)
    {
        try
        {
            NoteNames.RequireId(note.Id);
        }
        catch (ArgumentException ex)
        {
            return Task.FromException(ex);
        }

        lock (_gate)
        {
            var attachments = _notes.TryGetValue(note.Id, out var existing) ? existing.Attachments : note.Attachments;
            _notes[note.Id] = note with { Attachments = attachments };
        }
        Bump();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id)
    {
        lock (_gate)
        {
            _notes.Remove(id);
            var prefix = id + "/";
            foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _files.Remove(key);
        }
        Bump();
        return Task.CompletedTask;
    }

    public Task AddAttachmentAsync(string noteId, string fileName, Stream content)
    {
        try
        {
            NoteNames.RequireFileName(fileName);
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            lock (_gate)
            {
                if (!_notes.TryGetValue(noteId, out var note)) throw new FileNotFoundException("The note does not exist.", noteId);
                _files[noteId + "/" + fileName] = copy.ToArray();
                if (!note.Attachments.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    _notes[noteId] = note with { Attachments = note.Attachments.Append(fileName).ToList() };
            }
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
        Bump();
        return Task.CompletedTask;
    }

    /// <summary>Writes the attachment to a temporary file and returns its path.</summary>
    public Task<string> OpenAttachmentPathAsync(string noteId, string fileName)
    {
        try
        {
            NoteNames.RequireId(noteId);
            NoteNames.RequireFileName(fileName);
            byte[]? data;
            lock (_gate)
            {
                _files.TryGetValue(noteId + "/" + fileName, out data);
            }
            if (data is null) throw new FileNotFoundException("The attachment has no content.", fileName);
            var dir = Path.Combine(Path.GetTempPath(), "cshubben-notes", noteId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, data);
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            return Task.FromException<string>(ex);
        }
    }

    private void Bump()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }

    /// <summary>Six realistic, fictional notes for demos and screenshots.</summary>
    public static InMemoryNoteStore Demo(DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow;

        var ideas = Note.Create(
            "Ideas for Cshubben",
            "- Keyboard shortcuts in the inspector (J/K to move, A to approve)\n" +
            "- Draft review replies with Claude, then edit before sending\n" +
            "- A weekly digest of merged PRs per repo\n" +
            "- Drag a file onto a note to attach it\n" +
            "- Show deploy status next to the PR that caused it\n",
            new[] { "idea", "cshubben" }, null, true, t.AddHours(-2), "n-ideas");
        ideas = ideas with { UpdatedAt = t.AddHours(-2), CreatedAt = t.AddDays(-9) };

        var decision = Note.Create(
            "Decision: cursor pagination for /orders",
            "# Decision\n\nWe use **cursor based pagination** for `/orders` instead of page numbers.\n\n" +
            "## Why\n\n- Offsets get slow on large tables\n- Rows inserted while paging no longer cause duplicates\n" +
            "- The mobile client already stores an opaque cursor\n\n## Follow-ups\n\n" +
            "1. Document the cursor format as opaque\n2. Keep `page` working for one release, then remove it\n",
            new[] { "decision", "api" },
            new[]
            {
                new NoteLink(NoteLinkKind.Pr, "acme/api#412", "Add cursor pagination to /orders"),
                new NoteLink(NoteLinkKind.Issue, "ENG-231", "Slow order listing for large accounts"),
            },
            false, t.AddDays(-1), "n-decision");
        decision = decision with { UpdatedAt = t.AddDays(-1), CreatedAt = t.AddDays(-3), Attachments = new[] { "pagination-benchmark.png" } };

        var meeting = Note.Create(
            "Weekly sync, 18 Nov",
            "## Attendees\n\nMaja, Jonas, Priya, Tom\n\n## Notes\n\n" +
            "- Release 2.8 goes out on Thursday if the flaky checkout test is fixed\n" +
            "- Priya will look at the staging deploy failures\n- Jonas: new onboarding copy is ready for review\n\n" +
            "## Action items\n\n- [ ] Maja: update the runbook\n- [ ] Tom: close old feature flags\n- [x] Jonas: share the copy doc\n",
            new[] { "meeting", "todo" },
            new[] { new NoteLink(NoteLinkKind.Repo, "acme/web", "acme/web") },
            false, t.AddDays(-2), "n-meeting");
        meeting = meeting with { UpdatedAt = t.AddDays(-2), CreatedAt = t.AddDays(-2) };

        var snippet = Note.Create(
            "Snippet: retry with backoff",
            "Small helper I keep rewriting:\n\n```csharp\nstatic async Task<T> Retry<T>(Func<Task<T>> action, int attempts = 4)\n{\n" +
            "    for (var i = 0; ; i++)\n    {\n        try { return await action(); }\n" +
            "        catch when (i < attempts - 1)\n        {\n            await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, i)));\n        }\n    }\n}\n```\n\n" +
            "Add jitter before using it against a shared service.\n",
            new[] { "snippet", "dotnet" }, null, false, t.AddDays(-6), "n-snippet");
        snippet = snippet with { UpdatedAt = t.AddDays(-6), CreatedAt = t.AddDays(-14) };

        var claude = Note.Create(
            "Claude: why is the build flaky?",
            "**Question:** why does `CheckoutTests.AppliesDiscount` fail only on CI?\n\n" +
            "**Answer:** the test reads the clock twice. On a slow runner the two reads fall on different seconds, " +
            "so the discount window check disagrees with itself. Inject a fixed clock and the failure goes away.\n\n" +
            "> Saved from a Claude answer. Verify before acting on it.\n",
            new[] { "claude", "todo" },
            new[] { new NoteLink(NoteLinkKind.Pr, "acme/web#873", "Stabilise checkout tests") },
            false, t.AddHours(-30), "n-claude");
        claude = claude with { UpdatedAt = t.AddHours(-30), CreatedAt = t.AddHours(-30) };

        var reading = Note.Create(
            "Reading list",
            "- Designing Data-Intensive Applications, chapter 7\n- The WinUI 3 layout guide\n- A talk on trunk based development\n",
            new[] { "idea" },
            new[] { new NoteLink(NoteLinkKind.Url, "https://learn.microsoft.com/windows/apps/winui/", "WinUI documentation") },
            false, t.AddDays(-20), "n-reading");
        reading = reading with { UpdatedAt = t.AddDays(-20), CreatedAt = t.AddDays(-30) };

        return new InMemoryNoteStore(new[] { ideas, decision, meeting, snippet, claude, reading });
    }
}
