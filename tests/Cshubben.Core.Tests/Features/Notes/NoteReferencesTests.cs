using Cshubben.Core.Features.Notes;
using Xunit;

namespace Cshubben.Core.Tests.Notes;

public class NoteReferencesTests
{
    private static string[] Shown(string text, params string[] repos) =>
        NoteReferences.Find(text, repos).Select(r => r.Display).ToArray();

    [Fact]
    public void Finds_repo_qualified_pull_requests()
    {
        Assert.Equal(new[] { "acme/api#412", "acme/web#9" }, Shown("See acme/api#412 and (acme/web#9)."));
    }

    [Fact]
    public void Bare_number_needs_exactly_one_repo_in_context()
    {
        Assert.Equal(new[] { "acme/api#412" }, Shown("Fixed in #412.", "acme/api"));
        Assert.Equal(new[] { "acme/api#412" }, Shown("Fixed in #412.", "acme/api", "ACME/api"));
        Assert.Empty(Shown("Fixed in #412.", "acme/api", "acme/web"));
        Assert.Empty(Shown("Fixed in #412."));
    }

    [Fact]
    public void Finds_github_pull_request_urls_with_trailing_path_and_punctuation()
    {
        var refs = NoteReferences.Find("Look: https://github.com/acme/api/pull/412/files#diff-1.");
        var r = Assert.Single(refs);
        Assert.Equal(NoteReferenceKind.PullRequest, r.Kind);
        Assert.Equal("acme/api", r.Repo);
        Assert.Equal(412, r.Number);
        Assert.Equal("https://github.com/acme/api/pull/412", r.WebUrl);
    }

    [Fact]
    public void Github_urls_that_are_not_pull_requests_are_ignored()
    {
        Assert.Empty(Shown("https://github.com/acme/api/issues/12 and https://github.com/acme/api"));
    }

    [Fact]
    public void Finds_linear_identifiers_and_urls()
    {
        Assert.Equal(new[] { "API-231", "ENG-7" }, Shown("API-231 then https://linear.app/acme/issue/ENG-7/some-title"));
        var url = Assert.Single(NoteReferences.Find("https://linear.app/acme/issue/eng-7/x"));
        Assert.Equal("ENG-7", url.Identifier);
        Assert.Equal("https://linear.app/issue/ENG-7", url.WebUrl);
    }

    [Theory]
    [InlineData("Use UTF-8 here")]
    [InlineData("SHA-256 hashes")]
    [InlineData("api-231 in lower case")]
    [InlineData("XENG-1234567")]
    [InlineData("ENG-12abc")]
    [InlineData("a/b/ENG-5")]
    public void Things_that_only_look_like_issues_are_ignored(string text) => Assert.Empty(Shown(text));

    [Fact]
    public void Code_is_not_linked()
    {
        var text = "Real acme/api#1 but `acme/api#2` and API-3 in code.\n```\nacme/api#4 API-5\n```\nAfter API-6";
        Assert.Equal(new[] { "acme/api#1", "API-6" }, Shown(text).Where(s => s != "API-3").ToArray());
        Assert.DoesNotContain("acme/api#2", Shown(text));
        Assert.DoesNotContain("acme/api#4", Shown(text));
    }

    [Fact]
    public void Unmatched_backtick_does_not_hide_the_rest_of_the_line()
    {
        Assert.Equal(new[] { "API-1" }, Shown("it`s API-1"));
    }

    [Fact]
    public void Positions_point_at_the_reference()
    {
        const string text = "x acme/api#412 y";
        var r = Assert.Single(NoteReferences.Find(text));
        Assert.Equal("acme/api#412", text.Substring(r.Start, r.Length));
    }

    [Fact]
    public void Zero_and_huge_numbers_are_ignored()
    {
        Assert.Empty(Shown("acme/api#0 acme/api#123456789"));
    }

    [Fact]
    public void Empty_text_has_no_references()
    {
        Assert.Empty(NoteReferences.Find(null));
        Assert.Empty(NoteReferences.Find(""));
    }

    [Fact]
    public void For_note_resolves_bare_numbers_from_the_notes_pr_link()
    {
        var note = Note.Create("n", "also see #409", links: new[] { new NoteLink(NoteLinkKind.Pr, "acme/api#412", "") }, id: "n1");
        Assert.Equal(new[] { "acme/api#409" }, NoteReferences.ForNote(note).Select(r => r.Display).ToArray());
    }

