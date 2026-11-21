using System.Globalization;
using System.Text;
using Cshubben.Core.Data;
using Cshubben.Core.Features.Review;

namespace Cshubben.Services;

/// <summary>
/// A realistic, fictional pull request for demos and CI screenshots: C# and TypeScript changes, a rename, a deleted file,
/// a lockfile, four threads (one resolved, one outdated, one with a suggestion) and a commit list with a "last reviewed" mark.
/// Writes only change this object in memory.
/// </summary>
public sealed class DemoReviewData : IReviewData
{
    private const string Controller = "src/Payments/PaymentsController.cs";
    private const string Store = "src/Payments/Idempotency/IdempotencyStore.cs";
    private const string RetryOld = "src/Payments/Retry.cs";
    private const string Retry = "src/Payments/RetryPolicy.cs";
    private const string Legacy = "src/Legacy/ChargeRetryJob.cs";
    private const string Client = "web/src/api/payments.ts";
    private const string Lock = "package-lock.json";

    private static readonly DateTimeOffset Base = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private static readonly string[] Shas =
    {
        "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
        "b2c3d4e5f60718293a4b5c6d7e8f901234567890",
        "c3d4e5f60718293a4b5c6d7e8f90123456789012",
        "d4e5f60718293a4b5c6d7e8f9012345678901234",
        "e5f60718293a4b5c6d7e8f901234567890123456",
    };

    private readonly HashSet<string> _viewed = new(StringComparer.Ordinal) { Retry, Store, Legacy };
    private readonly List<ReviewThread> _threads;
    private readonly IReadOnlyList<DiffFile> _files;
    private int _counter;

    public DemoReviewData()
    {
        var text = BuildDiffText();
        DiffText = text;
        _files = UnifiedDiffParser.Parse(text);
        _threads = BuildThreads(_files);
    }

    /// <summary>The unified diff text of the demo pull request.</summary>
    public string DiffText { get; }

    public Task<ReviewContext> GetContextAsync(ReviewTarget pr, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReviewContext(Shas[4], "9f8e7d6c5b4a39281706f5e4d3c2b1a098765432", Shas[1], DemoData.Viewer, Commits(),
            "Adds `Idempotency-Key` handling to the payment endpoints.\n\n- charges require the header, refunds fall back to a random key\n- responses are cached in Redis for 24 hours\n- the retry loop only retries transient HTTP failures now"));

    public Task<PullRequestDiff> GetPullRequestFilesAsync(ReviewTarget pr, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PullRequestDiff(DiffText, _files, ViewedStates(_files)));

    public Task<PullRequestDiff> GetCompareFilesAsync(ReviewTarget pr, string baseSha, string headSha, CancellationToken cancellationToken = default)
    {
        // The follow-up commits touched the controller, the retry policy and the TypeScript client.
        var touched = _files.Where(f => f.Path == Controller || f.Path == Retry || f.Path == Client).ToList();
        return Task.FromResult(new PullRequestDiff(string.Empty, touched, ViewedStates(touched)));
    }

    public Task<IReadOnlyList<ReviewThread>> GetThreadsAsync(ReviewTarget pr, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReviewThread>>(_threads.ToList());

    public Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(ReviewTarget pr, CancellationToken cancellationToken = default) =>
        Task.FromResult(Commits());

    public Task<string?> GetFileContentAsync(ReviewTarget pr, string path, string sha, CancellationToken cancellationToken = default)
    {
        var file = _files.FirstOrDefault(f => f.Path == path);
        if (file is null || file.Status == DiffFileStatus.Deleted) return Task.FromResult<string?>(null);
        var known = new Dictionary<int, string>();
        foreach (var hunk in file.Hunks)
            foreach (var line in hunk.Lines)
                if (line.NewNumber is int n && line.Kind != DiffLineKind.Remove) known[n] = line.Text;
        var last = known.Count == 0 ? 0 : known.Keys.Max() + 14;
        var filler = path.EndsWith(".ts", StringComparison.Ordinal) ? "  // unchanged code" : "        // unchanged code";
        var sb = new StringBuilder();
        for (var i = 1; i <= last; i++)
            sb.Append(known.TryGetValue(i, out var text) ? text : filler).Append('\n');
        return Task.FromResult<string?>(sb.ToString());
    }

    public Task MarkFileViewedAsync(ReviewTarget pr, string path, CancellationToken cancellationToken = default)
    {
        _viewed.Add(path);
        return Task.CompletedTask;
    }

    public Task UnmarkFileViewedAsync(ReviewTarget pr, string path, CancellationToken cancellationToken = default)
    {
        _viewed.Remove(path);
        return Task.CompletedTask;
    }

    public Task<string> StartReviewAsync(ReviewTarget pr, CancellationToken cancellationToken = default) =>
        Task.FromResult("demo-review");

    public Task<string> AddPendingCommentAsync(ReviewTarget pr, string reviewId, PendingComment comment, CancellationToken cancellationToken = default) =>
        Task.FromResult("demo-thread-" + NextId());

    public Task SubmitReviewAsync(
        ReviewTarget pr,
        ReviewVerdict verdict,
        string body,
        IReadOnlyList<PendingComment> comments,
        string? reviewId = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var c in comments)
        {
            var comment = new ReviewComment("demo-c-" + NextId(), DemoData.Viewer, c.Body, DateTimeOffset.UtcNow, true);
            _threads.Add(new ReviewThread("demo-t-" + NextId(), c.Path, c.Line, c.Side, c.StartLine, false, false, new[] { comment }));
        }
        return Task.CompletedTask;
    }

