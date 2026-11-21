using System.Globalization;

namespace Cshubben.Core.Features.Review;

/// <summary>What to apply: the file, the commented line range (1-based, new side) and the comment holding the suggestion block.</summary>
public sealed record SuggestionRequest(string Path, int StartLine, int EndLine, string CommentBody, string ExpectedHeadSha);

/// <summary>
/// The result of turning a suggestion into a file change. When <see cref="Ok"/> is false nothing can be applied and
/// <see cref="Error"/> says why. <see cref="Removed"/> and <see cref="Added"/> are the lines shown to the reviewer first.
/// </summary>
public sealed record SuggestionPlan(
    bool Ok,
    string Error,
    string Path,
    int StartLine,
    int EndLine,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Added,
    string NewContent,
    string CommitMessage)
{
    public static SuggestionPlan Fail(string path, string error) =>
        new(false, error, path, 0, 0, Array.Empty<string>(), Array.Empty<string>(), string.Empty, string.Empty);

    /// <summary>True when the suggestion equals the current text, so a commit would be empty.</summary>
    public bool IsNoChange => Ok && Removed.SequenceEqual(Added);
}

/// <summary>What happened when the suggestion was applied. DryRun means nothing was written.</summary>
public sealed record SuggestionOutcome(bool Committed, bool DryRun, string Message, SuggestionPlan Plan);

/// <summary>Pure rules for "apply suggestion": who may, which threads qualify, and the new file text.</summary>
public static class SuggestionApplier
{
    /// <summary>The author of the pull request, or anyone with write access to the repository, may apply suggestions.</summary>
    public static bool CanApply(string? viewer, string? pullRequestAuthor, string? viewerPermission)
    {
        if (!string.IsNullOrEmpty(viewer) && string.Equals(viewer, pullRequestAuthor, StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(viewerPermission, "WRITE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(viewerPermission, "MAINTAIN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(viewerPermission, "ADMIN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The request for applying the suggestion in <paramref name="comment"/>, or null when it cannot be applied: no
    /// suggestion block, a comment on the old side of the diff, an outdated or resolved thread, or no line number.
    /// </summary>
    public static SuggestionRequest? RequestFor(ReviewThread thread, ReviewComment comment, string headSha)
    {
        if (thread.Side != DiffSide.Right || thread.IsOutdated || thread.IsResolved) return null;
        if (thread.Line is not int end || end <= 0) return null;
        if (SuggestionFormatter.Extract(comment.Body) is null) return null;
        var start = thread.StartLine is int s && s > 0 && s <= end ? s : end;
        return new SuggestionRequest(thread.Path, start, end, comment.Body, headSha);
    }

    /// <summary>Builds the new file text. Line endings and a final newline of the original are kept.</summary>
    public static SuggestionPlan Plan(string path, string content, int startLine, int endLine, string commentBody)
    {
        var replacement = SuggestionFormatter.Extract(commentBody);
        if (replacement is null) return SuggestionPlan.Fail(path, "The comment has no suggestion block.");

        var lo = Math.Min(startLine, endLine);
        var hi = Math.Max(startLine, endLine);
        if (lo < 1) return SuggestionPlan.Fail(path, "The suggestion has no valid line range.");

        var crlf = content.Contains("\r\n", StringComparison.Ordinal);
        var withoutCrlf = content.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        if (withoutCrlf.Contains('\r') || (crlf && withoutCrlf.Contains('\n')))
            return SuggestionPlan.Fail(path, "The file mixes line endings, so the suggestion cannot be applied safely from here.");
        var finalNewline = content.EndsWith('\n');
        var lines = SplitLines(content);
        if (hi > lines.Count)
            return SuggestionPlan.Fail(path, "The file has " + lines.Count.ToString(CultureInfo.InvariantCulture) + " lines, so the suggestion no longer fits. Reload the review.");

        var removed = lines.Skip(lo - 1).Take(hi - lo + 1).ToList();
        var normalized = replacement.Replace("\r\n", "\n", StringComparison.Ordinal);
        IReadOnlyList<string> added = normalized.Length == 0 ? Array.Empty<string>() : normalized.Split('\n');
        var updated = SuggestionFormatter.Apply(lines, lo, hi, replacement);

        var eol = crlf ? "\r\n" : "\n";
        var text = string.Join(eol, updated);
        if (finalNewline && updated.Count > 0) text += eol;
        return new SuggestionPlan(true, string.Empty, path, lo, hi, removed, added, text, CommitMessage(path, lo, hi));
    }

    public static string CommitMessage(string path, int startLine, int endLine)
    {
        var name = path.Substring(path.LastIndexOf('/') + 1);
        var range = startLine == endLine
            ? "line " + startLine.ToString(CultureInfo.InvariantCulture)
            : "lines " + startLine.ToString(CultureInfo.InvariantCulture) + "-" + endLine.ToString(CultureInfo.InvariantCulture);
        return "Apply suggested change to " + name + " (" + range + ")";
    }

    /// <summary>Lines of a text without their line endings; a final newline does not produce an empty last line.</summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}
