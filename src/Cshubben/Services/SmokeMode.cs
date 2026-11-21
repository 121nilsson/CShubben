using Cshubben.Core.Features.Diagnostics;
using Cshubben.Core.Features.Notes;

namespace Cshubben.Services;

/// <summary>
/// <c>Cshubben.exe --smoke</c>: runs the read-only diagnostics without a window, writes the report to
/// <c>smoke-report.txt</c> in the data folder and returns. The exit code is 1 when a check failed, otherwise 0.
/// </summary>
static class SmokeMode
{
    public const string ReportFileName = "smoke-report.txt";

    public static void Run(AppServices services)
    {
        string text;
        var failed = false;
        try
        {
            // Task.Run keeps the work off any UI synchronization context.
            var report = Task.Run(() => AppDiagnostics.RunAsync(services)).GetAwaiter().GetResult();
            text = AppDiagnostics.Text(services, report);
            failed = report.Overall == DiagnosticStatus.Fail;
        }
        catch (Exception ex)
        {
            // The runner never throws for a failing check, so this is a bug in the harness. Say so, without secrets.
            text = "Cshubben diagnostics could not run: " + Cshubben.Core.Shared.SecretRedactor.Scrub(ex.Message, AppDiagnostics.KnownSecrets(services));
            failed = true;
        }

        var written = TryWrite(AppPaths.DataDirectory, text) ?? TryWrite(Path.GetTempPath(), text);
        try
        {
            Console.WriteLine(text);
            Console.WriteLine(written is null ? "Could not write the report file." : "Report written to " + written);
        }
        catch (Exception)
        {
            // A windowed app may have no console; the file is the output.
        }

        Environment.ExitCode = failed ? 1 : 0;
    }

    private static string? TryWrite(string directory, string text)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, ReportFileName);
            File.WriteAllText(path, text);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
