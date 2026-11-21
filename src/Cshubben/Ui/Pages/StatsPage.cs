using System.Globalization;
using Cshubben.Core.Features.Stats;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// Cycle time, time to first review, throughput, review load and PR size, with median and p90, drawn with plain
/// borders (no charting package). The numbers come from <see cref="StatsCalculator"/>.
/// </summary>
static class StatsPage
{
    /// <summary>Real history (merged PRs and reviews given). When null the page shows deterministic demo data.</summary>
    public static StatsInput? Input { get; set; }

    private const double ChartHeight = 72;

    public static Element Build(HubSnapshot snap, StatsInput? input = null, Element? notice = null, TeamReport? team = null)
    {
        var real = input ?? Input;
        var extras = StatsExtrasCalculator.Compute(real ?? DemoStats.Build(snap.At, snap.Viewer), snap.At);
        team ??= real is null ? TeamStats.Compute(DemoTeam(snap.At)) : null;
        var report = StatsCalculator.Compute(real ?? DemoStats.Build(snap.At, snap.Viewer), snap.At);
        var weeks = report.Weeks;

        var subtitle = $"Last {weeks} weeks · {report.MergedCount} merged · {report.ReviewCount} reviews given" + (real is null ? " · demo data" : "");

        Element Tile(string label, string value, string hint) =>
            Card(VStack(2,
                Kit.Muted(label),
                TextBlock(value).FontSize(28).SemiBold(),
                Kit.Muted(hint).TextWrapping())).Flex(grow: 1, basis: 200);

        var perWeek = (double)report.MergedCount / weeks;
        var reviewsPerWeek = (double)report.ReviewCount / weeks;

        var tiles = (FlexRow(
            Tile("Cycle time", Duration(report.CycleTime), report.CycleTime.Count == 0 ? "no merged PRs yet" : "median, open to merge · p90 " + StatsFormat.Duration(report.CycleTime.P90)),
            Tile("First review", Duration(report.FirstReview), report.FirstReview.Count == 0 ? "no reviewed PRs yet" : "median wait · p90 " + StatsFormat.Duration(report.FirstReview.P90)),
            Tile("Merged per week", OneDecimal(perWeek), "average over " + weeks + " weeks"),
            Tile("Reviews per week", OneDecimal(reviewsPerWeek), "you gave, average")) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 })
            .Margin(0, 0, 0, 12);

        var starts = report.Throughput.Select(b => b.Start).ToList();

        var throughput = ChartCard(
            "Throughput",
            "Pull requests merged per week",
            report.Throughput.Count == 0 ? "0" : report.Throughput[^1].Count + " this week",
            report.Throughput.Select(b => (double)b.Count).ToList(),
            starts,
            i => report.Throughput[i].Count + " merged");

        var trend = ChartCard(
            "Cycle time trend",
            "Median open-to-merge time of the PRs merged each week",
            StatsFormat.Duration(report.CycleTime.Median) + " median",
            report.CycleTrend.Select(w => w.MedianHours).ToList(),
            starts,
            i => report.CycleTrend[i].Count == 0 ? "nothing merged" : StatsFormat.Duration(report.CycleTrend[i].MedianHours) + " median, " + report.CycleTrend[i].Count + " PRs");

        var load = ChartCard(
            "Review load",
            "Reviews you gave per week",
            report.ReviewLoad.Count == 0 ? "0" : report.ReviewLoad[^1].Count + " this week",
            report.ReviewLoad.Select(b => (double)b.Count).ToList(),
            starts,
            i => report.ReviewLoad[i].Count + (report.ReviewLoad[i].Count == 1 ? " review" : " reviews"));

        var spread = Card(VStack(14,
            HeaderBlock("How long things take", "Median, 90th percentile and the slowest"),
            SpreadRow("Open to merge", report.CycleTime),
            SpreadRow("Open to first review", report.FirstReview),
            Legend())).Flex(grow: 1, basis: 420);

        var sizes = Card(VStack(10,
            HeaderBlock("PR size", "Changed lines per merged pull request"),
            FlexColumn(report.Sizes.Select(SizeRow).ToArray()) with { RowGap = 6 })).Flex(grow: 1, basis: 420);

