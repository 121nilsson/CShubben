using System.Globalization;
using System.Text;
using System.Text.Json;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Review;
using ReviewVerdict = Cshubben.Core.Features.Review.ReviewVerdict;

namespace Cshubben.Services;

/// <summary>
/// <see cref="IReviewData"/> against the real GitHub API. Reads use REST (files, compare, commits, contents) and GraphQL
/// (threads, viewed state, reviews); writes go through <see cref="GitHubWriter"/> so <see cref="DryRun"/> is honoured.
/// Untrusted text (paths, patches, comment bodies) is only ever sent as JSON variables, never put into query text.
/// </summary>
public sealed class GitHubReviewData : IReviewData
{
    private const int MaxFilePages = 30;
    private readonly GitHubRest _rest;
    private readonly GitHubGraphQl _graphql;
    private readonly GitHubWriter _writer;

    public GitHubReviewData(GitHubRest rest, GitHubGraphQl graphql, GitHubWriter writer)
    {
        _rest = rest;
        _graphql = graphql;
        _writer = writer;
    }

    /// <summary>When true nothing is sent; writes pretend to succeed.</summary>
    public bool DryRun { get; set; }

    // ------------------------------------------------------------------ reads

    private const string ContextQuery = """
        query($owner: String!, $name: String!, $number: Int!) {
          viewer { login }
          repository(owner: $owner, name: $name) {
            viewerPermission
            pullRequest(number: $number) {
              headRefOid
              baseRefOid
              body
              author { login }
              reviews(last: 50, states: [APPROVED, CHANGES_REQUESTED, COMMENTED]) {
                nodes { author { login } commit { oid } submittedAt }
              }
            }
          }
        }
        """;

    public async Task<ReviewContext> GetContextAsync(ReviewTarget pr, CancellationToken cancellationToken = default)
    {
        var (owner, name) = Validate(pr);
        var data = await _graphql.QueryAsync(ContextQuery, RepoVars(owner, name, pr.Number), false, cancellationToken).ConfigureAwait(false);
        var viewer = Str(Get(data, "viewer"), "login");
        var node = Get(Get(data, "repository"), "pullRequest");
        var head = Str(node, "headRefOid");
        var baseSha = Str(node, "baseRefOid");

        string? mine = null;
        foreach (var review in Items(Get(node, "reviews"), "nodes"))
        {
            if (!string.Equals(Str(Get(review, "author"), "login"), viewer, StringComparison.OrdinalIgnoreCase)) continue;
            var oid = Str(Get(review, "commit"), "oid");
            if (oid.Length > 0) mine = oid; // reviews come oldest first, so the last one wins
        }

        var commits = await GetCommitsAsync(pr, cancellationToken).ConfigureAwait(false);
        var canApply = SuggestionApplier.CanApply(viewer, Str(Get(node, "author"), "login"), Str(Get(data, "repository"), "viewerPermission"));
        return new ReviewContext(head, baseSha, mine, viewer, commits, Str(node, "body"), canApply);
    }

    public async Task<PullRequestDiff> GetPullRequestFilesAsync(ReviewTarget pr, CancellationToken cancellationToken = default)
    {
        var (owner, name) = Validate(pr);
        var filesTask = ReadFilesAsync($"{Repo(owner, name)}/pulls/{pr.Number.ToString(CultureInfo.InvariantCulture)}/files", paged: true, cancellationToken);
        var viewedTask = ReadViewedAsync(owner, name, pr.Number, cancellationToken);
        await Task.WhenAll(filesTask, viewedTask).ConfigureAwait(false);
        var files = await filesTask.ConfigureAwait(false);
        return new PullRequestDiff(string.Empty, files, await viewedTask.ConfigureAwait(false));
    }

    public async Task<PullRequestDiff> GetCompareFilesAsync(ReviewTarget pr, string baseSha, string headSha, CancellationToken cancellationToken = default)
    {
        var (owner, name) = Validate(pr);
        if (!GitHubNames.IsCommitSha(baseSha) || !GitHubNames.IsCommitSha(headSha))
            throw new ArgumentException("Invalid commit id.");
        var files = await ReadFilesAsync($"{Repo(owner, name)}/compare/{baseSha}...{headSha}", paged: false, cancellationToken).ConfigureAwait(false);
        var viewed = files.ToDictionary(f => f.Path, _ => ViewedState.Unviewed, StringComparer.Ordinal);
        return new PullRequestDiff(string.Empty, files, viewed);
    }

