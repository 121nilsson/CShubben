using Cshubben.Core.Features.Notifications;
using Cshubben.Ui;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Services;

/// <summary>
/// The state behind the tray icon: counts for the tooltip and menu, the close-to-tray choice, and helpers to
/// show the main window or quit. Services talk to this through <see cref="ITrayIcon"/>; <see cref="TrayHost"/> renders it.
/// </summary>
sealed class TrayController : ITrayIcon
{
    public static TrayController Instance { get; } = new();

    /// <summary>Set before a real quit so the window closes instead of hiding to the tray.</summary>
    public static volatile bool AllowExit;

    /// <summary>True when the app was started with --background: the window stays hidden until the tray is used.</summary>
    public static bool StartHidden { get; set; }

    private TrayController()
    {
    }

    public bool IsAvailable { get; set; }

    public bool CloseToTray { get; set; }

    public TrayStatus Status { get; private set; } = TrayStatus.Empty;

    /// <summary>Raised (possibly on a background thread) when <see cref="Status"/> changed.</summary>
    public event Action? Changed;

    public void SetStatus(TrayStatus status)
    {
        Status = status;
        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // A broken subscriber must not break the refresh loop.
        }
    }

    /// <summary>Runs on the UI thread, swallowing errors (the window may already be gone).</summary>
    public static void RunOnUi(Action action)
    {
        void Guarded()
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // Window or shell already closed.
            }
        }

        var dispatcher = ReactorApp.UIDispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess) Guarded();
        else dispatcher.TryEnqueue(Guarded);
    }

    /// <summary>Shows, restores and focuses the main window.</summary>
    public static void ShowMainWindow() => RunOnUi(() =>
    {
        var window = ReactorApp.PrimaryWindow;
        if (window is null) return;
        window.Show();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
        window.Activate();
    });

    /// <summary>Really quits (also when close-to-tray is on).</summary>
    public static void Quit() => RunOnUi(() =>
    {
        AllowExit = true;
        NotificationsHub.Shutdown();
        ReactorApp.Exit(0);
    });
}

/// <summary>
/// Component that owns the notification-area icon (Reactor's <c>UseTrayIcon</c>). Mount it once inside the window,
/// through <see cref="NotificationsHub.TrayElement"/>. It renders nothing visible.
/// </summary>
sealed class TrayHost : Component
{
    private static bool _hiddenOnce;

    public override Element Render()
    {
        var controller = TrayController.Instance;
        var (status, setStatus) = UseState(controller.Status, true);
        var window = UseWindow();
        var icons = UseMemo(() => new[]
        {
            WindowIcon.FromRgba(TrayIconImage.Rgba(32, false), 32, 32),
            WindowIcon.FromRgba(TrayIconImage.Rgba(32, true), 32, 32),
        }, Array.Empty<object>());

        var tray = UseTrayIcon(new TrayIconSpec(
            status.NeedsYou > 0 ? icons[1] : icons[0],
            status.Tooltip("Cshubben"),
            WindowKey.Of("cshubben-tray")));

        // Status pushed by the notification hub after each refresh.
        UseEffect(() =>
        {
            void OnChanged() => setStatus(controller.Status);
            controller.Changed += OnChanged;
            OnChanged();
            return () => controller.Changed -= OnChanged;
        }, Array.Empty<object>());

        // Left click opens the window, right click shows the menu.
        UseEffect(() =>
        {
            if (tray is null)
            {
                controller.IsAvailable = false;
                return () => { };
            }

            controller.IsAvailable = true;
            var icon = tray;
            EventHandler onClick = (_, _) => TrayController.ShowMainWindow();
            EventHandler onRight = (_, _) => TrayController.RunOnUi(() =>
                icon.ShowFlyout(TrayMenu.Build(controller.Status, () => TrayController.RunOnUi(icon.HideFlyout))));
            icon.Click += onClick;
            icon.RightClick += onRight;
            return () =>
            {
                icon.Click -= onClick;
                icon.RightClick -= onRight;
            };
        }, tray ?? (object)"no-tray");

        // Close to tray: hide instead of closing, only for a real user close and only while the icon exists.
        UseEffect(() =>
        {
            if (window is null || tray is null) return () => { };
            EventHandler<WindowClosingEventArgs> onClosing = (_, e) =>
            {
                if (TrayController.AllowExit || !controller.CloseToTray || e.Reason != WindowCloseReason.UserClosed) return;
                e.Cancel = true;
                window.Hide();
            };
            window.Closing += onClosing;
            return () => window.Closing -= onClosing;
        }, window, tray);

        // Started by autostart (--background): begin hidden, once.
        UseEffect(() =>
        {
            if (TrayController.StartHidden && !_hiddenOnce && window is not null && tray is not null)
            {
                _hiddenOnce = true;
                window.Hide();
            }

            return () => { };
        }, window, tray);

        return Empty();
    }
}

/// <summary>The right-click menu: Open, Refresh, the counts, Quit.</summary>
static class TrayMenu
{
    public static Element Build(TrayStatus status, Action close)
    {
        Element Item(string label, Action action) =>
            Button(TextBlock(label), () =>
                {
                    close();
                    action();
                })
                .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Left)
                .AutomationName(label);

        var lines = status.Lines();
        var summary = new List<Element>();
        if (lines.Count == 0) summary.Add(Kit.Muted("Nothing needs you right now"));
        else foreach (var line in lines) summary.Add(Kit.Muted(line));

        return Border(VStack(2,
                Item("Open Cshubben", TrayController.ShowMainWindow),
                Item("Refresh now", () => NotificationsHub.RefreshRequested?.Invoke()),
                Border(Empty()).Height(1).Background(Theme.SurfaceStroke).Margin(0, 6, 0, 6),
                VStack(2, summary.ToArray()).Margin(12, 0, 12, 4),
                Border(Empty()).Height(1).Background(Theme.SurfaceStroke).Margin(0, 6, 0, 6),
                Item("Quit", TrayController.Quit)))
            .Padding(8).Width(264);
    }
}

/// <summary>Draws the tray icon in code so no image file has to ship: an accent square with a diamond, plus an optional alert dot.</summary>
static class TrayIconImage
{
    public static byte[] Rgba(int size, bool alert)
    {
        var pixels = new byte[size * size * 4];
        var centre = (size - 1) / 2.0;
        var radius = size * 0.22;
        var diamond = size * 0.28;
        var dotX = size * 0.74;
        var dotY = size * 0.26;
        var dotOuter = size * 0.22;
        var dotInner = size * 0.15;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                if (!InsideRounded(x, y, size, radius)) continue;

                byte r = 37, g = 99, b = 235;
                if (Math.Abs(x - centre) + Math.Abs(y - centre) <= diamond)
                {
                    r = 255;
                    g = 255;
                    b = 255;
                }

                if (alert)
                {
                    var d = Math.Sqrt((x - dotX) * (x - dotX) + (y - dotY) * (y - dotY));
                    if (d <= dotInner)
                    {
                        r = 239;
                        g = 68;
                        b = 68;
                    }
                    else if (d <= dotOuter)
                    {
                        r = 255;
                        g = 255;
                        b = 255;
                    }
                }

                pixels[i] = r;
                pixels[i + 1] = g;
                pixels[i + 2] = b;
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static bool InsideRounded(int x, int y, int size, double radius)
    {
        var cx = x < radius ? radius : x > size - 1 - radius ? size - 1 - radius : x;
        var cy = y < radius ? radius : y > size - 1 - radius ? size - 1 - radius : y;
        var dx = x - cx;
        var dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }
}
