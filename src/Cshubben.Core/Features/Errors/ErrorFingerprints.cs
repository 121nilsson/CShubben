using System.Globalization;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Errors;

/// <summary>What makes two log lines "the same problem".</summary>
/// <param name="Key">Exception type + logger + top application frame (file and line). Never the message text, which carries ids.</param>
public sealed record ErrorFingerprint(
    string Key,
    string Title,
    string Detail,
    string? Hint,
    ErrorKind Kind,
    string? ExceptionType,
    string? Method,
    string? File,
    int? Line);

/// <summary>
/// Reads one .NET log line (header, message, exception, stack trace) and decides which problem it belongs to. The header
/// <c>[2026-11-23T13:11:49Z Error] pod - Logger.Category - traceId</c> is optional.
/// </summary>
public static class ErrorFingerprints
{
    private static readonly Regex Header = new(
        @"^\[(?<ts>[^\]\s]+)\s+(?<level>[A-Za-z]+)\]\s+(?<pod>\S+)\s+-\s+(?<logger>\S+)(?:\s+-\s+(?<trace>\S+))?\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ExceptionLine = new(
        @"^\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*Exception)(?=:|\s|$)", RegexOptions.CultureInvariant);

    private static readonly Regex Frame = new(
        @"^\s*at\s+(?<method>.+?)(?:\s+in\s+(?<file>.+?):line\s+(?<line>\d+))?\s*$", RegexOptions.CultureInvariant);

    // A machine-to-machine call was refused: the code asks for a backend_role the client does not have in PolicyServer.
    private static readonly Regex BackendRole = new(
        @"User with clientId '""?(?<client>[^'""]+)""?' does not have backend_role '""?(?<role>[^'""]+)""?' required",
        RegexOptions.CultureInvariant);

    private static readonly Regex PathPrefix = new(@"^(?:[A-Za-z]:)?(?:/_)?/src/", RegexOptions.CultureInvariant);
    private static readonly Regex Arity = new(@"`\d+", RegexOptions.CultureInvariant);
    private static readonly Regex Generic = new(@"\[[^\]]*\]", RegexOptions.CultureInvariant);

    private static readonly Regex Guid = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant);

    private static readonly Regex LongHex = new(@"\b[0-9a-fA-F]{12,}\b", RegexOptions.CultureInvariant);

    // Codes like OKND9LTTC: upper case letters and digits, at least eight, with both kinds.
    private static readonly Regex Code = new(@"\b(?=[A-Z0-9]*\d)(?=[A-Z0-9]*[A-Z])[A-Z0-9]{8,}\b", RegexOptions.CultureInvariant);

    private static readonly Regex Number = new(@"\b\d+\b", RegexOptions.CultureInvariant);

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    public static ErrorFingerprint Of(string? message)
    {
        var lines = (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var logger = "";
        var skip = 0;
        if (lines.Length > 0 && Header.Match(lines[0]) is { Success: true } header)
        {
            logger = header.Groups["logger"].Value;
            skip = 1;
        }

        var body = lines.Skip(skip).ToList();

        var role = BackendRole.Match(message ?? "");
        if (role.Success)
        {
            var roleName = role.Groups["role"].Value;
            var client = role.Groups["client"].Value;
            return new ErrorFingerprint(
                "backend_role|" + roleName + "|" + client,
                "Missing backend_role " + roleName,
                "Client " + client + " is refused because it does not have this role.",
                "Add the backend_role '" + roleName + "' to client " + client + " in PolicyServer. The code that requires it is already deployed.",
                ErrorKind.MissingBackendRole,
                null,
                null,
                null,
                null);
        }

        string? exceptionType = null;
        foreach (var line in body)
        {
            var match = ExceptionLine.Match(line);
            if (!match.Success) continue;
            exceptionType = match.Groups["type"].Value;
            break;
        }

        string? method = null;
        string? file = null;
        int? lineNumber = null;
        string? firstMethod = null;
        foreach (var line in body)
        {
            var match = Frame.Match(line);
            if (!match.Success) continue;
            var name = CleanMethod(match.Groups["method"].Value);
            firstMethod ??= name;

            // The top application frame is the first one with a source file; framework frames have none.
            if (!match.Groups["file"].Success) continue;
            method = name;
            file = NormalizePath(match.Groups["file"].Value);
            if (int.TryParse(match.Groups["line"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) lineNumber = parsed;
            break;
        }

        method ??= firstMethod;

        var first = body.FirstOrDefault(l => l.Trim().Length > 0 && !ExceptionLine.IsMatch(l) && !Frame.IsMatch(l)) ?? "";

        if (exceptionType is not null)
        {
            var where = file is not null && lineNumber is not null
                ? file + ":" + lineNumber.Value.ToString(CultureInfo.InvariantCulture)
                : method ?? logger;
            var shownWhere = file is not null && lineNumber is not null
                ? FileName(file) + ":" + lineNumber.Value.ToString(CultureInfo.InvariantCulture)
                : ShortMethod(method) ?? ShortName(logger);
            var detail = first.Trim().Length > 0 ? Truncate(first.Trim(), 200) : ShortName(logger);
            return new ErrorFingerprint(
                exceptionType + "|" + logger + "|" + where,
                ShortName(exceptionType) + (shownWhere.Length > 0 ? " · " + shownWhere : ""),
                detail,
                null,
                ErrorKind.Exception,
                exceptionType,
                method,
                file,
                lineNumber);
        }

        var normalised = NormalizeMessage(first);
        return new ErrorFingerprint(
            "msg|" + logger + "|" + normalised,
            normalised.Length == 0 ? "Error without a message" : Truncate(normalised, 90),
            ShortName(logger),
            null,
            ErrorKind.Message,
            null,
            method,
            file,
            lineNumber);
    }

    /// <summary>Ids, hashes and numbers replaced by placeholders, on one line, so repeated errors compare equal.</summary>
    public static string NormalizeMessage(string? text)
    {
        var line = (text ?? "").Trim();
        line = Guid.Replace(line, "<id>");
        line = LongHex.Replace(line, "<id>");
        line = Code.Replace(line, "<id>");
        line = Number.Replace(line, "<n>");
        return Spaces.Replace(line, " ").Trim();
    }

    /// <summary>The method name without parameters, generic arity (`1) and generic arguments.</summary>
    public static string CleanMethod(string text)
    {
        var name = text.Trim();
        var paren = name.IndexOf('(');
        if (paren >= 0) name = name.Substring(0, paren);
        name = Arity.Replace(name, "");
        name = Generic.Replace(name, "");
        return name.Trim();
    }

    /// <summary>A build path made repository relative: "/_/src/Project/File.cs" becomes "Project/File.cs".</summary>
    public static string NormalizePath(string path) => PathPrefix.Replace(path.Trim().Replace('\\', '/'), "");

    private static string FileName(string path) => path.Substring(path.LastIndexOf('/') + 1);

    private static string ShortName(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
    }

    /// <summary>"Namespace.Type.Method" shortened to "Type.Method".</summary>
    private static string? ShortMethod(string? method)
    {
        if (string.IsNullOrEmpty(method)) return null;
        var parts = method.Split('.');
        return parts.Length <= 2 ? method : parts[^2] + "." + parts[^1];
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1) + "…";
}
