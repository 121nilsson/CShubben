namespace Cshubben.Core.Features.Updates;

public enum UpdateStatus
{
    /// <summary>Not an installed copy (dev build, demo, screenshots): updates are off.</summary>
    Unavailable,
    UpToDate,
    Available,
    Failed,
}

/// <summary>Result of one update check. Never thrown: failures are <see cref="UpdateStatus.Failed"/>.</summary>
public sealed record UpdateCheck(
    UpdateStatus Status,
    DateTimeOffset CheckedAt,
    string? NewVersion = null,
    string? Error = null);

/// <summary>
/// Install and update boundary (implemented with Velopack in the UI project). Every member must be safe to call
/// from the UI thread's background tasks and must never throw.
/// </summary>
public interface IUpdateService
{
    /// <summary>False for dev builds, --demo and screenshot runs; then <see cref="CheckAsync"/> returns Unavailable.</summary>
    bool IsEnabled { get; }

    /// <summary>The running version, or "dev" when not installed.</summary>
    string CurrentVersion { get; }

    Task<UpdateCheck> CheckAsync(CancellationToken ct);

    /// <summary>Downloads the update found by the last check. Returns false on failure or when there is none.</summary>
    Task<bool> DownloadAsync(Action<int>? progressPercent, CancellationToken ct);

    /// <summary>Restarts the app into the downloaded update. Does nothing when nothing was downloaded.</summary>
    void ApplyAndRestart();
}