    public Task<ReviewComment> ReplyAsync(ReviewTarget pr, string threadId, string body, CancellationToken cancellationToken = default)
    {
        var comment = new ReviewComment("demo-c-" + NextId(), DemoData.Viewer, body, DateTimeOffset.UtcNow, true);
        var index = _threads.FindIndex(t => t.Id == threadId);
        if (index >= 0) _threads[index] = _threads[index].WithComment(comment);
        return Task.FromResult(comment);
    }

    public Task ResolveThreadAsync(ReviewTarget pr, string threadId, CancellationToken cancellationToken = default) =>
        SetResolved(threadId, true);

    public Task UnresolveThreadAsync(ReviewTarget pr, string threadId, CancellationToken cancellationToken = default) =>
        SetResolved(threadId, false);

    public Task<ReviewComment> EditCommentAsync(ReviewTarget pr, string commentId, string body, CancellationToken cancellationToken = default)
    {
        var edited = new ReviewComment(commentId, DemoData.Viewer, body, DateTimeOffset.UtcNow, true);
        for (var i = 0; i < _threads.Count; i++)
            if (_threads[i].Comments.Any(c => c.Id == commentId)) _threads[i] = _threads[i].ReplaceComment(commentId, edited);
        return Task.FromResult(edited);
    }

    public Task DeleteCommentAsync(ReviewTarget pr, string commentId, CancellationToken cancellationToken = default)
    {
        for (var i = _threads.Count - 1; i >= 0; i--)
        {
            if (!_threads[i].Comments.Any(c => c.Id == commentId)) continue;
            var changed = _threads[i].WithoutComment(commentId);
            if (changed is null) _threads.RemoveAt(i);
            else _threads[i] = changed;
        }
        return Task.CompletedTask;
    }

    private Task SetResolved(string threadId, bool resolved)
    {
        var index = _threads.FindIndex(t => t.Id == threadId);
        if (index >= 0) _threads[index] = _threads[index].WithResolved(resolved);
        return Task.CompletedTask;
    }

    private string NextId() => (++_counter).ToString(CultureInfo.InvariantCulture);

    private IReadOnlyDictionary<string, ViewedState> ViewedStates(IReadOnlyList<DiffFile> files) =>
        files.ToDictionary(f => f.Path, f => _viewed.Contains(f.Path) ? ViewedState.Viewed : ViewedState.Unviewed, StringComparer.Ordinal);

    private static IReadOnlyList<CommitInfo> Commits() => new[]
    {
        new CommitInfo(Shas[0], "Add IdempotencyStore with Redis backing", "alex", Base.AddHours(-30)),
        new CommitInfo(Shas[1], "Require Idempotency-Key on POST /payments/charges", "alex", Base.AddHours(-28)),
        new CommitInfo(Shas[2], "Address review: include the customer id in the scope", "alex", Base.AddHours(-6)),
        new CommitInfo(Shas[3], "Narrow the retry to transient HTTP failures and add jitter", "alex", Base.AddHours(-3)),
        new CommitInfo(Shas[4], "web: send Idempotency-Key from the payments client", "alex", Base.AddHours(-1)),
    };

    // ------------------------------------------------------------------ threads

