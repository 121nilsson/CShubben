using Cshubben.Core.Notes;
using Xunit;

namespace Cshubben.Core.Tests.Notes;

public class NoteFormatTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 12, 30, 15, TimeSpan.FromHours(1));

    private static Note RoundTrip(Note note) =>
        NoteFormat.Parse(note.Id, NoteFormat.Serialize(note), At);

    [Theory]
    [InlineData("Plain title")]
    [InlineData("Title: with colon")]
    [InlineData("He said \"hello\" and left")]
    [InlineData("Back\\slash and \\n literal")]
    [InlineData("Line one\nLine two\r\nLine three")]
    [InlineData("Tab\tseparated")]
    [InlineData("Unicode åäö ñ 日本語 Ελληνικά")]
    [InlineData("Emoji 🚀 and 👩‍💻")]
    [InlineData("---")]
    [InlineData("- looks like a list item")]
    [InlineData("  leading and trailing spaces  ")]
    [InlineData("")]
    public void Title_round_trips(string title)
    {
        var note = Note.Create(title, "body", now: At, id: "a1");
        Assert.Equal(title, RoundTrip(note).Title);
    }

    [Fact]
    public void Lone_surrogate_and_control_characters_round_trip()
    {
        var title = "bad\ud800 end\u0001\u2028";
        var note = Note.Create(title, "", now: At, id: "a1");
        Assert.Equal(title, RoundTrip(note).Title);
    }

    [Fact]
    public void Tags_with_nasty_text_round_trip()
    {
        var tags = new[] { "idea", "a: b", "quote\"d", "multi\nline", "emoji🎉", "  spaced  " };
        var note = Note.Create("t", "b", tags, now: At, id: "a1");
        Assert.Equal(tags, RoundTrip(note).Tags);
    }

    [Fact]
    public void Links_round_trip_including_odd_labels()
    {
        var links = new[]
        {
            new NoteLink(NoteLinkKind.Pr, "acme/api#412", "Fix: \"quotes\" and\nnewlines"),
            new NoteLink(NoteLinkKind.Issue, "ENG-123", ""),
            new NoteLink(NoteLinkKind.Url, "https://example.com/a?b=c&d=e", "Example ✓"),
            new NoteLink(NoteLinkKind.Repo, "acme/api", "acme/api"),
        };
        var note = Note.Create("t", "b", null, links, now: At, id: "a1");
        Assert.Equal(links, RoundTrip(note).Links);
    }

    [Fact]
    public void Body_is_preserved_exactly_even_when_it_contains_fences()
    {
        var body = "---\nnot front matter\n---\r\n\r\n# Heading\n\n```\ncode\n```\n\ntrailing\n\n";
        var note = Note.Create("t", body, now: At, id: "a1");
        Assert.Equal(body, RoundTrip(note).Body);
    }

    [Fact]
    public void Empty_body_round_trips()
    {
        var note = Note.Create("t", "", now: At, id: "a1");
        Assert.Equal("", RoundTrip(note).Body);
    }

    [Fact]
    public void Pinned_and_times_round_trip()
    {
        var note = Note.Create("t", "b", pinned: true, now: At, id: "a1") with { UpdatedAt = At.AddHours(5) };
        var back = RoundTrip(note);
        Assert.True(back.Pinned);
        Assert.Equal(note.CreatedAt, back.CreatedAt);
        Assert.Equal(note.UpdatedAt, back.UpdatedAt);
    }

    [Fact]
    public void File_without_front_matter_becomes_the_body()
    {
        var fallback = At.AddDays(-1);
        var note = NoteFormat.Parse("x", "# Just text\n\nhello", fallback);
        Assert.Equal("# Just text\n\nhello", note.Body);
        Assert.Equal("", note.Title);
        Assert.Equal(fallback, note.UpdatedAt);
    }

    [Fact]
    public void Unclosed_front_matter_is_treated_as_body()
    {
        var note = NoteFormat.Parse("x", "---\ntitle: \"x\"\nno end", At);
        Assert.Contains("no end", note.Body);
    }

    [Fact]
    public void Windows_line_endings_in_front_matter_are_accepted()
    {
        var text = "---\r\ntitle: \"Hi\"\r\npinned: true\r\ntags:\r\n- \"a\"\r\n---\r\nbody";
        var note = NoteFormat.Parse("x", text, At);
        Assert.Equal("Hi", note.Title);
        Assert.True(note.Pinned);
        Assert.Equal(new[] { "a" }, note.Tags);
        Assert.Equal("body", note.Body);
    }

    [Fact]
    public void Malformed_items_are_skipped_not_fatal()
    {
        var text = "---\ntitle: \"ok\"\ntags:\n- \"bad\\q\"\n- \"good\"\nlinks:\n- Nonsense \"x\" \"y\"\n---\n";
        var note = NoteFormat.Parse("x", text, At);
        Assert.Equal("ok", note.Title);
        Assert.Equal(new[] { "good" }, note.Tags);
        Assert.Empty(note.Links);
    }

    [Theory]
    [InlineData("acme/api#412", NoteLinkKind.Pr)]
    [InlineData("ENG-123", NoteLinkKind.Issue)]
    [InlineData("https://example.com/x", NoteLinkKind.Url)]
    [InlineData("acme/api", NoteLinkKind.Repo)]
    public void Link_kind_is_guessed(string target, NoteLinkKind kind) =>
        Assert.Equal(kind, NoteLink.Parse(target).Kind);
}
