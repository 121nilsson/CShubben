using System.Globalization;
using Cshubben.Core.Composition;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Diagnostics;
using Cshubben.Core.Features.GitHub;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Logic;
using Cshubben.Core.Shared;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// Settings: one card per area (GitHub, Linear, Claude, Repositories, Notifications, Appearance, Data).
/// Preferences are saved through <see cref="PageContext.SaveSettings"/> (settings.json); tokens and keys only ever
/// go to the credential store, never to a file.
/// </summary>
sealed class SettingsPage : Component<PageContext>
{
    private static readonly string[] ThemeLabels = { "System", "Light", "Dark" };
    private static readonly int[] RefreshChoices = { 30, 60, 120, 300 };
    private static readonly string[] RefreshLabels = { "Every 30 seconds", "Every minute", "Every 2 minutes", "Every 5 minutes" };

    public override Element Render()
    {
        var ctx = Props;
        var services = ctx.Services;
        var settings = ctx.Settings;
        void Save(AppSettings next) => ctx.SaveSettings(next);

        // Connection probes (loaded once, in the background; setters are thread safe).
        var (ghToken, setGhToken) = UseState<GitHubTokenInfo?>(null, true);
        var (ghTest, setGhTest) = UseState<ConnectionResult?>(null, true);
        var (ghTesting, setGhTesting) = UseState(false, true);
        var (linearHasKey, setLinearHasKey) = UseState(false, true);
        var (linearInput, setLinearInput) = UseState("");
        var (linearTest, setLinearTest) = UseState<ConnectionResult?>(null, true);
        var (linearTesting, setLinearTesting) = UseState(false, true);
        var (claude, setClaude) = UseState<ClaudeInfo?>(null, true);
        var (claudeChecking, setClaudeChecking) = UseState(false, true);
        var (claudeHasToken, setClaudeHasToken) = UseState(false, true);
        var (claudeTokenInput, setClaudeTokenInput) = UseState("");
        var (repoFilter, setRepoFilter) = UseState("");
        var (notesDirInput, setNotesDirInput) = UseState(settings.NotesDirectory);
        var (diagReport, setDiagReport) = UseState<DiagnosticReport?>(null, true);
        var (diagRunning, setDiagRunning) = UseState(false, true);

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                setLinearHasKey(services.Secrets.Get(SecretKeys.LinearApiKey) is not null);
                setClaudeHasToken(services.Secrets.Get(SecretKeys.ClaudeOAuthToken) is not null);
                setGhToken(await services.ProbeGitHubTokenAsync());
                setClaude(await services.CheckClaudeAsync());
            });
            return () => { };
        }, new object[0]);

        void TestGitHub()
        {
            setGhTesting(true);
            _ = Task.Run(async () =>
            {
                var result = await services.TestGitHubAsync();
                setGhTest(result);
                setGhTesting(false);
            });
        }

        void TestLinear()
        {
            setLinearTesting(true);
            _ = Task.Run(async () =>
            {
                var result = await services.TestLinearAsync();
                setLinearTest(result);
                setLinearTesting(false);
            });
        }

        void CheckClaude()
        {
            setClaudeChecking(true);
            _ = Task.Run(async () =>
            {
                setClaude(await services.CheckClaudeAsync());
                setClaudeChecking(false);
            });
        }

        void RunChecks()
        {
            setDiagRunning(true);
            _ = Task.Run(async () =>
            {
                try
                {
                    setDiagReport(await AppDiagnostics.RunAsync(services));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    setDiagReport(new DiagnosticReport(DateTimeOffset.UtcNow,
                        new[] { DiagnosticResult.Fail("Diagnostics", ErrorText.Describe(ex)) }));
                }
                finally
                {
                    setDiagRunning(false);
                }
            });
        }

        // ---- GitHub ----
        var rate = services.GitHub?.Rest.LastRateLimit;
        var githubCard = Kit.SettingsCard("GitHub",
            "Pull requests, reviews and the activity feed. The token is read from GITHUB_TOKEN, GH_TOKEN or the GitHub CLI and is never stored by Cshubben.",
            Row("Data", Text(services.ForcedDemo
                ? "Demo data (--demo)"
                : services.IsDemo ? "Demo data, no GitHub token found" : "Live from GitHub")),
            Row("Token source", Text(ghToken is null ? "Checking..." : ghToken.Found ? ghToken.Source : "None found. Set GITHUB_TOKEN or run `gh auth login`.")),
            Row("Signed in as", Text(ctx.Snapshot.Viewer.Length > 0 ? ctx.Snapshot.Viewer : "-")),
            Row("Rate limit", Text(RateText(rate))),
            HStack(12,
                Button("Test connection", TestGitHub).IsEnabled(!ghTesting && !services.ForcedDemo),
                Result(ghTest, ghTesting)));

        // ---- Linear ----
        var linearEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LINEAR_API_KEY"));
        var linearStatus = linearEnv
            ? "Using LINEAR_API_KEY from the environment"
            : linearHasKey ? "API key saved in the Windows credential store" : "No API key yet";
        var linearCard = Kit.SettingsCard("Linear",
            "Create a personal API key under Linear > Settings > Security & access. It is kept in the Windows credential store.",
            Row("Status", Text(linearStatus)),
            HStack(8,
                PasswordBox(linearInput, setLinearInput, placeholderText: "Paste a Linear API key").MinWidth(280),
                Button("Save", () =>
                {
                    var key = linearInput.Trim();
                    if (key.Length == 0) return;
                    services.Secrets.Set(SecretKeys.LinearApiKey, key);
                    setLinearInput("");
                    setLinearHasKey(true);
                    setLinearTest(null);
                }).IsEnabled(linearInput.Trim().Length > 0),
                Button("Clear", () =>
                {
                    services.Secrets.Delete(SecretKeys.LinearApiKey);
                    setLinearHasKey(false);
                    setLinearTest(null);
                }).IsEnabled(linearHasKey)),
            HStack(12,
                Button("Test connection", TestLinear).IsEnabled(!linearTesting && (linearHasKey || linearEnv)),
                Result(linearTest, linearTesting)));

        // ---- Claude ----
        Element FeatureRow(ClaudeFeature feature)
        {
            var items = new[] { "Default" }.Concat(ClaudeOptions.Models).ToArray();
            var efforts = new[] { "Default" }.Concat(ClaudeOptions.Efforts).ToArray();
            settings.ClaudeProfiles.TryGetValue(feature.ToString(), out var chosen);
            var model = chosen?.Model ?? "";
            var effort = chosen?.Effort ?? "";
            return Grid(
                columns: [GridSize.Star(), GridSize.Auto, GridSize.Auto],
                rows: [GridSize.Auto],
                TextBlock(ClaudeFeatures.Label(feature)).VAlign(VerticalAlignment.Center).Grid(column: 0),
                ComboBox(items, IndexOr(items, model), i =>
                {
                    var next = i <= 0 || i >= items.Length ? "" : items[i];
                    if (next != model) Save(ClaudeSettings.With(settings, feature, next, effort));
                }).MinWidth(120).Margin(0, 0, 8, 0).AutomationName("Model for " + ClaudeFeatures.Label(feature)).Grid(column: 1),
                ComboBox(efforts, IndexOr(efforts, effort), i =>
                {
                    var next = i <= 0 || i >= efforts.Length ? "" : efforts[i];
                    if (next != effort) Save(ClaudeSettings.With(settings, feature, model, next));
                }).MinWidth(120).AutomationName("Effort for " + ClaudeFeatures.Label(feature)).Grid(column: 2));
        }

        var modelItems = new[] { "Default" }.Concat(ClaudeOptions.Models).ToArray();
        var effortItems = new[] { "Default" }.Concat(ClaudeOptions.Efforts).ToArray();
        var claudeCard = Kit.SettingsCard("Claude",
            "Summaries, briefings and review drafts run through the `claude` command line tool with no tools enabled.",
            Row("Claude CLI", Text(claude is null ? "Checking..." : claude.Path ?? "Not found. Install Claude Code or set CSHUBBEN_CLAUDE_PATH.")),
            Row("Version", Text(claude?.Version ?? "-")),
            Row("Sign-in", Text(claude?.Auth ?? "-")),
            Row("Model", ComboBox(modelItems, IndexOr(modelItems, settings.ClaudeModel), i =>
            {
                var next = i <= 0 || i >= modelItems.Length ? "" : modelItems[i];
                if (next != settings.ClaudeModel) Save(settings with { ClaudeModel = next });
            }).MinWidth(200)),
            Row("Effort", ComboBox(effortItems, IndexOr(effortItems, settings.ClaudeEffort), i =>
            {
                var next = i <= 0 || i >= effortItems.Length ? "" : effortItems[i];
                if (next != settings.ClaudeEffort) Save(settings with { ClaudeEffort = next });
            }).MinWidth(200)),
            BodyStrong("Per function"),
            Kit.Muted("Pick a cheaper model or lower effort for summaries. \"Default\" uses the built-in choice for that function (activity summaries default to haiku, low), then the general setting above.").TextWrapping(),
            FlexColumn(ClaudeFeatures.All.Select(f => FeatureRow(f)).ToArray()) with { RowGap = 6 },
            Kit.Muted(ClaudeAuth.Hint).TextWrapping(),
            HStack(8,
                PasswordBox(claudeTokenInput, setClaudeTokenInput, placeholderText: "Optional: token from `claude setup-token`").MinWidth(280),
                Button("Save", () =>
                {
                    var token = claudeTokenInput.Trim();
                    if (token.Length == 0) return;
                    services.Secrets.Set(SecretKeys.ClaudeOAuthToken, token);
                    setClaudeTokenInput("");
                    setClaudeHasToken(true);
                    CheckClaude();
                }).IsEnabled(claudeTokenInput.Trim().Length > 0),
                Button("Clear", () =>
                {
                    services.Secrets.Delete(SecretKeys.ClaudeOAuthToken);
                    setClaudeHasToken(false);
                    CheckClaude();
                }).IsEnabled(claudeHasToken)),
            HStack(12,
                Button("Check again", CheckClaude).IsEnabled(!claudeChecking),
                claudeChecking ? Kit.Muted("Checking...") : Empty()));

        // ---- Repositories ----
        var directory = RepoDirectory.Build(services.RepoActivity, settings, repoFilter);

        Element RepoRow(RepoEntry r, bool ownerWatched) =>
            Grid(
                columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto, GridSize.Auto],
                rows: [GridSize.Auto],
                ToggleSwitch(r.Watched, v => Save(RepoDirectory.SetRepoWatched(settings, r.Repo, v)), "", "")
                    .AutomationName("Watch " + r.Repo).IsEnabled(ownerWatched).MinWidth(0).VAlign(VerticalAlignment.Center).Grid(column: 0),
                VStack(0, Body(r.Name), Kit.Muted(r.Repo)).VAlign(VerticalAlignment.Center).Margin(12, 0, 0, 0).Grid(column: 1),
                ToggleButton(TextBlock("Important").FontSize(12), r.Important, v => Save(RepoDirectory.SetImportant(settings, r.Repo, v)))
                    .Padding(10, 3).VAlign(VerticalAlignment.Center).AutomationName("Important " + r.Repo).Grid(column: 2),
                Kit.Pill(r.Activity.ToString(CultureInfo.InvariantCulture), Tone.Neutral)
                    .VAlign(VerticalAlignment.Center).Margin(12, 0, 0, 0).MinWidth(40).Grid(column: 3))
            .Margin(28, 6, 0, 6).Opacity(r.Watched ? 1 : 0.55);

        Element OwnerBlock(OrgEntry o) =>
            VStack(0,
                Grid(
                    columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    ToggleSwitch(o.Watched, v => Save(RepoDirectory.SetOwnerWatched(settings, o.Owner, v)), "", "")
                        .AutomationName("Watch everything from " + o.Owner).MinWidth(0).VAlign(VerticalAlignment.Center).Grid(column: 0),
                    VStack(0,
                        BodyStrong(o.Owner),
                        Kit.Muted(o.WatchedRepos.ToString(CultureInfo.InvariantCulture) + " of " + o.Repos.Count.ToString(CultureInfo.InvariantCulture) + " repos watched"))
                        .VAlign(VerticalAlignment.Center).Margin(12, 0, 0, 0).Grid(column: 1),
                    Kit.Pill(o.Activity.ToString(CultureInfo.InvariantCulture), Tone.Neutral)
                        .VAlign(VerticalAlignment.Center).MinWidth(40).Grid(column: 2)).Margin(0, 10, 0, 4),
                VStack(0, o.Repos.Select(r => RepoRow(r, o.Watched).WithKey(r.Repo)).ToArray()))
            .WithKey(o.Owner);

        var reposCard = Kit.SettingsCard("Repositories",
            directory.WatchedRepos.ToString(CultureInfo.InvariantCulture) + " of " + directory.TotalRepos.ToString(CultureInfo.InvariantCulture)
                + " repos watched. Muted repos and organisations are hidden everywhere and send no notifications. "
                + "Important repos are always checked on the Drift page. The number is recent activity (pull requests and feed events).",
            TextBox(repoFilter, setRepoFilter, placeholderText: "Filter repositories").MaxWidth(360),
            directory.Orgs.Count == 0
                ? (Element)Kit.Muted("No repositories seen yet. They appear here after the first load.")
                : VStack(0, directory.Orgs.Select(OwnerBlock).ToArray()),
            settings.FollowedRepos.Count > 0
                ? HStack(8,
                    Kit.Muted("An older setting limits the feed and Drift to " + string.Join(", ", settings.FollowedRepos) + "."),
                    Button("Clear", () => Save(settings with { FollowedRepos = Array.Empty<string>() })).SubtleButton())
                : Empty());

        // ---- Notifications ----
        var notificationsCard = Kit.SettingsCard("Notifications",
            "Which events raise a desktop notification.",
            ToggleSwitch(settings.Notifications, v => Save(settings with { Notifications = v }), "On", "Off", "Desktop notifications"),
            CheckBox((bool?)settings.NotifyReviewRequested, v => Save(settings with { NotifyReviewRequested = v }), "Someone requests my review")
                .IsEnabled(settings.Notifications),
            CheckBox((bool?)settings.NotifyCiFailed, v => Save(settings with { NotifyCiFailed = v }), "CI fails on my pull request")
                .IsEnabled(settings.Notifications),
            CheckBox((bool?)settings.NotifyChangesRequested, v => Save(settings with { NotifyChangesRequested = v }), "Changes are requested on my pull request")
                .IsEnabled(settings.Notifications));

        // ---- Appearance ----
        var themeIndex = Math.Max(0, Array.IndexOf(ThemeChoice.All.ToArray(), ThemeChoice.Normalize(settings.Theme)));
        var appearanceCard = Kit.SettingsCard("Appearance",
            "System follows your Windows light or dark mode.",
            Row("Theme", ComboBox(ThemeLabels, themeIndex, i =>
            {
                var next = i >= 0 && i < ThemeChoice.All.Count ? ThemeChoice.All[i] : ThemeChoice.FollowSystem;
                if (next != ThemeChoice.Normalize(settings.Theme)) Save(settings with { Theme = next });
            }).MinWidth(200)));

        // ---- Data ----
        var refreshIndex = Math.Max(0, Array.IndexOf(RefreshChoices, settings.RefreshSeconds));
        var notesDefault = string.IsNullOrWhiteSpace(settings.NotesDirectory);
        var dataCard = Kit.SettingsCard("Data",
            "Where Cshubben keeps things on this computer, and how often it refreshes while the window is active.",
            Row("Refresh", ComboBox(RefreshLabels, refreshIndex, i =>
            {
                var next = i >= 0 && i < RefreshChoices.Length ? RefreshChoices[i] : AppSettings.DefaultRefreshSeconds;
                if (next != settings.RefreshSeconds) Save(settings with { RefreshSeconds = next });
            }).MinWidth(200)),
            Row("Notes folder", Text(services.NotesFolder).IsTextSelectionEnabled()),
            HStack(8,
                TextBox(notesDirInput, setNotesDirInput, placeholderText: "Custom notes folder (empty for the default)").MinWidth(320),
                Button("Save", () => Save(settings with { NotesDirectory = notesDirInput.Trim() }))
                    .IsEnabled(notesDirInput.Trim() != settings.NotesDirectory.Trim()),
                Button("Open", () => Shell.OpenFolder(services.NotesFolder))),
            Kit.Muted(notesDefault
                ? "Using the default folder. A custom folder applies after you restart the app."
                : "A custom folder applies after you restart the app. Existing notes are not moved."),
            Row("Settings file", Text(services.ForcedDemo ? "Not saved in demo mode" : System.IO.Path.Combine(services.DataFolder, "settings.json")).IsTextSelectionEnabled()),
            HStack(8,
                Button("Open data folder", () => Shell.OpenFolder(services.DataFolder)).IsEnabled(!services.ForcedDemo)));

        // ---- Diagnostics ----
        Element DiagRow(DiagnosticResult r) =>
            Grid(
                columns: [GridSize.Px(64), GridSize.Px(150), GridSize.Star()],
                rows: [GridSize.Auto],
                HStack(0, Kit.Pill(DiagnosticReport.Label(r.Status), r.Status switch
                {
                    DiagnosticStatus.Fail => Tone.Danger,
                    DiagnosticStatus.Warn => Tone.Warning,
                    _ => Tone.Neutral,
                })).Grid(column: 0),
                BodyStrong(r.Name).VAlign(VerticalAlignment.Top).Grid(column: 1),
                TextBlock(r.Detail).FontSize(12).Foreground(Theme.SecondaryText).TextWrapping().IsTextSelectionEnabled().Grid(column: 2))
            .WithKey(r.Name);

        var reportText = diagReport is null ? "" : AppDiagnostics.Text(services, diagReport);
        var diagnosticsCard = Kit.SettingsCard("Diagnostics",
            "Checks GitHub, Linear, the Claude CLI, the credential store and your data folder, one by one. It only reads: nothing is written to GitHub or Linear. The report never contains tokens, so it is safe to paste.",
            HStack(12,
                Button("Run checks", RunChecks).IsEnabled(!diagRunning),
                diagRunning ? Kit.Muted("Running checks...") : Empty(),
                diagReport is null || diagRunning ? Empty() : CopyLinkButton.For(reportText, "Copy report")),
            diagReport is null
                ? Empty()
                : VStack(8, diagReport.Results.Select(DiagRow).ToArray()),
            Kit.Muted("Without a window: run Cshubben.exe --smoke. The report is saved as " + SmokeMode.ReportFileName + " in the data folder."));

        return ScrollView(Kit.Capped(
            FlexColumn(
                Kit.PageHeader("Settings", "Connections, repositories and preferences"),
                AppOptions.ReposFirst ? reposCard.Margin(0, 0, 0, 12) : null,
                githubCard.Margin(0, 0, 0, 12),
                linearCard.Margin(0, 0, 0, 12),
                claudeCard.Margin(0, 0, 0, 12),
                AppOptions.ReposFirst ? null : reposCard.Margin(0, 0, 0, 12),
                Kit.SettingsCard("GitHub writes",
                    "Off by default: merging, reviewing, commenting and re-running jobs are only simulated (dry run) until you turn this on.",
                    ToggleSwitch(settings.AllowGitHubWrites, v => Save(settings with { AllowGitHubWrites = v }), "On", "Off", "Allow writes to GitHub"))
                    .Margin(0, 0, 0, 12),
                notificationsCard.Margin(0, 0, 0, 12),
                Component<NotificationsSettingsCard>().Margin(0, 0, 0, 12),
                appearanceCard.Margin(0, 0, 0, 12),
                dataCard.Margin(0, 0, 0, 12),
                diagnosticsCard.Margin(0, 0, 0, 12),
                AboutCard.Build())
            .FlexPadding(24, 16), 800));
    }

    private static Element Row(string label, Element value) =>
        Grid(
            columns: [GridSize.Px(130), GridSize.Star()],
            rows: [GridSize.Auto],
            Kit.Muted(label).VAlign(VerticalAlignment.Center).Grid(column: 0),
            value.VAlign(VerticalAlignment.Center).Grid(column: 1));

    private static TextBlockElement Text(string text) => TextBlock(text).TextWrapping();

    private static Element Result(ConnectionResult? result, bool busy)
    {
        if (busy)
            return HStack(8, ProgressRing().IsActive().Width(16).Height(16).VAlign(VerticalAlignment.Center),
                Kit.Muted("Testing...").VAlign(VerticalAlignment.Center));
        if (result is null) return Empty();
        return TextBlock(result.Message).Foreground(Kit.Fg(result.Ok ? Tone.Success : Tone.Danger))
            .TextWrapping().VAlign(VerticalAlignment.Center);
    }

    private static string RateText(RateLimitInfo? rate)
    {
        if (rate is null || rate.Remaining is null) return "Not known yet (shown after the first load)";
        var text = rate.Limit is null
            ? rate.Remaining.Value.ToString("N0", CultureInfo.InvariantCulture) + " requests left"
            : rate.Remaining.Value.ToString("N0", CultureInfo.InvariantCulture) + " of " + rate.Limit.Value.ToString("N0", CultureInfo.InvariantCulture) + " left";
        if (rate.ResetAt is { } reset) text += ", resets " + reset.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        return text;
    }

    private static int IndexOr(string[] items, string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var index = Array.IndexOf(items, value);
        return index < 0 ? 0 : index;
    }
}
