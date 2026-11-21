using Cshubben.Core.Data;
using Cshubben.Core.Features.Diagnostics;
using Cshubben.Core.Model;
using Cshubben.Core.Shared;
using Xunit;

namespace Cshubben.Core.Tests.Diagnostics;

public sealed class DiagnosticsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cshubben-diag-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class FakeData(HubSnapshot? snapshot, Exception? error = null) : IHubData
    {
        public Task<HubSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            error is not null ? throw error : Task.FromResult(snapshot!);
    }

    private static HubSnapshot Empty() => new("me", Array.Empty<PullRequest>(), Array.Empty<PullRequest>(),
        new Dictionary<string, ReviewInfo>(), Array.Empty<FeedItem>(), Now);

    [Fact]
    public async Task Throwing_check_becomes_a_failed_result_and_the_run_continues()
    {
        var checks = new[]
        {
            new DiagnosticCheck("Boom", _ => throw new HttpRequestException("no network")),
            new DiagnosticCheck("Fine", _ => Task.FromResult(DiagnosticResult.Ok("Fine", "all good"))),
        };
        var report = await DiagnosticsRunner.RunAsync(checks, Now);
        Assert.Equal(2, report.Results.Count);
        Assert.Equal(DiagnosticStatus.Fail, report.Results[0].Status);
        Assert.Contains("internet", report.Results[0].Detail);
        Assert.Equal(DiagnosticStatus.Ok, report.Results[1].Status);
        Assert.Equal(DiagnosticStatus.Fail, report.Overall);
    }

    [Fact]
    public async Task Hanging_check_times_out()
    {
        var checks = new[]
        {
            new DiagnosticCheck("Slow", async ct => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return DiagnosticResult.Ok("Slow", ""); }),
        };
        var report = await DiagnosticsRunner.RunAsync(checks, Now, timeout: TimeSpan.FromMilliseconds(50));
        Assert.Equal(DiagnosticStatus.Fail, report.Results[0].Status);
        Assert.Contains("too long", report.Results[0].Detail);
    }

    [Fact]
    public async Task Outer_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DiagnosticsRunner.RunAsync(new[] { BuiltInChecks.Skipped("x", "y") }, Now, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Overall_is_warn_when_only_warnings()
    {
        var report = await DiagnosticsRunner.RunAsync(new[] { BuiltInChecks.Skipped("Linear", "No key") }, Now);
        Assert.Equal(DiagnosticStatus.Warn, report.Overall);
    }

    [Fact]
    public async Task Report_text_never_contains_secrets()
    {
        var secret = "super-secret-token-value";
        var checks = new[]
        {
            new DiagnosticCheck("Leaky", _ => Task.FromResult(DiagnosticResult.Ok("Leaky", "used " + secret + " and ghp_" + "abcdefghijklmnopqrstuvwxyz0123456789"))),
            new DiagnosticCheck("Throws", _ => throw new InvalidOperationException("failed with " + secret)),
        };
        var report = await DiagnosticsRunner.RunAsync(checks, Now, knownSecrets: new[] { secret });
        var text = report.ToText(new[] { secret });
        Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain("ghp_abc", text);
        Assert.Contains("[FAIL] Throws", text);
        Assert.Contains("[OK] Leaky", text);
    }

    [Fact]
    public async Task Hub_data_check_ok_warn_and_fail()
    {
        var warn = await BuiltInChecks.HubData("GitHub", new FakeData(Empty())).Run(CancellationToken.None);
        Assert.Equal(DiagnosticStatus.Warn, warn.Status);
        Assert.Contains("signed in as me", warn.Detail);

        var report = await DiagnosticsRunner.RunAsync(
            new[] { BuiltInChecks.HubData("GitHub", new FakeData(null, new HttpRequestException("down"))) }, Now);
        Assert.Equal(DiagnosticStatus.Fail, report.Results[0].Status);
    }

    [Fact]
    public async Task Secret_store_check_reports_presence_only()
    {
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.LinearApiKey, "lin-key-value-123456");
        var result = await BuiltInChecks.SecretStore("Secrets", secrets).Run(CancellationToken.None);
        Assert.Equal(DiagnosticStatus.Ok, result.Status);
        Assert.Contains("Linear key saved: yes", result.Detail);
        Assert.Contains("Claude token saved: no", result.Detail);
        Assert.DoesNotContain("lin-key-value", result.Detail);
    }

    [Fact]
    public async Task Data_directory_check_writes_and_cleans_up()
    {
        var result = await BuiltInChecks.DataDirectoryWritable("Data", _dir).Run(CancellationToken.None);
        Assert.Equal(DiagnosticStatus.Ok, result.Status);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Data_directory_check_fails_when_path_is_a_file()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "blocker");
        await File.WriteAllTextAsync(file, "x");
        var result = await BuiltInChecks.DataDirectoryWritable("Data", Path.Combine(file, "sub")).Run(CancellationToken.None);
        Assert.Equal(DiagnosticStatus.Fail, result.Status);
    }
}
