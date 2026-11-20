using System.Globalization;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.GitHub;
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
        var (followedInput, setFollowedInput) = UseState("");
        var (mutedInput, setMutedInput) = UseState("");
        var (mutedOrgInput, setMutedOrgInput) = UseState("");
        var (repoError, setRepoError) = UseState("");
        var (notesDirInput, setNotesDirInput) = UseState(settings.NotesDirectory);

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
        void AddTo(string input, Action<string> clearInput, Func<AppSettings, IReadOnlyList<string>> get, Func<AppSettings, IReadOnlyList<string>, AppSettings> set, string what)
        {
            var name = RepoList.Normalize(input);
            if (name is null)
            {
                setRepoError("Enter " + what + ", for example acme/web.");
                return;
            }

            setRepoError("");
            clearInput("");
            Save(set(settings, RepoList.Add(get(settings), name)));
        }

        Element Editor(string title, IReadOnlyList<string> list, string input, Action<string> setInput, Action add, Action<string> remove, string empty) =>
            VStack(6,
                BodyStrong(title),
                list.Count == 0
                    ? (Element)Kit.Muted(empty)
                    : VStack(2, list.Select(name => (Element)Grid(
                            columns: [GridSize.Star(), GridSize.Auto],
                            rows: [GridSize.Auto],
                            Body(name).VAlign(VerticalAlignment.Center).Grid(column: 0),
                            Button("Remove", () => remove(name)).SubtleButton().AutomationName("Remove " + name).Grid(column: 1))
                        .WithKey(name)).ToArray()),
                HStack(8,
                    TextBox(input, setInput, placeholderText: "owner/name").MinWidth(240),
                    Button("Add", add).IsEnabled(input.Trim().Length > 0)));

        var reposCard = Kit.SettingsCard("Repositories",
            "Followed repos are what you care about. Muted repos and whole organisations are hidden everywhere.",
            Editor("Followed repositories", settings.FollowedRepos, followedInput, setFollowedInput,
                () => AddTo(followedInput, setFollowedInput, s => s.FollowedRepos, (s, l) => s with { FollowedRepos = l }, "owner/name"),
                name => Save(settings with { FollowedRepos = RepoList.Remove(settings.FollowedRepos, name) }),
                "None. Everything that shows up is followed."),
            Editor("Muted repositories", settings.MutedRepos, mutedInput, setMutedInput,
                () => AddTo(mutedInput, setMutedInput, s => s.MutedRepos, (s, l) => s with { MutedRepos = l }, "owner/name"),
                name => Save(settings with { MutedRepos = RepoList.Remove(settings.MutedRepos, name) }),
                "None."),
            Editor("Muted organisations", settings.MutedOrgs, mutedOrgInput, setMutedOrgInput,
                () => AddTo(mutedOrgInput, setMutedOrgInput, s => s.MutedOrgs, (s, l) => s with { MutedOrgs = l }, "an organisation name"),
                name => Save(settings with { MutedOrgs = RepoList.Remove(settings.MutedOrgs, name) }),
                "None."),
            repoError.Length > 0 ? TextBlock(repoError).Foreground(Kit.Fg(Tone.Danger)).FontSize(12) : Empty());

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

        return ScrollView(Kit.Capped(
            FlexColumn(
                Kit.PageHeader("Settings", "Connections, repositories and preferences"),
                githubCard.Margin(0, 0, 0, 12),
                linearCard.Margin(0, 0, 0, 12),
                claudeCard.Margin(0, 0, 0, 12),
                reposCard.Margin(0, 0, 0, 12),
                Kit.SettingsCard("GitHub writes",
                    "Off by default: merging, reviewing, commenting and re-running jobs are only simulated (dry run) until you turn this on.",
                    ToggleSwitch(settings.AllowGitHubWrites, v => Save(settings with { AllowGitHubWrites = v }), "On", "Off", "Allow writes to GitHub"))
                    .Margin(0, 0, 0, 12),
                notificationsCard.Margin(0, 0, 0, 12),
                Component<NotificationsSettingsCard>().Margin(0, 0, 0, 12),
                appearanceCard.Margin(0, 0, 0, 12),
                dataCard)
            .FlexPadding(24, 16)));
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
