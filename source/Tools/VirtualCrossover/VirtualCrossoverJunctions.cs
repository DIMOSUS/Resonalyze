using Resonalyze.Dsp;

namespace Resonalyze;

internal static class VirtualCrossoverJunctions
{
    /// <summary>Crossover corners via <see cref="VirtualCrossoverChannelSettings.EffectiveCrossover"/> (IIR, or FIR when IIR is off), full range otherwise.</summary>
    public static (double LowHz, double HighHz) GetChannelBand(
        VirtualCrossoverChannelSettings settings)
    {
        double lowHz = settings.EffectiveHighPassHz ?? 20;
        double highHz = settings.EffectiveLowPassHz ?? 20_000;
        return highHz > lowHz ? (lowHz, highHz) : (20, 20_000);
    }

    /// <summary>Lower low-pass, else upper high-pass, else geometric mean of band centres.</summary>
    public static double GetPairCrossoverHz(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper)
    {
        if (lower.EffectiveLowPassHz is { } lowerLowPass)
        {
            return lowerLowPass;
        }
        if (upper.EffectiveHighPassHz is { } upperHighPass)
        {
            return upperHighPass;
        }

        (double lowerLow, double lowerHigh) = GetChannelBand(lower);
        (double upperLow, double upperHigh) = GetChannelBand(upper);
        return Math.Sqrt(
            Math.Sqrt(lowerLow * lowerHigh) * Math.Sqrt(upperLow * upperHigh));
    }

    public static double BandCenterHz(VirtualCrossoverChannelSettings settings)
    {
        (double lowHz, double highHz) = GetChannelBand(settings);
        return Math.Sqrt(lowHz * highHz);
    }

    /// <summary>One octave each side of the handover: where adjacent drivers genuinely sum.</summary>
    public static (double LowHz, double HighHz) OverlapBand(double centerHz) =>
        (Math.Max(20, centerHz / 2), Math.Min(20_000, centerHz * 2));

    public static (double MinHz, double MaxHz) GetCrossoverWindow(
        IEnumerable<VirtualCrossoverChannelSettings> channels)
    {
        var corners = new List<double>();
        foreach (VirtualCrossoverChannelSettings settings in channels)
        {
            if (settings.EffectiveLowPassHz is { } lowPass)
            {
                corners.Add(lowPass);
            }
            if (settings.EffectiveHighPassHz is { } highPass)
            {
                corners.Add(highPass);
            }
        }

        if (corners.Count == 0)
        {
            return (100, 10_000);
        }

        return (Math.Max(20, corners.Min() / 2), Math.Min(20_000, corners.Max() * 2));
    }
}
