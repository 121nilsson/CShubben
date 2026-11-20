using Cshubben.Core.Model;
using Cshubben.Core.Features.Notes;
using Xunit;

namespace Cshubben.Core.Tests.Notes;

public class NoteSearchTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static Note N(string id, string title, string body = "", string[]? tags = null, bool pinned = false, int ageDays = 0) =>
        Note.Create(title, body, tags, null, pinned, T.AddDays(-ageDays), id);

    private static string[] Ids(IEnumerable<Note> notes) => notes.Select(n => n.Id).ToArray();

    [Fact]
    public void Empty_query_returns_everything_newest_first()
    {
        var notes = new[] { N("old", "a", ageDays: 5), N("new", "b", ageDays: 1) };
        Assert.Equal(new[] { "new", "old" }, Ids(NoteSearch.Filter(notes, "", null)));
        Assert.Equal(new[] { "new", "old" }, Ids(NoteSearch.Filter(notes, null, null)));
    }

    [Fact]
    public void Title_match_beats_tag_match_beats_body_match()
    {
        var notes = new[]
        {
            N("body", "Other", body: "talks about pagination here"),
            N("tag", "Other", tags: new[] { "pagination" }),
            N("title", "Pagination design"),
        };
        Assert.Equal(new[] { "title", "tag", "body" }, Ids(NoteSearch.Filter(notes, "pagination", null)));
    }

    [Fact]
    public void Search_is_case_insensitive()
    {
        var notes = new[] { N("a", "Cursor Pagination", tags: new[] { "API" }, body: "Uses ÅÄÖ") };
        Assert.Single(NoteSearch.Filter(notes, "cursor", null));
        Assert.Single(NoteSearch.Filter(notes, "api", null));
        Assert.Single(NoteSearch.Filter(notes, "åäö", null));
    }

    [Fact]
    public void Every_word_must_match_somewhere()
    {
        var notes = new[]
        {
            N("both", "Release notes", body: "checkout flaky"),
            N("one", "Release notes", body: "nothing"),
        };
        Assert.Equal(new[] { "both" }, Ids(NoteSearch.Filter(notes, "release flaky", null)));
    }

    [Fact]
    public void Non_matching_notes_are_dropped() =>
        Assert.Empty(NoteSearch.Filter(new[] { N("a", "Hello") }, "zzz", null));

    [Fact]
    public void Pinned_notes_come_first_even_when_less_relevant()
    {
        var notes = new[]
        {
            N("best", "Pagination"),
            N("pinned", "Other", body: "pagination", pinned: true),
        };
        Assert.Equal(new[] { "pinned", "best" }, Ids(NoteSearch.Filter(notes, "pagination", null)));
    }

    [Fact]
    public void Title_starting_with_the_word_ranks_higher()
    {
        var notes = new[] { N("mid", "A big idea"), N("start", "Idea list") };
        Assert.Equal(new[] { "start", "mid" }, Ids(NoteSearch.Filter(notes, "idea", null)));
    }

    [Fact]
    public void Tag_filter_is_exact_and_case_insensitive()
    {
        var notes = new[]
        {
            N("a", "A", tags: new[] { "Idea" }),
            N("b", "B", tags: new[] { "ideas" }),
            N("c", "C"),
        };
        Assert.Equal(new[] { "a" }, Ids(NoteSearch.Filter(notes, null, "idea")));
    }

    [Fact]
    public void Tag_filter_and_query_combine()
    {
        var notes = new[]
        {
            N("a", "Alpha", tags: new[] { "todo" }),
            N("b", "Alpha", tags: new[] { "idea" }),
        };
        Assert.Equal(new[] { "b" }, Ids(NoteSearch.Filter(notes, "alpha", "idea")));
    }

    [Fact]
    public void All_tags_are_counted_case_insensitively_most_used_first()
    {
        var notes = new[]
        {
            N("a", "A", tags: new[] { "todo", "idea" }),
            N("b", "B", tags: new[] { "TODO" }),
            N("c", "C", tags: new[] { "zeta", "todo" }),
        };
        Assert.Equal(new[] { "todo", "idea", "zeta" }, NoteSearch.AllTags(notes).ToArray());
    }

    [Fact]
    public void Parse_tags_trims_and_removes_duplicates()
    {
        Assert.Equal(new[] { "a", "b", "c d" }, NoteSearch.ParseTags(" a, b ;A,, c d ").ToArray());
        Assert.Empty(NoteSearch.ParseTags("  "));
    }

    [Fact]
    public void Preview_skips_headings_markers_and_code_fences()
    {
        Assert.Equal("Why we did it", NoteSearch.Preview("\n# Why we did it\nmore"));
        Assert.Equal("first item", NoteSearch.Preview("- first item\n- second"));
        Assert.Equal("real text", NoteSearch.Preview("```\n**real** text"));
        Assert.Equal("", NoteSearch.Preview("   \n\n"));
        Assert.Equal(10, NoteSearch.Preview(new string('x', 50), 10).Length);
    }

    [Fact]
    public void From_pull_request_links_to_the_pr()
    {
        var pr = new PullRequest("1", PrRole.Authored, "acme/api", 412, "Add cursor pagination", "https://x", "maja", false,
            T, T, 1, 1, 1, ReviewDecision.None, CiState.Success, MergeState.Mergeable, 0, 0, "feat/x");
        var note = Note.FromPullRequest(pr, T);
        Assert.Equal("Notes on acme/api #412", note.Title);
        var link = Assert.Single(note.Links);
        Assert.Equal(NoteLinkKind.Pr, link.Kind);
        Assert.Equal("acme/api#412", link.Target);
    }

    [Fact]
    public void From_text_saves_claude_answers_with_their_source()
    {
        var note = Note.FromText("Why flaky?", "Because clocks.", "ENG-9", T);
        Assert.Equal("Because clocks.", note.Body);
        Assert.Contains("claude", note.Tags);
        Assert.Equal(NoteLinkKind.Issue, Assert.Single(note.Links).Kind);
        Assert.Empty(Note.FromText("t", "b", null, T).Links);
    }
}
