using System.Net;
using System.Text;
using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

internal sealed class GrafanaFake : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public GrafanaFake(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<(string Url, string? Authorization)> Requests { get; } = new();

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? authorization = null;
        if (request.Headers.TryGetValues("Authorization", out var values)) authorization = values.FirstOrDefault();
        Requests.Add((request.RequestUri!.ToString(), authorization));
        return Task.FromResult(_respond(request));
    }
}

public class GrafanaClientTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly ErrorWindow Window = ErrorWindow.Ending(Noon, TimeSpan.FromMinutes(5), 12);

    private static GrafanaClient Client(GrafanaFake fake, string? url = "https://grafana.example.com/", string? token = "secret-token") =>
        new(new HttpClient(fake), () => url, () => token);

    [Fact]
    public async Task A_configured_uid_is_used_without_a_lookup()
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, "[]"));
        var source = await Client(fake).ResolveAsync(" loki-prod ");
        Assert.Equal("loki-prod", source.Uid);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Without_a_uid_the_first_loki_datasource_is_found()
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK,
            "[{\"uid\":\"p\",\"name\":\"Prom\",\"type\":\"prometheus\"},{\"uid\":\"l1\",\"name\":\"Loki\",\"type\":\"loki\"}]"));
        var source = await Client(fake).ResolveAsync(null);
        Assert.Equal("l1", source.Uid);
        Assert.Equal("https://grafana.example.com/api/datasources", fake.Requests[0].Url);
    }

    [Fact]
    public async Task No_loki_datasource_is_a_helpful_error()
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, "[]"));
        var ex = await Assert.ThrowsAsync<GrafanaException>(() => Client(fake).ResolveAsync(null));
        Assert.Contains("UID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_counts_use_the_proxy_a_bearer_token_and_a_window_aligned_to_the_samples()
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK,
            "{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[{\"metric\":{\"appname\":\"binders-api\"},\"values\":[[" +
            (Window.Start.ToUnixTimeSeconds() + 300) + ",\"4\"]]}]}}"));

        var series = await Client(fake).GetErrorSeriesAsync(new LokiDatasource("l1", "Loki"), Window, "default", false);

        var request = fake.Requests.Single();
        Assert.Equal("Bearer secret-token", request.Authorization);
        Assert.StartsWith("https://grafana.example.com/api/datasources/proxy/uid/l1/loki/api/v1/query_range?query=", request.Url, StringComparison.Ordinal);
        Assert.Contains("start=" + (Window.Start.ToUnixTimeSeconds() + 300), request.Url, StringComparison.Ordinal);
        Assert.Contains("end=" + Window.End.ToUnixTimeSeconds(), request.Url, StringComparison.Ordinal);
        Assert.Contains("step=300", request.Url, StringComparison.Ordinal);
        Assert.Contains("count_over_time", Uri.UnescapeDataString(request.Url), StringComparison.Ordinal);
        Assert.Equal(4, Assert.Single(series).Counts[0]);
    }

    [Fact]
    public async Task Lines_are_asked_newest_first_with_a_limit()
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, "{\"status\":\"success\",\"data\":{\"resultType\":\"streams\",\"result\":[]}}"));
        var lines = await Client(fake).GetErrorLinesAsync(new LokiDatasource("l1", "Loki"), Window, "default", "binders-api", false, limit: 99999);
        Assert.Empty(lines);
        var url = fake.Requests.Single().Url;
        Assert.Contains("direction=backward", url, StringComparison.Ordinal);
        Assert.Contains("limit=5000", url, StringComparison.Ordinal);
        Assert.Contains("binders-api", Uri.UnescapeDataString(url), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "t")]
    [InlineData("https://g.example.com", null)]
    [InlineData("", "t")]
    [InlineData("https://g.example.com", "  ")]
    public async Task A_missing_address_or_token_is_reported_without_a_request(string? url, string? token)
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, "[]"));
        var ex = await Assert.ThrowsAsync<GrafanaAuthException>(() => Client(fake, url, token).ResolveAsync(null));
        Assert.True(ex.Missing);
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_rejected_token_is_an_auth_failure_that_does_not_leak_the_token(HttpStatusCode status)
    {
        var fake = new GrafanaFake(_ => GrafanaFake.Json(status, "{\"message\":\"nope\"}"));
        var ex = await Assert.ThrowsAsync<GrafanaAuthException>(() => Client(fake).ResolveAsync(null));
        Assert.False(ex.Missing);
        Assert.DoesNotContain("secret-token", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_found_bad_request_and_server_errors_are_http_failures_with_the_status()
    {
        var notFound = await Assert.ThrowsAsync<GrafanaHttpException>(() =>
            Client(new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.NotFound, "{}"))).GetErrorSeriesAsync(new LokiDatasource("x", "x"), Window, "default", false));
        Assert.Equal(404, notFound.StatusCode);

        var bad = await Assert.ThrowsAsync<GrafanaHttpException>(() =>
            Client(new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.BadRequest, "parse error at line 1"))).GetErrorSeriesAsync(new LokiDatasource("x", "x"), Window, "default", false));
        Assert.Equal(400, bad.StatusCode);
        Assert.Contains("parse error", bad.Message, StringComparison.Ordinal);

        var server = await Assert.ThrowsAsync<GrafanaHttpException>(() =>
            Client(new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.InternalServerError, "oops"))).GetErrorSeriesAsync(new LokiDatasource("x", "x"), Window, "default", false));
        Assert.Equal(500, server.StatusCode);
    }

    [Fact]
    public async Task A_network_failure_is_a_grafana_exception()
    {
        var fake = new GrafanaFake(_ => throw new HttpRequestException("no route"));
        var ex = await Assert.ThrowsAsync<GrafanaException>(() => Client(fake).ResolveAsync(null));
        Assert.Contains("no route", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_reports_the_data_source_and_missing_labels()
    {
        var good = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, "{\"status\":\"success\",\"data\":[\"appname\",\"namespace\",\"loglevel\",\"pod\"]}"));
        var ok = await Client(good).TestAsync("l1");
        Assert.Equal("Connected to the Loki data source UID l1.", ok);

        var partial = new GrafanaFake(_ => GrafanaFake.Json(HttpStatusCode.OK, "{\"status\":\"success\",\"data\":[\"appname\"]}"));
        var message = await Client(partial).TestAsync("l1");
        Assert.Contains("no namespace, loglevel label", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_map_to_calm_facts()
    {
        Assert.NotNull(GrafanaFailureFacts.From(new GrafanaAuthException("x", missing: true)));
        Assert.NotNull(GrafanaFailureFacts.From(new GrafanaAuthException("x", missing: false)));
        Assert.NotNull(GrafanaFailureFacts.From(new GrafanaHttpException(500)));
        Assert.NotNull(GrafanaFailureFacts.From(new GrafanaException("x")));
    }
}

public class GrafanaErrorSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Load_builds_the_overview_and_adds_quiet_known_apps()
    {
        var window = ErrorWindow.Default(Noon);
        var fake = new GrafanaFake(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/query_range", StringComparison.Ordinal)
                ? GrafanaFake.Json(HttpStatusCode.OK,
                    "{\"status\":\"success\",\"data\":{\"resultType\":\"matrix\",\"result\":[{\"metric\":{\"appname\":\"binders-api\"},\"values\":[[" +
                    window.End.ToUnixTimeSeconds() + ",\"30\"]]}]}}")
                : GrafanaFake.Json(HttpStatusCode.OK, "[]"));
        var client = new GrafanaClient(new HttpClient(fake), () => "https://g.example.com", () => "t");
        var source = new GrafanaErrorSource(client, new ErrorsQuery("default", false, "l1", new[] { "binders-api", "contracts-api" }), () => Noon);

        var snapshot = await source.LoadAsync();

        Assert.False(source.IsDemo);
        Assert.Equal(new[] { "binders-api", "contracts-api" }, snapshot.Apps.Select(a => a.App).ToArray());
        Assert.Equal(30, snapshot.Apps[0].Total);
        Assert.Equal(0, snapshot.Apps[1].Total);
    }

    [Fact]
    public async Task Load_groups_runs_the_lines_through_grouping_and_resolves_the_datasource_once()
    {
        var fake = new GrafanaFake(request =>
            request.RequestUri!.AbsolutePath == "/api/datasources"
                ? GrafanaFake.Json(HttpStatusCode.OK, "[{\"uid\":\"l1\",\"name\":\"Loki\",\"type\":\"loki\"}]")
                : GrafanaFake.Json(HttpStatusCode.OK, "{\"status\":\"success\",\"data\":{\"resultType\":\"streams\",\"result\":[{\"stream\":{\"appname\":\"binders-api\",\"pod\":\"p1\"},\"values\":[[\"" +
                    ((Noon.ToUnixTimeSeconds() - 600) * 1_000_000_000L) + "\",\"System.InvalidOperationException: x\\n   at A.B() in /src/A/B.cs:line 3\"]]}]}}"));
        var client = new GrafanaClient(new HttpClient(fake), () => "https://g.example.com", () => "t");
        var source = new GrafanaErrorSource(client, new ErrorsQuery("default", false, "", Array.Empty<string>()), () => Noon);

        var first = await source.LoadGroupsAsync("binders-api");
        await source.LoadGroupsAsync("binders-api");

        Assert.Equal(1, Assert.Single(first).Count);
        Assert.Single(fake.Requests, r => r.Url.EndsWith("/api/datasources", StringComparison.Ordinal));
    }
}