    private async Task<IReadOnlyList<DiffFile>> ReadFilesAsync(string basePath, bool paged, CancellationToken ct)
    {
        var result = new List<DiffFile>();
        for (var page = 1; page <= MaxFilePages; page++)
        {
            var url = paged
                ? $"{GitHubRest.ApiBase}{basePath}?per_page=100&page={page.ToString(CultureInfo.InvariantCulture)}"
                : $"{GitHubRest.ApiBase}{basePath}";
            var response = await _rest.GetAsync(url, null, ct).ConfigureAwait(false);
            var items = paged ? Items(response.Json, null) : Items(response.Json, "files");
            foreach (var item in items) result.Add(MapFile(item));
            if (!paged || items.Count < 100) break;
        }
        return result;
    }

    private static DiffFile MapFile(JsonElement item)
    {
        var path = Str(item, "filename");
        var previous = StrOrNull(item, "previous_filename");
        var status = Str(item, "status") switch
        {
            "added" => DiffFileStatus.Added,
            "removed" => DiffFileStatus.Deleted,
            "renamed" => DiffFileStatus.Renamed,
            _ => DiffFileStatus.Modified,
        };
        var additions = Int(item, "additions");
        var deletions = Int(item, "deletions");
        var changes = Int(item, "changes");
        var patch = StrOrNull(item, "patch");

        var hunks = (IReadOnlyList<DiffHunk>)Array.Empty<DiffHunk>();
        var tooLarge = false;
        var binary = false;
        if (!string.IsNullOrEmpty(patch))
        {
            // The path in the patch header is a placeholder: the real path never reaches the parser.
            var text = "diff --git a/x b/x\n--- a/x\n+++ b/x\n" + patch + "\n";
            var parsed = UnifiedDiffParser.Parse(text);
            if (parsed.Count > 0)
            {
                hunks = parsed[0].Hunks;
                tooLarge = parsed[0].IsTooLarge;
            }
        }
        else if (changes > 0 || additions > 0 || deletions > 0)
        {
            tooLarge = true; // GitHub leaves the patch out for very large diffs
        }
        else if (status != DiffFileStatus.Renamed && status != DiffFileStatus.Added && status != DiffFileStatus.Deleted)
        {
            binary = true;
        }
        else if (status != DiffFileStatus.Renamed)
        {
            binary = true; // an added or deleted file without any lines is empty or binary
        }

        return new DiffFile(path, status == DiffFileStatus.Renamed ? previous : null, status, binary, tooLarge, additions, deletions, hunks);
    }

    private const string ViewedQuery = """
        query($owner: String!, $name: String!, $number: Int!, $after: String) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              files(first: 100, after: $after) {
                nodes { path viewerViewedState }
                pageInfo { hasNextPage endCursor }
              }
            }
          }
        }
        """;

    private async Task<IReadOnlyDictionary<string, ViewedState>> ReadViewedAsync(string owner, string name, int number, CancellationToken ct)
    {
        var map = new Dictionary<string, ViewedState>(StringComparer.Ordinal);
        string? after = null;
        for (var page = 0; page < MaxFilePages; page++)
        {
            var vars = RepoVars(owner, name, number);
            vars["after"] = after;
            var data = await _graphql.QueryAsync(ViewedQuery, vars, false, ct).ConfigureAwait(false);
            var files = Get(Get(Get(data, "repository"), "pullRequest"), "files");
            foreach (var node in Items(files, "nodes"))
            {
                map[Str(node, "path")] = Str(node, "viewerViewedState") switch
                {
                    "VIEWED" => ViewedState.Viewed,
                    "DISMISSED" => ViewedState.Dismissed,
                    _ => ViewedState.Unviewed,
                };
            }
            var info = Get(files, "pageInfo");
            if (!Bool(info, "hasNextPage")) break;
            after = StrOrNull(info, "endCursor");
            if (after is null) break;
        }
        return map;
    }

    private const string ThreadsQuery = """
        query($owner: String!, $name: String!, $number: Int!, $after: String) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              reviewThreads(first: 100, after: $after) {
                nodes {
                  id path line startLine diffSide isResolved isOutdated
                  comments(first: 100) {
                    nodes { id body createdAt url viewerDidAuthor author { login } }
                  }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
          }
        }
        """;