    [Fact]
    public void Distinct_for_note_lists_each_target_once()
    {
        var note = Note.Create("n", "API-1 acme/api#2 api-1 API-1 ACME/API#2", id: "n1");
        Assert.Equal(new[] { "API-1", "acme/api#2" }, NoteReferences.DistinctForNote(note).Select(r => r.Display).ToArray());
    }

    [Fact]
    public void Pull_requests_of_a_note_merge_links_and_text_without_duplicates()
    {
        var note = Note.Create("n", "acme/api#412 and ACME/API#9 and #9",
            links: new[] { new NoteLink(NoteLinkKind.Pr, "acme/api#412", ""), new NoteLink(NoteLinkKind.Url, "https://x.y", "") }, id: "n1");
        var prs = NoteReferences.PullRequestsOf(note);
        Assert.Equal(new[] { 412, 9 }, prs.Select(p => p.Number).ToArray());
    }

    [Fact]
    public async Task Notes_mentioning_a_pr_come_from_links_and_text_pinned_first()
    {
        var notes = await InMemoryNoteStore.Demo(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)).ListAsync();
        var hit = NoteReferences.NotesMentioningPr(notes, "ACME/api", 412).Select(n => n.Id).ToArray();
        Assert.Contains("n-decision", hit);
        Assert.Contains("n-links", hit);
        Assert.DoesNotContain("n-ideas", hit);
        Assert.Empty(NoteReferences.NotesMentioningPr(notes, "acme/api", 99999));
    }

    [Fact]
    public async Task Notes_mentioning_an_issue_come_from_links_and_text()
    {
        var notes = await InMemoryNoteStore.Demo(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)).ListAsync();
        var hit = NoteReferences.NotesMentioningIssue(notes, "eng-231").Select(n => n.Id).ToArray();
        Assert.Contains("n-decision", hit);
        Assert.Contains("n-links", hit);
    }

    [Theory]
    [InlineData("acme/api#412", true, "acme/api", 412)]
    [InlineData(" acme/api#412 ", true, "acme/api", 412)]
    [InlineData("acme/api#", false, "", 0)]
    [InlineData("api#412", false, "", 0)]
    [InlineData("acme/api#4x", false, "", 0)]
    public void Parses_pr_targets(string target, bool ok, string repo, int number)
    {
        Assert.Equal(ok, NoteReferences.TryParsePrTarget(target, out var r, out var n));
        Assert.Equal(repo, r);
        Assert.Equal(number, n);
    }

    [Fact]
    public void Demo_links_note_has_references_but_none_in_code()
    {
        var note = InMemoryNoteStore.Demo().GetAsync("n-links").GetAwaiter().GetResult()!;
        var shown = NoteReferences.ForNote(note).Select(r => r.Display).ToArray();
        Assert.Contains("acme/api#412", shown);
        Assert.Contains("acme/api#409", shown);
        Assert.Contains("ENG-231", shown);
        Assert.DoesNotContain("acme/api#999", shown);
        Assert.DoesNotContain("acme/api#998", shown);
    }
}

public class NoteLinkUrlTests
{
    [Theory]
    [InlineData(NoteLinkKind.Pr, "acme/api#412", "https://github.com/acme/api/pull/412")]
    [InlineData(NoteLinkKind.Issue, "eng-231", "https://linear.app/issue/ENG-231")]
    [InlineData(NoteLinkKind.Repo, "acme/web", "https://github.com/acme/web")]
    [InlineData(NoteLinkKind.Url, "https://learn.microsoft.com/x", "https://learn.microsoft.com/x")]
    [InlineData(NoteLinkKind.Url, "javascript:alert(1)", null)]
    [InlineData(NoteLinkKind.Url, "file:///c:/windows", null)]
    [InlineData(NoteLinkKind.Url, "not a url", null)]
    [InlineData(NoteLinkKind.Pr, "garbage", null)]
    [InlineData(NoteLinkKind.Issue, "garbage", null)]
    public void Link_urls_are_only_ever_http_s(NoteLinkKind kind, string target, string? expected) =>
        Assert.Equal(expected, NoteReferences.LinkUrl(new NoteLink(kind, target, "")));
}
