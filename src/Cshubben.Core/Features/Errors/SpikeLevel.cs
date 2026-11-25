namespace Cshubben.Core.Features.Errors;

/// <summary>How loudly a spiking application should be drawn in the list.</summary>
public enum SpikeLevel
{
    /// <summary>Slightly above normal: a red edge only.</summary>
    Mild,

    /// <summary>Clearly above normal: a red edge and a larger multiplier.</summary>
    Elevated,

    /// <summary>Far above normal: the whole row is tinted red.</summary>
    Severe,
}

/// <summary>
/// Scales the urgency of a spiking application with its multiplier, so six spikes do not all shout equally.
/// Pure, so the thresholds are tested.
/// </summary>
public static class SpikeLevels
{
    public const double SevereFrom = 5;
    public const double ElevatedFrom = 3;

    public static SpikeLevel Of(double factor) =>
        factor >= SevereFrom ? SpikeLevel.Severe : factor >= ElevatedFrom ? SpikeLevel.Elevated : SpikeLevel.Mild;

    /// <summary>Font size of the multiplier in the list: bigger for a bigger spike.</summary>
    public static double FontSize(SpikeLevel level) => level switch
    {
        SpikeLevel.Severe => 18,
        SpikeLevel.Elevated => 16,
        _ => 14,
    };
}
