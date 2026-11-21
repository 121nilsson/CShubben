using Cshubben.Core.Features.Cache;
using Cshubben.Core.Data;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Feed;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Features.Notes;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Model;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>Outcome of a "Test connection" button.</summary>
sealed record ConnectionResult(bool Ok, string Message);

/// <summary>Where the GitHub token comes from (never the token itself).</summary>
sealed record GitHubTokenInfo(bool Found, string Source);

/// <summary>What was found about the Claude CLI.</summary>
sealed record ClaudeInfo(string? Path, string? Version, string Auth);

/// <summary>
/// The composition root: wires the Core features to real I/O (network, files, credential store) and hands them to the UI.
/// Features never reference each other; this class is the one place where they meet.
/// </summary>
sealed class AppServices
{
    private readonly DemoData _demo = new();
    private HubSnapshot? _initialSnapshot;

    private AppServices(bool forcedDemo, ISettingsStore settingsStore, ISecretStore secrets)
    {
        ForcedDemo = forcedDemo;
        SettingsStore = settingsStore;
        Secrets = secrets;
        Settings = settingsStore.Load();
        Http = new HttpClient();
        // Demo mode pretends the user last looked at the feed seven hours ago, so the new/seen divider is visible.
        FeedSeen = forcedDemo
            ? new InMemoryFeedSeenStore(DateTimeOffset.UtcNow - TimeSpan.FromHours(7))
            : new FileFeedSeenStore(FileFeedSeenStore.DefaultPath(AppPaths.DataDirectory));
    }

    /// <summary>The services of this process. Call <see cref="Initialize"/> once at startup.</summary>
    public static AppServices Current { get; private set; } = Create(forcedDemo: true);

    /// <summary>An empty snapshot for pages that are shown before the first load finishes.</summary>
    public static readonly HubSnapshot EmptySnapshot = new(
        "",
        Array.Empty<PullRequest>(),
        Array.Empty<PullRequest>(),
        new Dictionary<string, ReviewInfo>(),
        Array.Empty<FeedItem>(),
        DateTimeOffset.UtcNow);

    public static AppServices Initialize(bool demo)
    {
        Current = Create(demo);
        return Current;
    }

    private static AppServices Create(bool forcedDemo)
    {
        // Demo mode (screenshots, CI) never touches the user's real settings or credentials.
        ISettingsStore settings = forcedDemo
            ? new InMemorySettingsStore()
            : new FileSettingsStore(FileSettingsStore.DefaultPath(AppPaths.DataDirectory));
        ISecretStore secrets = forcedDemo ? new InMemorySecretStore() : new WindowsSecretStore();
        return new AppServices(forcedDemo, settings, secrets);
    }

    /// <summary>True when --demo was passed.</summary>
    public bool ForcedDemo { get; }

    /// <summary>True when the data on screen is demo data (forced, or because no GitHub token was found).</summary>
    public bool IsDemo => ForcedDemo || GitHub is null;

    /// <summary>Why demo data is shown although --demo was not passed; null otherwise.</summary>
    public string? DemoReason { get; private set; }

    public ISettingsStore SettingsStore { get; }

    public ISecretStore Secrets { get; }

    public HttpClient Http { get; }

    /// <summary>When the user last marked the feed as seen (drives the new/seen divider on the Feed page).</summary>
    public IFeedSeenStore FeedSeen { get; }

    /// <summary>Current settings. The shell keeps this in step with what it shows.</summary>
    public AppSettings Settings { get; set; }

    /// <summary>The live GitHub connection once a token was found: Writer, Rest.LastRateLimit, LastWarnings.</summary>
    public GitHubHubData? GitHub { get; private set; }

    /// <summary>Demo snapshot for --demo (shown immediately, no loading state); null for live mode.</summary>
    private bool _cacheRead;

    public HubSnapshot? InitialSnapshot
    {
        get
        {
            if (!ForcedDemo)
            {
                // Live mode: paint the last real snapshot from disk at once; the first load replaces it a few seconds later.
                if (!_cacheRead)
                {
                    _cacheRead = true;
                    var cached = SnapshotCache.TryLoad(AppPaths.DataDirectory);
                    if (cached is not null) _initialSnapshot = SnapshotFilter.Apply(cached, Settings);
                }
                return _initialSnapshot;
            }
            _initialSnapshot ??= SnapshotFilter.Apply(_demo.Build(), Settings);
            return _initialSnapshot;
        }
    }

