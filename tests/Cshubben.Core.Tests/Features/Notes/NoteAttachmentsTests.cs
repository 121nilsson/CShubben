using System.Text;
using Cshubben.Core.Features.Notes;
using Xunit;

namespace Cshubben.Core.Tests.Notes;

public sealed class NoteAttachmentsTests
{
    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    private static async Task<InMemoryNoteStore> StoreWithNote()
    {
        var store = new InMemoryNoteStore();
        await store.SaveAsync(Note.Create("n", "", id: "n1"));
        return store;
    }

    [Theory]
    [InlineData(@"C:\Users\me\Desktop\report.pdf", "report.pdf")]
    [InlineData("/home/me/report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..", "_")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("a:b*c?.txt", "a_b_c_.txt")]
    [InlineData("name. ", "name")]
    [InlineData("a..b.txt", "a_b.txt")]
    public void Sanitizes_names_from_outside(string? input, string expected) =>
        Assert.Equal(expected, NoteAttachments.SanitizeName(input));

    [Theory]
    [InlineData("setup.EXE", true)]
    [InlineData("run.ps1", true)]
    [InlineData("link.lnk", true)]
    [InlineData("report.pdf", false)]
    [InlineData("noext", false)]
    public void Executable_types_are_never_launched(string name, bool executable) =>
        Assert.Equal(executable, NoteAttachments.IsExecutable(name));

    [Fact]
    public void Sanitized_names_are_always_valid_and_short()
    {
        foreach (var input in new[] { new string('x', 500) + ".pdf", "CON", "a\0b", "  ", "...", "dir\\" })
            Assert.True(NoteNames.IsValidFileName(NoteAttachments.SanitizeName(input)), input);
        Assert.True(NoteAttachments.SanitizeName(new string('x', 500) + ".pdf").EndsWith(".pdf"));
    }

    [Fact]
    public void Unique_name_adds_a_counter_before_the_extension_ignoring_case()
    {
        Assert.Equal("a.txt", NoteAttachments.UniqueName("a.txt", Array.Empty<string>()));
        Assert.Equal("a (1).txt", NoteAttachments.UniqueName("a.txt", new[] { "A.TXT" }));
        Assert.Equal("a (2).txt", NoteAttachments.UniqueName("a.txt", new[] { "a.txt", "a (1).txt" }));
        Assert.Equal("README (1)", NoteAttachments.UniqueName("README", new[] { "README" }));
    }

    [Fact]
    public async Task Add_keeps_the_original_name_and_content()
    {
        var store = await StoreWithNote();
        var result = await NoteAttachments.AddAsync(store, "n1", @"C:\x\Spec v2.pdf", Bytes("hello"));
        Assert.True(result.Ok);
        Assert.Equal("Spec v2.pdf", result.FileName);
        var path = await store.OpenAttachmentPathAsync("n1", "Spec v2.pdf");
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
        File.Delete(path);
    }

    [Fact]
    public async Task Add_never_overwrites()
    {
        var store = await StoreWithNote();
        await NoteAttachments.AddAsync(store, "n1", "a.txt", Bytes("one"));
        var second = await NoteAttachments.AddAsync(store, "n1", "a.txt", Bytes("two"));
        Assert.True(second.Ok);
        Assert.Equal("a (1).txt", second.FileName);
        Assert.Contains("already exists", second.Message);
        var note = await store.GetAsync("n1");
        Assert.Equal(new[] { "a.txt", "a (1).txt" }, note!.Attachments.ToArray());
        var first = await store.OpenAttachmentPathAsync("n1", "a.txt");
        Assert.Equal("one", await File.ReadAllTextAsync(first));
        File.Delete(first);
        File.Delete(await store.OpenAttachmentPathAsync("n1", "a (1).txt"));
    }

    [Fact]
    public async Task Add_refuses_a_known_large_size_without_reading()
    {
        var store = await StoreWithNote();
        var stream = new ThrowingStream();
        var result = await NoteAttachments.AddAsync(store, "n1", "big.iso", stream, NoteAttachments.MaxBytes + 1);
        Assert.False(result.Ok);
        Assert.Contains("limit", result.Message);
        Assert.Contains("big.iso", result.Message);
        Assert.Empty((await store.GetAsync("n1"))!.Attachments);
    }

