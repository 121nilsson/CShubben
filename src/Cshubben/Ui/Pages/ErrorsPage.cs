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
/// error groups). The page only exists while the module is switched on in Settings. Deploy markers and Claude's analysis come later.
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

        void Refresh()
        {
            if (resolved is { } r)
            {
                PageCaches.Errors.Remove(r.Key);
                if (selected is not null) PageCaches.ErrorGroups.Remove(ErrorsServices.GroupsKey(r.Key, selected.App));
            }

            setTick(tick + 1);
        }

        var loading = Parts.IsLoading(overview) || overview is AsyncValue<ErrorsSnapshot>.Reloading;
        var subtitle = snapshot is null
            ? "Error spikes from your logs"
            : ErrorsText.Summary(snapshot) + (resolved?.Source.IsDemo == true ? " · demo data" : "");

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

                var list = AppList(snapshot, selected, Pick);
                var detail = selected is null
                    ? Card(Kit.Muted("Pick an application."))
                    : Detail(ctx, errors, snapshot, selected, groups, now, Refresh);

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
            FlexColumn(header, content).FlexPadding(24, 16)));
    }

    private static string ServiceName(ErrorsSettings errors) => errors.Source == ErrorsSettings.SourceGrafana ? "Grafana" : "The log service";

    /// <summary>The picked application if it is still there, else the worst one (the list is already ordered).</summary>
    private static AppSpike? Pick(ErrorsSnapshot? snapshot, string picked) =>
        snapshot is null
            ? null
            : snapshot.Apps.FirstOrDefault(a => string.Equals(a.App, picked, StringComparison.OrdinalIgnoreCase))
              ?? snapshot.Apps.FirstOrDefault();

    private static Element AppList(ErrorsSnapshot snapshot, AppSpike? selected, Action<string> pick)
    {
        if (snapshot.Apps.Count == 0)
            return Card(Kit.Muted("No applications with errors in the last 24 hours."));

        // One scale for the whole list, so a quiet application looks quiet next to a spiking one.
        var peak = snapshot.Apps.Max(a => ErrorsKit.Peak(a.Counts, ListBars));
        var rows = snapshot.Apps.Select(app => AppRow(app, snapshot.Window, string.Equals(app.App, selected?.App, StringComparison.Ordinal), peak, pick)).ToArray();
        return VStack(4, rows);
    }

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
            .HAlign(HorizontalAlignment.Stretch)
            .AutomationName(app.App + ", " + rate + ", " + ErrorsText.TrendWord(app.Trend) + (app.IsSpike ? ", spiking" : "") + (isSelected ? ", selected" : ""))
            .WithKey(app.App);
    }

    private static Element Detail(
        PageContext ctx,
        ErrorsSettings errors,
        ErrorsSnapshot snapshot,
        AppSpike app,
        AsyncValue<IReadOnlyList<ErrorGroup>> groups,
        DateTimeOffset now,
        Action refresh)
    {
        var repo = RepoBindings.RepoFor(errors, app.App);
        var repoLine = repo is null
            ? (Element)(FlexRow(Kit.Muted("Not bound to a repository."), Button("Bind in Settings", ctx.OpenSettings).SubtleButton().AutomationName("Open Settings to bind a repository"))
                with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 })
            : Kit.Muted("Repository: " + repo);

        var facts = FlexRow(
            app.IsSpike ? Kit.Pill("Spiking " + ErrorsText.Factor(app.Factor), Tone.Danger) : null,
            Kit.Pill(ErrorsText.TrendGlyph(app.Trend) + " " + ErrorsText.TrendWord(app.Trend), Tone.Neutral),
            Kit.Pill(app.Total + (app.Total == 1 ? " error" : " errors") + " in 24 h", Tone.Neutral)) with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 6 };

        var chart = app.Total == 0
            ? (Element)Kit.Muted("No errors in the last 24 hours.")
            : VStack(4,
                ErrorsKit.Bars(app.Counts, ChartBars, 72, app.IsSpike ? Tone.Danger : Tone.Neutral),
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto, GridSize.Star()],
                    rows: [GridSize.Auto],
                    Kit.Muted("6 h ago").Grid(column: 0),
                    Kit.Muted("peak " + ErrorsKit.Peak(app.Counts, ChartBars) + " per " + (int)snapshot.Window.Step.TotalMinutes + " min").Grid(column: 1),
                    Kit.Muted("now").HAlign(HorizontalAlignment.Right).Grid(column: 2)));

        var overview = Card(VStack(10,
            Title(app.App).TextTrimming(TextTrimming.CharacterEllipsis),
            repoLine,
            facts,
            chart.Margin(0, 4, 0, 0)));

        Element list;
        var found = Parts.Peek(groups);
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
            list = VStack(8, found.Select(g => GroupCard(errors, g, snapshot.Window, now)).ToArray());
        }

        return VStack(12, overview, list);
    }

    private static Element GroupCard(ErrorsSettings errors, ErrorGroup group, ErrorWindow window, DateTimeOffset now)
    {
        var warning = group.Kind == ErrorKind.MissingBackendRole;
        var tone = warning ? Tone.Warning : Tone.Danger;
        var repo = RepoBindings.RepoForNamespace(errors, group.Method);

        var meta = FlexRow(
            Kit.Pill(group.Count + (group.Count == 1 ? " error" : " errors"), tone),
            Kit.Muted("first " + ErrorsText.Ago(group.FirstSeen, now) + " · last " + ErrorsText.Ago(group.LastSeen, now)),
            group.Pods.Count == 0 ? null : Kit.Muted(group.Pods.Count + (group.Pods.Count == 1 ? " pod" : " pods")),
            repo is null ? null : Kit.Pill(repo, Tone.Neutral)) with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 4 };

        return Card(VStack(8,
            TextBlock(group.Title).SemiBold().TextWrapping().MaxLines(2).TextTrimming(TextTrimming.CharacterEllipsis),
            Kit.Muted(group.Detail).TextWrapping().MaxLines(3).TextTrimming(TextTrimming.CharacterEllipsis),
            group.Hint is null
                ? null
                : Border(TextBlock(group.Hint).FontSize(12).TextWrapping())
                    .Background(Kit.Bg(Tone.Warning)).CornerRadius(6).Padding(10, 6),
            ErrorsKit.Bars(group.Counts, ChartBars, 24, tone),
            meta)).WithKey(group.Key);
    }
}
