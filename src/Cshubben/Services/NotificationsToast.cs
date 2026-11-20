using Cshubben.Core.Features.Notifications;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Cshubben.Services;

/// <summary>
/// Windows toast notifications through the Windows App SDK (works for unpackaged apps). Every call is guarded:
/// when registration or showing fails the app simply has no toasts, it never crashes.
/// </summary>
sealed class WindowsToastNotifier : IToastNotifier
{
    private const int MaxTitle = 120;
    private const int MaxBody = 400;

    private bool _registered;

    public WindowsToastNotifier(Action<string?> onClicked)
    {
        Clicked = onClicked;
        Register();
    }

    /// <summary>Called (on a background thread) with the pull request URL, or null for a digest.</summary>
    public Action<string?> Clicked { get; }

    public bool IsAvailable => _registered;

    private void Register()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnInvoked;
            manager.Register();
            _registered = true;
        }
        catch (Exception)
        {
            // Not supported here (older Windows, missing runtime): carry on without toasts.
            _registered = false;
        }
    }

    public void Show(ToastMessage message)
    {
        if (!_registered) return;
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(Limit(message.Title, MaxTitle))
                .AddText(Limit(message.Body, MaxBody));
            builder = string.IsNullOrWhiteSpace(message.Url)
                ? builder.AddArgument("open", "app")
                : builder.AddArgument("url", message.Url);
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception)
        {
            // A failed toast must never take the app down.
        }
    }

    /// <summary>Unregisters at exit so Windows stops routing activations to a dead process.</summary>
    public void Shutdown()
    {
        if (!_registered) return;
        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception)
        {
            // Nothing useful to do while exiting.
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            string? url = null;
            if (args.Arguments is { } arguments && arguments.TryGetValue("url", out var value)) url = value;
            Clicked(url);
        }
        catch (Exception)
        {
            // Ignore: a click handler must not throw into the notification platform.
        }
    }

    private static string Limit(string text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text.Substring(0, max - 1) + "…";
}
