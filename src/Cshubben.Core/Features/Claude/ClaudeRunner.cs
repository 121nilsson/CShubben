using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cshubben.Core.Shared;
namespace Cshubben.Core.Features.Claude;

public enum ClaudeErrorKind { None, NotInstalled, AuthFailed, Timeout, Failed }

/// <summary>A system prompt and the user prompt (sent on stdin) built by <see cref="Prompts"/>.</summary>
public sealed record PromptPair(string SystemPrompt, string UserPrompt);

public sealed record ClaudeRequest(string SystemPrompt, string UserPrompt)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(240);

    /// <summary>Reviews with high effort and a big diff take much longer than a summary.</summary>
    public static readonly TimeSpan ReviewTimeout = TimeSpan.FromSeconds(900);

    /// <summary>A model alias (haiku, sonnet, opus), a full model id, or null for the default.</summary>
    public string? Model { get; init; }

    /// <summary>low, medium, high, xhigh or max; null or invalid means Claude Code's own default.</summary>
    public string? Effort { get; init; }

    /// <summary>What this request is for. When no model or effort is set, the assistant takes them from the settings of this feature.</summary>
    public ClaudeFeature? Feature { get; init; }

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>When true (default) every link that is not https://github.com/ is removed from the answer.</summary>
    public bool KeepOnlyGitHubLinks { get; init; } = true;

    public static ClaudeRequest From(PromptPair prompt) => new(prompt.SystemPrompt, prompt.UserPrompt);
}

public sealed record ClaudeResult(
    bool Success,
    string Text,
    string Model,
    double CostUsd,
    TimeSpan Duration,
    ClaudeErrorKind Error,
    string? ErrorMessage,
    long InputTokens = 0,
    long OutputTokens = 0)
{
    public static ClaudeResult Fail(ClaudeErrorKind kind, string message, string model, TimeSpan duration) =>
        new(false, "", model, 0, duration, kind, message);
}

/// <summary>Model and effort choices, with validation so a stale setting cannot make a call fail.</summary>
public static class ClaudeOptions
{
    public const string DefaultModel = "sonnet";

    public static readonly IReadOnlyList<string> Models = new[] { "haiku", "sonnet", "opus" };

    public static readonly IReadOnlyList<string> Efforts = new[] { "low", "medium", "high", "xhigh", "max" };

    private static readonly Regex ModelId = new("^claude-[a-z0-9][a-z0-9._-]{0,60}$", RegexOptions.CultureInvariant);

    public static string ResolveModel(string? requested)
    {
        var model = requested?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(model)) return DefaultModel;
        if (Models.Contains(model) || ModelId.IsMatch(model)) return model;
        return DefaultModel;
    }

    public static string? ResolveEffort(string? requested)
    {
        var effort = requested?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(effort)) return null;
        return Efforts.Contains(effort) ? effort : null;
    }
}

/// <summary>Recognises "you need to log in again" failures and holds the advice shown to the user.</summary>
public static class ClaudeAuth
{
    public const string TokenEnvVar = "CLAUDE_CODE_OAUTH_TOKEN";

    public const string Hint =
        "The Claude login has expired. Run `claude setup-token` in a terminal and paste the token under Settings, or run `claude auth login`.";

    private static readonly string[] Phrases =
    {
        "failed to authenticate",
        "authentication",
        "not logged in",
        "please run /login",
        "claude auth login",
        "invalid api key",
        "cannot be renewed",
        "oauth",
    };

    public static bool IsAuthError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var m = message.ToLowerInvariant();
        foreach (var phrase in Phrases)
        {
            if (m.Contains(phrase, StringComparison.Ordinal)) return true;
        }

        // 401 only counts in an auth context, so port numbers and the like do not match.
        return m.Contains("401", StringComparison.Ordinal)
            && (m.Contains("unauthorized", StringComparison.Ordinal)
                || m.Contains("auth", StringComparison.Ordinal)
                || m.Contains("login", StringComparison.Ordinal)
                || m.Contains("token", StringComparison.Ordinal));
    }
}

