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

    /// <summary>
    /// Geometric-mean centre of a channel's playing band, used to order the
    /// channels along the spectrum.
    /// </summary>
    public static double BandCenterHz(VirtualCrossoverChannelSettings settings)
    {
        (double lowHz, double highHz) = GetChannelBand(settings);
        return Math.Sqrt(lowHz * highHz);
    }

    /// <summary>
    /// An octave to each side of a handover frequency, clamped to the audio band:
    /// the overlap region where two adjacent drivers genuinely sum.
    /// </summary>
    public static (double LowHz, double HighHz) OverlapBand(double centerHz) =>
        (Math.Max(20, centerHz / 2), Math.Min(20_000, centerHz * 2));

    /// <summary>
    /// The frequency window worth plotting for a set of channels: an octave below
    /// the lowest crossover corner to an octave above the highest, clamped to the
    /// audio band, or a sensible default when no channel is filtered.
    /// </summary>
    public static (double MinHz, double MaxHz) GetCrossoverWindow(
        IEnumerable<VirtualCrossoverChannelSettings> channels)
    {
        var corners = new List<double>();
        foreach (VirtualCrossoverChannelSettings settings in channels)
        {
            if (settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass)
            {
                corners.Add(settings.LowPassEdge.FrequencyHz);
            }
            if (settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass)
            {
                corners.Add(settings.HighPassEdge.FrequencyHz);
            }
        }

        if (corners.Count == 0)
        {
            return (100, 10_000);
        }

        return (Math.Max(20, corners.Min() / 2), Math.Min(20_000, corners.Max() * 2));
    }
}
