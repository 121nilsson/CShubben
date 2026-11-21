using System.Globalization;
using Cshubben.Core.Composition;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Digest;
using Cshubben.Core.Features.Notes;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Model;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// "What happened": pick a period (yesterday, week, month, year), read the merged pull requests of the watched repositories from
/// GitHub and let Claude summarize them or answer a question about them. Runs only when asked. Model and effort default to the
/// cheap profile of the Activity function and can be changed for one run here.
/// </summary>
sealed class DigestPage : Component<PageContext>
{
    private sealed record Output(string Text, string Model, double CostUsd, TimeSpan Duration, int PrCount, bool Truncated, string Label);

    public override Element Render()
    {
        var ctx = Props;
        var snap = ctx.Snapshot;
        var settings = ctx.Settings;
        var claude = IntegrationServices.Claude;

        var (period, setPeriod) = UseState(DigestPeriod.Yesterday);
        var (onlyMine, setOnlyMine) = UseState(false);
        var (onlyImportant, setOnlyImportant) = UseState(false);
        var (question, setQuestion) = UseState("");
        var (modelChoice, setModelChoice) = UseState(0);
        var (effortChoice, setEffortChoice) = UseState(0);
        var (running, setRunning) = UseState(false, true);
        var (error, setError) = UseState("", true);
        var (output, setOutput) = UseState<Output?>(null, true);
        var (note, setNote) = UseState("");

        var live = !ctx.Services.ForcedDemo && ctx.Services.GitHub is not null && ctx.Loaded && snap.Viewer.Length > 0;
        var repos = WatchedRepos(ctx.Services.RepoActivity, settings, onlyImportant);

        var models = new[] { "Function default" }.Concat(ClaudeOptions.Models).ToArray();
        var efforts = new[] { "Function default" }.Concat(ClaudeOptions.Efforts).ToArray();
        var (defModel, defEffort) = ClaudeSettings.Resolve(settings, ClaudeFeature.Activity);

        async Task Run(bool asking)
        {
            setRunning(true);
            setError("");
            setNote("");
            try
            {
                var now = DateTimeOffset.Now;
                var label = DigestRange.Label(period, now);
                DigestInput input;
                if (live && ctx.Services.GitHub is { } hub)
                {
                    input = await new GitHubDigestData(hub.GraphQl).LoadAsync(period, repos, now);
                }
                else
                {
                    var (from, to) = DigestRange.For(period, now);
                    input = new DigestInput(period, from, to, snap.All.Select(p => p.Repo).Distinct().ToList(), DemoPrs(snap, from, to), false);
                }

                var digest = DigestText.Build(input, onlyMine ? snap.Viewer : null);
                var pair = Prompts.ActivityDigest(label, snap.Viewer, digest, asking ? question : null);
                var request = ClaudeRequest.From(pair) with
                {
                    Feature = ClaudeFeature.Activity,
                    Model = modelChoice > 0 && modelChoice < models.Length ? models[modelChoice] : null,
                    Effort = effortChoice > 0 && effortChoice < efforts.Length ? efforts[effortChoice] : null,
                };
                var result = await claude.RunAsync(request, CancellationToken.None);
                if (!result.Success)
                    setError(result.Error == ClaudeErrorKind.AuthFailed ? ClaudeAuth.Hint : (result.ErrorMessage ?? "Claude could not write the summary."));
                else
                    setOutput(new Output(result.Text, result.Model, result.CostUsd, result.Duration,
                        DigestText.Filter(input.Prs, onlyMine ? snap.Viewer : null).Count, input.Truncated, label));
            }
            catch (Exception ex)
            {
                setError(ErrorText.Classify(ex, "GitHub").Message);
            }

            setRunning(false);
        }

        void Start(bool asking)
        {
            if (running) return;
            _ = Run(asking);
        }

        void Save()
        {
            if (output is null) return;
            try
            {
                NotesPage.Store.SaveAsync(Note.FromText("Activity: " + output.Label, output.Text + "\n\n_Claude, " + output.Label + "_", "activity")).GetAwaiter().GetResult();
                setNote("Saved to notes.");
            }
            catch (Exception ex)
            {
                setNote("Could not save: " + ex.Message);
            }
        }

        Element PeriodButton(DigestPeriod p)
        {
            var button = Button(DigestRange.ShortLabel(p), () => setPeriod(p)).IsEnabled(!running);
            return p == period ? button.AccentButton() : button.SubtleButton();
        }

        var periods = FlexRow(
            PeriodButton(DigestPeriod.Yesterday), PeriodButton(DigestPeriod.Week), PeriodButton(DigestPeriod.Month), PeriodButton(DigestPeriod.Year))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 };

