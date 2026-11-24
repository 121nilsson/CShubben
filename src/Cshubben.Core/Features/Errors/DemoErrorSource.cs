using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cshubben.Core.Features.Errors;

/// <summary>
/// Made-up error data for --demo, screenshots and tests. Deterministic (the same clock gives the same data). The log lines have
/// the shape of real .NET logs (header, message, exception, stack trace) and go through the real fingerprinting and grouping,
/// but every name and id is invented.
/// </summary>
public sealed class DemoErrorSource : IErrorSource
{
    private static readonly string[] PodSuffixes = { "fp46m", "x2k9d", "q7w1z" };

    private static readonly Profile[] Profiles =
    {
        // A real spike: nine times the normal rate for the last 45 minutes, since a "deploy".
        new("binders-api", "bbc6c45f6", 2.0, 9.0, 9, new[]
        {
            new Template(Template.PermissionsNull, 10),
            new Template(Template.MissingRole, 2),
        }),

        // Rising, but still inside what is normal for it: shows the arrow without the spike label.
        new("notifications-messagefactory", "749c4bb975", 1.0, 2.2, 12, new[]
        {
            new Template(Template.EventHandlerNull, 1),
        }),

        // Noisy and steady.
        new("properties-api", "5d9f8c7b44", 3.0, 1.0, 0, new[]
        {
            new Template(Template.SqlTimeout, 1),
        }),

        // Quiet, now and then one error.
        new("activities-api", "6f7d9b8c5", 0.15, 1.0, 0, new[]
        {
            new Template(Template.CleanupFailed, 1),
        }),

        // Healthy: never an error.
        new("contracts-api", "7c8b6d5f9", 0.0, 1.0, 0, Array.Empty<Template>()),
    };

    private readonly Func<DateTimeOffset> _clock;

    public DemoErrorSource(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The applications of the demo data.</summary>
    public static IReadOnlyList<string> AppNames { get; } = Profiles.Select(p => p.App).ToList();

    public bool IsDemo => true;

    /// <summary>
    /// Made-up deployments of an application (newest last), so the chart markers and "started after deploy" can be seen in demos
    /// and screenshots. The spiking application got a deployment shortly before its spike began.
    /// </summary>
    public static IReadOnlyList<DeployedVersion> DeploysFor(string app, DateTimeOffset now)
    {
        var minutesAgo = app switch
        {
            "binders-api" => new[] { 1020, 52 },
            "notifications-messagefactory" => new[] { 400 },
            "properties-api" => new[] { 1300 },
            _ => Array.Empty<int>(),
        };

        var creators = new[] { "anna", "peter" };
        return minutesAgo
            .Select((m, i) => new DeployedVersion(
                "prod",
                Sha(app + "|" + i.ToString(CultureInfo.InvariantCulture)),
                "main",
                creators[i % creators.Length],
                now - TimeSpan.FromMinutes(m)))
            .ToList();
    }

    private static string Sha(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..40].ToLowerInvariant();

    public Task<ErrorsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock();
        var window = ErrorWindow.Default(now);
        var series = new List<ErrorSeries>();
        foreach (var profile in Profiles)
        {
            var entries = Generate(profile, window);
            if (entries.Count == 0) continue;
            var counts = new int[window.Buckets];
            foreach (var entry in entries)
            {
                var index = window.IndexOf(entry.At);
                if (index >= 0) counts[index]++;
            }

            series.Add(new ErrorSeries(profile.App, counts));
        }

        return Task.FromResult(ErrorsOverview.Build(window, series, AppNames, now));
    }

    /// <summary>A quiet week ago: the same baseline, without the spike, so the demo spike reads as "higher than a week ago".</summary>
    public Task<IReadOnlyList<ErrorSeries>> LoadLastWeekAsync(CancellationToken cancellationToken = default)
    {
        var window = WeeklyBaseline.WindowFor(_clock());
        var series = new List<ErrorSeries>();
        foreach (var profile in Profiles)
        {
            var counts = new int[window.Buckets];
            foreach (var entry in Generate(profile, window, spike: false))
            {
                var index = window.IndexOf(entry.At);
                if (index >= 0) counts[index]++;
            }

            series.Add(new ErrorSeries(profile.App, counts));
        }

        return Task.FromResult<IReadOnlyList<ErrorSeries>>(series);
    }

