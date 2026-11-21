using Cshubben.Core.Features.Updates;

namespace Cshubben.Services;

/// <summary>
/// Update service backed by Velopack and the GitHub Releases of this repository. Only active in an installed copy
/// (one made by the Setup.exe from a release); everywhere else it reports <see cref="UpdateStatus.Unavailable"/>.
/// Nothing here throws and nothing runs at startup: the manager is only created, the network is only used on a check.
/// </summary>
sealed class VelopackUpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/121nilsson/Cshubben";

    private readonly Velopack.UpdateManager? _manager;
    private Velopack.UpdateInfo? _pending;

    public VelopackUpdateService()
    {
        try
        {
            var manager = new Velopack.UpdateManager(new Velopack.Sources.GithubSource(RepoUrl, null, false));
            if (manager.IsInstalled) _manager = manager;
        }
        catch
        {
            _manager = null;
        }
    }

    public bool IsEnabled => _manager is not null;

    public string CurrentVersion
    {
        get
        {
            try { return _manager?.CurrentVersion?.ToString() ?? "dev"; }
            catch { return "dev"; }
        }
    }

    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_manager is null) return new UpdateCheck(UpdateStatus.Unavailable, now);
        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            _pending = info;
            return info is null
                ? new UpdateCheck(UpdateStatus.UpToDate, now)
                : new UpdateCheck(UpdateStatus.Available, now, info.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            return new UpdateCheck(UpdateStatus.Failed, now, Error: ex.Message);
        }
    }

    public async Task<bool> DownloadAsync(Action<int>? progressPercent, CancellationToken ct)
    {
        if (_manager is null || _pending is null) return false;
        try
        {
            await _manager.DownloadUpdatesAsync(_pending, progressPercent, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ApplyAndRestart()
    {
        if (_manager is null || _pending is null) return;
        try { _manager.ApplyUpdatesAndRestart(_pending); }
        catch { /* stay on the current version */ }
    }
}

/// <summary>Disabled stand-in: dev builds, --demo and screenshots.</summary>
sealed class NoUpdateService : IUpdateService
{
    public bool IsEnabled => false;
    public string CurrentVersion => "dev";
    public Task<UpdateCheck> CheckAsync(CancellationToken ct) =>
        Task.FromResult(new UpdateCheck(UpdateStatus.Unavailable, DateTimeOffset.UtcNow));
    public Task<bool> DownloadAsync(Action<int>? progressPercent, CancellationToken ct) => Task.FromResult(false);
    public void ApplyAndRestart() { }
}

/// <summary>Holds the one update service. Call <see cref="Initialize"/> once at startup.</summary>
static class UpdatesHost
{
    public static IUpdateService Service { get; private set; } = new NoUpdateService();

    /// <param name="disabled">True for --demo, --smoke and screenshot runs.</param>
    public static void Initialize(bool disabled)
    {
        if (disabled) return;
        try { Service = new VelopackUpdateService(); }
        catch { Service = new NoUpdateService(); }
    }
}
