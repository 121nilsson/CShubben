using Cshubben.Core.Shared;
namespace Cshubben.Core.Features.Linear;

public enum LinearStateType { Unknown, Triage, Backlog, Unstarted, Started, Completed, Canceled }

public sealed record LinearViewer(string Id, string Name, string? Email);

public sealed record LinearTeam(string Id, string Key, string Name);

public sealed record LinearLabel(string Id, string Name);

/// <summary>A workflow state of a team (Todo, In Progress, Done, ...). <paramref name="Position"/> orders states of one type.</summary>
public sealed record LinearState(string Id, string Name, LinearStateType Type, double Position);

/// <summary>A Linear issue. Priority follows Linear: 0 none, 1 urgent, 2 high, 3 medium, 4 low.</summary>
public sealed record LinearIssue(
    string Id,
    string Identifier,
    string Title,
    string StateName,
    LinearStateType StateType,
    string? Assignee,
    int Priority,
    string TeamKey,
    string? Project,
    IReadOnlyList<string> Labels,
    string Url,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string Description = "",
    string? AssigneeAvatarUrl = null)
{
    /// <summary>Completed or canceled.</summary>
    public bool IsDone => StateType is LinearStateType.Completed or LinearStateType.Canceled;

    public bool IsInProgress => StateType == LinearStateType.Started;

    public string PriorityLabel => Priority switch
    {
        1 => "Urgent",
        2 => "High",
        3 => "Medium",
        4 => "Low",
        _ => "No priority",
    };
}

public sealed record LinearPage<T>(IReadOnlyList<T> Items, bool HasNextPage, string? EndCursor);

/// <summary>What Linear last told us about the rate limit.</summary>
public sealed record LinearRateLimit(int? RequestsRemaining, DateTimeOffset? ResetsAt);

public sealed record LinearRequest(string Query, IReadOnlyDictionary<string, object?> Variables);

public class LinearException : Exception
{
    public LinearException(string message) : base(message)
    {
    }

    public LinearException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>The API key is missing or was rejected.</summary>
public sealed class LinearAuthException : LinearException
{
    public LinearAuthException(string message) : base(message)
    {
    }
}

public sealed class LinearRateLimitException : LinearException
{
    public LinearRateLimitException(string message, TimeSpan? retryAfter) : base(message)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}

/// <summary>Linear answered, but with GraphQL errors.</summary>
public sealed class LinearApiException : LinearException
{
    public LinearApiException(string message, IReadOnlyList<string> messages, bool isNotFound) : base(message)
    {
        Messages = messages;
        IsNotFound = isNotFound;
    }

    public IReadOnlyList<string> Messages { get; }

    public bool IsNotFound { get; }
}

public sealed class LinearHttpException : LinearException
{
    public LinearHttpException(int statusCode) : base("Linear answered with HTTP " + statusCode + ".")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
