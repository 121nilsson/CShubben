using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Cshubben.Core.Shared;
namespace Cshubben.Core.Features.Claude;

/// <summary>One process to run: the program, its arguments, text for stdin, extra environment variables and a time limit.</summary>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StdIn,
    IReadOnlyDictionary<string, string>? Env,
    TimeSpan Timeout)
{
    public string? WorkingDirectory { get; init; }
}

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut = false);

/// <summary>Runs an external program. Faked in tests.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the process. Throws <see cref="Win32Exception"/> when the program cannot be started and
    /// <see cref="OperationCanceledException"/> when the caller cancels. A timeout is reported in the result.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments) info.ArgumentList.Add(argument);
        if (!string.IsNullOrEmpty(request.WorkingDirectory)) info.WorkingDirectory = request.WorkingDirectory;
        if (request.Env is not null)
        {
            foreach (var pair in request.Env) info.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = info };
        process.Start();

        // Read and write concurrently so a full pipe can never block the child.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdinTask = WriteStdInAsync(process, request.StdIn);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(request.Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested) throw;
            timedOut = true;
        }

        await stdinTask.ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut);
    }

    private static async Task WriteStdInAsync(Process process, string? text)
    {
        try
        {
            if (!string.IsNullOrEmpty(text)) await process.StandardInput.WriteAsync(text).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child closed its input early; nothing more to write.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