public sealed record ClaudeOutput(
    bool IsError,
    string Text,
    double CostUsd,
    TimeSpan? Duration,
    long InputTokens,
    long OutputTokens,
    string? Model);

/// <summary>Reads the output of <c>claude -p --output-format json</c>, tolerating small format differences.</summary>
public static class ClaudeOutputParser
{
    public static ClaudeOutput? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();

        JsonElement? root = null;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Fall back to finding an object inside surrounding text.
        }

        root ??= JsonText.ParseObject(trimmed);
        if (root is not JsonElement top) return null;

        JsonElement obj;
        if (top.ValueKind == JsonValueKind.Array)
        {
            // Some versions print the whole event list; the last "result" event is the answer.
            var found = false;
            obj = default;
            foreach (var item in top.EnumerateArray())
            {
                if (JsonRead.Str(item, "type") == "result")
                {
                    obj = item;
                    found = true;
                }
            }

            if (!found) return null;
        }
        else if (top.ValueKind == JsonValueKind.Object)
        {
            obj = top;
        }
        else
        {
            return null;
        }

        var isError = JsonRead.Bool(obj, "is_error") == true;
        var text = JsonRead.Str(obj, "result");
        if (text is null)
        {
            if (!isError) return null;
            text = JsonRead.Str(obj, "error") ?? "";
        }

        var cost = JsonRead.Num(obj, "total_cost_usd") ?? JsonRead.Num(obj, "cost_usd") ?? 0;
        if (double.IsNaN(cost) || double.IsInfinity(cost) || cost < 0) cost = 0;

        TimeSpan? duration = null;
        var ms = JsonRead.Num(obj, "duration_ms");
        if (ms is double millis && millis >= 0 && millis < 1e10) duration = TimeSpan.FromMilliseconds(millis);

        long inputTokens = 0;
        long outputTokens = 0;
        if (JsonRead.Obj(obj, "usage") is JsonElement usage)
        {
            inputTokens = Tokens(usage, "input_tokens")
                + Tokens(usage, "cache_creation_input_tokens")
                + Tokens(usage, "cache_read_input_tokens");
            outputTokens = Tokens(usage, "output_tokens");
        }

        string? model = null;
        if (JsonRead.Obj(obj, "modelUsage") is JsonElement modelUsage)
        {
            foreach (var property in modelUsage.EnumerateObject())
            {
                model = property.Name;
                break;
            }
        }

        return new ClaudeOutput(isError, text, cost, duration, inputTokens, outputTokens, model);
    }

    private static long Tokens(JsonElement usage, string name)
    {
        var value = JsonRead.Num(usage, name);
        return value is double d && d >= 0 && d < 1e15 ? (long)d : 0;
    }
}

/// <summary>
/// Runs <c>claude -p</c> headless with no tools. The prompt goes on stdin; the system prompt and options on the
/// command line. The token (if any) is passed only through the child's environment, never on the command line.
/// </summary>
public sealed class ClaudeRunner
{
    private readonly IProcessRunner _process;
    private readonly Func<string?> _locate;
    private readonly Func<string?> _token;

    public ClaudeRunner(IProcessRunner process, Func<string?> locate, Func<string?>? tokenProvider = null)
    {
        _process = process;
        _locate = locate;
        _token = tokenProvider ?? (() => null);
    }

    /// <summary>The real runner: real processes, binary discovery, and the token from the environment or the secret store.</summary>
    public static ClaudeRunner CreateDefault(ISecretStore? secrets = null) =>
        new(new ProcessRunner(), () => ClaudeLocator.Find(), () => ResolveToken(secrets));

