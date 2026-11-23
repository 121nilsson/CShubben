using System.Globalization;
using System.Text;

namespace Cshubben.Core.Features.Errors;

/// <summary>
/// Builds the LogQL queries. Label values are always quoted and escaped, so an odd application name can never change the query.
/// The label names are the ones used in iBinder's Grafana (appname, namespace, loglevel).
/// </summary>
public static class LogQl
{
    public const string AppLabel = "appname";
    public const string NamespaceLabel = "namespace";
    public const string LevelLabel = "loglevel";

    /// <summary>Quotes a string for LogQL: backslash, double quote and control characters are escaped.</summary>
    public static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': text.Append("\\\\"); break;
                case '"': text.Append("\\\""); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default: text.Append(c); break;
            }
        }

        return text.Append('"').ToString();
    }

    /// <summary>Loki label regexes are anchored, so this matches whole level names, in any case.</summary>
    public static string LevelPattern(bool includeWarnings) =>
        includeWarnings ? "(?i)error|critical|fatal|warn|warning" : "(?i)error|critical|fatal";

    /// <summary>The log stream of one namespace, or of one application in it, filtered to error levels.</summary>
    public static string Selector(string ns, string? app, bool includeWarnings)
    {
        var parts = new List<string> { NamespaceLabel + "=" + Quote(ns) };
        if (!string.IsNullOrEmpty(app)) parts.Add(AppLabel + "=" + Quote(app));
        parts.Add(LevelLabel + "=~" + Quote(LevelPattern(includeWarnings)));
        return "{" + string.Join(", ", parts) + "}";
    }

    /// <summary>Error lines per application, counted over one step. One query gives the series of every application.</summary>
    public static string ErrorCounts(string ns, bool includeWarnings, TimeSpan step) =>
        "sum by (" + AppLabel + ") (count_over_time(" + Selector(ns, null, includeWarnings) + "["
        + ((long)step.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s]))";

    /// <summary>The error lines of one application.</summary>
    public static string Lines(string ns, string app, bool includeWarnings) => Selector(ns, app, includeWarnings);
}
