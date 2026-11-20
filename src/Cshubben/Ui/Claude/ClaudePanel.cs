using System.Globalization;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Notes;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Ui.Pages;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

sealed record ClaudePanelProps(PullRequest Pr);

/// <summary>What the panel last did. Result is null while nothing was asked yet.</summary>
sealed record ClaudeRun(bool Running, ClaudePreset Preset, ClaudeResult? Result, bool NoDiff)
{
    public static readonly ClaudeRun Idle = new(false, ClaudePreset.Summarize, null, false);
}

/// <summary>
/// "Ask Claude" inside the inspector: presets, a free question, running state, the answer as markdown with cost and duration,
/// readable errors, and Save to notes / Copy on any answer. Nothing Claude writes is posted anywhere.
/// </summary>
sealed class ClaudePanel : Component<ClaudePanelProps>
{
    public override Element Render()
    {
        var pr = Props.Pr;
        var claude = IntegrationServices.Claude;
        var actions = IntegrationServices.Actions;

        var (run, setRun) = UseState(ClaudeRun.Idle, true);
        var (question, setQuestion) = UseState("");
        var (freeOpen, setFreeOpen) = UseState(false);
        var (saved, setSaved) = UseState("", true);
        var (copied, setCopied) = UseState(false);

        async Task Execute(ClaudePreset preset, string text)
        {
            ClaudeResult result;
            var noDiff = false;
            try
            {
                var diff = "";
                try
                {
                    diff = await actions.GetDiffAsync(pr, CancellationToken.None);
                }
                catch (Exception)
                {
                    diff = "";
                }

                noDiff = string.IsNullOrWhiteSpace(diff);
                var request = ClaudePresets.Build(preset, pr, diff, text);
                result = await claude.RunAsync(request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                result = ClaudeResult.Fail(ClaudeErrorKind.Failed, ex.Message, "", TimeSpan.Zero);
            }

            setRun(new ClaudeRun(false, preset, result, noDiff));
        }

        void Start(ClaudePreset preset)
        {
            if (run.Running) return;
            setRun(new ClaudeRun(true, preset, null, false));
            setSaved("");
            setCopied(false);
            _ = Execute(preset, question);
        }

        void Save()
        {
            if (run.Result is null || !run.Result.Success) return;
            try
            {
                var title = ClaudePresets.Label(run.Preset) + ": " + pr.Repo + " #" + pr.Number;
                var body = run.Result.Text + "\n\n_Claude, about " + pr.Repo + " #" + pr.Number + "_";
                NotesPage.Store.SaveAsync(Note.FromText(title, body, pr.Repo + "#" + pr.Number)).GetAwaiter().GetResult();
                setSaved("Saved to notes.");
            }
            catch (Exception ex)
            {
                setSaved("Could not save: " + ex.Message);
            }
        }

        void Copy()
        {
            if (run.Result is null) return;
            setCopied(ExternalActions.Copy(run.Result.Text));
        }

        Element PresetButton(ClaudePreset preset) =>
            Button(ClaudePresets.Label(preset), () => Start(preset))
                .SubtleButton().IsEnabled(!run.Running).ToolTip(ClaudePresets.Hint(preset));

        var presets = FlexRow(
            PresetButton(ClaudePreset.Summarize),
            PresetButton(ClaudePreset.DraftReview),
            PresetButton(ClaudePreset.Explain),
            Button(freeOpen ? "Hide question" : "Ask a question…", () => setFreeOpen(!freeOpen)).SubtleButton())
            with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 };

        Element? free = null;
        if (freeOpen)
        {
            free = VStack(8,
                TextBox(question, setQuestion, placeholderText: "What do you want to know about this pull request?")
                    .AcceptsReturn().TextWrapping(TextWrapping.Wrap).MinHeight(72),
                Button("Ask Claude", () => Start(ClaudePreset.Free))
                    .AccentButton().IsEnabled(!run.Running && question.Trim().Length > 0));
        }

        Element? unavailable = null;
        if (!claude.IsAvailable)
        {
            unavailable = Parts.Banner(
                "Claude Code is not installed",
                "Install Claude Code and sign in (claude auth login). Then the presets work here.",
                Tone.Warning);
        }

        var result = BuildResult(run, claude.IsDemo, saved, copied, Save, Copy);

        return Card(VStack(10,
                HStack(8, TextBlock("✦").Foreground(Theme.AccentText), BodyStrong("Ask Claude")),
                Kit.Muted("Claude reads this pull request. Answers stay here until you save them; nothing is posted to GitHub."),
                unavailable,
                presets,
                free,
                result))
            .Margin(0, 8, 0, 0);
    }

    private static Element BuildResult(ClaudeRun run, bool demo, string saved, bool copied, Action save, Action copy)
    {
        if (run.Running) return Parts.Busy("Claude is working on \"" + ClaudePresets.Label(run.Preset) + "\"…");
        var res = run.Result;
        if (res is null) return Empty();
        if (!res.Success) return ErrorView(res);

        var meta = new List<string>();
        if (res.Model.Length > 0) meta.Add(res.Model);
        meta.Add(res.Duration.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s");
        if (res.CostUsd > 0) meta.Add("$" + res.CostUsd.ToString("0.000", CultureInfo.InvariantCulture));
        if (demo) meta.Add("demo answer");

        Element? noDiff = run.NoDiff
            ? Kit.Muted("The diff was not available, so Claude only saw the title, description and status.")
            : null;

        return VStack(8,
            Border(Kit.Md(res.Text)).Padding(12).CornerRadius(6).Background(Theme.SubtleFill),
            noDiff,
            Kit.Muted(string.Join(" · ", meta)),
            FlexRow(
                Button("✎ Save to notes", save).SubtleButton().AutomationName("Save answer to notes"),
                Button(copied ? "Copied" : "Copy", copy).SubtleButton().AutomationName("Copy answer"),
                saved.Length > 0 ? Kit.Muted(saved).VAlign(VerticalAlignment.Center) : null)
                with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 });
    }

    private static Element ErrorView(ClaudeResult res)
    {
        var detail = string.IsNullOrWhiteSpace(res.ErrorMessage) ? "No details were given." : res.ErrorMessage!;
        return res.Error switch
        {
            ClaudeErrorKind.NotInstalled => Parts.Banner("Claude Code is not installed",
                string.IsNullOrWhiteSpace(res.ErrorMessage) ? "Install Claude Code and sign in, then try again." : detail, Tone.Warning),
            ClaudeErrorKind.AuthFailed => Parts.Banner("Claude needs you to sign in", ClaudeAuth.Hint, Tone.Warning),
            ClaudeErrorKind.Timeout => Parts.Banner("Claude took too long",
                "No answer after " + res.Duration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s. Try again, or try a smaller question.", Tone.Warning),
            _ => Parts.Banner("Claude could not answer", detail, Tone.Danger),
        };
    }
}
