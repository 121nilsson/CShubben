using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Linear;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Assist;

/// <param name="Active">True while the panel is open. Nothing is fetched from Linear before that.</param>
sealed record LinearPanelProps(PullRequest Pr, bool Active);

/// <summary>
/// The Linear part of the inspector. When the PR names an issue (ENG-123 in the title or branch) it shows the issue and its
/// status. Otherwise it offers "Create Linear issue": an editable title, description, priority and team, optionally drafted by
/// Claude, created in Linear and linked to the pull request.
/// </summary>
sealed class LinearIssuePanel : Component<LinearPanelProps>
{
    private static readonly string[] Priorities = { "No priority", "Urgent", "High", "Medium", "Low" };

    public override Element Render()
    {
        var pr = Props.Pr;
        var active = Props.Active;
        var linear = IntegrationServices.Linear;
        var claude = IntegrationServices.Claude;

        var (created, setCreated) = UseState<LinearIssue?>(null, true);
        var (title, setTitle) = UseState(CleanTitle(pr.Title), true);
        var (description, setDescription) = UseState("Tracks " + pr.Url, true);
        var (priority, setPriority) = UseState(0);
        var (teamIndex, setTeamIndex) = UseState(0);
        var (busy, setBusy) = UseState("", true);
        var (error, setError) = UseState("", true);
        var (info, setInfo) = UseState("", true);

        var identifier = created?.Identifier ?? linear.IdentifierFor(pr);

        var issueResource = UseResource<LinearIssue?>(
            async ct =>
            {
                if (!active || identifier is null || !linear.IsConnected) return null;
                if (created is not null) return created;
                return await linear.GetIssueAsync(identifier, ct);
            },
            new object[] { pr.Id, active, identifier ?? "", linear.IsConnected });

        var teamsResource = UseResource<IReadOnlyList<LinearTeam>>(
            async ct =>
            {
                if (!active || identifier is not null || !linear.IsConnected) return Array.Empty<LinearTeam>();
                return await linear.GetTeamsAsync(ct);
            },
            new object[] { pr.Id, active, identifier is null, linear.IsConnected });

        var issue = created ?? Parts.Peek(issueResource);

        async Task Draft()
        {
            setBusy("draft");
            setError("");
            setInfo("");
            try
            {
                var result = await claude.RunAsync(ClaudeRequest.From(Prompts.LinearIssueDraft(pr)) with { Feature = ClaudeFeature.Drafts }, CancellationToken.None);
                if (!result.Success)
                {
                    setError(result.Error == ClaudeErrorKind.AuthFailed ? ClaudeAuth.Hint : (result.ErrorMessage ?? "Claude could not draft the issue."));
                }
                else
                {
                    var draft = IssueDraftParser.Parse(result.Text);
                    if (draft is null)
                    {
                        setError("Claude's answer could not be read as an issue. Try again.");
                    }
                    else
                    {
                        setTitle(draft.Title);
                        setDescription(draft.Description.Contains(pr.Url, StringComparison.Ordinal)
                            ? draft.Description
                            : draft.Description + "\n\nPull request: " + pr.Url);
                        if (draft.Priority is int p) setPriority(p);
                        setInfo("Drafted by Claude. Check it before creating.");
                    }
                }
            }
            catch (Exception ex)
            {
                setError(ex.Message);
            }

            setBusy("");
        }

        async Task CreateIssue(IReadOnlyList<LinearTeam> teams, string issueTitle, string issueDescription, int issuePriority, int team)
        {
            if (teams.Count == 0 || issueTitle.Trim().Length == 0) return;
            setBusy("create");
            setError("");
            setInfo("");
            LinearIssue? made = null;
            try
            {
                var chosen = teams[Math.Clamp(team, 0, teams.Count - 1)];
                made = await linear.CreateIssueAsync(chosen.Id, issueTitle.Trim(), issueDescription, issuePriority, CancellationToken.None);
                await linear.LinkPullRequestAsync(made.Id, pr.Url, pr.Title, CancellationToken.None);
                setCreated(made);
                setInfo("Created " + made.Identifier + " and linked this pull request.");
            }
            catch (Exception ex)
            {
                if (made is not null)
                {
                    setCreated(made);
                    setError("Created " + made.Identifier + ", but linking the pull request failed: " + ex.Message);
                }
                else
                {
                    setError(ex.Message);
                }
            }

            setBusy("");
        }

        Element body;
        if (identifier is not null)
        {
            body = LinkedView(identifier, issue, Parts.IsLoading(issueResource), linear.IsConnected, info, error);
        }
        else if (!linear.IsConnected)
        {
            body = VStack(6,
                BodyStrong("Linear is not connected"),
                Kit.Muted("Add a Linear API key in Settings to create issues from pull requests and to see linked issues here.").TextWrapping());
        }
        else
        {
            var teams = Parts.Peek(teamsResource) ?? Array.Empty<LinearTeam>();
            body = CreateView(
                teams, Parts.IsLoading(teamsResource), Parts.ErrorOf(teamsResource), linear.IsDemo, claude.IsAvailable,
                title, setTitle, description, setDescription, priority, setPriority, teamIndex, setTeamIndex,
                busy, info, error,
                () => _ = Draft(),
                () => _ = CreateIssue(teams, title, description, priority, teamIndex));
        }

        return Card(body).Margin(0, 8, 0, 0);
    }

