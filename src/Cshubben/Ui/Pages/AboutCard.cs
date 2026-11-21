using Cshubben.Core.Features.Updates;
using Cshubben.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// "About" card for the Settings page: version, last update check and a "Check for updates" button.
/// Use: <c>AboutCard.Build()</c>. Checks only happen on a click, never at startup.
/// </summary>
sealed class AboutCard : Component
{
    public static Element Build() => Component<AboutCard>();

    // Survives the page being rebuilt.
    private static UpdateCheck? _last;

    public override Element Render()
    {
        var service = UpdatesHost.Service;
        var (check, setCheck) = UseState(_last, true);
        var (busy, setBusy) = UseState(false, true);
        var (downloaded, setDownloaded) = UseState(false, true);
        var (message, setMessage) = UseState("", true);

        async Task Check()
        {
            setBusy(true);
            setMessage("");
            var result = await service.CheckAsync(CancellationToken.None);
            _last = result;
            setCheck(result);
            setBusy(false);
        }

        async Task Download()
        {
            setBusy(true);
            setMessage("Downloading...");
            var ok = await service.DownloadAsync(p => setMessage($"Downloading... {p}%"), CancellationToken.None);
            setDownloaded(ok);
            setMessage(ok ? "Ready to install." : "The download failed. Try again.");
            setBusy(false);
        }

        var status = message.Length > 0 ? message : UpdateText.Status(check);
        var available = check?.Status == UpdateStatus.Available;

        Element ActionButton() =>
            downloaded
                ? Button("Restart to update", () => service.ApplyAndRestart())
                : available
                    ? Button("Download update", () => _ = Download()).IsEnabled(!busy)
                    : Button("Check for updates", () => _ = Check()).IsEnabled(!busy && service.IsEnabled);

        return Kit.SettingsCard("About",
            "Cshubben installs and updates itself from GitHub Releases.",
            HStack(8, Kit.Muted("Version").VAlign(VerticalAlignment.Center), Body(service.CurrentVersion)),
            HStack(8, Kit.Muted("Last checked").VAlign(VerticalAlignment.Center),
                Body(UpdateText.LastChecked(check?.CheckedAt, DateTimeOffset.UtcNow))),
            HStack(8, ActionButton(),
                status.Length > 0 ? Kit.Muted(status).VAlign(VerticalAlignment.Center) : Empty()));
    }
}
