using System.Net;
using System.Text;
using Cshubben.Core.Integrations.Claude;
using Cshubben.Core.Model;

namespace Cshubben.Core.Tests.Integrations;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessRequest, ProcessResult> _handler;

    public FakeProcessRunner(ProcessResult result) : this(_ => result)
    {
    }

    public FakeProcessRunner(Func<ProcessRequest, ProcessResult> handler)
    {
        _handler = handler;
    }

    public List<ProcessRequest> Calls { get; } = new();

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(request);
        return Task.FromResult(_handler(request));
    }
}

internal sealed class FakeFileSystem : ILocatorFileSystem
{
    private readonly HashSet<string> _files;

    public FakeFileSystem(params string[] files)
    {
        _files = new HashSet<string>(files);
    }

    public bool FileExists(string path) => _files.Contains(path);

    public IReadOnlyList<string> Directories(string path)
    {
        var prefix = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var dirs = new List<string>();
        foreach (var file in _files)
        {
            if (!file.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = file.Substring(prefix.Length);
            var slash = rest.IndexOf(Path.DirectorySeparatorChar);
            if (slash <= 0) continue;
            var dir = prefix + rest.Substring(0, slash);
            if (!dirs.Contains(dir)) dirs.Add(dir);
        }

        return dirs;
    }
}

internal sealed record RecordedRequest(string Body, string? Authorization, string Url);

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public FakeHandler Enqueue(HttpStatusCode status, string json, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        string? authorization = null;
        if (request.Headers.TryGetValues("Authorization", out var values)) authorization = values.FirstOrDefault();
        Requests.Add(new RecordedRequest(body, authorization, request.RequestUri?.ToString() ?? ""));
        if (_responses.Count == 0) throw new InvalidOperationException("No response was queued.");
        return _responses.Dequeue()();
    }
}

internal static class Fx
{
    public static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    public static PullRequest Pr(string title = "Fix login", string branch = "feat/login", int number = 1, string description = "") =>
        new("id" + number, PrRole.Authored, "acme/api", number, title, "https://github.com/acme/api/pull/" + number, "alex",
            false, T, T, 10, 2, 3, ReviewDecision.None, CiState.Success, MergeState.Mergeable, 0, 0, branch, description);

    public static string IssueJson(string id, string identifier, string stateName, string stateType, string completedAt = "null") =>
        "{\"id\":\"" + id + "\",\"identifier\":\"" + identifier + "\",\"title\":\"Title " + identifier + "\"," +
        "\"url\":\"https://linear.app/acme/issue/" + identifier + "\",\"priority\":2," +
        "\"updatedAt\":\"2026-11-19T10:00:00.000Z\",\"completedAt\":" + completedAt + "," +
        "\"state\":{\"name\":\"" + stateName + "\",\"type\":\"" + stateType + "\"}," +
        "\"assignee\":{\"name\":\"Alex Example\",\"displayName\":\"alex\"},\"team\":{\"key\":\"ENG\"}," +
        "\"project\":{\"name\":\"Auth\"},\"labels\":{\"nodes\":[{\"name\":\"bug\"}]}}";

    public static string MyIssuesResponse(string nodesJson, bool hasNext, string? cursor) =>
        "{\"data\":{\"viewer\":{\"assignedIssues\":{\"nodes\":[" + nodesJson + "],\"pageInfo\":{\"hasNextPage\":" +
        (hasNext ? "true" : "false") + ",\"endCursor\":" + (cursor is null ? "null" : "\"" + cursor + "\"") + "}}}}}";

    public static Cshubben.Core.Integrations.Linear.LinearIssue Issue(string identifier, string stateName, Cshubben.Core.Integrations.Linear.LinearStateType type) =>
        new("id-" + identifier, identifier, "Title " + identifier, stateName, type, "alex", 2, "ENG", null,
            Array.Empty<string>(), "https://linear.app/x/" + identifier, T, null);
}
