using Cshubben.Core.Features.Notes;
using Xunit;

namespace Cshubben.Core.Tests.Notes;

public class DemoAndPathsTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Demo_has_six_notes_with_valid_ids()
    {
        var notes = await InMemoryNoteStore.Demo(T).ListAsync();
        Assert.Equal(6, notes.Count);
        Assert.All(notes, n => Assert.True(NoteNames.IsValidId(n.Id)));
        Assert.Equal(6, notes.Select(n => n.Id).Distinct().Count());
    }

    [Fact]
    public async Task Demo_has_a_pinned_note_and_the_expected_tags()
    {
        var notes = await InMemoryNoteStore.Demo(T).ListAsync();
        Assert.Single(notes, n => n.Pinned);
        var tags = NoteSearch.AllTags(notes);
        Assert.Contains("idea", tags);
        Assert.Contains("decision", tags);
        Assert.Contains("todo", tags);
    }

    [Fact]
    public async Task Demo_decision_note_links_to_the_pr_and_a_snippet_has_code()
    {
        var notes = await InMemoryNoteStore.Demo(T).ListAsync();
        var decision = Assert.Single(notes, n => n.Tags.Contains("decision"));
        Assert.Contains(decision.Links, l => l.Kind == NoteLinkKind.Pr && l.Target == "acme/api#412");
        Assert.Contains(notes, n => n.Body.Contains("```csharp"));
        Assert.All(notes, n => Assert.True(n.UpdatedAt <= T));
    }

    [Fact]
    public async Task Demo_sorts_pinned_first_in_search()
    {
        var notes = await InMemoryNoteStore.Demo(T).ListAsync();
        Assert.True(NoteSearch.Filter(notes, null, null)[0].Pinned);
    }

    [Fact]
    public async Task In_memory_store_saves_deletes_and_keeps_attachments()
    {
        var store = new InMemoryNoteStore();
        await store.SaveAsync(Note.Create("n", "b", id: "n1"));
        await store.AddAttachmentAsync("n1", "a.txt", new MemoryStream(new byte[] { 1, 2, 3 }));
        await store.SaveAsync(Note.Create("renamed", "b", id: "n1"));
        var note = await store.GetAsync("n1");
        Assert.Equal("renamed", note!.Title);
        Assert.Equal(new[] { "a.txt" }, note.Attachments.ToArray());
        var path = await store.OpenAttachmentPathAsync("n1", "a.txt");
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
        File.Delete(path);
        await store.DeleteAsync("n1");
        Assert.Null(await store.GetAsync("n1"));
    }

    [Fact]
    public async Task In_memory_store_rejects_bad_names()
    {
        var store = new InMemoryNoteStore();
        await store.SaveAsync(Note.Create("n", "", id: "n1"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAttachmentAsync("n1", "../x", new MemoryStream()));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Note.Create("n", "", id: "../x")));
    }

    [Fact]
    public void Data_directory_ends_in_cshubben_and_notes_live_below_it()
    {
        Assert.Equal("Cshubben", Path.GetFileName(AppPaths.DataDirectory));
        Assert.Equal("notes", Path.GetFileName(AppPaths.NotesDirectory));
        Assert.Equal(AppPaths.DataDirectory, Path.GetDirectoryName(AppPaths.NotesDirectory));
        Assert.Equal(Path.Combine("x", "notes"), AppPaths.NotesIn("x"));
    }
}
