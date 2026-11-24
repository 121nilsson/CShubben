using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.Linear;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <param name="Draft">The issue to create. Its description is already redacted.</param>
/// <param name="Key">Identifies the error group, so the state resets when another one is shown.</param>
sealed record ErrorIssueProps(ErrorIssueDraft Draft, string Key);

/// <summary>
/// "Create Linear issue" for one error group: pick a team, create, then link to the new issue. Nothing is fetched from Linear
/// until the person opens it. Hidden entirely when Linear is not connected.
/// </summary>
sealed class ErrorIssueButton : Component<ErrorIssueProps>
{
    public override Element Render()
    {
        var linear = IntegrationServices.Linear;
        if (!linear.IsConnected) return Empty();

        var (open, setOpen) = UseState(false);
        var (team, setTeam) = UseState(0);
        var (busy, setBusy) = UseState(false, true);
        var (created, setCreated) = UseState<LinearIssue?>(null, true);
        var (error, setError) = UseState("", true);

        var teamsResource = UseResource<IReadOnlyList<LinearTeam>>(
            async ct => open ? await linear.GetTeamsAsync(ct) : Array.Empty<LinearTeam>(),
            new object[] { Props.Key, open });
        var teams = Parts.Peek(teamsResource) ?? Array.Empty<LinearTeam>();

        async Task Create()
        {
            if (teams.Count == 0) return;
            setBusy(true);
            setError("");
            try
            {
                var chosen = teams[Math.Clamp(team, 0, teams.Count - 1)];
                setCreated(await linear.CreateIssueAsync(chosen.Id, Props.Draft.Title, Props.Draft.Description, Props.Draft.Priority, CancellationToken.None));
                setOpen(false);
            }
            catch (Exception ex)
            {
                setError(ex.Message);
            }

            setBusy(false);
        }

        if (created is not null)
        {
            return FlexRow(
                Kit.Muted("Created " + created.Identifier + "."),
                Button("Open in Linear", () => ExternalActions.OpenUrl(created.Url)).SubtleButton()
                    .AutomationName("Open " + created.Identifier + " in Linear"))
                with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 };
        }

        if (!open)
        {
            return Button("Create Linear issue", () => setOpen(true)).AutomationName("Create a Linear issue for this error");
        }

        var names = teams.Select(t => t.Key + " · " + t.Name).ToArray();
        return VStack(6,
            FlexRow(
                names.Length > 0
                    ? ComboBox(names, Math.Clamp(team, 0, names.Length - 1), setTeam).MinWidth(180)
                    : Kit.Muted(Parts.IsLoading(teamsResource) ? "Loading teams…" : "No teams found."),
                Button("Create issue", () => _ = Create()).AccentButton().IsEnabled(!busy && names.Length > 0),
                Button("Cancel", () => setOpen(false)).SubtleButton().IsEnabled(!busy))
                with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 },
            error.Length > 0 ? Kit.Muted(error).TextWrapping() : null);
    }
}
