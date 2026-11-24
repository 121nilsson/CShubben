namespace Cshubben.Core.Features.Errors;

/// <summary>What the page reads errors from. Grafana in real use, generated data for demos and screenshots.</summary>
public interface IErrorSource
{
    /// <summary>True when the data is made up.</summary>
    bool IsDemo { get; }

    /// <summary>Counts per application for the last 24 hours, analysed.</summary>
    Task<ErrorsSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>The error groups of one application for the last 24 hours, most frequent first.</summary>
    Task<IReadOnlyList<ErrorGroup>> LoadGroupsAsync(string app, CancellationToken cancellationToken = default);

    /// <summary>Counts per application for the two hours that start where the latest hour started one week ago (see <see cref="WeeklyBaseline"/>).</summary>
    Task<IReadOnlyList<ErrorSeries>> LoadLastWeekAsync(CancellationToken cancellationToken = default);
}

/// <summary>What to read from Grafana. The caller builds it from the settings (this slice does not know about settings).</summary>
/// <param name="Namespace">Value of the namespace label, for example "default".</param>
/// <param name="IncludeWarnings">Also read warnings, not only errors.</param>
/// <param name="LokiDatasource">uid of the Loki data source, or empty to use the first one.</param>
/// <param name="KnownApps">Applications to show even when they had no errors.</param>
public sealed record ErrorsQuery(string Namespace, bool IncludeWarnings, string LokiDatasource, IReadOnlyList<string> KnownApps);

/// <summary>Reads errors from Grafana (Loki).</summary>
public sealed class GrafanaErrorSource : IErrorSource
{
    private readonly GrafanaClient _client;
    private readonly ErrorsQuery _query;
    private readonly Func<DateTimeOffset> _clock;
    private LokiDatasource? _source;

    public GrafanaErrorSource(GrafanaClient client, ErrorsQuery query, Func<DateTimeOffset>? clock = null)
    {
        _client = client;
        _query = query;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsDemo => false;

    public async Task<ErrorsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var source = await SourceAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock();
        var window = ErrorWindow.Default(now);
        var series = await _client.GetErrorSeriesAsync(source, window, _query.Namespace, _query.IncludeWarnings, cancellationToken)
            .ConfigureAwait(false);
        return ErrorsOverview.Build(window, series, _query.KnownApps, now);
    }

    public async Task<IReadOnlyList<ErrorGroup>> LoadGroupsAsync(string app, CancellationToken cancellationToken = default)
    {
        var source = await SourceAsync(cancellationToken).ConfigureAwait(false);
        var window = ErrorWindow.Default(_clock());
        var lines = await _client.GetErrorLinesAsync(source, window, _query.Namespace, app, _query.IncludeWarnings, 1000, cancellationToken)
            .ConfigureAwait(false);
        return ErrorGrouping.Group(lines, window);
    }

    public async Task<IReadOnlyList<ErrorSeries>> LoadLastWeekAsync(CancellationToken cancellationToken = default)
    {
        var source = await SourceAsync(cancellationToken).ConfigureAwait(false);
        var window = WeeklyBaseline.WindowFor(_clock());
        return await _client.GetErrorSeriesAsync(source, window, _query.Namespace, _query.IncludeWarnings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LokiDatasource> SourceAsync(CancellationToken cancellationToken) =>
        _source ??= await _client.ResolveAsync(_query.LokiDatasource, cancellationToken).ConfigureAwait(false);
}