    public static string? ResolveToken(ISecretStore? secrets)
    {
        var fromEnv = Environment.GetEnvironmentVariable(ClaudeAuth.TokenEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
        var stored = secrets?.Get(SecretKeys.ClaudeOAuthToken);
        return string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
    }

    /// <summary>The command-line arguments (without the prompt, which goes on stdin).</summary>
    public static List<string> BuildArguments(string systemPrompt, string model, string? effort)
    {
        var args = new List<string>
        {
            "-p",
            "--output-format", "json",
            "--model", model,
            "--tools", "",
            "--no-session-persistence",
            "--disable-slash-commands",
            "--strict-mcp-config",
            "--system-prompt", systemPrompt,
        };
        if (effort is not null)
        {
            args.Add("--effort");
            args.Add(effort);
        }

        return args;
    }

    public async Task<ClaudeResult> RunAsync(ClaudeRequest request, CancellationToken cancellationToken = default)
    {
        var model = ClaudeOptions.ResolveModel(request.Model);
        var exe = _locate();
        if (string.IsNullOrEmpty(exe))
        {
            return ClaudeResult.Fail(
                ClaudeErrorKind.NotInstalled,
                "Could not find `claude`. Install Claude Code, or set CSHUBBEN_CLAUDE_PATH to the claude executable.",
                model,
                TimeSpan.Zero);
        }

        Dictionary<string, string>? env = null;
        var token = _token();
        if (!string.IsNullOrWhiteSpace(token))
        {
            env = new Dictionary<string, string> { [ClaudeAuth.TokenEnvVar] = token.Trim() };
        }

        var args = BuildArguments(request.SystemPrompt, model, ClaudeOptions.ResolveEffort(request.Effort));
        // A neutral directory so no project's CLAUDE.md or settings are picked up.
        var process = new ProcessRequest(exe, args, request.UserPrompt, env, request.Timeout)
        {
            WorkingDirectory = Path.GetTempPath(),
        };

        var stopwatch = Stopwatch.StartNew();
        ProcessResult result;
        try
        {
            result = await _process.RunAsync(process, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return ClaudeResult.Fail(
                ClaudeErrorKind.NotInstalled,
                "Could not start `claude`. Check that the file exists and can run.",
                model,
                stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return ClaudeResult.Fail(ClaudeErrorKind.Failed, "Could not run `claude`: " + SecretRedactor.Scrub(ex.Message, new[] { token }), model, stopwatch.Elapsed);
        }

        var elapsed = stopwatch.Elapsed;
        if (result.TimedOut)
        {
            return ClaudeResult.Fail(
                ClaudeErrorKind.Timeout,
                "Claude did not answer within " + (int)request.Timeout.TotalSeconds + " s.",
                model,
                elapsed);
        }

        var output = ClaudeOutputParser.Parse(result.StdOut);
        if (output is null)
        {
            var detail = ShortText(result.StdErr + " " + result.StdOut, 300, token);
            if (result.ExitCode != 0)
            {
                var message = "claude exited with code " + result.ExitCode + (detail.Length > 0 ? ": " + detail : "");
                return ClaudeAuth.IsAuthError(detail)
                    ? ClaudeResult.Fail(ClaudeErrorKind.AuthFailed, message + "\n" + ClaudeAuth.Hint, model, elapsed)
                    : ClaudeResult.Fail(ClaudeErrorKind.Failed, message, model, elapsed);
            }

            return ClaudeResult.Fail(ClaudeErrorKind.Failed, "claude returned output that could not be read.", model, elapsed);
        }

        if (output.IsError)
        {
            var message = "Claude reported an error: " + ShortText(output.Text, 300, token);
            return ClaudeAuth.IsAuthError(output.Text)
                ? ClaudeResult.Fail(ClaudeErrorKind.AuthFailed, message + "\n" + ClaudeAuth.Hint, model, elapsed)
                : ClaudeResult.Fail(ClaudeErrorKind.Failed, message, model, elapsed);
        }

        var text = output.Text.Trim();
        if (request.KeepOnlyGitHubLinks) text = LinkFilter.StripNonGitHubLinks(text);
        return new ClaudeResult(
            true,
            text,
            output.Model ?? model,
            output.CostUsd,
            output.Duration ?? elapsed,
            ClaudeErrorKind.None,
            null,
            output.InputTokens,
            output.OutputTokens);
    }

    private static string ShortText(string text, int max, string? secret = null)
    {
        text = SecretRedactor.Scrub(text, new[] { secret });
        var collapsed = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return collapsed.Length > max ? collapsed.Substring(0, max) + "…" : collapsed;
    }
}