        var scope = FlexRow(
            ToggleSwitch(onlyMine, setOnlyMine, "Only my pull requests", "Everyone's pull requests", "Only my pull requests"),
            ToggleSwitch(onlyImportant, setOnlyImportant, "Important repositories only", "All watched repositories", "Important repositories only"))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 16, RowGap = 8 };

        var cost = FlexRow(
            TextBlock("Model").VAlign(VerticalAlignment.Center),
            ComboBox(models, modelChoice, setModelChoice).MinWidth(150).AutomationName("Model for this summary"),
            TextBlock("Effort").VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0),
            ComboBox(efforts, effortChoice, setEffortChoice).MinWidth(150).AutomationName("Effort for this summary"))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 };

        var scopeNote = live
            ? repos.Count + (repos.Count == 1 ? " repository" : " repositories") + " in scope. Change which ones in Settings."
            : "Demo data: nothing is read from GitHub or sent to Claude.";
        var defaults = "Default for this function: " + (defModel ?? "sonnet") + ", " + (defEffort ?? "default effort") + ". Change it in Settings, Claude.";

        Element? unavailable = claude.IsAvailable ? null : Parts.Banner("Claude Code is not installed",
            "Install Claude Code and sign in (claude auth login) to get summaries.", Tone.Warning);

        var canRun = !running && claude.IsAvailable && (!live || repos.Count > 0);

        var controls = Card(VStack(12,
            BodyStrong("Summarize what happened"),
            Kit.Muted("Reads the pull requests merged in the period, then Claude writes a summary. It runs only when you press a button."),
            periods,
            scope,
            cost,
            Kit.Muted(scopeNote + " " + defaults).TextWrapping(),
            Button(running ? "Working…" : "Summarize", () => Start(false)).AccentButton().IsEnabled(canRun).AutomationName("Summarize the period"),
            Separator(),
            TextBox(question, setQuestion, placeholderText: "Or ask a question about the period, for example: which repository changed the most?")
                .AcceptsReturn().TextWrapping(TextWrapping.Wrap).MinHeight(64),
            Button("Ask Claude", () => Start(true)).IsEnabled(canRun && question.Trim().Length > 0)));

        Element result;
        if (running) result = Card(Parts.Busy("Reading pull requests and asking Claude…"));
        else if (error.Length > 0) result = Parts.Banner("No summary", error, Tone.Danger);
        else if (output is null) result = Empty();
        else
        {
            var meta = new List<string> { output.Label, output.PrCount + " merged pull requests" };
            if (output.Model.Length > 0) meta.Add(output.Model);
            meta.Add(output.Duration.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s");
            if (output.CostUsd > 0) meta.Add("$" + output.CostUsd.ToString("0.000", CultureInfo.InvariantCulture));
            result = Card(VStack(10,
                Border(Kit.Md(output.Text)).Padding(12).CornerRadius(6).Background(Theme.SubtleFill),
                output.Truncated ? Kit.Muted("GitHub had more pull requests than were read, so the numbers are a lower bound.") : null,
                Kit.Muted(string.Join(" · ", meta)),
                FlexRow(
                    Button("✎ Save to notes", Save).SubtleButton().AutomationName("Save summary to notes"),
                    Button("Copy", () => ExternalActions.Copy(output.Text)).SubtleButton().AutomationName("Copy summary"),
                    note.Length > 0 ? Kit.Muted(note).VAlign(VerticalAlignment.Center) : null)
                    with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 }));
        }

        return ScrollView(Kit.Capped(
            FlexColumn(
                Kit.PageHeader("Claude", "What happened in your repositories: yesterday, the week, the year"),
                unavailable?.Margin(0, 0, 0, 12),
                controls.Margin(0, 0, 0, 12),
                result)
            .FlexPadding(24, 16), 900));
    }

    /// <summary>Repositories seen in loaded data that are not muted (and, when asked, marked important).</summary>
    private static IReadOnlyList<string> WatchedRepos(IReadOnlyDictionary<string, int> activity, AppSettings settings, bool onlyImportant) =>
        RepoDirectory.Build(activity, settings).Orgs
            .SelectMany(o => o.Repos)
            .Where(r => r.Watched && (!onlyImportant || r.Important))
            .Select(r => r.Repo)
            .ToList();

    /// <summary>A few demo pull requests so the page can be shown without a connection.</summary>
    private static IReadOnlyList<MergedPr> DemoPrs(HubSnapshot snap, DateTimeOffset from, DateTimeOffset to)
    {
        var at = to.AddHours(-3) > from ? to.AddHours(-3) : from;
        return snap.All.Take(8).Select((p, i) => new MergedPr(p.Repo, p.Number, p.Title, p.Author, at.AddMinutes(-i * 20), 40 + i * 15, 10 + i * 5, 3 + i, p.Url)).ToList();
    }
}
