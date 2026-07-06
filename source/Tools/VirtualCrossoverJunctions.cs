using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Pure junction/band arithmetic of the Virtual DSP tool: which band a channel
/// plays in and where two adjacent channels hand over. Kept free of UI state so
/// the rules are unit-testable.
/// </summary>
internal static class VirtualCrossoverJunctions
{
    /// <summary>
    /// The band a channel actually plays in: its crossover corners when set, the
    /// full range otherwise. Used to order the channels along the spectrum.
    /// </summary>
    public static (double LowHz, double HighHz) GetChannelBand(
        VirtualCrossoverChannelSettings settings)
    {
        double lowHz =
            settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
                ? settings.HighPassEdge.FrequencyHz
                : 20;
        double highHz =
            settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass
                ? settings.LowPassEdge.FrequencyHz
                : 20_000;
        return highHz > lowHz ? (lowHz, highHz) : (20, 20_000);
    }

    /// <summary>
    /// The crossover frequency between two adjacent channels: the lower one's
    /// low-pass corner when set, the upper one's high-pass corner otherwise, and
    /// the geometric mean of their band centers as the filterless fallback.
    /// </summary>
    public static double GetPairCrossoverHz(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper)
    {
        if (lower.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass)
        {
            return lower.LowPassEdge.FrequencyHz;
        }
        if (upper.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass)
        {
            return upper.HighPassEdge.FrequencyHz;
        }

        (double lowerLow, double lowerHigh) = GetChannelBand(lower);
        (double upperLow, double upperHigh) = GetChannelBand(upper);
        return Math.Sqrt(
            Math.Sqrt(lowerLow * lowerHigh) * Math.Sqrt(upperLow * upperHigh));
    }
}
