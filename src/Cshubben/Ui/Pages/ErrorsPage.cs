using Cshubben.Core.Features.Errors;
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

/// <summary>
/// Errors: which applications have errors in the logs, which ones are spiking, and what the errors are. A list of applications
/// on the left (rate, trend, a small chart), the picked application on the right (a bigger chart of the last six hours and the
/// error groups). The page only exists while the module is switched on in Settings. Deployments of the bound repository are marked on
/// the charts, a spike is tied to the deployment before it, error groups show whether they are new or rising and can be sorted,
/// and summaries can be copied. Claude's analysis is in <see cref="ErrorAnalysisPanel"/>.
/// </summary>
sealed class ErrorsPage : Component<PageContext>
{
    private const int ListBars = 36;
    private const int ChartBars = 72;

    public static Element Build(PageContext ctx) => Component<ErrorsPage, PageContext>(ctx);

    public override Element Render()
    {
        var ctx = Props;
        var errors = ctx.Settings.Errors;

        var (windowWidth, _) = UseWindowSize();
        var narrow = windowWidth < 900;
        var (tick, setTick) = UseState(0, true);
        var (picked, setPicked) = UseState("");
        var (showDetail, setShowDetail) = UseState(false);
        var (quietOpen, setQuietOpen) = UseState(false);
        var (expanded, setExpanded) = UseState("");
        var (sort, setSort) = UseState(GroupSort.Frequent);

        var resolved = ErrorsServices.SourceFor(ctx.Services, errors);
        var sourceKey = resolved?.Key ?? "";

        var overview = UseResource<ErrorsSnapshot>(
            async ct =>
            {
                if (resolved is not { } r) return ErrorsSnapshot.Empty(DateTimeOffset.UtcNow);
                return await PageCaches.Errors.GetAsync(r.Key, PageCaches.ErrorsTtl, () => r.Source.LoadAsync(ct));
            },
            new object[] { sourceKey, tick });

        var snapshot = Parts.Peek(overview) ?? (resolved is { } cached ? PageCaches.Errors.Peek(cached.Key) : null);
        var selected = Pick(snapshot, picked);

        var groups = UseResource<IReadOnlyList<ErrorGroup>>(
            async ct =>
            {
                if (resolved is not { } r || selected is null) return Array.Empty<ErrorGroup>();
                return await PageCaches.ErrorGroups.GetAsync(
                    ErrorsServices.GroupsKey(r.Key, selected.App), PageCaches.ErrorsTtl, () => r.Source.LoadGroupsAsync(selected.App, ct));
            },
            new object[] { sourceKey, selected?.App ?? "", tick });

        // Deployments are a bonus: without them (not bound, no GitHub) the page is complete, only without markers.
        var deploys = UseResource<IReadOnlyList<DeployedVersion>>(
            async ct =>
            {
                if (resolved is not { } r || selected is null) return Array.Empty<DeployedVersion>();
                return await ErrorsServices.DeploysAsync(ctx.Services, errors, r.Key, selected.App, r.Source.IsDemo, ct);
            },
            new object[] { sourceKey, selected?.App ?? "", tick });

        void Refresh()
        {
            if (resolved is { } r)
            {
                PageCaches.Errors.Remove(r.Key);
                if (selected is not null)
                {
                    PageCaches.ErrorGroups.Remove(ErrorsServices.GroupsKey(r.Key, selected.App));
                    if (RepoBindings.RepoFor(errors, selected.App) is { } bound)
                        PageCaches.ErrorDeploys.Remove(ErrorsServices.DeploysKey(r.Key, bound, ErrorEnvironment.Guess(errors.Workspace, errors.ResourceGroup, errors.Namespace)));
                }
            }

            setTick(tick + 1);
        }

        var loading = Parts.IsLoading(overview) || overview is AsyncValue<ErrorsSnapshot>.Reloading;
        var subtitle = snapshot is null
            ? "Error spikes from your logs"
            : ErrorsText.Summary(snapshot) + " · updated " + ErrorsText.Ago(snapshot.At, DateTimeOffset.UtcNow) + (resolved?.Source.IsDemo == true ? " · demo data" : "");

        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Kit.PageHeader("Errors", subtitle).Grid(column: 0),
            // A fixed slot, so the header does not shift when the button turns into a spinner.
            Border((loading
                    ? ProgressRing().IsActive().Width(18).Height(18)
                    : (Element)Button("Refresh", Refresh).SubtleButton().AutomationName("Refresh errors"))
                    .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center))
                .Width(96).Height(36).VAlign(VerticalAlignment.Top).Margin(8, 0, 0, 0).Grid(column: 1));

        Element content;
        if (resolved is null)
        {
            var grafana = errors.Source == ErrorsSettings.SourceGrafana;
            content = Kit.Banner(Tone.Accent, grafana ? "Connect Grafana" : "Connect the log service",
                grafana
                    ? "Add the Grafana address and a service account token in Settings to start reading the logs."
                    : "Add the MCP address, workspace and resource group in Settings, then sign in, to start reading the logs.",
                "Open Settings", ctx.OpenSettings);
        }
        else if (snapshot is null)
        {
            content = overview is AsyncValue<ErrorsSnapshot>.Error failed
                ? FailureView.Banner(ErrorText.Classify(failed.Exception, ServiceName(errors)), Refresh, ctx.OpenSettings)
                : Card(Parts.Busy("Reading the logs…"));
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            if (snapshot.Apps.Count == 0)
            {
                content = Card(Kit.Muted("No applications with errors in the last 24 hours. Bind applications to repositories in Settings to list them here even when they are quiet."));
            }
            else
            {
                void Pick(string app)
                {
                    setPicked(app);
                    setShowDetail(true);
                }

                var list = AppList(snapshot, selected, Pick, quietOpen, setQuietOpen);
                var detail = selected is null
                    ? Card(Kit.Muted("Pick an application."))
                    : Detail(ctx, errors, snapshot, selected, groups, deploys, now, Refresh, expanded, setExpanded, sort, setSort);

                if (narrow)
                {
                    // Narrow windows: one pane at a time, the list or the picked application with a Back button.
                    content = showDetail && selected is not null
                        ? VStack(12,
                            Button("← Applications", () => setShowDetail(false)).SubtleButton().HAlign(HorizontalAlignment.Left).AutomationName("Back to the application list"),
                            detail)
                        : list;
                }
                else
                {
                    content = Grid(
                        columns: [GridSize.Px(320), GridSize.Star()],
                        rows: [GridSize.Auto],
                        list.Grid(column: 0),
                        detail.Margin(16, 0, 0, 0).Grid(column: 1));
                }
            }
        }

        return ScrollView(Kit.Capped(
            FlexColumn(header, content).FlexPadding(24, 16), 2400));
    }

    private static string ServiceName(ErrorsSettings errors) => errors.Source == ErrorsSettings.SourceGrafana ? "Grafana" : "The log service";

    /// <summary>The picked application if it is still there, else the worst one (the list is already ordered).</summary>
    private static AppSpike? Pick(ErrorsSnapshot? snapshot, string picked) =>
        snapshot is null
            ? null
            : snapshot.Apps.FirstOrDefault(a => string.Equals(a.App, picked, StringComparison.OrdinalIgnoreCase))
              ?? snapshot.Apps.FirstOrDefault();

    private static Element AppList(ErrorsSnapshot snapshot, AppSpike? selected, Action<string> pick, bool quietOpen, Action<bool> setQuietOpen)
    {
        if (snapshot.Apps.Count == 0)
            return Card(Kit.Muted("No applications with errors in the last 24 hours."));

        // One scale for the whole list, so a quiet application looks quiet next to a spiking one.
        var peak = snapshot.Apps.Max(a => ErrorsKit.Peak(a.Counts, ListBars));
        Element Row(AppSpike app) =>
            AppRow(app, snapshot.Window, string.Equals(app.App, selected?.App, StringComparison.Ordinal), peak, pick);

        var spiking = snapshot.Apps.Where(a => a.IsSpike).ToArray();
        var active = snapshot.Apps.Where(a => !a.IsSpike && a.Now > 0).ToArray();
        var quiet = snapshot.Apps.Where(a => !a.IsSpike && a.Now <= 0).ToArray();
        var quietShown = quietOpen || (selected is not null && quiet.Any(a => a.App == selected.App));

        var parts = new List<Element?>();
        if (spiking.Length > 0)
        {
            parts.Add(Section("Spiking", spiking.Length, Tone.Danger));
            parts.AddRange(spiking.Select(Row));
        }

        if (active.Length > 0)
        {
            parts.Add(Section("Active", active.Length, Tone.Neutral));
            parts.AddRange(active.Select(Row));
        }

        if (quiet.Length > 0)
        {
            parts.Add(Button(
                    Section((quietShown ? "▾ " : "▸ ") + "Quiet last hour", quiet.Length, Tone.Neutral),
                    () => setQuietOpen(!quietShown))
                .SubtleButton().HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Left)
                .AutomationName((quietShown ? "Hide" : "Show") + " quiet applications"));
            if (quietShown) parts.AddRange(quiet.Select(Row));
        }

        return VStack(4, parts.Where(p => p is not null).Select(p => p!).ToArray());
    }

    private static Element Section(string label, int count, Tone tone) =>
        HStack(6,
            TextBlock(label.ToUpperInvariant()).FontSize(11).SemiBold().Foreground(Kit.Fg(tone)),
            TextBlock(count.ToString()).FontSize(11).Foreground(Theme.SecondaryText))
            .Margin(4, 12, 0, 2);

    private static Element AppRow(AppSpike app, ErrorWindow window, bool isSelected, int peak, Action<string> pick)
    {
        var tone = app.IsSpike ? Tone.Danger : Tone.Neutral;
        var rate = app.Total == 0 ? "no errors" : ErrorsText.PerHour(app, window);
        var quiet = app.Total == 0;

        var name = TextBlock(app.App).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis);
        var title = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            (quiet ? name.Foreground(Theme.SecondaryText) : name).Grid(column: 0),
            (app.IsSpike ? Kit.Pill(ErrorsText.Factor(app.Factor), Tone.Danger) : Empty()).Margin(8, 0, 0, 0).Grid(column: 1));

        var glyph = TextBlock(ErrorsText.TrendGlyph(app.Trend)).Foreground(app.IsSpike ? Kit.Fg(Tone.Danger) : Theme.SecondaryText)
            .AutomationName(ErrorsText.TrendWord(app.Trend));
        var rateText = app.IsSpike ? TextBlock(rate).FontSize(12).Foreground(Kit.Fg(Tone.Danger)) : Kit.Muted(rate);

        var body = VStack(6,
            title,
            HStack(6, glyph, rateText),
            ErrorsKit.Bars(app.Counts, ListBars, 22, tone, peak));

        // A spiking row is tinted, the picked row is filled, and the picked row also has an accent strip.
        var fill = app.IsSpike ? Kit.Bg(Tone.Danger) : isSelected ? Theme.SubtleFill : Theme.CardBackground;
        var strip = Border(Empty()).Width(3).Margin(0, 6, 0, 6).CornerRadius(2).Background(isSelected ? Theme.Accent : Theme.CardBackground);
        var row = Border(Grid(
                columns: [GridSize.Px(3), GridSize.Star()],
                rows: [GridSize.Auto],
                strip.Grid(column: 0),
                body.Margin(10, 6, 8, 6).Grid(column: 1)))
            .Background(fill).CornerRadius(6);

        return Button(row, () => pick(app.App))
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(app.App + ", " + rate + ", " + ErrorsText.TrendWord(app.Trend) + (app.IsSpike ? ", spiking" : "") + (isSelected ? ", selected" : ""))
            .WithKey(app.App);
    }

    private static Element Detail(
        PageContext ctx,
        ErrorsSettings errors,
        ErrorsSnapshot snapshot,
        AppSpike app,
        AsyncValue<IReadOnlyList<ErrorGroup>> groups,
        AsyncValue<IReadOnlyList<DeployedVersion>> deployments,
        DateTimeOffset now,
        Action refresh,
        string expanded,
        Action<string> setExpanded,
        GroupSort sort,
        Action<GroupSort> setSort)
    {
        var window = snapshot.Window;
        var repo = RepoBindings.RepoFor(errors, app.App);
        var found = Parts.Peek(groups);
        var deploys = Parts.Peek(deployments) ?? (IReadOnlyList<DeployedVersion>)Array.Empty<DeployedVersion>();
        var running = DeployCorrelation.Before(deploys, now);
        Element? bindBanner = repo is null
            ? Kit.Banner(Tone.Accent, "Not bound to a repository",
                "Bind " + app.App + " to a repository to tie a spike to recent deploys and code.",
                "Bind in Settings", ctx.OpenSettings)
            : null;
        var repoLine = repo is null ? null : Kit.Muted("Repository: " + repo);

        var facts = FlexRow(
            app.IsSpike ? Kit.Pill("Spiking " + ErrorsText.Factor(app.Factor), Tone.Danger) : null,
            Kit.Pill(ErrorsText.TrendGlyph(app.Trend) + " " + ErrorsText.TrendWord(app.Trend), Tone.Neutral),
            Kit.Pill(app.Total + (app.Total == 1 ? " error" : " errors") + " in 24 h", Tone.Neutral)) with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 6 };

        // Deployments on the chart: a thin line in the bucket of each one.
        var markers = ErrorMarkers.Indices(
            ErrorMarkers.TailFrom(window, ChartBars), window.Step, Math.Min(ChartBars, window.Buckets), deploys.Select(d => d.DeployedAt));

        var chart = app.Total == 0
            ? (Element)Kit.Muted("No errors in the last 24 hours.")
            : VStack(4,
                ErrorsKit.GridBars(app.Counts, ChartBars, 110, app.IsSpike ? Tone.Danger : Tone.Neutral, markers),
                ErrorsKit.TimeAxis(window, ChartBars),
                Kit.Muted("Peak " + ErrorsKit.Peak(app.Counts, ChartBars) + " errors per " + (int)window.Step.TotalMinutes + " min"
                        + (markers.Count > 0 ? " · vertical lines mark deploys" : ""))
                    .AutomationName(ErrorsText.ChartSummary(app.Counts, window, ChartBars) + (markers.Count > 0 ? " Deployments are marked on the chart." : "")));

        // When the spike began and which deployment came just before it.
        var onset = app.IsSpike ? SpikeOnset.Find(app.Counts, window) : null;
        var startLink = onset is { } begun ? DeployCorrelation.Link(deploys, begun, DeployCorrelation.SpikeWindow, window.Step) : null;
        Element? startCard = onset is { } startedAt
            ? Border(TextBlock(ErrorsText.StartLine(startedAt, now, startLink)).FontSize(13).TextWrapping())
                .Background(startLink is null ? Theme.SubtleFill : Kit.Bg(Tone.Warning)).CornerRadius(6).Padding(10, 6)
            : null;

        var commitUrl = startLink is null ? null : ErrorLinks.CommitUrl(repo, startLink.Deploy.Sha);
        var overviewActions = FlexRow(
            commitUrl is null ? null : Button("Open deploy on GitHub", () => ExternalActions.OpenUrl(commitUrl)).SubtleButton().AutomationName("Open the deployed commit on GitHub"),
            Button("Copy summary", () => ExternalActions.Copy(ErrorReport.App(app, window, found ?? Array.Empty<ErrorGroup>(), onset, startLink, repo, now)))
                .SubtleButton().AutomationName("Copy a summary of " + app.App))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 };

        var overview = Card(VStack(12,
            Title(app.App).TextTrimming(TextTrimming.CharacterEllipsis),
            repoLine,
            facts,
            startCard,
            bindBanner,
            chart.Margin(0, 4, 0, 0),
            app.Total == 0 ? null : overviewActions));

        Element list;
        if (found is null)
        {
            list = groups is AsyncValue<IReadOnlyList<ErrorGroup>>.Error failed
                ? FailureView.Banner(ErrorText.Classify(failed.Exception, ServiceName(errors)), refresh, ctx.OpenSettings)
                : Card(Parts.Busy("Reading the errors of " + app.App + "…"));
        }
        else if (found.Count == 0)
        {
            list = Card(Kit.Muted(app.Total == 0 ? "Nothing to show." : "The log lines are no longer available for this period."));
        }
        else
        {
            // The groups come from the newest log lines only; when that cut off older ones, "new" cannot be told from "old".
            var complete = GroupSampling.IsComplete(found);
            var ordered = GroupOrdering.Order(found, sort, window, now, complete);

            var items = new List<Element?>
            {
                found.Count > 1 ? SortBar(sort, setSort) : null,
                complete
                    ? null
                    : Kit.Muted("Showing the newest " + GroupSampling.MaxLines + " log lines. Older errors are counted in the chart but not listed here, and \"new\" is not shown.").TextWrapping(),
            };
            items.AddRange(ordered.Select(g => (Element?)GroupCard(
                ctx.Services, errors, app, g, window, now, deploys, running, complete,
                expanded == g.Key, () => setExpanded(expanded == g.Key ? "" : g.Key)).WithKey(g.Key)));
            list = VStack(8, items.Where(i => i is not null).Select(i => i!).ToArray());
        }

        return VStack(12, overview, list);
    }

    /// <summary>"Sort: Most frequent · Latest · Newest · Rising", the chosen one filled.</summary>
    private static Element SortBar(GroupSort sort, Action<GroupSort> setSort)
    {
        Element Chip(GroupSort option)
        {
            var label = GroupOrdering.Label(option);
            var name = "Sort errors by " + label.ToLowerInvariant() + (option == sort ? ", selected" : "");
            Element chip = option == sort
                ? Button(label, () => setSort(option)).AccentButton().AutomationName(name)
                : Button(label, () => setSort(option)).SubtleButton().AutomationName(name);
            return chip.WithKey("sort|" + option);
        }

        return FlexRow(
            Kit.Muted("Sort").VAlign(VerticalAlignment.Center),
            Chip(GroupSort.Frequent),
            Chip(GroupSort.Latest),
            Chip(GroupSort.Newest),
            Chip(GroupSort.Rising)) with { Wrap = FlexWrap.Wrap, ColumnGap = 6, RowGap = 4 };
    }

    private static Element GroupCard(
        AppServices services,
        ErrorsSettings errors,
        AppSpike app,
        ErrorGroup group,
        ErrorWindow window,
        DateTimeOffset now,
        IReadOnlyList<DeployedVersion> deploys,
        DeployedVersion? running,
        bool complete,
        bool open,
        Action toggle)
    {
        var status = GroupStatuses.Of(group, window, now, complete);
        var firstSeenAfter = DeployCorrelation.FirstSeenLink(deploys, group, window);
        var warning = group.Kind == ErrorKind.MissingBackendRole;
        var tone = warning ? Tone.Warning : Tone.Danger;
        var repo = RepoBindings.RepoForNamespace(errors, group.Method);
        var mono = "Cascadia Mono, Consolas";

        // "ExceptionType · File.cs:40" becomes a bold name with the location as code text under it. Lines without an
        // exception (no " · ") keep the whole text as the name and show the logger as the location.
        var split = group.Title.IndexOf(" · ", StringComparison.Ordinal);
        var name = split > 0 ? group.Title[..split] : group.Title;
        var location = split > 0 ? group.Title[(split + 3)..] : (group.Detail.Length > 0 ? group.Detail : null);
        var showDetail = split > 0 && group.Detail.Length > 0;

        var head = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            VStack(2,
                TextBlock(name).FontSize(16).SemiBold().TextWrapping().MaxLines(open ? 6 : 2).TextTrimming(TextTrimming.CharacterEllipsis),
                location is null
                    ? null
                    : TextBlock(location).FontFamily(mono).FontSize(12).Foreground(Theme.SecondaryText)).Grid(column: 0),
            HStack(6, Badges(status, group.Count, tone)).VAlign(VerticalAlignment.Top).Margin(12, 0, 0, 0).Grid(column: 1));

        // The chart zooms in on when this error happened, so a short burst is not one bar among 72.
        var zoom = ErrorZoom.Around(group.Counts, window, group.FirstSeen, group.LastSeen);
        var zoomMarkers = ErrorMarkers.Indices(zoom.From, window.Step, zoom.Counts.Count, deploys.Select(d => d.DeployedAt));
        var chart = zoom.Counts.Count == 0
            ? null
            : VStack(2,
                ErrorsKit.GridBars(zoom.Counts, zoom.Counts.Count, 44, tone, zoomMarkers),
                ErrorsKit.RangeAxis(zoom.From, zoom.To));

        var meta = FlexRow(
            Kit.Muted("first " + ErrorsText.Ago(group.FirstSeen, now) + " · last " + ErrorsText.Ago(group.LastSeen, now)),
            (status is GroupStatus.Fading or GroupStatus.Stopped) ? Kit.Muted(GroupStatuses.Label(status).ToLowerInvariant()) : null,
            group.Pods.Count == 0 ? null : Kit.Muted(group.Pods.Count + (group.Pods.Count == 1 ? " pod" : " pods")),
            repo is null ? null : Kit.Pill(repo, Tone.Neutral)) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 4 };

        var toggleButton = Button(open ? "Hide details ▴" : "Show details ▾", toggle)
            .SubtleButton().HAlign(HorizontalAlignment.Left)
            .AutomationName((open ? "Hide" : "Show") + " details of " + name);

        var body = Card(VStack(10,
            head,
            showDetail ? TextBlock(group.Detail).FontSize(13).TextWrapping().MaxLines(open ? 8 : 3).TextTrimming(TextTrimming.CharacterEllipsis) : null,
            group.Hint is null
                ? null
                : Border(TextBlock(group.Hint).FontSize(12).TextWrapping())
                    .Background(Kit.Bg(Tone.Warning)).CornerRadius(6).Padding(10, 6),
            chart,
            firstSeenAfter is null ? null : Kit.Muted(ErrorsText.DeployLine("First seen", firstSeenAfter)).TextWrapping(),
            meta,
            toggleButton,
            open ? Details(services, errors, app, group, now, status, firstSeenAfter, running) : null));

        return body.WithKey(group.Key);
    }

    /// <summary>The pills at the top right of an error: "New" or "Rising" when that is so (quiet otherwise), and how many errors.</summary>
    private static Element[] Badges(GroupStatus status, int count, Tone tone)
    {
        var badges = new List<Element>();
        if (status == GroupStatus.New) badges.Add(Kit.Pill("New", Tone.Warning));
        else if (status == GroupStatus.Rising) badges.Add(Kit.Pill("Rising", Tone.Danger));
        badges.Add(Kit.Pill(count + (count == 1 ? " error" : " errors"), tone));
        return badges.ToArray();
    }

    /// <summary>The digging part of an error: where in the code, the latest full log line with its stack trace, and recent occurrences.</summary>
    private static Element Details(
        AppServices services,
        ErrorsSettings errors,
        AppSpike app,
        ErrorGroup group,
        DateTimeOffset now,
        GroupStatus status,
        DeployLink? firstSeenAfter,
        DeployedVersion? running)
    {
        const string mono = "Cascadia Mono, Consolas";
        var repo = RepoBindings.RepoForNamespace(errors, group.Method) ?? RepoBindings.RepoFor(errors, app.App);
        var samples = group.Samples ?? Array.Empty<LogEntry>();
        var latest = samples.Count > 0 ? samples[0] : null;

        Element Label(string text) => TextBlock(text.ToUpperInvariant()).FontSize(11).SemiBold().Foreground(Theme.SecondaryText);
        Element Code(string text) => TextBlock(text).FontFamily(mono).FontSize(12).TextWrapping().IsTextSelectionEnabled();

        // At the commit that is deployed when that is known, so the line numbers of the stack trace match the code.
        var url = ErrorLinks.FileUrl(repo, group.File, group.Line, running?.Sha);

        var where = FlexColumn(
            Label("Where"),
            group.Method is null ? Kit.Muted("No stack trace in the log line.") : Code(group.Method),
            group.File is null ? null : Code(group.File + (group.Line is { } l ? ":" + l : ""))) with { RowGap = 4 };

        var actions = FlexRow(
            url is null
                ? null
                : Button(running is null ? "Open in GitHub" : "Open as deployed", () => ExternalActions.OpenUrl(url))
                    .AutomationName(running is null ? "Open the file in GitHub" : "Open the file in GitHub at the deployed commit"),
            latest is null ? null : Button("Copy log line", () => ExternalActions.Copy(latest.Message)).SubtleButton().AutomationName("Copy the full log line"),
            Button("Copy summary", () => ExternalActions.Copy(ErrorReport.Group(app.App, group, status, repo, running, firstSeenAfter, now)))
                .SubtleButton().AutomationName("Copy a summary of this error"),
            group.Method is null ? null : Button("Copy location", () => ExternalActions.Copy(group.Method + (group.File is null ? "" : " (" + group.File + (group.Line is { } n ? ":" + n : "") + ")"))).SubtleButton().AutomationName("Copy the code location"))
            with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 };

        Element? trace = null;
        if (latest is not null)
        {
            var text = latest.Message.Replace("\r\n", "\n").Trim();
            trace = FlexColumn(
                Label("Latest log line · " + latest.At.ToLocalTime().ToString("HH:mm:ss") + (latest.Pod.Length > 0 ? " · " + latest.Pod : "")),
                Border(ScrollView(Code(text)).MaxHeight(320))
                    .Background(Theme.SubtleFill).CornerRadius(6).Padding(10, 8)) with { RowGap = 4 };
        }

        Element? occurrences = null;
        if (samples.Count > 1)
        {
            occurrences = FlexColumn(
                Label("Recent occurrences"),
                VStack(2, samples.Select(s => Grid(
                    columns: [GridSize.Px(130), GridSize.Star()],
                    rows: [GridSize.Auto],
                    Kit.Muted(s.At.ToLocalTime().ToString("HH:mm:ss") + " · " + ErrorsText.Ago(s.At, now)).Grid(column: 0),
                    Kit.Muted(s.Pod.Length > 0 ? s.Pod : "-").TextTrimming(TextTrimming.CharacterEllipsis).Grid(column: 1)).WithKey(s.At.Ticks.ToString() + "|" + s.Pod)).ToArray())) with { RowGap = 4 };
        }

        var pods = group.Pods.Count == 0
            ? null
            : FlexColumn(Label("Pods"), Code(string.Join("\n", group.Pods))) with { RowGap = 4 };

        var analysis = ErrorAnalysisPanel.Build(new ErrorAnalysisProps(services, errors, app, group)).WithKey("analysis|" + group.Key);

        return Border(FlexColumn(analysis, where, actions, trace, occurrences, pods) with { RowGap = 12 })
            .Padding(0, 6, 0, 0);
    }
}