    private static List<ReviewThread> BuildThreads(IReadOnlyList<DiffFile> files)
    {
        int? LineOf(string path, string contains)
        {
            var file = files.First(f => f.Path == path);
            foreach (var hunk in file.Hunks)
                foreach (var line in hunk.Lines)
                    if (line.Kind != DiffLineKind.Remove && line.Text.Contains(contains, StringComparison.Ordinal)) return line.NewNumber;
            return null;
        }

        var threads = new List<ReviewThread>
        {
            new("demo-t1", Controller, LineOf(Controller, "if (string.IsNullOrWhiteSpace(idempotencyKey))"), DiffSide.Right, null, false, false, new[]
            {
                new ReviewComment("demo-c1", "maria", "Should a missing key really be a `400`? Most clients will not send one on day one, so this breaks every existing integration.", Base.AddHours(-5)),
                new ReviewComment("demo-c2", DemoData.Viewer, "For charges yes, they move money. Refunds fall back to a random key for now. I will add a rollout flag in a follow-up.", Base.AddHours(-4), true),
            }),
            new("demo-t2", Controller, LineOf(Controller, "var scope = $\"charge:"), DiffSide.Right, null, true, false, new[]
            {
                new ReviewComment("demo-c3", "jonas", "The scope has to include the customer id, otherwise two customers can collide on the same key.", Base.AddHours(-12)),
                new ReviewComment("demo-c4", DemoData.Viewer, "Good catch, added in the next commit.", Base.AddHours(-6), true),
            }),
            new("demo-t3", Retry, null, DiffSide.Right, null, false, true, new[]
            {
                new ReviewComment("demo-c5", "priya", "Retrying every `Exception` hides real bugs. Can we narrow it to transient failures?", Base.AddHours(-20)),
            }),
            new("demo-t4", Client, LineOf(Client, "headers: { \"Idempotency-Key\": key }"), DiffSide.Right, null, false, false, new[]
            {
                new ReviewComment("demo-c6", "maria", "Generate the key once per user action, not per call. A double click should replay the first charge.\n\n```suggestion\nexport async function charge(input: ChargeInput, key: string) {\n```", Base.AddHours(-2)),
            }),
        };
        return threads;
    }

    // ------------------------------------------------------------------ the diff

