using System.Globalization;
using System.Text;
using Cshubben.Core.Shared;

namespace Cshubben.Core.Features.Diagnostics;

public enum DiagnosticStatus { Ok, Warn, Fail }

/// <summary>One line of the report. <see cref="Detail"/> is short and never contains a token or key.</summary>
public sealed record DiagnosticResult(string Name, DiagnosticStatus Status, string Detail, TimeSpan Duration = default)
{
    public static DiagnosticResult Ok(string name, string detail) => new(name, DiagnosticStatus.Ok, detail);

    public static DiagnosticResult Warn(string name, string detail) => new(name, DiagnosticStatus.Warn, detail);

    public static DiagnosticResult Fail(string name, string detail) => new(name, DiagnosticStatus.Fail, detail);
}

/// <summary>A named, read-only check. The runner times it, bounds it and turns exceptions into a failed result.</summary>
public sealed record DiagnosticCheck(string Name, Func<CancellationToken, Task<DiagnosticResult>> Run);

/// <summary>The outcome of a run, with a plain-text rendering to paste into a chat or an issue.</summary>
public sealed record DiagnosticReport(DateTimeOffset At, IReadOnlyList<DiagnosticResult> Results, string AppInfo = "")
{
    public DiagnosticStatus Overall =>
        Results.Any(r => r.Status == DiagnosticStatus.Fail) ? DiagnosticStatus.Fail
        : Results.Any(r => r.Status == DiagnosticStatus.Warn) ? DiagnosticStatus.Warn
        : DiagnosticStatus.Ok;

    public static string Label(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Ok => "OK",
        DiagnosticStatus.Warn => "WARN",
        _ => "FAIL",
    };

    /// <summary>Plain text. Secrets are scrubbed once more here, as a last line of defence.</summary>
    public string ToText(IEnumerable<string?>? knownSecrets = null)
    {
        var text = new StringBuilder();
        text.Append("Cshubben diagnostics, ").Append(At.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" UTC\n");
        if (AppInfo.Length > 0) text.Append(AppInfo).Append('\n');
        text.Append("Overall: ").Append(Label(Overall)).Append('\n').Append('\n');
        foreach (var r in Results)
        {
            text.Append('[').Append(Label(r.Status)).Append("] ").Append(r.Name);
            if (r.Duration > TimeSpan.Zero)
                text.Append(" (").Append(((int)r.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append(" ms)");
            text.Append('\n');
            if (r.Detail.Length > 0) text.Append("    ").Append(r.Detail.Replace("\n", "\n    ", StringComparison.Ordinal)).Append('\n');
        }

        return SecretRedactor.Scrub(text.ToString(), knownSecrets);
    }
}