    /// <summary>
    /// The data source to load from: demo with --demo, otherwise GitHub when a token can be found, otherwise
    /// demo data with <see cref="DemoReason"/> set. Safe to call again to retry after the user fixed the token.
    /// The returned source drops muted repos and organisations from every snapshot it loads.
    /// </summary>
    public async Task<IHubData> GetHubDataAsync(CancellationToken cancellationToken = default)
    {
        if (ForcedDemo) return new FilteringHubData(_demo, () => Settings);
        if (GitHub is not null) return new FilteringHubData(GitHub, () => Settings);
        try
        {
            GitHub = await GitHubHubData.CreateAsync(Http, null, null, cancellationToken);
            DemoReason = null;
            return new FilteringHubData(GitHub, () => Settings);
        }
        catch (GitHubAuthException ex)
        {
            DemoReason = ex.Message;
            return new FilteringHubData(_demo, () => Settings);
        }
    }

    /// <summary>Forget the GitHub connection so the next load resolves the token again.</summary>
    public void ResetGitHub() => GitHub = null;

    /// <summary>Which source a GitHub token would come from: environment variable, gh CLI, or none.</summary>
    public async Task<GitHubTokenInfo> ProbeGitHubTokenAsync(CancellationToken cancellationToken = default)
    {
        foreach (var name in GitHubToken.EnvironmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                return new GitHubTokenInfo(true, "environment variable " + name);
        }

        try
        {
            var result = await new ProcessCommandRunner()
                .RunAsync("gh", new[] { "auth", "token" }, cancellationToken);
            if (result.ExitCode == 0 && result.StdOut.Trim().Length > 0)
                return new GitHubTokenInfo(true, "GitHub CLI (gh auth login)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // gh is not installed or could not run: no token from there.
        }

        return new GitHubTokenInfo(false, "none");
    }

    /// <summary>Calls GET /user with the live connection and reports the login.</summary>
    public async Task<ConnectionResult> TestGitHubAsync(CancellationToken cancellationToken = default)
    {
        if (ForcedDemo) return new ConnectionResult(false, "Demo mode (--demo): GitHub is not used.");
        try
        {
            await GetHubDataAsync(cancellationToken);
            var gh = GitHub;
            if (gh is null) return new ConnectionResult(false, DemoReason ?? "No GitHub token found.");
            var response = await gh.Rest.GetAsync(GitHubRest.ApiBase + "/user", null, cancellationToken);
            var login = response.Json is { } json ? JsonRead.Str(json, "login") : null;
            return new ConnectionResult(true, login is null ? "Connected." : "Connected as " + login + ".");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionResult(false, ErrorText.Describe(ex));
        }
    }

    /// <summary>Asks Linear who the saved or environment API key belongs to.</summary>
    public async Task<ConnectionResult> TestLinearAsync(CancellationToken cancellationToken = default)
    {
        var key = LinearClient.ResolveApiKey(Secrets);
        if (key is null) return new ConnectionResult(false, "No Linear API key saved.");
        try
        {
            var viewer = await new LinearClient(Http, key).GetViewerAsync(cancellationToken);
            return new ConnectionResult(true, "Connected as " + viewer.Name + ".");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionResult(false, ErrorText.Describe(ex));
        }
    }

    /// <summary>Finds the claude CLI, asks it for its version and reports how it would authenticate.</summary>
    public async Task<ClaudeInfo> CheckClaudeAsync(CancellationToken cancellationToken = default)
    {
        var path = ClaudeLocator.Find();
        var token = ClaudeRunner.ResolveToken(Secrets);
        var auth = token is not null
            ? "Token configured (" + (Environment.GetEnvironmentVariable(ClaudeAuth.TokenEnvVar) is { Length: > 0 } ? "environment" : "saved in Windows") + ")"
            : "No token set; uses the login of the Claude CLI";
        if (path is null) return new ClaudeInfo(null, null, auth);
        try
        {
            var result = await new ProcessCommandRunner().RunAsync(path, new[] { "--version" }, cancellationToken);
            var version = result.ExitCode == 0 ? result.StdOut.Trim() : null;
            return new ClaudeInfo(path, string.IsNullOrEmpty(version) ? null : version, auth);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ClaudeInfo(path, null, auth);
        }
    }

    /// <summary>The notes folder: the setting when set, otherwise the default under the data directory.</summary>
    public string NotesFolder => string.IsNullOrWhiteSpace(Settings.NotesDirectory)
        ? AppPaths.NotesDirectory
        : Settings.NotesDirectory;

    /// <summary>Where settings.json lives.</summary>
    public string DataFolder => AppPaths.DataDirectory;
}