    public async Task<IReadOnlyList<ReviewThread>> GetThreadsAsync(ReviewTarget pr, CancellationToken cancellationToken = default)
    {
        var (owner, name) = Validate(pr);
        var result = new List<ReviewThread>();
        string? after = null;
        for (var page = 0; page < 20; page++)
        {
            var vars = RepoVars(owner, name, pr.Number);
            vars["after"] = after;
            var data = await _graphql.QueryAsync(ThreadsQuery, vars, false, cancellationToken).ConfigureAwait(false);
            var threads = Get(Get(Get(data, "repository"), "pullRequest"), "reviewThreads");
            foreach (var node in Items(threads, "nodes"))
            {
                var comments = Items(Get(node, "comments"), "nodes").Select(MapComment).ToList();
                if (comments.Count == 0) continue;
                var side = Str(node, "diffSide") == "LEFT" ? DiffSide.Left : DiffSide.Right;
                result.Add(new ReviewThread(
                    Str(node, "id"), Str(node, "path"), IntOrNull(node, "line"), side, IntOrNull(node, "startLine"),
                    Bool(node, "isResolved"), Bool(node, "isOutdated"), comments));
            }
            var info = Get(threads, "pageInfo");
            if (!Bool(info, "hasNextPage")) break;
            after = StrOrNull(info, "endCursor");
            if (after is null) break;
        }
        return result;
    }

    private static ReviewComment MapComment(JsonElement node) =>
        new(
            Str(node, "id"),
            StrOrNull(Get(node, "author"), "login") ?? "ghost",
            Str(node, "body"),
            Time(node, "createdAt") ?? DateTimeOffset.UtcNow,
            Bool(node, "viewerDidAuthor"),
            StrOrNull(node, "url"));

    public async Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(ReviewTarget pr, CancellationToken cancellationToken = default)
    {
        var (owner, name) = Validate(pr);
        var result = new List<CommitInfo>();
        for (var page = 1; page <= 3; page++)
        {
            var url = $"{GitHubRest.ApiBase}{Repo(owner, name)}/pulls/{pr.Number.ToString(CultureInfo.InvariantCulture)}/commits?per_page=100&page={page.ToString(CultureInfo.InvariantCulture)}";
            var response = await _rest.GetAsync(url, null, cancellationToken).ConfigureAwait(false);
            var items = Items(response.Json, null);
            foreach (var item in items)
            {
                var commit = Get(item, "commit");
                var author = StrOrNull(Get(item, "author"), "login") ?? StrOrNull(Get(commit, "author"), "name") ?? "unknown";
                result.Add(new CommitInfo(
                    Str(item, "sha"), Str(commit, "message"), author,
                    Time(Get(commit, "author"), "date") ?? DateTimeOffset.UtcNow));
            }
            if (items.Count < 100) break;
        }
        return result;
    }

