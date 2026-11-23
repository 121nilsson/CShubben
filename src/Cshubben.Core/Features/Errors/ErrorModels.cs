namespace Cshubben.Core.Features.Errors;

/// <summary>
/// The time range the Errors page looks at, cut into equally long buckets. Bucket <c>i</c> covers
/// <c>[Start + i * Step, Start + (i + 1) * Step)</c>. The default is the last 24 hours in 5 minute buckets.
/// </summary>
public sealed record ErrorWindow(DateTimeOffset Start, TimeSpan Step, int Buckets)
{
    public static readonly TimeSpan DefaultStep = TimeSpan.FromMinutes(5);

    public const int DefaultBuckets = 288;

    public DateTimeOffset End => Start + TimeSpan.FromTicks(Step.Ticks * Buckets);

    public static ErrorWindow Default(DateTimeOffset now) => Ending(now, DefaultStep, DefaultBuckets);

    /// <summary>A window that ends at the last whole step before <paramref name="now"/>, so every run lines up on the same buckets.</summary>
    public static ErrorWindow Ending(DateTimeOffset now, TimeSpan step, int buckets)
    {
        if (step < TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(step), "A step is at least one second.");
        if (buckets < 1) throw new ArgumentOutOfRangeException(nameof(buckets), "A window has at least one bucket.");
        var stepSeconds = (long)step.TotalSeconds;
        var end = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / stepSeconds * stepSeconds);
        return new ErrorWindow(end - TimeSpan.FromTicks(TimeSpan.FromSeconds(stepSeconds).Ticks * buckets), TimeSpan.FromSeconds(stepSeconds), buckets);
    }

    /// <summary>The bucket a point in time falls in, or -1 when it is outside the window.</summary>
    public int IndexOf(DateTimeOffset at)
    {
        var ticks = (at - Start).Ticks;
        if (ticks < 0) return -1;
        var index = ticks / Step.Ticks;
        return index < Buckets ? (int)index : -1;
    }

    /// <summary>
    /// The bucket of a Loki sample. A range query sample at time <c>t</c> counts the lines in <c>(t - step, t]</c>, so the sample
    /// at <c>Start + (i + 1) * Step</c> belongs to bucket <c>i</c>. Returns -1 outside the window.
    /// </summary>
    public int IndexOfSampleEnd(double unixSeconds)
    {
        var seconds = unixSeconds - Start.ToUnixTimeSeconds();
        var index = (int)Math.Round(seconds / Step.TotalSeconds) - 1;
        return index >= 0 && index < Buckets ? index : -1;
    }
}

/// <summary>One log line from Loki.</summary>
public sealed record LogEntry(DateTimeOffset At, string App, string Pod, string Level, string Message);

/// <summary>Error counts per bucket for one application. <see cref="Counts"/> has one value per bucket of the window.</summary>
public sealed record ErrorSeries(string App, IReadOnlyList<int> Counts);

public enum Trend { Flat, Up, Down }

/// <summary>One application in the list: its counts, how they compare with normal, and where they are heading.</summary>
/// <param name="Now">Mean errors per bucket in the latest hour.</param>
/// <param name="Baseline">Median errors per bucket before that.</param>
/// <param name="Factor">Now divided by the baseline (the baseline counts as at least one per bucket).</param>
public sealed record AppSpike(
    string App,
    IReadOnlyList<int> Counts,
    int Total,
    double Now,
    double Baseline,
    double Factor,
    Trend Trend,
    bool IsSpike);

/// <summary>What the Errors page shows for all applications.</summary>
public sealed record ErrorsSnapshot(ErrorWindow Window, IReadOnlyList<AppSpike> Apps, DateTimeOffset At)
{
    public int Spiking => Apps.Count(a => a.IsSpike);

    public int WithErrors => Apps.Count(a => a.Total > 0);

    public static ErrorsSnapshot Empty(DateTimeOffset now) => new(ErrorWindow.Default(now), Array.Empty<AppSpike>(), now);
}

public enum ErrorKind
{
    /// <summary>A logged exception.</summary>
    Exception,

    /// <summary>A machine-to-machine call was refused because the client lacks a backend_role in PolicyServer.</summary>
    MissingBackendRole,

    /// <summary>An error line without an exception.</summary>
    Message,
}

/// <summary>Log lines that are the same problem, counted per bucket.</summary>
/// <param name="Key">The fingerprint the lines share.</param>
/// <param name="Hint">What to do about it, when the kind of error tells.</param>
/// <param name="Method">Full name of the top application frame (namespace, type, method), when there is a stack trace.</param>
/// <param name="File">Path of that frame inside its repository (starts with the project folder), when known.</param>
public sealed record ErrorGroup(
    string Key,
    string Title,
    string Detail,
    string? Hint,
    ErrorKind Kind,
    int Count,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    IReadOnlyList<string> Pods,
    IReadOnlyList<int> Counts,
    string? Method,
    string? File);