public class DemoErrorSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_demo_has_a_spike_a_riser_a_noisy_app_and_quiet_ones()
    {
        var snapshot = await new DemoErrorSource(() => Noon).LoadAsync();

        Assert.True(new DemoErrorSource().IsDemo);
        Assert.Equal(DemoErrorSource.AppNames.Count, snapshot.Apps.Count);
        Assert.Equal("binders-api", snapshot.Apps[0].App);
        Assert.True(snapshot.Apps[0].IsSpike);
        Assert.Equal(Trend.Up, snapshot.Apps[0].Trend);
        Assert.Equal(1, snapshot.Spiking);

        var contracts = snapshot.Apps.Single(a => a.App == "contracts-api");
        Assert.Equal(0, contracts.Total);
        Assert.False(contracts.IsSpike);
    }

    [Fact]
    public async Task The_demo_is_deterministic()
    {
        var a = await new DemoErrorSource(() => Noon).LoadAsync();
        var b = await new DemoErrorSource(() => Noon).LoadAsync();
        Assert.Equal(a.Apps.Select(x => x.Total).ToArray(), b.Apps.Select(x => x.Total).ToArray());
    }

    [Fact]
    public async Task Demo_groups_show_the_null_reference_and_the_missing_backend_role()
    {
        var groups = await new DemoErrorSource(() => Noon).LoadGroupsAsync("binders-api");

        Assert.Equal(ErrorKind.Exception, groups[0].Kind);
        Assert.Contains("PolicyServerRuntimeRepository.cs:154", groups[0].Title, StringComparison.Ordinal);
        Assert.Contains("Acme.BuildingBlocks", groups[0].Method, StringComparison.Ordinal);
        Assert.Contains(groups, g => g.Kind == ErrorKind.MissingBackendRole && g.Hint is not null);
        Assert.Equal(groups.Sum(g => g.Count), groups.Sum(g => g.Counts.Sum()));
    }

    [Fact]
    public async Task Unknown_apps_have_no_groups()
    {
        Assert.Empty(await new DemoErrorSource(() => Noon).LoadGroupsAsync("nope"));
    }
}
