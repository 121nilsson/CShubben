using System.Globalization;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Review;
using Cshubben.Core.Logic;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Review;

sealed partial class ReviewWorkspace
{
    // ------------------------------------------------------------------ ask Claude about code lines

    /// <summary>m: remembers the current line as the start of a range. Pressing m again on the same line clears it.</summary>
    private void MarkLine()
    {
        if (Current is null) return;
        if (S.CursorKey is null)
        {
            Update(s => s with { Info = "Press j or click a line first, then press m." });
            return;
        }

        var key = S.CursorKey;
        if (S.MarkKey == key) Update(s => s with { MarkKey = null, Info = "Mark cleared." });
        else Update(s => s with { MarkKey = key, Info = "Marked. Move with j / k, then press a to ask Claude about the lines." });
    }

    /// <summary>a: opens the question panel for the marked range (or the selected line).</summary>
    private void OpenAsk()
    {
        if (Current is null) return;
        var selection = CodeSelectionBuilder.Build(CurrentRows(), S.MarkKey, S.CursorKey);
        if (selection is null)
        {
            Update(s => s with { Info = "Press j or click a code line first, then press a." });
            return;
        }

        var path = Current.Path;
        Update(s => s with { Ask = new AskState(path, selection, string.Empty, false, null), Info = string.Empty });
    }

    private void SendQuestion(AskState ask)
    {
        var title = Target.Title;
        var repo = Target.Repo;
        var number = Target.Number;
        var question = ask.Question.Trim();
        if (question.Length == 0 || ask.Running) return;

        Update(s => s.Ask is null ? s : s with { Ask = s.Ask with { Running = true, Answer = null } });
        Run(async () =>
        {
            ClaudeResult result;
            try
            {
                var pair = Prompts.CodeQuestion(repo, number, title, ask.Path, ask.Selection.Label, ask.Selection.Code, question);
                result = await IntegrationServices.Claude.RunAsync(ClaudeRequest.From(pair) with { Feature = ClaudeFeature.CodeQuestion }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                result = ClaudeResult.Fail(ClaudeErrorKind.Failed, ex.Message, "", TimeSpan.Zero);
            }

            Update(s => s.Ask is null ? s : s with { Ask = s.Ask with { Running = false, Answer = result } });
        }, s => s.Ask is null ? s : s with { Ask = s.Ask with { Running = false } });
    }

    private Element BuildAskPanel(AskState ask)
    {
        var claude = IntegrationServices.Claude;
        void Close() => Update(s => s with { Ask = null });

        Element answer = Empty();
        if (ask.Running)
        {
            answer = Parts.Busy("Claude is reading the lines…");
        }
        else if (ask.Answer is { } res)
        {
            if (!res.Success)
            {
                answer = Parts.Banner("Claude could not answer",
                    res.Error == ClaudeErrorKind.AuthFailed ? ClaudeAuth.Hint : (res.ErrorMessage ?? "No details were given."), Tone.Danger);
            }
            else
            {
                var meta = new List<string>();
                if (res.Model.Length > 0) meta.Add(res.Model);
                meta.Add(res.Duration.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s");
                if (res.CostUsd > 0) meta.Add("$" + res.CostUsd.ToString("0.000", CultureInfo.InvariantCulture));
                answer = VStack(6,
                    Border(Kit.Md(res.Text)).Padding(12).CornerRadius(6).Background(Theme.SubtleFill),
                    Kit.Muted(string.Join(" · ", meta)),
                    Button("Copy", () => ExternalActions.Copy(res.Text)).SubtleButton().AutomationName("Copy answer"));
            }
        }

        var unavailable = claude.IsAvailable ? null : Parts.Banner("Claude Code is not installed", "Install Claude Code and sign in (claude auth login) to ask questions.", Tone.Warning);
        var code = Border(ScrollView(TextBlock(ask.Selection.Code).FontFamily("Cascadia Mono").FontSize(12).TextWrapping()))
            .MaxHeight(140).Padding(8).CornerRadius(6).Background(Theme.SubtleFill);

        var card = Card(VStack(10,
            Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                HStack(8, TextBlock("✦").Foreground(Theme.AccentText), BodyStrong("Ask Claude about " + ask.Selection.Label + " in " + ask.Path))
                    .VAlign(VerticalAlignment.Center).Grid(column: 0),
                Button("Close", Close).SubtleButton().AutomationName("Close the question panel").Grid(column: 1)),
            code,
            unavailable,
            TextBox(ask.Question, text => Update(s => s.Ask is null ? s : s with { Ask = s.Ask with { Question = text } }),
                    placeholderText: "What do you want to know about these lines?")
                .AcceptsReturn().TextWrapping(TextWrapping.Wrap).MinHeight(64),
            Button(ask.Running ? "Working…" : "Ask Claude", () => SendQuestion(ask)).AccentButton()
                .IsEnabled(!ask.Running && claude.IsAvailable && ask.Question.Trim().Length > 0),
            answer,
            Kit.Muted("Claude sees only these lines, the file name and the pull request title. Nothing is posted to GitHub.")));

        return card.HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Bottom).MaxWidth(760).Margin(16);
    }
}
