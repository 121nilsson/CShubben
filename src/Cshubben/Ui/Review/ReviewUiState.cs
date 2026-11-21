using Cshubben.Core.Features.Review;

namespace Cshubben.Ui.Review;

/// <summary>Where the inline composer is open and what it starts with.</summary>
sealed record ComposerState(string Path, int Line, DiffSide Side, int? StartLine, string Initial, string? EditPendingId = null);

/// <summary>An open "apply suggestion" preview under one comment.</summary>
sealed record SuggestionState(string ThreadId, string CommentId, SuggestionRequest Request, SuggestionPlan Plan);

/// <summary>
/// All state of the review workspace in one immutable record, updated with a reducer so that async results never
/// overwrite changes the user made in the meantime.
/// </summary>
sealed record ReviewUiState
{
    public ReviewSession Session { get; init; } = new ReviewSession(string.Empty);

    public IReadOnlyList<ReviewThread> Threads { get; init; } = Array.Empty<ReviewThread>();

    public string? SelectedPath { get; init; }

    public bool ShowSince { get; init; }

    public bool Split { get; init; }

    public string Filter { get; init; } = string.Empty;

    public bool FilterFocused { get; init; }

    public IReadOnlyCollection<string> CollapsedDirs { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ExpandedFiles { get; init; } = Array.Empty<string>();

    /// <summary>Keys "path|gap-N" of expanded context gaps.</summary>
    public IReadOnlyCollection<string> ExpandedGaps { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> FileLines { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public ComposerState? Composer { get; init; }

    public string? ReplyThreadId { get; init; }

    public string? EditCommentId { get; init; }

    public string? FocusedThreadId { get; init; }

    public string? CursorKey { get; init; }

    /// <summary>Resolved threads the reviewer opened again.</summary>
    public IReadOnlyCollection<string> OpenResolved { get; init; } = Array.Empty<string>();

    public bool SubmitOpen { get; init; }

    public bool HelpOpen { get; init; }

    public SuggestionState? Suggestion { get; init; }

    public bool FindOpen { get; init; }

    public string FindQuery { get; init; } = string.Empty;

    /// <summary>Index of the current match, -1 before the first jump.</summary>
    public int FindIndex { get; init; } = -1;

    public bool FindFocused { get; init; }

    /// <summary>Changes whenever the find box or the file filter should take keyboard focus.</summary>
    public int FindFocusNonce { get; init; }

    public int FilterFocusNonce { get; init; }

    /// <summary>Narrow layout: 0 shows the file list, 1 the diff.</summary>
    public int Screen { get; init; }

    public string Error { get; init; } = string.Empty;

    public string Info { get; init; } = string.Empty;

    public bool Busy { get; init; }

    /// <summary>Row key to bring into view; ScrollNonce changes so the same key can be requested twice.</summary>
    public string? ScrollKey { get; init; }

    public int ScrollNonce { get; init; }

    public bool ShowCommits { get; init; }

    public bool ShowResolved { get; init; }

    public bool ShowDescription { get; init; }

    /// <summary>Wrap long code lines instead of cutting them off (off by default so line numbers stay aligned).</summary>
    public bool Wrap { get; init; }

    /// <summary>True while a text box has focus or a dialog is open: single-key shortcuts must stay quiet then.</summary>
    public bool Typing =>
        Composer is not null || ReplyThreadId is not null || EditCommentId is not null || SubmitOpen || HelpOpen || FilterFocused || FindFocused;
}
