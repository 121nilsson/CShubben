using Cshubben.Core.Features.Pipeline;
using Cshubben.Core.Logic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pipeline;

/// <summary>The overview strip and the per-repository cards of the Drift page. Pure functions from data to elements.</summary>
static class DriftCards
{
    private const int MaxRunChips = 3;
    private const int MaxEnvironmentRows = 4;

    /// <summary>The overview strip: how many repositories need attention, run, fail or lag behind.</summary>
    public static Element Strip(DriftReport report)
    {
        Element Tile(string label, int value, string hint, Tone tone) =>
            Card(VStack(2,
                Kit.Muted(label),
                TextBlock(value.ToString()).FontSize(28).SemiBold()
                    .Foreground(value > 0 && tone != Tone.Neutral ? Kit.Fg(tone) : Theme.PrimaryText),
                Kit.Muted(hint).TextWrapping())).Flex(grow: 1, basis: 170);

        return FlexRow(
            Tile("Needs attention", report.NeedAttention, "red main or failed deploy", Tone.Danger),
            Tile("Running now", report.Running, "runs or deploys in progress", Tone.Neutral),
            Tile("Failing workflows", report.FailingWorkflows, "on default branches", Tone.Danger),
            Tile("Behind on deploys", report.ReposBehindDeploy, "repos with undeployed commits", Tone.Warning),
            Tile("Environments differ", report.Rows.Count(r => EnvironmentSync.Describe(r.Environments)?.Differs == true), "test and prod run different commits", Tone.Warning),
            Tile("Unreleased commits", report.UnreleasedCommits, "since the latest releases", Tone.Neutral))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 };
    }

    /// <summary>
    /// One repository: CI on the default branch (one chip per workflow), the latest deployment per environment, how far
    /// the deploy and the release lag behind, and the actions. Colour only where something needs attention.
    /// </summary>
    public static Element RepoCard(RepoRow row, DateTimeOffset now, bool busy, Action rerun, Action open)
    {
        var tone = PipelineKit.ToneOf(row.Health);
        var loud = row.Health is RepoHealth.Attention;
        var stripe = Border(Empty()).Width(4).CornerRadius(2).Background(loud ? Kit.Fg(tone) : Theme.SubtleFill);

        Element status = row.Health switch
        {
            RepoHealth.Attention => Kit.Pill("Needs attention", Tone.Danger),
            RepoHealth.Running => Kit.Pill("Running", Tone.Neutral),
            RepoHealth.Unavailable => Kit.Pill("Unavailable", Tone.Neutral),
            RepoHealth.Quiet => Kit.Muted("Quiet"),
            _ => Kit.Muted("Healthy"),
        };

        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            TextBlock(row.Repo).FontSize(15).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 0),
            status.VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 1));

        var headline = TextBlock(row.Headline)
            .Foreground(row.Health == RepoHealth.Attention ? Kit.Fg(Tone.Danger) : Theme.SecondaryText)
            .TextWrapping();

        var children = new List<Element> { header, headline };

        if (row.Health == RepoHealth.Unavailable)
        {
            children.Add(Kit.Muted(row.Activity.Error ?? "Unknown error").TextWrapping());
        }
        else
        {
            if (row.MainRuns.Count > 0)
            {
                var chips = row.MainRuns.Take(MaxRunChips).Select(r => RunChip(r, now)).ToList();
                if (row.MainRuns.Count > MaxRunChips)
                    chips.Add(Kit.Muted("+" + (row.MainRuns.Count - MaxRunChips)).VAlign(VerticalAlignment.Center)
                        .ToolTip(string.Join("\n", row.MainRuns.Skip(MaxRunChips).Select(r => r.Name + " · " + PipelineKit.Label(r.State)))));
                children.Add(VStack(6, Caption("Default branch"), FlexRow(chips.ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 6 }));
            }

            if (row.Environments.Count > 0)
            {
                var envs = row.Environments.Take(MaxEnvironmentRows).Select(d => EnvironmentRow(d, now)).ToArray();
                var sync = EnvironmentSync.Describe(row.Environments);
                children.Add(VStack(4, Caption("Environments"), FlexColumn(envs) with { RowGap = 2 },
                    sync is null ? null : TextBlock(sync.Text).FontSize(12)
                        .Foreground(sync.Differs ? Kit.Fg(Tone.Warning) : Theme.SecondaryText).TextWrapping()));
            }

            var behind = row.UndeployedCommits is > 0;
            children.Add(TextBlock(row.DeployLine).FontSize(12)
                .Foreground(behind ? Kit.Fg(Tone.Warning) : Theme.SecondaryText).TextWrapping());
            if (row.ReleaseLine is not null) children.Add(Kit.Muted(row.ReleaseLine).TextWrapping());
        }

        children.Add(FlexRow(
            row.FirstFailing is null
                ? null
                : Button(busy ? "Re-running…" : "Re-run", rerun).AccentButton().IsEnabled(!busy)
                    .AutomationName("Re-run the failed workflows of " + row.Repo),
            Button("Open ↗", open).AutomationName("Open " + row.Repo + " on GitHub"),
            busy ? ProgressRing().IsActive().Width(16).Height(16).VAlign(VerticalAlignment.Center) : null)
            with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 });

        return Card(Grid(
                columns: [GridSize.Px(4), GridSize.Star()],
                rows: [GridSize.Auto],
                stripe.Grid(column: 0),
                VStack(10, children.ToArray()).Margin(12, 0, 0, 0).Grid(column: 1)))
            .Flex(grow: 1, basis: 400)
            .WithKey(row.Repo);
    }

    private static Element Caption(string text) =>
        TextBlock(text.ToUpperInvariant()).FontSize(11).SemiBold().Foreground(Theme.SecondaryText);

    private static Element RunChip(WorkflowRun run, DateTimeOffset now)
    {
        var tone = PipelineKit.ToneOf(run.State);
        var content = HStack(6,
            TextBlock("●").FontSize(9).Foreground(Kit.Fg(tone)).VAlign(VerticalAlignment.Center),
            TextBlock(run.Name).TextTrimming(TextTrimming.CharacterEllipsis).MaxWidth(170).VAlign(VerticalAlignment.Center),
            Kit.Muted(PipelineFormat.Ago(run.UpdatedAt, now).Replace(" ago", "")).VAlign(VerticalAlignment.Center));
        return Border(Button(content, () => Cshubben.Ui.Assist.ExternalActions.OpenUrl(run.Url))
                .SubtleButton().ToolTip(run.Name + " · " + PipelineKit.Label(run.State)).AutomationName(run.Name + " " + PipelineKit.Label(run.State)))
            .WithBorder(Theme.ControlStroke).CornerRadius(6)
            .WithKey("run-" + run.Id);
    }

    private static Element EnvironmentRow(Deployment deployment, DateTimeOffset now)
    {
        var detail = PipelineFormat.ShortSha(deployment.Sha)
            + (deployment.Ref.Length > 0 ? " · " + deployment.Ref : "")
            + (deployment.Creator.Length > 0 ? " · " + deployment.Creator : "");
        var row = Grid(
            columns: [GridSize.Px(24), GridSize.Auto, GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            PipelineKit.StateIcon(PipelineKit.AsRunState(deployment.State)).VAlign(VerticalAlignment.Center)
                .ToolTip(PipelineKit.Label(PipelineKit.AsRunState(deployment.State))).Grid(column: 0),
            Kit.Pill(deployment.Environment, Tone.Neutral).VAlign(VerticalAlignment.Center).Grid(column: 1),
            TextBlock(detail).FontSize(12).Foreground(Theme.SecondaryText).TextTrimming(TextTrimming.CharacterEllipsis)
                .VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 2),
            Kit.Muted(PipelineFormat.Ago(deployment.CreatedAt, now)).VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 3));

        Element result = deployment.Url.Length == 0
            ? row
            : Button(row, () => Cshubben.Ui.Assist.ExternalActions.OpenUrl(deployment.Url))
                .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .AutomationName("Open the " + deployment.Environment + " deployment");
        return result.WithKey("env-" + deployment.Id);
    }
}