    public async Task<string?> GetFileContentAsync(ReviewTarget pr, string path, string sha, CancellationToken cancellationToken = default)
    {
        var (owner, name) = Validate(pr);
        if (!GitHubNames.IsCommitSha(sha) || string.IsNullOrEmpty(path)) return null;
        var escaped = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        var url = $"{GitHubRest.ApiBase}{Repo(owner, name)}/contents/{escaped}?ref={sha}";
        try
        {
            var response = await _rest.GetAsync(url, null, cancellationToken).ConfigureAwait(false);
            if (Str(response.Json, "encoding") != "base64") return null;
            var raw = Str(response.Json, "content").Replace("\n", string.Empty).Replace("\r", string.Empty);
            return Encoding.UTF8.GetString(Convert.FromBase64String(raw));
        }
        catch (GitHubApiException ex) when (ex.StatusCode == 404 || ex.StatusCode == 403)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ writes

    public async Task MarkFileViewedAsync(ReviewTarget pr, string path, CancellationToken cancellationToken = default) =>
        await MutateAsync(
            "mutation($input: MarkFileAsViewedInput!) { markFileAsViewed(input: $input) { clientMutationId } }",
            Input(("pullRequestId", PrId(pr)), ("path", path)), cancellationToken).ConfigureAwait(false);

    public async Task UnmarkFileViewedAsync(ReviewTarget pr, string path, CancellationToken cancellationToken = default) =>
        await MutateAsync(
            "mutation($input: UnmarkFileAsViewedInput!) { unmarkFileAsViewed(input: $input) { clientMutationId } }",
            Input(("pullRequestId", PrId(pr)), ("path", path)), cancellationToken).ConfigureAwait(false);

    public async Task<string> StartReviewAsync(ReviewTarget pr, CancellationToken cancellationToken = default)
    {
        var root = await MutateAsync(
            "mutation($input: AddPullRequestReviewInput!) { addPullRequestReview(input: $input) { pullRequestReview { id } } }",
            Input(("pullRequestId", PrId(pr))), cancellationToken).ConfigureAwait(false);
        return Str(Get(Get(Get(root, "data"), "addPullRequestReview"), "pullRequestReview"), "id") is { Length: > 0 } id ? id : "dry-run-review";
    }

    public async Task<string> AddPendingCommentAsync(ReviewTarget pr, string reviewId, PendingComment comment, CancellationToken cancellationToken = default)
    {
        CheckId(reviewId);
        var input = Input(("pullRequestId", PrId(pr)), ("pullRequestReviewId", reviewId), ("path", comment.Path), ("body", comment.Body),
            ("line", comment.Line), ("side", SideName(comment.Side)));
        if (comment.IsRange)
        {
            input["startLine"] = comment.StartLine;
            input["startSide"] = SideName(comment.Side);
        }
        var root = await MutateAsync(
            "mutation($input: AddPullRequestReviewThreadInput!) { addPullRequestReviewThread(input: $input) { thread { id } } }",
            input, cancellationToken).ConfigureAwait(false);
        return Str(Get(Get(Get(root, "data"), "addPullRequestReviewThread"), "thread"), "id") is { Length: > 0 } id ? id : "dry-run-thread";
    }

    public async Task SubmitReviewAsync(
        ReviewTarget pr,
        ReviewVerdict verdict,
        string body,
        IReadOnlyList<PendingComment> comments,
        string? reviewId = null,
        CancellationToken cancellationToken = default)
    {
        var ev = verdict switch
        {
            ReviewVerdict.Approve => "APPROVE",
            ReviewVerdict.RequestChanges => "REQUEST_CHANGES",
            _ => "COMMENT",
        };

        if (reviewId is not null)
        {
            CheckId(reviewId);
            await MutateAsync(
                "mutation($input: SubmitPullRequestReviewInput!) { submitPullRequestReview(input: $input) { pullRequestReview { id } } }",
                Input(("pullRequestReviewId", reviewId), ("event", ev), ("body", body)), cancellationToken).ConfigureAwait(false);
            return;
        }

        var threads = new List<Dictionary<string, object?>>();
        foreach (var c in comments)
        {
            var thread = new Dictionary<string, object?>
            {
                ["path"] = c.Path,
                ["body"] = c.Body,
                ["line"] = c.Line,
                ["side"] = SideName(c.Side),
            };
            if (c.IsRange)
            {
                thread["startLine"] = c.StartLine;
                thread["startSide"] = SideName(c.Side);
            }
            threads.Add(thread);
        }

        var input = Input(("pullRequestId", PrId(pr)), ("event", ev), ("body", body));
        if (threads.Count > 0) input["threads"] = threads;
        await MutateAsync(
            "mutation($input: AddPullRequestReviewInput!) { addPullRequestReview(input: $input) { pullRequestReview { id } } }",
            input, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReviewComment> ReplyAsync(ReviewTarget pr, string threadId, string body, CancellationToken cancellationToken = default)
    {
        CheckId(threadId);
        var root = await MutateAsync(
            "mutation($input: AddPullRequestReviewThreadReplyInput!) { addPullRequestReviewThreadReply(input: $input) { comment { id body createdAt url viewerDidAuthor author { login } } } }",
            Input(("pullRequestReviewThreadId", threadId), ("body", body)), cancellationToken).ConfigureAwait(false);
        var node = Get(Get(Get(root, "data"), "addPullRequestReviewThreadReply"), "comment");
        return node is { } n && Str(n, "id").Length > 0
            ? MapComment(n) with { IsMine = true }
            : new ReviewComment("dry-run-" + Guid.NewGuid().ToString("N"), "you", body, DateTimeOffset.UtcNow, true);
    }

    public async Task ResolveThreadAsync(ReviewTarget pr, string threadId, CancellationToken cancellationToken = default)
    {
        CheckId(threadId);
        await MutateAsync(
            "mutation($input: ResolveReviewThreadInput!) { resolveReviewThread(input: $input) { thread { id } } }",
            Input(("threadId", threadId)), cancellationToken).ConfigureAwait(false);
    }

    public async Task UnresolveThreadAsync(ReviewTarget pr, string threadId, CancellationToken cancellationToken = default)
    {
        CheckId(threadId);
        await MutateAsync(
            "mutation($input: UnresolveReviewThreadInput!) { unresolveReviewThread(input: $input) { thread { id } } }",
            Input(("threadId", threadId)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReviewComment> EditCommentAsync(ReviewTarget pr, string commentId, string body, CancellationToken cancellationToken = default)
    {
        CheckId(commentId);
        var root = await MutateAsync(
            "mutation($input: UpdatePullRequestReviewCommentInput!) { updatePullRequestReviewComment(input: $input) { pullRequestReviewComment { id body createdAt url viewerDidAuthor author { login } } } }",
            Input(("pullRequestReviewCommentId", commentId), ("body", body)), cancellationToken).ConfigureAwait(false);
        var node = Get(Get(Get(root, "data"), "updatePullRequestReviewComment"), "pullRequestReviewComment");
        return node is { } n && Str(n, "id").Length > 0
            ? MapComment(n) with { IsMine = true }
            : new ReviewComment(commentId, "you", body, DateTimeOffset.UtcNow, true);
    }

    public async Task DeleteCommentAsync(ReviewTarget pr, string commentId, CancellationToken cancellationToken = default)
    {
        CheckId(commentId);
        await MutateAsync(
            "mutation($input: DeletePullRequestReviewCommentInput!) { deletePullRequestReviewComment(input: $input) { clientMutationId } }",
            Input(("id", commentId)), cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ apply suggestion

    public bool IsDryRun => DryRun;

    private sealed record PreparedSuggestion(SuggestionPlan Plan, string? Repo, string? Branch, string? BlobSha);

    /// <summary>Reads the PR head and the file (harmless GETs, also in a dry run) and plans the change.</summary>
    private async Task<PreparedSuggestion> PrepareSuggestionAsync(ReviewTarget pr, SuggestionRequest request, CancellationToken ct)
    {
        Validate(pr);
        PreparedSuggestion Fail(string message) => new(SuggestionPlan.Fail(request.Path, message), null, null, null);

        if (!SuggestionCommits.IsSafePath(request.Path)) return Fail("This file path cannot be changed from here.");
        if (!GitHubNames.IsCommitSha(request.ExpectedHeadSha)) return Fail("The review has no commit to apply the suggestion to. Reload the review.");

        var headResponse = await _rest.GetAsync(GitHubActions.FetchHeadShaRequest(pr.Repo, pr.Number).Url, null, ct).ConfigureAwait(false);
        var head = SuggestionCommits.ParseHead(headResponse.Json);
        if (head is null) return Fail("Could not read the branch of the pull request.");
        if (!string.Equals(head.Sha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            return Fail("The branch has new commits since this review was loaded. Reload the review, then apply the suggestion again.");
        if (string.IsNullOrEmpty(head.RepoFullName) || !GitHubNames.TrySplitRepo(head.RepoFullName, out _, out _))
            return Fail("The repository of the pull request branch is not available.");

        RestResponse file;
        try
        {
            var read = SuggestionCommits.ReadFileRequest(head.RepoFullName, request.Path, head.Sha);
            file = await _rest.GetAsync(read.Url, null, ct).ConfigureAwait(false);
        }
        catch (GitHubApiException ex) when (ex.StatusCode == 404 || ex.StatusCode == 403)
        {
            return Fail("Could not read " + request.Path + " on the pull request branch.");
        }

        var parsed = SuggestionCommits.ParseFile(file.Json);
        if (parsed is null) return Fail("Could not read the text of " + request.Path + ".");

        var plan = SuggestionApplier.Plan(request.Path, parsed.Value.Text, request.StartLine, request.EndLine, request.CommentBody);
        return new PreparedSuggestion(plan, head.RepoFullName, head.Ref, parsed.Value.BlobSha);
    }

    public async Task<SuggestionPlan> PreviewSuggestionAsync(ReviewTarget pr, SuggestionRequest request, CancellationToken cancellationToken = default) =>
        (await PrepareSuggestionAsync(pr, request, cancellationToken).ConfigureAwait(false)).Plan;

    public async Task<SuggestionOutcome> ApplySuggestionAsync(ReviewTarget pr, SuggestionRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareSuggestionAsync(pr, request, cancellationToken).ConfigureAwait(false);
        var plan = prepared.Plan;
        if (!plan.Ok) return new SuggestionOutcome(false, DryRun, plan.Error, plan);
        if (plan.IsNoChange) return new SuggestionOutcome(false, DryRun, "The suggestion matches the current code, so there is nothing to commit.", plan);

        var commit = SuggestionCommits.CommitFileRequest(prepared.Repo!, request.Path, prepared.Branch!, plan.CommitMessage, plan.NewContent, prepared.BlobSha!);
        var result = await _writer.SendAsync(commit, DryRun, cancellationToken).ConfigureAwait(false);
        return result.Sent
            ? new SuggestionOutcome(true, false, "Committed the suggestion to " + prepared.Branch + ". Reload the review to see the new diff.", plan)
            : new SuggestionOutcome(false, true, "Dry run: nothing was written. Turn on \"Allow writes to GitHub\" in Settings to commit this change.", plan);
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Sends a GraphQL mutation. Returns the response root, or null for a dry run.</summary>
    private async Task<JsonElement?> MutateAsync(string query, Dictionary<string, object?> input, CancellationToken ct)
    {
        var vars = new Dictionary<string, object?> { ["input"] = input };
        var request = new WriteRequest(HttpMethod.Post, GitHubGraphQl.Endpoint, PrQueries.BuildBody(query, vars));
        var result = await _writer.SendAsync(request, DryRun, ct).ConfigureAwait(false);
        return result.Sent ? result.Response : null;
    }

    private static Dictionary<string, object?> Input(params (string Key, object? Value)[] pairs)
    {
        var map = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs) map[key] = value;
        return map;
    }

    private static string SideName(DiffSide side) => side == DiffSide.Left ? "LEFT" : "RIGHT";

    private static (string Owner, string Name) Validate(ReviewTarget pr)
    {
        GitHubNames.CheckNumber(pr.Number);
        return GitHubNames.SplitRepo(pr.Repo);
    }

    private static string PrId(ReviewTarget pr)
    {
        CheckId(pr.Id);
        return pr.Id;
    }

    private static void CheckId(string id)
    {
        if (!GitHubNames.IsNodeId(id)) throw new ArgumentException("Invalid GitHub id.");
    }

    private static string Repo(string owner, string name) => "/repos/" + owner + "/" + name;

    private static Dictionary<string, object?> RepoVars(string owner, string name, int number) =>
        new() { ["owner"] = owner, ["name"] = name, ["number"] = number };

    // Small JSON readers; the Core helper is internal.
    private static JsonElement? Get(JsonElement? e, string name)
    {
        if (e is not { ValueKind: JsonValueKind.Object } obj) return null;
        return obj.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
    }

    private static string? StrOrNull(JsonElement? e, string name) =>
        Get(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static string Str(JsonElement? e, string name) => StrOrNull(e, name) ?? string.Empty;

    private static int? IntOrNull(JsonElement? e, string name) =>
        Get(e, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;

    private static int Int(JsonElement? e, string name) => IntOrNull(e, name) ?? 0;

    private static bool Bool(JsonElement? e, string name) => Get(e, name) is { ValueKind: JsonValueKind.True };

    private static DateTimeOffset? Time(JsonElement? e, string name) =>
        DateTimeOffset.TryParse(StrOrNull(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    /// <summary>The elements of an array: either <paramref name="e"/> itself (name null) or its named property.</summary>
    private static List<JsonElement> Items(JsonElement? e, string? name)
    {
        var target = name is null ? e : Get(e, name);
        var list = new List<JsonElement>();
        if (target is { ValueKind: JsonValueKind.Array } array)
            foreach (var item in array.EnumerateArray()) list.Add(item);
        return list;
    }
}
