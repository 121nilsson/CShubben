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

        var (tick, setTick) = UseState(0, true);
        var (picked, setPicked) = UseState("");

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
            (loading
                ? ProgressRing().IsActive().Width(18).Height(18)
                : (Element)Button("Refresh", Refresh).SubtleButton().AutomationName("Refresh errors"))
                .VAlign(VerticalAlignment.Top).Margin(8, 4, 0, 0).Grid(column: 1));

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
            var list = AppList(snapshot, selected, app => setPicked(app));
            var detail = selected is null
                ? Card(Kit.Muted("No applications to show yet. Bind applications to repositories in Settings, or wait for the first errors."))
                : Detail(ctx, errors, snapshot, selected, groups, now, Refresh);

            content = FlexRow(
                list.Width(320).Flex(shrink: 0),
                detail.Flex(grow: 1, basis: 360)) with { Wrap = FlexWrap.Wrap, ColumnGap = 16, RowGap = 16 };
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

        var rows = snapshot.Apps.Select(app => AppRow(app, snapshot.Window, string.Equals(app.App, selected?.App, StringComparison.Ordinal), pick)).ToArray();
        return VStack(4, rows);
    }

    private static Element AppRow(AppSpike app, ErrorWindow window, bool isSelected, Action<string> pick)
    {
        var tone = app.IsSpike ? Tone.Danger : Tone.Neutral;
        var rate = app.Total == 0 ? "no errors" : ErrorsText.PerHour(app, window);
        var title = FlexRow(
            BodyStrong(app.App).Flex(grow: 1),
            app.IsSpike ? Kit.Pill(ErrorsText.Factor(app.Factor), Tone.Danger) : null) with { ColumnGap = 8 };

        var body = VStack(6,
            title,
            HStack(8,
                TextBlock(ErrorsText.TrendGlyph(app.Trend)).FontSize(14).Foreground(app.IsSpike ? Kit.Fg(Tone.Danger) : Theme.SecondaryText)
                    .AutomationName(ErrorsText.TrendWord(app.Trend)),
                Kit.Muted(rate)),
            ErrorsKit.Bars(app.Counts, ListBars, 22, 4, tone));

        var accent = Border(Empty()).Width(3).Background(isSelected ? Theme.Accent : Theme.CardBackground).CornerRadius(2);
        var row = Grid(
            columns: [GridSize.Px(3), GridSize.Star()],
            rows: [GridSize.Auto],
            accent.Grid(column: 0),
            body.Margin(10, 4, 4, 4).Grid(column: 1));

        return Button(row, () => pick(app.App))
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch)
            .AutomationName(app.App + ", " + rate + ", " + ErrorsText.TrendWord(app.Trend) + (app.IsSpike ? ", spiking" : ""))
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
            ? (Element)HStack(8, Kit.Muted("Not bound to a repository."), Button("Bind in Settings", ctx.OpenSettings).SubtleButton().AutomationName("Open Settings to bind a repository"))
            : Kit.Muted("Repository: " + repo);

        var facts = HStack(8,
            app.IsSpike ? Kit.Pill("Spiking " + ErrorsText.Factor(app.Factor), Tone.Danger) : null,
            Kit.Pill(ErrorsText.TrendGlyph(app.Trend) + " " + ErrorsText.TrendWord(app.Trend), Tone.Neutral),
            Kit.Pill(app.Total + (app.Total == 1 ? " error" : " errors") + " in 24 h", Tone.Neutral));

        var chart = app.Total == 0
            ? (Element)Kit.Muted("No errors in the last 24 hours.")
            : VStack(4,
                ErrorsKit.Bars(app.Counts, ChartBars, 72, 6, app.IsSpike ? Tone.Danger : Tone.Neutral),
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    Kit.Muted("6 h ago").Grid(column: 0),
                    Kit.Muted("now").Grid(column: 1)));

        var overview = Card(VStack(10,
            Title(app.App),
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
        var tone = group.Kind == ErrorKind.MissingBackendRole ? Tone.Warning : Tone.Danger;
        var repo = RepoBindings.RepoForNamespace(errors, group.Method);

        var meta = HStack(8,
            Kit.Pill(group.Count + (group.Count == 1 ? " error" : " errors"), group.Kind == ErrorKind.MissingBackendRole ? Tone.Warning : Tone.Danger),
            Kit.Muted("first " + ErrorsText.Ago(group.FirstSeen, now) + " · last " + ErrorsText.Ago(group.LastSeen, now)),
            group.Pods.Count == 0 ? null : Kit.Muted(group.Pods.Count + (group.Pods.Count == 1 ? " pod" : " pods")),
            repo is null ? null : Kit.Pill(repo, Tone.Neutral));

        return Card(VStack(8,
            BodyStrong(group.Title),
            Kit.Muted(group.Detail).TextWrapping(),
            group.Hint is null
                ? null
                : Border(TextBlock(group.Hint).FontSize(12).TextWrapping())
                    .Background(Kit.Bg(Tone.Warning)).CornerRadius(6).Padding(10, 6),
            ErrorsKit.Bars(group.Counts, ChartBars, 24, 6, tone),
            meta)).WithKey(group.Key);
    }
}
