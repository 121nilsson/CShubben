using Cshubben.Core.Features.Notifications;
using Microsoft.Win32;

namespace Cshubben.Services;

/// <summary>
/// Starts the app at Windows sign-in through the per-user Run key (no admin rights, no installer).
/// The stored command carries <c>--background</c> so the app starts hidden in the tray.
/// </summary>
sealed class WindowsAutostart : IAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Cshubben";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return AutostartCommand.Matches(key?.GetValue(ValueName) as string, ExePath());
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null) return false;
            if (enabled) key.SetValue(ValueName, AutostartCommand.Build(ExePath()), RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ExePath() => Environment.ProcessPath ?? "";
}
