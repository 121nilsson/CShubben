using System.Globalization;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Errors;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Logic;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

sealed record ErrorAnalysisProps(AppServices Services, ErrorsSettings Errors, AppSpike App, ErrorGroup Group);

/// <summary>An analysis kept for the session, with what it was based on.</summary>
sealed record ErrorAnalysisCache(ClaudeResult Result, string Basis, DateTimeOffset At);

/// <summary>
/// "Ask Claude" for one error. Never runs on its own. On the button it reads the code from GitHub (the commit that was deployed,
/// found from the deployments of the bound repository, and the default branch), builds the prompt with <see cref="ErrorPrompts"/>
/// and shows Claude's answer. Reads only; nothing is written to GitHub.
/// </summary>
sealed class ErrorAnalysisPanel : Component<ErrorAnalysisProps>
{
    private static readonly Dictionary<string, ErrorAnalysisCache> Cache = new(StringComparer.Ordinal);

    public static Element Build(ErrorAnalysisProps props) => Component<ErrorAnalysisPanel, ErrorAnalysisProps>(props);

    public override Element Render()
    {
        var services = Props.Services;
        var errors = Props.Errors;
        var app = Props.App;
        var group = Props.Group;
        var claude = IntegrationServices.Claude;

        var key = app.App + "|" + group.Key + "|" + group.FirstSeen.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Cache.TryGetValue(key, out var cached);

        var (running, setRunning) = UseState(false, true);
        var (stage, setStage) = UseState("", true);
        var (error, setError) = UseState("", true);
        var (tick, setTick) = UseState(0, true);
        var (copied, setCopied) = UseState(false);

        async Task Run()
        {
            setRunning(true);
            setError("");
            try
            {
                var repo = RepoBindings.RepoForNamespace(errors, group.Method) ?? RepoBindings.RepoFor(errors, app.App);
                var environment = ErrorEnvironment.Guess(errors.Workspace, errors.ResourceGroup, errors.Namespace);

                ErrorCodeContext? context = null;
                var basis = "No repository is bound, so Claude only saw the log lines.";
                if (repo is not null && services.GitHub is { } hub)
                {
                    setStage("Finding the deployed version of " + repo + "…");
                    try
                    {
                        context = await new ErrorContextLoader(hub.Rest).LoadAsync(repo, group.File, environment, group.FirstSeen, CancellationToken.None);
                        basis = context.Note;
                    }
                    catch (Exception ex) when (ex is GitHubException or HttpRequestException or ArgumentException)
                    {
                        basis = "The code could not be read from GitHub (" + ErrorText.Describe(ex) + "), so Claude only saw the log lines.";
                    }
                }
                else if (repo is not null)
                {
                    basis = "Not connected to GitHub, so Claude only saw the log lines.";
                }

                setStage("Claude is analysing the error…");
                var prompt = ErrorPrompts.Analyze(app.App, group, context, DateTimeOffset.UtcNow);
                var result = await claude.RunAsync(
                    new ClaudeRequest(prompt.SystemPrompt, prompt.UserPrompt) { Feature = ClaudeFeature.ErrorAnalysis }, CancellationToken.None);

                if (!result.Success)
                    setError(result.Error == ClaudeErrorKind.AuthFailed ? ClaudeAuth.Hint : (result.ErrorMessage ?? "Claude could not analyse the error."));
                else
                    Cache[key] = new ErrorAnalysisCache(result, basis, DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                setError(ex.Message);
            }

            setRunning(false);
            setTick(tick + 1);
        }

        if (!claude.IsAvailable)
        {
            return Parts.Banner("Claude Code is not installed",
                "Install Claude Code and sign in (claude auth login) to analyse errors here.", Tone.Warning);
        }

        var button = Button(running ? "Working…" : cached is null ? "✦ Ask Claude to analyse" : "Analyse again", () => _ = Run())
            .AccentButton().IsEnabled(!running).HAlign(HorizontalAlignment.Left)
            .AutomationName("Ask Claude to analyse this error");

        Element? body = null;
        if (running)
        {
            body = Parts.Busy(stage.Length > 0 ? stage : "Working…");
        }
        else if (cached is { } c)
        {
            var meta = new List<string>();
            if (c.Result.Model.Length > 0) meta.Add(c.Result.Model);
            meta.Add(c.Result.Duration.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s");
            if (c.Result.CostUsd > 0) meta.Add("$" + c.Result.CostUsd.ToString("0.000", CultureInfo.InvariantCulture));
            if (claude.IsDemo) meta.Add("demo answer");

            body = VStack(8,
                Border(Kit.Md(c.Result.Text)).Padding(12).CornerRadius(6).Background(Theme.SubtleFill),
                Kit.Muted("Based on: " + c.Basis).TextWrapping(),
                Kit.Muted(string.Join(" · ", meta)),
                Button(copied ? "Copied" : "Copy answer", () => setCopied(ExternalActions.Copy(c.Result.Text))).SubtleButton().HAlign(HorizontalAlignment.Left)
                    .AutomationName("Copy the analysis"));
        }

        return VStack(8,
            button,
            error.Length > 0 ? Parts.Banner("No analysis", error, Tone.Danger) : null,
            body);
    }
}
