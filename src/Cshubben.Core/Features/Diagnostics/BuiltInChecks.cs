using System.Globalization;
using Cshubben.Core.Data;
using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.Diagnostics;

/// <summary>
/// Checks that only need Core interfaces. Checks that need another feature (Linear, Claude, settings, GitHub token)
/// are built by the app, which is where the features meet.
/// </summary>
public static class BuiltInChecks
{
    /// <summary>Loads a snapshot through <see cref="IHubData"/> (read only) and reports counts. Zero pull requests is a warning, not a failure.</summary>
    public static DiagnosticCheck HubData(string name, IHubData data) => new(name, async ct =>
    {
        var snapshot = await data.LoadAsync(ct).ConfigureAwait(false);
        var detail = (snapshot.Viewer.Length > 0 ? "signed in as " + snapshot.Viewer + ", " : "")
            + snapshot.Authored.Count.ToString(CultureInfo.InvariantCulture) + " authored, "
            + snapshot.Reviews.Count.ToString(CultureInfo.InvariantCulture) + " review requests, "
            + snapshot.Feed.Count.ToString(CultureInfo.InvariantCulture) + " feed items";
        if (snapshot.Authored.Count == 0 && snapshot.Reviews.Count == 0)
            return DiagnosticResult.Warn(name, detail + ". No open pull requests found; that is fine if you have none, otherwise check muted repos and the token's access.");
        return DiagnosticResult.Ok(name, detail);
    });

    /// <summary>Reads (never writes) the well-known secrets and reports only whether each one exists.</summary>
    public static DiagnosticCheck SecretStore(string name, ISecretStore secrets) => new(name, _ =>
    {
        var linear = secrets.Get(SecretKeys.LinearApiKey) is { Length: > 0 };
        var claude = secrets.Get(SecretKeys.ClaudeOAuthToken) is { Length: > 0 };
        var detail = "Linear key saved: " + (linear ? "yes" : "no") + "; Claude token saved: " + (claude ? "yes" : "no");
        return Task.FromResult(DiagnosticResult.Ok(name, detail));
    });

    /// <summary>Creates the folder if needed, writes a small temporary file, deletes it. Touches nothing else.</summary>
    public static DiagnosticCheck DataDirectoryWritable(string name, string directory) => new(name, _ =>
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult(DiagnosticResult.Ok(name, directory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(DiagnosticResult.Fail(name, "Cannot write to " + directory + ": " + ex.Message
                + ". Settings, notes and read state cannot be saved until this works."));
        }
    });

    /// <summary>A check that is not applicable right now, reported as a warning with the reason.</summary>
    public static DiagnosticCheck Skipped(string name, string reason) =>
        new(name, _ => Task.FromResult(DiagnosticResult.Warn(name, reason)));
}