        var repos = Card(VStack(10,
            HeaderBlock("Where the work happens", "Merged pull requests by repository"),
            report.TopRepos.Count == 0
                ? Kit.EmptyNote("No merged pull requests in this period.")
                : FlexColumn(report.TopRepos.Select(r => RepoRow(r, report.TopRepos[0].Count)).ToArray()) with { RowGap = 6 }))
            .Flex(grow: 1, basis: 420);

        var more = (FlexRow(
            Tile("Lines changed", "+" + Compact(extras.LinesAdded) + " / -" + Compact(extras.LinesDeleted), "in your merged PRs"),
            Tile("Merge streak", extras.StreakWeeks + (extras.StreakWeeks == 1 ? " week" : " weeks"), "in a row with a merged PR")) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 })
            .Margin(0, 0, 0, 12);

        var reviewedRepos = Card(VStack(10,
            HeaderBlock("Where you review", "Pull requests you reviewed, by repository"),
            extras.ReviewedRepos.Count == 0
                ? Kit.EmptyNote("No reviews in this period.")
                : FlexColumn(extras.ReviewedRepos.Select(r => RepoRow(r, extras.ReviewedRepos[0].Count)).ToArray()) with { RowGap = 6 }))
            .Flex(grow: 1, basis: 420);

        var weekdayMax = Math.Max(1, extras.Weekdays.Max(d => d.Count));
        var weekdays = Card(VStack(10,
            HeaderBlock("When you merge", "Merged pull requests by weekday (UTC)"),
            FlexColumn(extras.Weekdays.Select(d => WeekdayRow(d, weekdayMax)).ToArray()) with { RowGap = 6 }))
            .Flex(grow: 1, basis: 420);

        var slowest = Card(VStack(10,
            HeaderBlock("Slowest to merge", "Your three longest open-to-merge times"),
            extras.Slowest.Count == 0
                ? Kit.EmptyNote("Nothing merged in this period.")
                : FlexColumn(extras.Slowest.Select(SlowRow).ToArray()) with { RowGap = 8 }))
            .Flex(grow: 1, basis: 420);

        var teamCards = new List<Element>();
        if (team is not null && team.Merged > 0)
        {
            teamCards.Add(Card(VStack(10,
                HeaderBlock("Most active people", "Merged pull requests, last 30 days, watched repositories"),
                FlexColumn(team.TopPeople.Select(p => PersonRow(p, team.TopPeople[0].Merged)).ToArray()) with { RowGap = 6 }))
                .Flex(grow: 1, basis: 420));
            teamCards.Add(Card(VStack(10,
                HeaderBlock("Most active repositories", "Merged pull requests by everyone, last 30 days (" + team.Merged + " in total, " + team.People + " people)"),
                FlexColumn(team.TopRepos.Select(r => RepoRow(r, team.TopRepos[0].Count)).ToArray()) with { RowGap = 6 }))
                .Flex(grow: 1, basis: 420));
        }

        var now = RightNow(snap);

        return ScrollView(Kit.Capped(
            FlexColumn(
                Kit.PageHeader("Stats", subtitle),
                notice?.Margin(0, 0, 0, 12),
                tiles,
                (FlexRow(throughput, trend, load, spread, sizes, repos) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 })
                    .Margin(0, 0, 0, 12),
                more,
                (FlexRow(new[] { reviewedRepos, weekdays, slowest }.Concat(teamCards).ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 })
                    .Margin(0, 0, 0, 12),
                now)
            .FlexPadding(24, 16)));
    }

    // ------------------------------------------------------------------ pieces

    private static string Compact(long n) =>
        n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : n >= 10_000 ? (n / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : n.ToString(CultureInfo.InvariantCulture);

    private static Element WeekdayRow(WeekdayCount d, int max) =>
        Grid(
            columns: [GridSize.Px(44), GridSize.Star(), GridSize.Px(28)],
            rows: [GridSize.Auto],
            TextBlock(d.Day.ToString().Substring(0, 3)).VAlign(VerticalAlignment.Center).Grid(column: 0),
            Track((double)d.Count / max).VAlign(VerticalAlignment.Center).Margin(8, 0, 8, 0).Grid(column: 1),
            Kit.Muted(d.Count.ToString(CultureInfo.InvariantCulture)).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 2))
            .WithKey("day-" + d.Day);

    private static Element SlowRow(MergedPrRecord pr) =>
        VStack(2,
            TextBlock(pr.Title).TextTrimming(TextTrimming.CharacterEllipsis),
            Kit.Muted(pr.Repo + " #" + pr.Number + " · " + StatsFormat.Duration(pr.CycleTime.TotalHours) + " · " + pr.Lines + " lines"))
            .WithKey("slow-" + pr.Repo + pr.Number);

    private static Element PersonRow(PersonCount p, int max)
    {
        var f = max <= 0 ? 0 : (double)p.Merged / max;
        return Grid(
            columns: [GridSize.Star(2), GridSize.Star(3), GridSize.Px(28)],
            rows: [GridSize.Auto],
            TextBlock(p.Author).TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 0),
            Track(f).VAlign(VerticalAlignment.Center).Margin(8, 0, 8, 0).Grid(column: 1),
            Kit.Muted(p.Merged.ToString(CultureInfo.InvariantCulture)).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 2))
            .ToolTip(p.Merged + " merged, " + p.Lines + " lines changed")
            .WithKey("person-" + p.Author);
    }

    private static IEnumerable<TeamPr> DemoTeam(DateTimeOffset now)
    {
        var people = new[] { "alex", "sam", "robin", "kim", "alex", "sam", "alex", "jo" };
        var repos = new[] { "acme/api", "acme/web", "acme/api", "acme/infra", "acme/mobile", "acme/api" };
        for (var i = 0; i < 40; i++) yield return new TeamPr(repos[i % repos.Length], people[i % people.Length], now.AddHours(-i * 15), 20 + i * 7);
    }

    private static string Duration(Spread s) => s.Count == 0 ? "–" : StatsFormat.Duration(s.Median);

    private static string OneDecimal(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);

    private static Element HeaderBlock(string title, string caption) =>
        VStack(2, BodyStrong(title), Kit.Muted(caption).TextWrapping());

    private static Element ChartCard(string title, string caption, string headline, IReadOnlyList<double> values, IReadOnlyList<DateTimeOffset> starts, Func<int, string> tip) =>
        Card(VStack(10,
                HeaderBlock(title, caption),
                TextBlock(headline).FontSize(20).SemiBold(),
                BarChart(values, starts, tip)))
            .Flex(grow: 1, basis: 420);

    /// <summary>One bar per week, scaled to the tallest. The current (last) week is drawn in the accent colour, the rest quieter.</summary>
    private static Element BarChart(IReadOnlyList<double> values, IReadOnlyList<DateTimeOffset> starts, Func<int, string> tip)
    {
        var n = values.Count;
        if (n == 0) return Kit.EmptyNote("No data.");
        var max = Math.Max(values.Max(), 0.0001);

        var columns = Enumerable.Repeat(GridSize.Star(), n).ToArray();
        var cells = new List<Element>();
        for (var i = 0; i < n; i++)
        {
            var value = values[i];
            var height = value <= 0 ? 2.0 : Math.Max(4.0, value / max * ChartHeight);
            var fill = value <= 0 ? Theme.DividerStroke : i == n - 1 ? Theme.Accent : Theme.AccentSecondary;
            var label = starts[i].ToString("MMM d", CultureInfo.InvariantCulture);
            cells.Add(Border(Empty())
                .Height(height).CornerRadius(2).Margin(2, 0, 2, 0).Background(fill)
                .VAlign(VerticalAlignment.Bottom)
                .ToolTip("Week of " + label + ": " + tip(i))
                .Grid(column: i, row: 0)
                .WithKey("bar-" + i));

            // A date under every third bar, counted back from the current week, right-aligned under its bar.
            if ((n - 1 - i) % 3 == 0)
            {
                var from = Math.Max(0, i - 2);
                cells.Add(Kit.Muted(label).FontSize(10)
                    .HAlign(HorizontalAlignment.Right).Margin(0, 4, 2, 0)
                    .Grid(column: from, row: 1, columnSpan: i - from + 1)
                    .WithKey("label-" + i));
            }
        }

        return Grid(columns, [GridSize.Px(ChartHeight), GridSize.Auto], cells.ToArray());
    }

    private static Element SpreadRow(string label, Spread s)
    {
        if (s.Count == 0) return VStack(2, BodyStrong(label), Kit.Muted("No data in this period."));

        var median = Math.Max(s.Median, 0.0001);
        var toP90 = Math.Max(s.P90 - s.Median, 0.0001);
        var tail = Math.Max(s.Max - s.P90, 0.0001);

        var bar = Grid(
            columns: [GridSize.Star(median), GridSize.Star(toP90), GridSize.Star(tail)],
            rows: [GridSize.Auto],
            Border(Empty()).Height(10).CornerRadius(2).Margin(0, 0, 1, 0).Background(Theme.Accent).Grid(column: 0),
            Border(Empty()).Height(10).CornerRadius(2).Margin(0, 0, 1, 0).Background(Theme.AccentSecondary).Grid(column: 1),
            Border(Empty()).Height(10).CornerRadius(2).Background(Theme.DividerStroke).Grid(column: 2));

        return VStack(6,
            Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                BodyStrong(label).Grid(column: 0),
                Kit.Muted(s.Count + " PRs").Grid(column: 1)),
            bar,
            FlexRow(
                Kit.Muted("median " + StatsFormat.Duration(s.Median)).Foreground(Theme.AccentText),
                Kit.Muted("p90 " + StatsFormat.Duration(s.P90)),
                Kit.Muted("slowest " + StatsFormat.Duration(s.Max))) with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 2 });
    }

    private static Element Legend() =>
        Kit.Muted("The bar runs from zero to the slowest PR. Dark is up to the median, lighter up to the 90th percentile.").TextWrapping();

    private static Element SizeRow(SizeBucket bucket)
    {
        var f = Math.Clamp(bucket.Fraction, 0, 1);
        return Grid(
            columns: [GridSize.Px(32), GridSize.Star(), GridSize.Px(92)],
            rows: [GridSize.Auto],
            BodyStrong(bucket.Label).VAlign(VerticalAlignment.Center).Grid(column: 0),
            Track(f).VAlign(VerticalAlignment.Center).Grid(column: 1),
            Kit.Muted(bucket.Count + " · " + Math.Round(f * 100).ToString("0", CultureInfo.InvariantCulture) + " %")
                .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 2))
            .ToolTip(bucket.Range)
            .WithKey("size-" + bucket.Label);
    }

    private static Element RepoRow(RepoCount repo, int max)
    {
        var f = max <= 0 ? 0 : (double)repo.Count / max;
        return Grid(
            columns: [GridSize.Star(2), GridSize.Star(3), GridSize.Px(28)],
            rows: [GridSize.Auto],
            TextBlock(repo.Repo).TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 0),
            Track(f).VAlign(VerticalAlignment.Center).Margin(8, 0, 8, 0).Grid(column: 1),
            Kit.Muted(repo.Count.ToString()).HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(column: 2))
            .WithKey("repo-" + repo.Repo);
    }

    /// <summary>A horizontal bar filled to <paramref name="fraction"/> (0 to 1).</summary>
    private static Element Track(double fraction)
    {
        var f = Math.Clamp(fraction, 0, 1);
        return Border(Grid(
                columns: [GridSize.Star(Math.Max(f, 0.0001)), GridSize.Star(Math.Max(1 - f, 0.0001))],
                rows: [GridSize.Auto],
                Border(Empty()).Height(10).CornerRadius(2).Background(f <= 0 ? Theme.DividerStroke : Theme.Accent).Grid(column: 0),
                Empty().Grid(column: 1)))
            .CornerRadius(2);
    }

    private static Element RightNow(HubSnapshot snap)
    {
        var waiting = OverviewModel.Compute(snap).ReviewsForYou.Count;
        var oldest = snap.Authored.Count == 0 ? null : snap.Authored.OrderBy(p => p.CreatedAt).First();

        Element Tile(string label, string value, string hint) =>
            Card(VStack(2,
                Kit.Muted(label),
                TextBlock(value).FontSize(28).SemiBold(),
                Kit.Muted(hint).TextWrapping())).Flex(grow: 1, basis: 200);

        return VStack(8,
            Kit.SectionLabel("Right now").Margin(0, 8, 0, 0),
            FlexRow(
                Tile("Open PRs", snap.Authored.Count.ToString(), "authored by you"),
                Tile("Review queue", snap.Reviews.Count.ToString(), waiting == 0 ? "nothing waiting on you" : waiting + " waiting on you"),
                Tile("Oldest open PR", oldest is null ? "–" : Age.Format(oldest.CreatedAt, snap.At), oldest is null ? "nothing open" : oldest.Repo + " #" + oldest.Number))
                with { Wrap = FlexWrap.Wrap, ColumnGap = 12, RowGap = 12 });
    }
}
