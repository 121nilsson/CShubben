using System.Globalization;
using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using FailureInfo = Cshubben.Core.Shared.Failure;
using FailureKindInfo = Cshubben.Core.Shared.FailureKind;

namespace Cshubben.Ui;

/// <summary>
/// Calm, actionable error presentation: what happened, what to do, and the buttons to do it (Retry, Open Settings).
/// The wording comes from the tested classifier in Core; this class only lays it out.
/// </summary>
static class FailureView
{
    /// <summary>Waiting problems are amber; things the user has to fix are red.</summary>
    public static Tone ToneOf(FailureInfo failure) => failure.Kind switch
    {
        FailureKindInfo.RateLimited or FailureKindInfo.Offline or FailureKindInfo.Timeout or FailureKindInfo.ServerError => Tone.Warning,
        _ => Tone.Danger,
    };

    /// <summary>The classifier's message plus, for rate limits, the local clock time to retry at.</summary>
    public static string MessageOf(FailureInfo failure) =>
        failure.RetryAt is { } at && failure.Kind == FailureKindInfo.RateLimited
            ? failure.Message + " Around " + at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + " local time."
            : failure.Message;

    /// <summary>Retry and Settings buttons as the failure calls for.</summary>
    public static Element[] Buttons(FailureInfo failure, Action? retry, Action? openSettings, bool settingsPrimary = true)
    {
        var buttons = new List<Element>();
        if (failure.NeedsSettings && openSettings is not null)
        {
            var settings = settingsPrimary
                ? Button("Open Settings", openSettings).AccentButton()
                : Button("Open Settings", openSettings);
            buttons.Add(settings.VAlign(VerticalAlignment.Center).AutomationName("Open Settings"));
        }

        if (failure.CanRetry && retry is not null)
            buttons.Add(Button("Retry", retry).VAlign(VerticalAlignment.Center).AutomationName("Retry"));
        if (!failure.NeedsSettings && openSettings is not null && failure.Kind == FailureKindInfo.Unknown)
            buttons.Add(Button("Run checks", openSettings).VAlign(VerticalAlignment.Center).AutomationName("Open Settings to run checks"));
        return buttons.ToArray();
    }

    /// <summary>A message strip with any number of buttons on the right.</summary>
    public static Element Strip(Tone tone, string title, string message, params Element[] buttons) =>
        Border(Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                VStack(2,
                    BodyStrong(title),
                    TextBlock(message).FontSize(12).Foreground(Theme.SecondaryText).TextWrapping()).Grid(column: 0),
                buttons.Length == 0
                    ? Empty().Grid(column: 1)
                    : HStack(8, buttons).Margin(12, 0, 0, 0).Grid(column: 1)))
            .Background(Kit.Bg(tone)).CornerRadius(8).Padding(14, 10);

    public static Element Banner(FailureInfo failure, Action? retry, Action? openSettings) =>
        Strip(ToneOf(failure), failure.Title, MessageOf(failure), Buttons(failure, retry, openSettings));

    /// <summary>
    /// Full-page state for "the first load failed": replaces the spinner so the page is never stuck, and says what to do.
    /// </summary>
    public static Element Panel(FailureInfo failure, Action? retry, Action? openSettings) =>
        ScrollView(Kit.Capped(
            FlexColumn(
                Kit.PageHeader("Could not load your data", "Nothing is wrong with your account until proven otherwise; here is what happened."),
                Card(VStack(12,
                    BodyStrong(failure.Title),
                    TextBlock(MessageOf(failure)).Foreground(Theme.SecondaryText).TextWrapping(),
                    HStack(8, Buttons(failure, retry, openSettings)),
                    Kit.Muted("Settings has a \"Run checks\" button that tests GitHub, Linear and Claude one by one and gives you a report to copy.")
                        .TextWrapping())))
            .FlexPadding(24, 16)));
}
