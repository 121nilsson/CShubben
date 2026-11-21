using System.Diagnostics;
using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.Diagnostics;

/// <summary>
/// Runs checks one after the other. A check that throws or hangs becomes a failed result, so the run always finishes.
/// Nothing here writes to GitHub or Linear: checks are supplied by the caller and must be read-only.
/// </summary>
public static class DiagnosticsRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task<DiagnosticReport> RunAsync(
        IReadOnlyList<DiagnosticCheck> checks,
        DateTimeOffset now,
        string appInfo = "",
        TimeSpan? timeout = null,
        IEnumerable<string?>? knownSecrets = null,
        Func<Exception, FailureFacts?>? factsFor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var limit = timeout ?? DefaultTimeout;
        var results = new List<DiagnosticResult>(checks.Count);
        var secrets = knownSecrets?.ToList();
        foreach (var check in checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunOneAsync(check, limit, secrets, now, factsFor, cancellationToken).ConfigureAwait(false));
        }

        return new DiagnosticReport(now, results, appInfo);
    }

    private static async Task<DiagnosticResult> RunOneAsync(
        DiagnosticCheck check, TimeSpan limit, IReadOnlyList<string?>? secrets, DateTimeOffset now,
        Func<Exception, FailureFacts?>? factsFor, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(limit);
            var result = await check.Run(cts.Token).ConfigureAwait(false);
            return Finish(result with { Name = check.Name, Duration = watch.Elapsed }, secrets);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var service = check.Name;
            var failure = FailureClassifier.Classify(ex, service, now, factsFor?.Invoke(ex));
            return Finish(DiagnosticResult.Fail(check.Name, failure.Title + ". " + failure.Message) with { Duration = watch.Elapsed }, secrets);
        }
    }

    private static DiagnosticResult Finish(DiagnosticResult result, IReadOnlyList<string?>? secrets) =>
        result with { Detail = SecretRedactor.Scrub(result.Detail, secrets) };
}