    public Task<IReadOnlyList<ErrorGroup>> LoadGroupsAsync(string app, CancellationToken cancellationToken = default)
    {
        var window = ErrorWindow.Default(_clock());
        var profile = Profiles.FirstOrDefault(p => string.Equals(p.App, app, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<ErrorGroup> groups = profile is null
            ? Array.Empty<ErrorGroup>()
            : ErrorGrouping.Group(Generate(profile, window), window);
        return Task.FromResult(groups);
    }

    private static List<LogEntry> Generate(Profile profile, ErrorWindow window, bool spike = true)
    {
        var entries = new List<LogEntry>();
        if (profile.Baseline <= 0 || profile.Templates.Length == 0) return entries;

        var random = new Random(profile.App.Aggregate(17, (h, c) => unchecked(h * 31 + c)));
        var totalWeight = profile.Templates.Sum(t => t.Weight);
        var stepSeconds = (int)window.Step.TotalSeconds;

        for (var bucket = 0; bucket < window.Buckets; bucket++)
        {
            var inSpike = spike && bucket >= window.Buckets - profile.SpikeBuckets;
            var rate = profile.Baseline * (inSpike ? profile.SpikeFactor : 1.0);
            var count = Math.Max(0, (int)Math.Round(rate + (random.NextDouble() - 0.5) * 2 * Math.Sqrt(Math.Max(rate, 0.25))));
            for (var i = 0; i < count; i++)
            {
                var at = window.Start + TimeSpan.FromTicks(window.Step.Ticks * bucket) + TimeSpan.FromSeconds(random.Next(0, stepSeconds));
                var template = Pick(profile.Templates, totalWeight, random);
                var pod = profile.App + "-" + profile.PodHash + "-" + PodSuffixes[random.Next(PodSuffixes.Length)];
                var level = template.Kind == Template.MissingRole ? "Warning" : "Error";
                entries.Add(new LogEntry(at, profile.App, pod, level, Render(template.Kind, level, at, pod, random)));
            }
        }

        return entries.OrderBy(e => e.At).ToList();
    }

    private static Template Pick(Template[] templates, int totalWeight, Random random)
    {
        var roll = random.Next(totalWeight);
        foreach (var template in templates)
        {
            if (roll < template.Weight) return template;
            roll -= template.Weight;
        }

        return templates[^1];
    }

    private static string Render(string kind, string level, DateTimeOffset at, string pod, Random random)
    {
        var stamp = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var trace = random.NextInt64().ToString("x16", CultureInfo.InvariantCulture);

        string Header(string logger) => "[" + stamp + " " + level + "] " + pod + " - " + logger + " - " + trace;

        switch (kind)
        {
            case Template.PermissionsNull:
            {
                var resource = Code(random);
                var tenant = "tenant-" + random.Next(1, 40).ToString(CultureInfo.InvariantCulture);
                return string.Join("\n",
                    Header("Acme.BuildingBlocks.Core.Authorization.Runtime.TenantBasedRuntimeService"),
                    "Could not get the user's permissions on resource: 'Id: \"" + resource + "\", name: \"Binder_" + resource
                    + "\"'. Could not get permissions for policyPath: '\"Core, Binders\"' and tenantPath: '\"" + tenant + ", " + tenant
                    + "_Binders, " + tenant + "_Binders_Binder_" + resource + "\"'",
                    "System.NullReferenceException: Object reference not set to an instance of an object.",
                    "   at Acme.BuildingBlocks.Infrastructure.Authorization.PolicyServer.PolicyServerRuntimeRepository.GetGroups(IEnumerable`1 tenantPath) in /_/src/Acme.BuildingBlocks.Infrastructure.Authorization/PolicyServer/PolicyServerRuntimeRepository.cs:line 154",
                    "   at Acme.BuildingBlocks.Infrastructure.Authorization.PolicyServer.PolicyServerRuntimeRepository.GetPermissions(IEnumerable`1 policyPath, IEnumerable`1 tenantPath, Boolean includeChildPolicies) in /_/src/Acme.BuildingBlocks.Infrastructure.Authorization/PolicyServer/PolicyServerRuntimeRepository.cs:line 61",
                    "   at Acme.BuildingBlocks.Core.Authorization.Runtime.TenantBasedRuntimeService`4.GetPermissionsToResource(TResource resource, Boolean includePermissionsFromAnyDomain) in /_/src/Acme.BuildingBlocks.Core/Authorization/Runtime/TenantBasedRuntimeService.cs:line 109");
            }

            case Template.MissingRole:
                return string.Join("\n",
                    Header("Acme.BuildingBlocks.Api.Authorization.Handlers.MachineToMachineAuthorizationHandler"),
                    "User with clientId '\"" + Guid(random) + "\"' does not have backend_role '\"binders_get_participants\"' required to fulfill machine requirement.");

            case Template.EventHandlerNull:
                return string.Join("\n",
                    Header("Acme.Notifications.MessageFactory.Services.AzureListener"),
                    "Failed to handle the event.",
                    "System.ArgumentNullException: Value cannot be null. (Parameter 'source')",
                    "   at System.ThrowHelper.ThrowArgumentNullException(ExceptionArgument argument)",
                    "   at System.Linq.Enumerable.TryGetFirst[TSource](IEnumerable`1 source, Func`2 predicate, Boolean& found)",
                    "   at Acme.Notifications.MessageFactory.Services.Handlers.Inspections.InspectorChangedEventHandler.ExtractNotificationDataFromEvent(InspectorChangedEvent ev) in /src/Notifications.MessageFactory/Services/Handlers/Inspections/InspectorChangedEventHandler.cs:line 107",
                    "   at Acme.Notifications.MessageFactory.Services.Handlers.Inspections.InspectorChangedEventHandler.Process(EventEnvelope eventEnvelope, String handledEvent) in /src/Notifications.MessageFactory/Services/Handlers/Inspections/InspectorChangedEventHandler.cs:line 58");

            case Template.SqlTimeout:
                return string.Join("\n",
                    Header("Acme.Properties.Api.Services.ImportService"),
                    "Import of property " + random.Next(1000, 9999).ToString(CultureInfo.InvariantCulture) + " timed out.",
                    "System.TimeoutException: The operation has timed out.",
                    "   at Acme.Properties.Infrastructure.Sql.PropertyRepository.LoadAsync(Guid id) in /_/src/Acme.Properties.Infrastructure/Sql/PropertyRepository.cs:line 88");

            default:
                return string.Join("\n",
                    Header("Acme.Activities.Api.Jobs.CleanupJob"),
                    "Cleanup of " + random.Next(2, 90).ToString(CultureInfo.InvariantCulture) + " expired items failed, retrying in 5 minutes.");
        }
    }

    private static string Code(Random random)
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "0123456789";
        var chars = new char[9];
        for (var i = 0; i < chars.Length; i++) chars[i] = i % 3 == 2 ? digits[random.Next(digits.Length)] : letters[random.Next(letters.Length)];
        return new string(chars);
    }

    private static string Guid(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new System.Guid(bytes).ToString();
    }

    private sealed record Profile(string App, string PodHash, double Baseline, double SpikeFactor, int SpikeBuckets, Template[] Templates);

    private sealed record Template(string Kind, int Weight)
    {
        public const string PermissionsNull = "permissions-null";
        public const string MissingRole = "missing-role";
        public const string EventHandlerNull = "event-handler-null";
        public const string SqlTimeout = "sql-timeout";
        public const string CleanupFailed = "cleanup-failed";
    }
}
