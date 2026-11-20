using Cshubben.Core.Features.Notifications;
using Cshubben.Ui;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Services;

/// <summary>
/// Settings card for what the app settings do not cover: more notification types, quiet hours, the tray and autostart.
/// Place it right under the existing "Notifications" card: <c>Component&lt;NotificationsSettingsCard&gt;()</c>.
/// </summary>
sealed class NotificationsSettingsCard : Component
{
    private static readonly string[] Hours = Enumerable.Range(0, 24).Select(h => h.ToString("00") + ":00").ToArray();

    public override Element Render()
    {
        var (prefs, setPrefs) = UseState(NotificationsHub.Prefs);
        var (autostart, setAutostart) = UseState(NotificationsHub.Autostart.IsEnabled());
        var (message, setMessage) = UseState("");

        void Save(NotificationPrefs next)
        {
            NotificationsHub.SavePrefs(next);
            setPrefs(NotificationsHub.Prefs);
        }

        CheckBoxElement Check(string label, bool value, Func<NotificationPrefs, bool, NotificationPrefs> apply) =>
            CheckBox((bool?)value, v => Save(apply(prefs, v)), label);

        var startHour = Math.Min(23, prefs.QuietStartMinute / 60);
        var endHour = Math.Min(23, prefs.QuietEndMinute / 60);

        Element QuietRow() => HStack(8,
            Kit.Muted("From").VAlign(VerticalAlignment.Center),
            ComboBox(Hours, startHour, i => Save(prefs with { QuietStartMinute = i * 60 })).AutomationName("Quiet hours start"),
            Kit.Muted("to").VAlign(VerticalAlignment.Center),
            ComboBox(Hours, endHour, i => Save(prefs with { QuietEndMinute = i * 60 })).AutomationName("Quiet hours end"));

        var trayAvailable = NotificationsHub.IsLive;
        var autostartAvailable = NotificationsHub.Autostart.IsSupported;

        return Kit.SettingsCard("More notification options",
            "What else is worth a toast, when to stay quiet, and how the app lives in the tray. More than "
            + prefs.MaxIndividual + " changes at once become a single summary toast.",
            Check("Someone asks me to review again", prefs.ReReviewRequested, (p, v) => p with { ReReviewRequested = v }),
            Check("Ready to merge (approved, green, no conflicts)", prefs.ReadyToMerge, (p, v) => p with { ReadyToMerge = v }),
            Check("New comments on my pull requests", prefs.Comment, (p, v) => p with { Comment = v }),
            Check("I am @mentioned", prefs.Mention, (p, v) => p with { Mention = v }),
            Check("My pull request was merged", prefs.Merged, (p, v) => p with { Merged = v }),
            Check("Quiet hours (held back, then shown when they end)", prefs.QuietHoursEnabled, (p, v) => p with { QuietHoursEnabled = v }),
            prefs.QuietHoursEnabled ? QuietRow() : Empty(),
            Check("Closing the window keeps Cshubben running in the tray", prefs.CloseToTray, (p, v) => p with { CloseToTray = v })
                .IsEnabled(trayAvailable),
            CheckBox((bool?)autostart, v =>
                {
                    var ok = NotificationsHub.Autostart.SetEnabled(v);
                    setAutostart(NotificationsHub.Autostart.IsEnabled());
                    setMessage(ok ? "" : "Could not change the Windows startup entry.");
                }, "Start Cshubben when I sign in to Windows (hidden in the tray)")
                .IsEnabled(autostartAvailable),
            HStack(8,
                Button("Send a test notification", () =>
                    setMessage(NotificationsHub.ShowTest() ? "" : "Notifications are not available here.")),
                message.Length > 0 ? Kit.Muted(message).VAlign(VerticalAlignment.Center) : Empty()));
    }
}
