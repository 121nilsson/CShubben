using System.Globalization;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;

namespace Cshubben.Core.Features.Notifications;

/// <summary>Shows a toast. The Windows implementation lives in the UI project; this is the seam.</summary>
public interface IToastNotifier
{
    /// <summary>False when toasts cannot be shown (demo mode, API unavailable).</summary>
    bool IsAvailable { get; }

    /// <summary>Shows the toast. Never throws.</summary>
    void Show(ToastMessage message);
}

/// <summary>Does nothing. Used in demo mode and when the platform API is unavailable.</summary>
public sealed class NullToastNotifier : IToastNotifier
{
    public bool IsAvailable => false;

    public void Show(ToastMessage message)
    {
    }
}

/// <summary>Remembers what it was asked to show. Used by tests.</summary>
public sealed class RecordingToastNotifier : IToastNotifier
{
    public List<ToastMessage> Shown { get; } = new();

    public bool IsAvailable => true;

    public void Show(ToastMessage message) => Shown.Add(message);
}

/// <summary>Counts shown by the tray icon.</summary>
/// <param name="ReviewsWaiting">Review-queue PRs that need the viewer.</param>
/// <param name="OwnCiFailing">Own PRs with failing CI.</param>
/// <param name="ReadyToMerge">Own PRs that can be merged now.</param>
public sealed record TrayStatus(int ReviewsWaiting, int OwnCiFailing, int ReadyToMerge)
{
    public static TrayStatus Empty { get; } = new(0, 0, 0);

    /// <summary>Review requests that need attention plus failing CI: the number worth a badge.</summary>
    public int NeedsYou => ReviewsWaiting + OwnCiFailing;

    public static TrayStatus From(HubSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var waiting = snapshot.Reviews.Count(p => ReviewQueue.StateOf(snapshot.ReviewInfos.GetValueOrDefault(p.Id)) is
            ReviewQueueState.WaitingOnYou or ReviewQueueState.UpdatedSinceReview);
        var failing = snapshot.Authored.Count(p => p.Ci == CiState.Failure);
        var ready = snapshot.Authored.Count(NotificationDiff.IsReadyToMerge);
        return new TrayStatus(waiting, failing, ready);
    }

    /// <summary>One line per non-zero count ("3 waiting for your review"). Empty when nothing needs the viewer.</summary>
    public IReadOnlyList<string> Lines()
    {
        var parts = new List<string>();
        if (ReviewsWaiting > 0) parts.Add(ReviewsWaiting.ToString(CultureInfo.InvariantCulture) + " waiting for your review");
        if (OwnCiFailing == 1) parts.Add("1 own PR with failing CI");
        else if (OwnCiFailing > 1) parts.Add(OwnCiFailing.ToString(CultureInfo.InvariantCulture) + " own PRs with failing CI");
        if (ReadyToMerge > 0) parts.Add(ReadyToMerge.ToString(CultureInfo.InvariantCulture) + " ready to merge");
        return parts;
    }

    /// <summary>"Cshubben - 3 waiting for your review - 1 own PR with failing CI" (just the name when quiet).</summary>
    public string Tooltip(string appName)
    {
        var parts = Lines();
        var text = parts.Count == 0 ? appName : appName + " - " + string.Join(" - ", parts);
        // The shell truncates tooltips at 127 characters.
        return text.Length <= 120 ? text : text.Substring(0, 117) + "...";
    }
}

/// <summary>The tray icon. Windows implementation in the UI project; this is the seam.</summary>
public interface ITrayIcon
{
    bool IsAvailable { get; }

    /// <summary>When true, closing the window hides it instead of quitting.</summary>
    bool CloseToTray { get; set; }

    /// <summary>Updates counts and tooltip. Never throws.</summary>
    void SetStatus(TrayStatus status);
}

/// <summary>Does nothing. Used in demo mode and when the shell API is unavailable.</summary>
public sealed class NullTrayIcon : ITrayIcon
{
    public bool IsAvailable => false;

    public bool CloseToTray { get; set; }

    public void SetStatus(TrayStatus status)
    {
    }
}

/// <summary>Remembers the last status. Used by tests.</summary>
public sealed class RecordingTrayIcon : ITrayIcon
{
    public TrayStatus Last { get; private set; } = TrayStatus.Empty;

    public bool IsAvailable => true;

    public bool CloseToTray { get; set; }

    public void SetStatus(TrayStatus status) => Last = status;
}

/// <summary>Start with Windows.</summary>
public interface IAutostart
{
    bool IsSupported { get; }

    bool IsEnabled();

    /// <summary>Switches autostart on or off. Returns false (and never throws) when it could not be changed.</summary>
    bool SetEnabled(bool enabled);
}

/// <summary>Autostart that only remembers the flag. Used in demo mode and tests.</summary>
public sealed class InMemoryAutostart : IAutostart
{
    private bool _on;

    public bool IsSupported => true;

    public bool IsEnabled() => _on;

    public bool SetEnabled(bool enabled)
    {
        _on = enabled;
        return true;
    }
}

/// <summary>Autostart that does nothing and is never on.</summary>
public sealed class NullAutostart : IAutostart
{
    public bool IsSupported => false;

    public bool IsEnabled() => false;

    public bool SetEnabled(bool enabled) => false;
}

/// <summary>The command line stored in the Windows Run key. Pure so it can be tested anywhere.</summary>
public static class AutostartCommand
{
    public const string BackgroundFlag = "--background";

    /// <summary>"C:\path\Cshubben.exe" --background</summary>
    public static string Build(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        return "\"" + exePath.Trim().Trim('"') + "\" " + BackgroundFlag;
    }

    /// <summary>The executable path inside a stored command; null when the value is empty.</summary>
    public static string? ExePathOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c.Substring(1, end - 1) : null;
        }

        var space = c.IndexOf(' ');
        return space < 0 ? c : c.Substring(0, space);
    }

    /// <summary>True when the stored command starts the given executable (path compare ignores case).</summary>
    public static bool Matches(string? command, string exePath) =>
        ExePathOf(command) is { } stored && string.Equals(stored, exePath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the app was started by autostart and should begin hidden in the tray.</summary>
    public static bool IsBackgroundLaunch(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, BackgroundFlag, StringComparison.OrdinalIgnoreCase));
}
