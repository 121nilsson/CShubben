using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Services;

/// <summary>
/// Glue between the notification rules (Core) and the Windows adapters: toasts, tray icon, autostart.
/// One static entry point so the shell needs only a few lines: <see cref="Initialize"/> at startup,
/// <see cref="OnSnapshot"/> after each refresh, <see cref="TrayElement"/> once in the window.
/// Demo mode gets silent fakes, and every adapter degrades to a no-op when its Windows API is unavailable.
/// </summary>
static class NotificationsHub
{
    private static readonly object Gate = new();
    private static NotificationEngine? _engine;
    private static HubSnapshot? _previous;
    private static WindowsToastNotifier? _windowsToasts;
    private static bool _live;

    public static IToastNotifier Toasts { get; private set; } = new NullToastNotifier();

    public static ITrayIcon Tray { get; private set; } = new NullTrayIcon();

    public static IAutostart Autostart { get; private set; } = new NullAutostart();

    public static INotificationPrefsStore PrefsStore { get; private set; } = new InMemoryNotificationPrefsStore();

    /// <summary>Notification preferences beyond the app settings (quiet hours, extra toggles, close to tray).</summary>
    public static NotificationPrefs Prefs { get; private set; } = new();

    /// <summary>The most recent real snapshot (used to map a toast click to a pull request).</summary>
    public static HubSnapshot? LatestSnapshot { get; private set; }

    /// <summary>Set by the shell: reload the data now (tray menu "Refresh now").</summary>
    public static Action? RefreshRequested { get; set; }

    /// <summary>Set by the shell: open the pull request with this id in the inspector. When null the PR opens on GitHub.</summary>
    public static Action<string>? SelectPr { get; set; }

    /// <summary>True when real adapters are active (not demo mode).</summary>
    public static bool IsLive => _live;

    /// <summary>
    /// Call once at startup, before the window opens.
    /// </summary>
    /// <param name="demo">True for --demo: nothing touches the user's files, registry, toasts or tray.</param>
    /// <param name="dataDirectory">Where the seen-set and preferences are stored.</param>
    /// <param name="args">Command-line arguments (looks for --background).</param>
    public static void Initialize(bool demo, string dataDirectory, IEnumerable<string> args)
    {
        lock (Gate)
        {
            _previous = null;
            if (demo || string.IsNullOrWhiteSpace(dataDirectory))
            {
                _live = false;
                _engine = new NotificationEngine(new InMemoryNotificationState());
                Toasts = new NullToastNotifier();
                Tray = new NullTrayIcon();
                Autostart = new InMemoryAutostart();
                PrefsStore = new InMemoryNotificationPrefsStore();
                Prefs = PrefsStore.Load();
                return;
            }

            _live = true;
            try
            {
                _engine = new NotificationEngine(new FileNotificationState(FileNotificationState.DefaultPath(dataDirectory)));
                PrefsStore = new FileNotificationPrefsStore(FileNotificationPrefsStore.DefaultPath(dataDirectory));
                Prefs = PrefsStore.Load();
            }
            catch (Exception)
            {
                _engine = new NotificationEngine(new InMemoryNotificationState());
                PrefsStore = new InMemoryNotificationPrefsStore();
                Prefs = PrefsStore.Load();
            }

            try
            {
                _windowsToasts = new WindowsToastNotifier(HandleToastClick);
                Toasts = _windowsToasts;
            }
            catch (Exception)
            {
                Toasts = new NullToastNotifier();
            }

            Autostart = new WindowsAutostart();
            Tray = TrayController.Instance;
            Tray.CloseToTray = Prefs.CloseToTray;
            TrayController.StartHidden = AutostartCommand.IsBackgroundLaunch(args);
        }
    }

    /// <summary>
    /// Call after every successful refresh (not from Render). Updates the tray counts and shows toasts for what changed
    /// since the previous snapshot. Safe to call with the same snapshot twice. Never throws.
    /// </summary>
    /// <param name="snapshot">The data just loaded.</param>
    /// <param name="settings">Current app settings (master switch, review/CI/changes toggles, mute lists).</param>
    /// <param name="isDemoData">True when the snapshot is demo data: only the tray is updated and the next real snapshot becomes a fresh baseline.</param>
    public static void OnSnapshot(HubSnapshot snapshot, AppSettings settings, bool isDemoData = false)
    {
        try
        {
            lock (Gate)
            {
                if (ReferenceEquals(snapshot, _previous)) return;
                Tray.SetStatus(TrayStatus.From(snapshot));
                if (isDemoData)
                {
                    _previous = null;
                    return;
                }

                var before = _previous;
                _previous = snapshot;
                LatestSnapshot = snapshot;
                if (_engine is null || !Toasts.IsAvailable) return;

                var plan = _engine.Plan(before, snapshot, Effective(settings, Prefs), DateTimeOffset.Now);
                foreach (var message in plan.Messages) Toasts.Show(message);
            }
        }
        catch (Exception)
        {
            // Notifications are a convenience; a bug here must never break refreshing.
        }
    }

    /// <summary>The preferences the engine uses: the app settings win for the master switch, the three classic toggles and the mute lists.</summary>
    public static NotificationPrefs Effective(AppSettings settings, NotificationPrefs prefs) => prefs with
    {
        Enabled = settings.Notifications,
        ReviewRequested = settings.NotifyReviewRequested && prefs.ReviewRequested,
        ReReviewRequested = settings.NotifyReviewRequested && prefs.ReReviewRequested,
        CiFailed = settings.NotifyCiFailed && prefs.CiFailed,
        ChangesRequested = settings.NotifyChangesRequested && prefs.ChangesRequested,
        MutedRepos = settings.MutedRepos,
        MutedOrgs = settings.MutedOrgs,
        ImportantRepos = settings.ImportantRepos,
    };

    /// <summary>Saves new preferences and applies close-to-tray immediately.</summary>
    public static void SavePrefs(NotificationPrefs next)
    {
        var clean = next.Sanitized();
        lock (Gate)
        {
            Prefs = clean;
            Tray.CloseToTray = clean.CloseToTray;
        }

        try
        {
            PrefsStore.Save(clean);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the choice for this session even if the file cannot be written.
        }
    }

    /// <summary>A toast was clicked (any thread): bring the window up and open the pull request.</summary>
    public static void HandleToastClick(string? url)
    {
        TrayController.ShowMainWindow();
        if (string.IsNullOrWhiteSpace(url)) return;
        var pr = LatestSnapshot?.All.FirstOrDefault(p => string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase));
        var select = SelectPr;
        if (pr is not null && select is not null)
        {
            var id = pr.Id;
            TrayController.RunOnUi(() => select(id));
            return;
        }

        Shell.OpenUrl(url);
    }

    /// <summary>Shows a sample toast so the user can see what notifications look like. Returns false when toasts are unavailable.</summary>
    public static bool ShowTest()
    {
        if (!Toasts.IsAvailable) return false;
        Toasts.Show(new ToastMessage("Notifications are on", "This is how Cshubben tells you about pull requests.", null, false, 1, null));
        return true;
    }

    /// <summary>Mount this once inside the window (for example as the last child of the root Grid). Empty in demo mode.</summary>
    public static Element TrayElement()
    {
        if (!_live) return Empty();
        return Component<TrayHost>();
    }

    /// <summary>Call when the app exits (the tray Quit item does).</summary>
    public static void Shutdown()
    {
        try
        {
            _windowsToasts?.Shutdown();
        }
        catch (Exception)
        {
            // Exiting anyway.
        }
    }
}