    private static string CleanTitle(string title) => title.Trim();

    private static Element LinkedView(string identifier, LinearIssue? issue, bool loading, bool connected, string info, string error)
    {
        Element chip;
        if (issue is null)
        {
            chip = HStack(8,
                Kit.Pill(identifier, Tone.Accent),
                Kit.Muted(!connected ? "Connect Linear in Settings to see its status." : loading ? "Loading…" : "Not found in Linear."));
        }
        else
        {
            var meta = new List<string> { issue.PriorityLabel };
            if (!string.IsNullOrEmpty(issue.Assignee)) meta.Add(issue.Assignee!);
            if (!string.IsNullOrEmpty(issue.Project)) meta.Add(issue.Project!);
            chip = Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                VStack(6,
                    FlexRow(Kit.Pill(issue.Identifier, Tone.Accent), Kit.Pill(issue.StateName, StateTone(issue.StateType)))
                        with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 4 },
                    TextBlock(issue.Title).SemiBold().TextWrapping(),
                    Kit.Muted(string.Join(" · ", meta))).Grid(column: 0),
                VStack(2,
                    Button("Open", () => ExternalActions.OpenUrl(issue.Url))
                        .SubtleButton().AutomationName("Open " + issue.Identifier + " in Linear"),
                    CopyLinkButton.For(issue.Url, "Copy link", issue.Identifier + " " + issue.Title))
                    .VAlign(VerticalAlignment.Top).IsVisible(issue.Url.Length > 0).Grid(column: 1));
        }

        return VStack(10,
            Parts.Label("Linear issue"),
            chip,
            info.Length > 0 ? Kit.Muted(info).Foreground(Kit.Fg(Tone.Success)).TextWrapping() : null,
            error.Length > 0 ? Parts.Banner("Linear", error, Tone.Danger) : null);
    }

    private static Element CreateView(
        IReadOnlyList<LinearTeam> teams, bool teamsLoading, string? teamsError, bool demo, bool claudeAvailable,
        string title, Action<string> setTitle, string description, Action<string> setDescription,
        int priority, Action<int> setPriority, int teamIndex, Action<int> setTeamIndex,
        string busy, string info, string error, Action draft, Action create)
    {
        var teamNames = teams.Select(t => t.Key + " · " + t.Name).ToArray();
        var working = busy.Length > 0;

        Element teamPicker = teams.Count > 0
            ? ComboBox(teamNames, Math.Clamp(teamIndex, 0, teams.Count - 1), setTeamIndex).HAlign(HorizontalAlignment.Stretch)
            : Kit.Muted(teamsLoading ? "Loading teams…" : (teamsError ?? "No teams found."));

        var picks = FlexRow(
            VStack(4, Parts.Label("Team"), teamPicker).Flex(grow: 1, basis: 160),
            VStack(4, Parts.Label("Priority"),
                ComboBox(Priorities, Math.Clamp(priority, 0, 4), setPriority).HAlign(HorizontalAlignment.Stretch)).Flex(grow: 1, basis: 160))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 8 };

        return VStack(10,
            HStack(8, TextBlock("◇").Foreground(Theme.AccentText), BodyStrong("Create Linear issue")),
            Kit.Muted("No Linear issue is named in this pull request's title or branch.").TextWrapping(),
            Button(busy == "draft" ? "Drafting…" : "✦ Draft with Claude", draft)
                .SubtleButton().IsEnabled(!working && claudeAvailable).HAlign(HorizontalAlignment.Left),
            VStack(4, Parts.Label("Title"), TextBox(title, setTitle, placeholderText: "Issue title")),
            VStack(4, Parts.Label("Description"),
                TextBox(description, setDescription, placeholderText: "Describe the work (Markdown)")
                    .AcceptsReturn().TextWrapping(TextWrapping.Wrap).MinHeight(96)),
            picks,
            demo ? Kit.Muted("Demo data: this creates the issue in memory only.") : null,
            busy == "create"
                ? Parts.Busy("Creating the issue and linking the pull request…")
                : Button("Create issue and link PR", create)
                    .AccentButton().IsEnabled(!working && teams.Count > 0 && title.Trim().Length > 0).HAlign(HorizontalAlignment.Left),
            busy == "draft" ? Parts.Busy("Claude is drafting the issue…") : null,
            info.Length > 0 ? Kit.Muted(info).Foreground(Kit.Fg(Tone.Success)).TextWrapping() : null,
            error.Length > 0 ? Parts.Banner("Could not finish", error, Tone.Danger) : null);
    }

    private static Tone StateTone(LinearStateType type) => type switch
    {
        LinearStateType.Started => Tone.Accent,
        LinearStateType.Completed => Tone.Success,
        _ => Tone.Neutral,
    };
}