    private static string BuildDiffText()
    {
        var sb = new StringBuilder();

        Patch(sb, Controller, null, DiffFileStatus.Modified, new[]
        {
            Hunk(1, "namespace Acme.Api.Payments;",
                " using Microsoft.AspNetCore.Mvc;",
                "+using Acme.Api.Payments.Idempotency;",
                " ",
                " namespace Acme.Api.Payments;",
                " ",
                " [ApiController]"),
            Hunk(12, "public sealed class PaymentsController : ControllerBase",
                " public sealed class PaymentsController : ControllerBase",
                " {",
                "     private readonly IPaymentService _payments;",
                "+    private readonly IIdempotencyStore _idempotency;",
                " ",
                "-    public PaymentsController(IPaymentService payments)",
                "+    public PaymentsController(IPaymentService payments, IIdempotencyStore idempotency)",
                "     {",
                "         _payments = payments;",
                "+        _idempotency = idempotency;",
                "     }"),
            Hunk(30, "public async Task<IActionResult> Charge",
                "     [HttpPost(\"charges\")]",
                "-    public async Task<IActionResult> Charge(ChargeRequest request, CancellationToken ct)",
                "+    public async Task<IActionResult> Charge(",
                "+        [FromHeader(Name = \"Idempotency-Key\")] string? idempotencyKey,",
                "+        ChargeRequest request,",
                "+        CancellationToken ct)",
                "     {",
                "-        var receipt = await _payments.ChargeAsync(request, ct);",
                "-        return Ok(receipt);",
                "+        if (string.IsNullOrWhiteSpace(idempotencyKey))",
                "+        {",
                "+            return BadRequest(new { error = \"Idempotency-Key header is required.\" });",
                "+        }",
                "+",
                "+        var scope = $\"charge:{request.CustomerId}:{idempotencyKey}\";",
                "+        var cached = await _idempotency.TryGetAsync(scope, ct);",
                "+        if (cached is not null)",
                "+        {",
                "+            Response.Headers[\"Idempotent-Replay\"] = \"true\";",
                "+            return StatusCode(cached.StatusCode, cached.Body);",
                "+        }",
                "+",
                "+        var receipt = await _payments.ChargeAsync(request, ct);",
                "+        await _idempotency.SaveAsync(scope, new StoredResponse(StatusCodes.Status200OK, receipt), TimeSpan.FromHours(24), ct);",
                "+        return Ok(receipt);",
                "     }",
                " ",
                "     [HttpPost(\"refunds\")]",
                "-    public async Task<IActionResult> Refund(RefundRequest request, CancellationToken ct)",
                "+    public async Task<IActionResult> Refund(",
                "+        [FromHeader(Name = \"Idempotency-Key\")] string? idempotencyKey,",
                "+        RefundRequest request,",
                "+        CancellationToken ct)",
                "     {",
                "-        return Ok(await _payments.RefundAsync(request, ct));",
                "+        var scope = $\"refund:{request.ChargeId}:{idempotencyKey ?? Guid.NewGuid().ToString(\"N\")}\";",
                "+        var cached = await _idempotency.TryGetAsync(scope, ct);",
                "+        return cached is not null",
                "+            ? StatusCode(cached.StatusCode, cached.Body)",
                "+            : Ok(await _payments.RefundAsync(request, ct));",
                "     }"),
        });

        Patch(sb, Store, null, DiffFileStatus.Added, new[]
        {
            Hunk(0, string.Empty,
                "+using System.Text.Json;",
                "+using StackExchange.Redis;",
                "+",
                "+namespace Acme.Api.Payments.Idempotency;",
                "+",
                "+public sealed record StoredResponse(int StatusCode, object? Body);",
                "+",
                "+public interface IIdempotencyStore",
                "+{",
                "+    Task<StoredResponse?> TryGetAsync(string scope, CancellationToken ct);",
                "+",
                "+    Task SaveAsync(string scope, StoredResponse response, TimeSpan ttl, CancellationToken ct);",
                "+}",
                "+",
                "+/// <summary>Keeps responses in Redis so a retried request gets the original answer back.</summary>",
                "+public sealed class RedisIdempotencyStore : IIdempotencyStore",
                "+{",
                "+    private readonly IDatabase _redis;",
                "+",
                "+    public RedisIdempotencyStore(IConnectionMultiplexer connection)",
                "+    {",
                "+        _redis = connection.GetDatabase();",
                "+    }",
                "+",
                "+    public async Task<StoredResponse?> TryGetAsync(string scope, CancellationToken ct)",
                "+    {",
                "+        var value = await _redis.StringGetAsync(Key(scope));",
                "+        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<StoredResponse>((string)value!);",
                "+    }",
                "+",
                "+    public Task SaveAsync(string scope, StoredResponse response, TimeSpan ttl, CancellationToken ct)",
                "+    {",
                "+        var json = JsonSerializer.Serialize(response);",
                "+        return _redis.StringSetAsync(Key(scope), json, ttl, When.NotExists);",
                "+    }",
                "+",
                "+    private static string Key(string scope) => $\"idem:{scope}\";",
                "+}"),
        });

        Patch(sb, Retry, RetryOld, DiffFileStatus.Renamed, new[]
        {
            Hunk(3, "public static class RetryPolicy",
                " public static class RetryPolicy",
                " {",
                "-    public static async Task<T> RunAsync<T>(Func<Task<T>> action, int attempts = 3)",
                "+    public static async Task<T> RunAsync<T>(Func<Task<T>> action, int attempts = 4)",
                "     {",
                "         for (var attempt = 1; ; attempt++)",
                "         {",
                "             try",
                "             {",
                "                 return await action();",
                "             }",
                "-            catch (Exception) when (attempt < attempts)",
                "+            catch (HttpRequestException) when (attempt < attempts)",
                "             {",
                "-                await Task.Delay(100 * attempt);",
                "+                var jitter = Random.Shared.Next(0, 50);",
                "+                await Task.Delay((100 * attempt) + jitter);",
                "             }",
                "         }",
                "     }"),
        });

        Patch(sb, Legacy, null, DiffFileStatus.Deleted, new[]
        {
            Hunk(1, string.Empty,
                "-namespace Acme.Api.Legacy;",
                "-",
                "-// Replaced by RetryPolicy and the idempotency store.",
                "-public sealed class ChargeRetryJob",
                "-{",
                "-    public async Task RunAsync(IPaymentService payments, ChargeRequest request)",
                "-    {",
                "-        for (var i = 0; i < 3; i++)",
                "-        {",
                "-            try",
                "-            {",
                "-                await payments.ChargeAsync(request, CancellationToken.None);",
                "-                return;",
                "-            }",
                "-            catch (Exception)",
                "-            {",
                "-                await Task.Delay(500);",
                "-            }",
                "-        }",
                "-    }",
                "-}"),
        });

        Patch(sb, Client, null, DiffFileStatus.Modified, new[]
        {
            Hunk(1, string.Empty,
                " /* Payments API client.",
                "  * Keys are generated once per user action,",
                "  * never per network attempt. */",
                " import { http } from \"./http\";",
                "+import { newIdempotencyKey } from \"./idempotency\";",
                " ",
                " const base = `/payments/${apiVersion}`;",
                " export interface ChargeInput {",
                "   customerId: string;",
                "   amountCents: number;",
                " }",
                " ",
                "-export async function charge(input: ChargeInput) {",
                "-  const response = await http.post(\"/payments/charges\", input);",
                "-  return response.data;",
                "+export async function charge(input: ChargeInput, key = newIdempotencyKey()) {",
                "+  const response = await http.post(\"/payments/charges\", input, {",
                "+    headers: { \"Idempotency-Key\": key },",
                "+  });",
                "+  return response.data as Receipt;",
                " }",
                " ",
                "-export async function refund(chargeId: string) {",
                "-  const response = await http.post(\"/payments/refunds\", { chargeId });",
                "-  return response.data;",
                "+export async function refund(chargeId: string, key = newIdempotencyKey()) {",
                "+  const response = await http.post(",
                "+    \"/payments/refunds\",",
                "+    { chargeId },",
                "+    { headers: { \"Idempotency-Key\": key } },",
                "+  );",
                "+  return response.data as Receipt;",
                " }"),
        });

        Patch(sb, Lock, null, DiffFileStatus.Modified, new[]
        {
            Hunk(3412, "\"node_modules/uuid\": {",
                "     \"node_modules/uuid\": {",
                "-      \"version\": \"9.0.0\",",
                "-      \"resolved\": \"https://registry.npmjs.org/uuid/-/uuid-9.0.0.tgz\",",
                "-      \"integrity\": \"sha512-MockHashForDemoPurposesOnlyAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==\",",
                "+      \"version\": \"9.0.1\",",
                "+      \"resolved\": \"https://registry.npmjs.org/uuid/-/uuid-9.0.1.tgz\",",
                "+      \"integrity\": \"sha512-MockHashForDemoPurposesOnlyBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB==\",",
                "       \"funding\": [",
                "         \"https://github.com/sponsors/broofa\",",
                "         \"https://github.com/sponsors/ctavan\"",
                "       ],"),
            Hunk(5210, "\"node_modules/zod\": {",
                "     \"node_modules/zod\": {",
                "-      \"version\": \"3.22.2\",",
                "-      \"resolved\": \"https://registry.npmjs.org/zod/-/zod-3.22.2.tgz\",",
                "+      \"version\": \"3.22.4\",",
                "+      \"resolved\": \"https://registry.npmjs.org/zod/-/zod-3.22.4.tgz\",",
                "       \"funding\": {",
                "         \"url\": \"https://github.com/sponsors/colinhacks\"",
                "       }"),
        });

        return sb.ToString();
    }

