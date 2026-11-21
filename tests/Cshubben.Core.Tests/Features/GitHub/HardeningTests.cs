using System.Text;
using System.Text.Json;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Notes;
using Cshubben.Core.Features.Review;
using Cshubben.Core.Shared;
using Xunit;

using Cshubben.Core.Data;
namespace Cshubben.Core.Tests.GitHubTests;

public class HardeningTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private static string Contents(byte[] bytes) =>
        "{\"encoding\":\"base64\",\"sha\":\"" + Sha + "\",\"content\":\"" + Convert.ToBase64String(bytes) + "\"}";

    [Fact]
    public void Non_utf8_file_is_refused_instead_of_being_corrupted()
    {
        var latin1 = new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x0A };
        Assert.Null(SuggestionCommits.ParseFile(Setup.Parse(Contents(latin1))));
    }

    [Fact]
    public void Utf8_file_with_bom_round_trips()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("a\r\n")).ToArray();
        var parsed = SuggestionCommits.ParseFile(Setup.Parse(Contents(bytes)));
        Assert.NotNull(parsed);
        var request = SuggestionCommits.CommitFileRequest("me/repo", "a.txt", "main", "m", parsed!.Value.Text, Sha);
        var sent = Convert.FromBase64String(JsonDocument.Parse(request.JsonBody!).RootElement.GetProperty("content").GetString()!);
        Assert.Equal(bytes, sent);
    }

    [Fact]
    public void File_with_mixed_line_endings_is_not_rewritten()
    {
        var body = "```suggestion\nX\n```";
        var plan = SuggestionApplier.Plan("a.cs", "one\r\ntwo\nthree\r\n", 1, 1, body);
        Assert.False(plan.Ok);
        Assert.False(SuggestionApplier.Plan("a.cs", "one\rtwo\r", 1, 1, body).Ok);
        Assert.True(SuggestionApplier.Plan("a.cs", "one\r\ntwo\r\n", 1, 1, body).Ok);
    }

    [Fact]
    public async Task Merge_is_refused_when_the_branch_moved_since_the_reviewed_commit()
    {
        var head = "fedcba9876543210fedcba9876543210fedcba98";
        var handler = new FakeHandler(r => r.Method == HttpMethod.Get
            ? FakeHandler.Ok("{\"head\":{\"sha\":\"" + head + "\"}}")
            : FakeHandler.Ok("{\"merged\":true}"));
        var writer = new GitHubWriter(Setup.Rest(handler));

        await Assert.ThrowsAsync<GitHubApiException>(() =>
            writer.MergeAsync("acme/api", 5, GitHubMergeMethod.Merge, dryRun: false, expectedHeadSha: Sha));

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Merge_goes_ahead_when_the_reviewed_commit_is_still_the_head()
    {
        var handler = new FakeHandler(r => r.Method == HttpMethod.Get
            ? FakeHandler.Ok("{\"head\":{\"sha\":\"" + Sha + "\"}}")
            : FakeHandler.Ok("{\"merged\":true}"));
        var writer = new GitHubWriter(Setup.Rest(handler));
        var result = await writer.MergeAsync("acme/api", 5, GitHubMergeMethod.Merge, dryRun: false, expectedHeadSha: Sha.Substring(0, 12));
        Assert.True(result.Sent);
    }

    [Theory]
    [InlineData("see [x](file:///C:/Windows/system32/cmd.exe) now")]
    [InlineData("see [x](javascript:alert(1)) now")]
    [InlineData("see ftp://evil.example/x now")]
    [InlineData("see [x](ms-msdt://id/PCWDiagnostic) now")]
    [InlineData("see www.evil.example/x now")]
    public void Link_filter_removes_non_web_schemes_too(string text)
    {
        var stripped = LinkFilter.StripNonGitHubLinks(text);
        Assert.Contains(LinkFilter.Removed, stripped);
        Assert.DoesNotContain("evil", stripped);
        Assert.DoesNotContain("cmd.exe", stripped);
        Assert.DoesNotContain("alert", stripped);
        Assert.DoesNotContain("msdt", stripped);
    }

    [Fact]
    public void Link_filter_leaves_ordinary_words_alone()
    {
        Assert.Equal("metadata:value and profile:x", LinkFilter.StripNonGitHubLinks("metadata:value and profile:x"));
    }

    [Theory]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("com1")]
    public void Windows_device_names_are_not_note_ids(string id)
    {
        Assert.False(NoteNames.IsValidId(id));
    }

    [Theory]
    [InlineData("CON.txt")]
    [InlineData("lpt3.pdf")]
    [InlineData("nul")]
    public void Windows_device_names_are_not_attachment_names(string name)
    {
        Assert.False(NoteNames.IsValidFileName(name));
        Assert.True(NoteNames.IsValidFileName("console.txt"));
    }

    [Fact]
    public async Task Claude_failure_text_never_contains_the_token()
    {
        var secret = "sk-" + "ant-oat01-abcdefghijklmnopqrstuvwxyz";
        var fake = new Cshubben.Core.Tests.Integrations.FakeProcessRunner(new ProcessResult(1, "", "boom with " + secret + " inside"));
        var runner = new ClaudeRunner(fake, () => "/bin/claude", () => secret);
        var result = await runner.RunAsync(new ClaudeRequest("s", "u"));
        Assert.False(result.Success);
        Assert.DoesNotContain("abcdefghijklmnop", result.ErrorMessage);
    }

    [Fact]
    public void Redactor_hides_the_common_token_shapes()
    {
        var text = "a " + "ghp_" + "abcdefghijklmnopqrstu b " + "github_pat_" + "abcdefghijklmnopqrstu c "
            + "lin_api_" + "abcdefghijklmnopqrstu d Authorization: Bearer abcdefghijklmnopqrstuvwxyz";
        var scrubbed = SecretRedactor.Scrub(text);
        Assert.DoesNotContain("abcdefghijklmnop", scrubbed);
    }
}
