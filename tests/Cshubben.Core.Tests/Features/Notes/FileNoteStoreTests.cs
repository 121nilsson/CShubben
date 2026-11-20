using System.Text;
using Cshubben.Core.Features.Notes;
using Xunit;

namespace Cshubben.Core.Tests.Notes;

public sealed class FileNoteStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cshubben-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FileNoteStore _store;

    public FileNoteStoreTests() => _store = new FileNoteStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static Stream Bytes(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Save_then_get_returns_the_note()
    {
        var note = Note.Create("Hello: \"world\"", "body\nmore", new[] { "a", "b" }, id: "n1");
        await _store.SaveAsync(note);
        var back = await _store.GetAsync("n1");
        Assert.NotNull(back);
        Assert.Equal(note.Title, back.Title);
        Assert.Equal(note.Body, back.Body);
        Assert.Equal(note.Tags.ToArray(), back.Tags.ToArray());
    }

    [Fact]
    public async Task Each_note_is_one_markdown_file()
    {
        await _store.SaveAsync(Note.Create("One", "x", id: "n1"));
        await _store.SaveAsync(Note.Create("Two", "y", id: "n2"));
        var files = Directory.GetFiles(_dir, "*.md").Select(f => Path.GetFileName(f)).OrderBy(f => f).ToArray();
        Assert.Equal(new[] { "n1.md", "n2.md" }, files);
    }

    [Fact]
    public async Task Get_unknown_note_returns_null() =>
        Assert.Null(await _store.GetAsync("missing"));

    [Fact]
    public async Task List_is_newest_first()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await _store.SaveAsync(Note.Create("old", "", now: t, id: "a"));
        await _store.SaveAsync(Note.Create("new", "", now: t.AddDays(2), id: "b"));
        await _store.SaveAsync(Note.Create("mid", "", now: t.AddDays(1), id: "c"));
        var list = await _store.ListAsync();
        Assert.Equal(new[] { "b", "c", "a" }, list.Select(n => n.Id).ToArray());
    }

    [Fact]
    public async Task Save_overwrites_and_delete_removes()
    {
        await _store.SaveAsync(Note.Create("v1", "", id: "n1"));
        await _store.SaveAsync(Note.Create("v2", "", id: "n1"));
        Assert.Equal("v2", (await _store.GetAsync("n1"))!.Title);
        Assert.Single(await _store.ListAsync());
        await _store.DeleteAsync("n1");
        Assert.Null(await _store.GetAsync("n1"));
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task Delete_also_removes_attachments()
    {
        await _store.SaveAsync(Note.Create("n", "", id: "n1"));
        await _store.AddAttachmentAsync("n1", "a.txt", Bytes("hi"));
        Assert.True(Directory.Exists(Path.Combine(_dir, "n1.files")));
        await _store.DeleteAsync("n1");
        Assert.False(Directory.Exists(Path.Combine(_dir, "n1.files")));
    }

    [Fact]
    public async Task Attachments_are_listed_and_openable()
    {
        await _store.SaveAsync(Note.Create("n", "", id: "n1"));
        await _store.AddAttachmentAsync("n1", "b.txt", Bytes("bee"));
        await _store.AddAttachmentAsync("n1", "a report.pdf", Bytes("pdf"));
        var note = await _store.GetAsync("n1");
        Assert.Equal(new[] { "a report.pdf", "b.txt" }, note!.Attachments.ToArray());
        var path = await _store.OpenAttachmentPathAsync("n1", "b.txt");
        Assert.Equal("bee", await File.ReadAllTextAsync(path));
        Assert.StartsWith(Path.GetFullPath(_dir), path);
    }

    [Fact]
    public async Task Saving_a_note_keeps_its_attachments()
    {
        await _store.SaveAsync(Note.Create("n", "", id: "n1"));
        await _store.AddAttachmentAsync("n1", "a.txt", Bytes("x"));
        await _store.SaveAsync(Note.Create("renamed", "", id: "n1"));
        Assert.Equal(new[] { "a.txt" }, (await _store.GetAsync("n1"))!.Attachments.ToArray());
    }

    [Fact]
    public async Task Attachment_for_missing_note_fails() =>
        await Assert.ThrowsAsync<FileNotFoundException>(() => _store.AddAttachmentAsync("nope", "a.txt", Bytes("x")));

    [Fact]
    public async Task Opening_a_missing_attachment_fails() =>
        await Assert.ThrowsAsync<FileNotFoundException>(() => _store.OpenAttachmentPathAsync("n1", "a.txt"));

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("sub/file.txt")]
    [InlineData("sub\\file.txt")]
    [InlineData("..")]
    [InlineData("a..b.txt")]
    [InlineData("C:\\windows\\x.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Attachment_names_with_paths_are_rejected(string name)
    {
        await _store.SaveAsync(Note.Create("n", "", id: "n1"));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddAttachmentAsync("n1", name, Bytes("x")));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.OpenAttachmentPathAsync("n1", name));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("has space")]
    public async Task Note_ids_that_could_escape_are_rejected(string id)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.GetAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.DeleteAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(Note.Create("x", "", id: id)));
    }

    [Fact]
    public async Task Nothing_is_written_outside_the_root()
    {
        var parent = Directory.GetParent(_dir)!.FullName;
        var before = Directory.GetFiles(parent, "evil*").Length;
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(Note.Create("x", "", id: "../evil")));
        Assert.Equal(before, Directory.GetFiles(parent, "evil*").Length);
    }

    [Fact]
    public async Task Version_and_event_change_on_every_mutation()
    {
        var events = 0;
        _store.Changed += () => events++;
        var v0 = _store.Version;
        await _store.SaveAsync(Note.Create("n", "", id: "n1"));
        await _store.AddAttachmentAsync("n1", "a.txt", Bytes("x"));
        await _store.DeleteAsync("n1");
        Assert.Equal(v0 + 3, _store.Version);
        Assert.Equal(3, events);
    }

    [Fact]
    public async Task Unrelated_files_in_the_folder_are_ignored()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "readme.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(_dir, "bad name.md"), "x");
        await _store.SaveAsync(Note.Create("n", "", id: "n1"));
        var list = await _store.ListAsync();
        Assert.Equal("n1", Assert.Single(list).Id);
    }

    [Fact]
    public async Task Hand_written_markdown_file_is_listed()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "manual.md"), "# From my editor\n");
        var note = await _store.GetAsync("manual");
        Assert.Equal("# From my editor\n", note!.Body);
    }

    [Fact]
    public void Temp_directory_is_removed_on_dispose()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cshubben-tests-" + Guid.NewGuid().ToString("N"));
        _ = new FileNoteStore(dir);
        Assert.True(Directory.Exists(dir));
        Directory.Delete(dir, true);
        Assert.False(Directory.Exists(dir));
    }
}
