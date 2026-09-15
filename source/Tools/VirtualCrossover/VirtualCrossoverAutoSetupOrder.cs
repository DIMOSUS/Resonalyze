using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Chain order for a group, from effective bands. See docs/tech/crossover-auto-setup.md#chain-order.</summary>
public static class VirtualCrossoverAutoSetupOrder
{
    /// <summary>Same half-octave the optimizer demands between adjacent junctions.</summary>
    public const double AmbiguousSeparationOctaves = 0.5;

    /// <summary>Measured band narrowed by existing corners; a pair that leaves nothing falls back to the measured band.</summary>
    public static (double LowHz, double HighHz) EffectiveBand(
        DriverBandEstimate band,
        double? highPassHz,
        double? lowPassHz)
    {
        ArgumentNullException.ThrowIfNull(band);

        double low = Math.Max(band.LowHz, highPassHz ?? 0);
        double high = Math.Min(band.HighHz, lowPassHz ?? double.PositiveInfinity);
        return high > low ? (low, high) : (band.LowHz, band.HighHz);
    }

    public static double CenterHz(
        DriverBandEstimate band,
        double? highPassHz,
        double? lowPassHz)
    {
        (double low, double high) = EffectiveBand(band, highPassHz, lowPassHz);
        return Math.Sqrt(low * high);
    }

    public static VirtualCrossoverChainOrder Judge(
        double earlierCenterHz,
        double laterCenterHz)
    {
        if (!(earlierCenterHz > 0) || !(laterCenterHz > 0))
        {
            return VirtualCrossoverChainOrder.AsMeasured;
        }

        double octaves = Math.Log2(laterCenterHz / earlierCenterHz);
        if (Math.Abs(octaves) < AmbiguousSeparationOctaves)
        {
            return VirtualCrossoverChainOrder.Unclear;
        }

        return octaves > 0
            ? VirtualCrossoverChainOrder.AsMeasured
            : VirtualCrossoverChainOrder.Reversed;
    }
}

public enum VirtualCrossoverChainOrder
{
    AsMeasured,

    /// <summary>Within half an octave, e.g. two full-range subs: the shown order is a guess.</summary>
    Unclear,

    Reversed
}
