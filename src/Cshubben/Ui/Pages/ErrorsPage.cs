using Cshubben.Core.Features.Settings;
using Cshubben.Core.Logic;
using Cshubben.Core.Shared;
using Cshubben.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// Errors: error spikes from the logs, tied to deploys. The page only exists while the module is switched on in Settings.
/// This first version shows the connection and the application-to-repository mappings; the spike view builds on them.
/// </summary>
static class ErrorsPage
{
    public static Element Build(PageContext ctx)
    {
        var errors = ctx.Settings.Errors;
        var hasToken = ctx.Services.Secrets.Get(SecretKeys.GrafanaToken) is not null;
        var connected = errors.GrafanaUrl.Length > 0 && hasToken;

        Element connection = connected
            ? Kit.SettingsCard("Connection", null,
                Kit.Muted("Grafana: " + errors.GrafanaUrl).TextWrapping(),
                Kit.Muted("Spike trends and deploy markers are the next step. They will appear here, per application.").TextWrapping())
            : Kit.Banner(Tone.Accent, "Connect Grafana",
                "Add the Grafana address and a service account token in Settings to start reading the logs.",
                "Open Settings", ctx.OpenSettings);

        var mappings = errors.Bindings.Count == 0
            ? Kit.SettingsCard("Applications", "Each log application is bound to the repository it is built from.",
                Kit.Muted("No applications are bound yet. Add them under Settings > Errors.").TextWrapping())
            : Kit.SettingsCard("Applications", "Each log application is bound to the repository it is built from.",
                VStack(6, errors.Bindings.Select(Row).ToArray()));

        return ScrollView(Kit.Capped(
            FlexColumn(
                Kit.PageHeader("Errors", "Error spikes from your logs, tied to deploys"),
                connection.Margin(0, 0, 0, 12),
                mappings.Margin(0, 0, 0, 12))
            .FlexPadding(24, 16), 800));
    }

    private static Element Row(RepoBinding binding) =>
        Grid(
            columns: [GridSize.Star(), GridSize.Star()],
            rows: [GridSize.Auto],
            BodyStrong(binding.App).Grid(column: 0),
            Kit.Muted(binding.Repo).VAlign(VerticalAlignment.Center).Grid(column: 1))
        .WithKey(binding.App);
}