    [Fact]
    public async Task Add_refuses_a_stream_that_turns_out_to_be_too_large()
    {
        var store = await StoreWithNote();
        var result = await NoteAttachments.AddAsync(store, "n1", "big.bin", new EndlessStream());
        Assert.False(result.Ok);
        Assert.Contains("25 MB", result.Message);
        Assert.Empty((await store.GetAsync("n1"))!.Attachments);
    }

    [Fact]
    public async Task Add_accepts_a_file_exactly_at_the_limit()
    {
        Assert.Null(NoteAttachments.CheckSize("x", NoteAttachments.MaxBytes));
        Assert.NotNull(NoteAttachments.CheckSize("x", NoteAttachments.MaxBytes + 1));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Add_to_a_missing_note_fails_with_a_message()
    {
        var result = await NoteAttachments.AddAsync(new InMemoryNoteStore(), "nope", "a.txt", Bytes("x"));
        Assert.False(result.Ok);
        Assert.Contains("no longer exists", result.Message);
    }

    [Fact]
    public async Task Remove_deletes_the_file_and_the_listing_entry()
    {
        var store = await StoreWithNote();
        await NoteAttachments.AddAsync(store, "n1", "a.txt", Bytes("x"));
        await NoteAttachments.AddAsync(store, "n1", "b.txt", Bytes("y"));
        await store.RemoveAttachmentAsync("n1", "a.txt");
        Assert.Equal(new[] { "b.txt" }, (await store.GetAsync("n1"))!.Attachments.ToArray());
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenAttachmentPathAsync("n1", "a.txt"));
        await store.RemoveAttachmentAsync("n1", "a.txt"); // removing twice is fine
        await Assert.ThrowsAsync<ArgumentException>(() => store.RemoveAttachmentAsync("n1", "../n1.md"));
    }

    [Fact]
    public async Task File_store_remove_deletes_from_disk_and_rejects_traversal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cshubben-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileNoteStore(dir);
            await store.SaveAsync(Note.Create("n", "", id: "n1"));
            await NoteAttachments.AddAsync(store, "n1", "a.txt", Bytes("x"));
            await NoteAttachments.AddAsync(store, "n1", "a.txt", Bytes("y"));
            Assert.Equal(new[] { "a (1).txt", "a.txt" }, (await store.GetAsync("n1"))!.Attachments.ToArray());
            await store.RemoveAttachmentAsync("n1", "a.txt");
            Assert.False(File.Exists(Path.Combine(dir, "n1.files", "a.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "n1.files", "a (1).txt")));
            await Assert.ThrowsAsync<ArgumentException>(() => store.RemoveAttachmentAsync("n1", "..\\n1.md"));
            Assert.True(File.Exists(Path.Combine(dir, "n1.md")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("must not be read");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => count;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed class NoteAttachFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cshubben-tests-" + Guid.NewGuid().ToString("N"));

    public NoteAttachFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Attaches_real_files_reports_folders_and_missing_files()
    {
        var store = new InMemoryNoteStore();
        await store.SaveAsync(Note.Create("n", "", id: "n1"));
        var a = Path.Combine(_dir, "a.txt");
        await File.WriteAllTextAsync(a, "hello");
        var folder = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(folder);

        var result = await NoteAttachments.AddFilesAsync(store, "n1", new[] { a, folder, Path.Combine(_dir, "missing.txt"), a });

        Assert.Equal(new[] { "a.txt", "a (1).txt" }, result.Added.ToArray());
        Assert.Equal(2, result.Problems.Count);
        Assert.True(result.HasProblems);
        Assert.Contains("folder", result.Summary);
        Assert.Contains("could not be found", result.Summary);
        Assert.Contains("Attached 2 files.", result.Summary);
    }

    [Fact]
    public async Task Single_file_summary_names_the_file()
    {
        var store = new InMemoryNoteStore();
        await store.SaveAsync(Note.Create("n", "", id: "n1"));
        var a = Path.Combine(_dir, "one.md");
        await File.WriteAllTextAsync(a, "x");
        var result = await NoteAttachments.AddFilesAsync(store, "n1", new[] { a });
        Assert.False(result.HasProblems);
        Assert.Equal("Attached one.md.", result.Summary);
    }
}