    /// <summary>One hunk: lines start with ' ', '+' or '-'. Numbers in the header are computed from the lines.</summary>
    private sealed record HunkSpec(int OldStart, string Context, string[] Lines);

    private static HunkSpec Hunk(int oldStart, string context, params string[] lines) => new(oldStart, context, lines);

    private static void Patch(StringBuilder sb, string path, string? oldPath, DiffFileStatus status, HunkSpec[] hunks)
    {
        var from = oldPath ?? path;
        sb.Append("diff --git a/").Append(from).Append(" b/").Append(path).Append('\n');
        if (status == DiffFileStatus.Added) sb.Append("new file mode 100644\n");
        if (status == DiffFileStatus.Deleted) sb.Append("deleted file mode 100644\n");
        if (status == DiffFileStatus.Renamed)
            sb.Append("similarity index 82%\nrename from ").Append(from).Append("\nrename to ").Append(path).Append('\n');
        sb.Append("index 1111111..2222222 100644\n");
        sb.Append("--- ").Append(status == DiffFileStatus.Added ? "/dev/null" : "a/" + from).Append('\n');
        sb.Append("+++ ").Append(status == DiffFileStatus.Deleted ? "/dev/null" : "b/" + path).Append('\n');

        var delta = 0;
        foreach (var hunk in hunks)
        {
            var oldCount = hunk.Lines.Count(l => l.Length > 0 && (l[0] == ' ' || l[0] == '-'));
            var newCount = hunk.Lines.Count(l => l.Length > 0 && (l[0] == ' ' || l[0] == '+'));
            var oldStart = hunk.OldStart;
            var newStart = oldStart + delta;
            if (status == DiffFileStatus.Added)
            {
                oldStart = 0;
                newStart = 1;
            }
            else if (status == DiffFileStatus.Deleted)
            {
                newStart = 0;
            }
            sb.Append("@@ -").Append(oldStart).Append(',').Append(oldCount)
              .Append(" +").Append(newStart).Append(',').Append(newCount).Append(" @@");
            if (hunk.Context.Length > 0) sb.Append(' ').Append(hunk.Context);
            sb.Append('\n');
            foreach (var line in hunk.Lines) sb.Append(line).Append('\n');
            delta += newCount - oldCount;
        }
    }
}
