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

    /// <summary>How far down two slopes may cross and still hand over a gap. See docs/tech/virtual-dsp-analysis.md#measured-bands-and-junctions.</summary>
    public const double GapHandoverFloorDb = -12;

    private const int GapHandoverSteps = 48;

    // Where no processor is named: high enough that the bilinear warp leaves an audio-band slope where its corner puts it.
    private const int UnwarpedRateHz = 192_000;

    /// <summary>Where the slopes across a gap of an octave or more cross, as the chain realizes them; null for closer corners or a crossing below <see cref="GapHandoverFloorDb"/>.</summary>
    public static double? GapHandoverHz(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper,
        int? processorSampleRateHz = null) =>
        GapCrossing(lower, upper, processorSampleRateHz) is { } crossing && crossing.LevelDb >= GapHandoverFloorDb
            ? crossing.Hz
            : null;

    /// <summary>The slopes' crossing across a gap of an octave or more, as the chain realizes them: where, and how far down
    /// the two meet. Null for closer corners, a missing corner, or a high-pass at or past Nyquist.</summary>
    public static (double Hz, double LevelDb)? GapCrossing(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper,
        int? processorSampleRateHz = null)
    {
        int rateHz = processorSampleRateHz is > 0 ? processorSampleRateHz.Value : UnwarpedRateHz;
        if (lower.EffectiveLowPassHz is not { } lowPassHz ||
            upper.EffectiveHighPassHz is not { } highPassHz ||
            highPassHz < 2 * lowPassHz ||
            highPassHz >= rateHz / 2.0)
        {
            return null;
        }

        double[] frequencies = Enumerable.Range(0, GapHandoverSteps + 1)
            .Select(step => lowPassHz * Math.Pow(highPassHz / lowPassHz, (double)step / GapHandoverSteps))
            .ToArray();
        double[] lowerLevels = FilterMagnitudes(lower, frequencies, rateHz);
        double[] upperLevels = FilterMagnitudes(upper, frequencies, rateHz);
        int crossing = Enumerable.Range(0, frequencies.Length)
            .MaxBy(index => Math.Min(lowerLevels[index], upperLevels[index]));
        return (frequencies[crossing], 20 * Math.Log10(Math.Min(lowerLevels[crossing], upperLevels[crossing])));
    }

    // Both filtering stages as the chain runs them: a FIR's slope is its kernel's, not its edges'.
    private static double[] FilterMagnitudes(
        VirtualCrossoverChannelSettings settings, double[] frequencies, int rateHz)
    {
        double[] magnitudes = Enumerable.Repeat(1.0, frequencies.Length).ToArray();
        if (settings.CrossoverKind != CrossoverKind.Off)
        {
            var iir = new CrossoverSpec(settings.CrossoverKind, settings.LowPassEdge, settings.HighPassEdge);
            for (int index = 0; index < frequencies.Length; index++)
            {
                magnitudes[index] *= CrossoverFilter.Response(iir, frequencies[index], rateHz).Magnitude;
            }
        }

        if (settings.Fir is { } kernel)
        {
            IReadOnlyList<System.Numerics.Complex> responses = kernel.Responses(frequencies, rateHz);
            for (int index = 0; index < frequencies.Length; index++)
            {
                magnitudes[index] *= responses[index].Magnitude;
            }
        }

        return magnitudes;
    }

    /// <summary>Where a gap hands over, else lower low-pass, else upper high-pass, else geometric mean of band centres.</summary>
    public static double GetPairCrossoverHz(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper,
        int? processorSampleRateHz = null)
    {
        if (GapHandoverHz(lower, upper, processorSampleRateHz) is { } gapHz)
        {
            return gapHz;
        }

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

    /// <summary>Whether two adjacent channels hand over to each other: across a gap of an octave or more, where their slopes
    /// cross is the whole answer (above <see cref="GapHandoverFloorDb"/> or not); closer corners hand over when both play
    /// within an octave of the pair's corner. The one rule the panel, Auto delay and its agent share; a pair that fails it
    /// is a hole. See docs/tech/virtual-dsp-analysis.md#measured-bands-and-junctions.</summary>
    public static bool HandsOver(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper,
        int? processorSampleRateHz = null)
    {
        if (lower.EffectiveLowPassHz is { } lowPassHz &&
            upper.EffectiveHighPassHz is { } highPassHz &&
            highPassHz >= 2 * lowPassHz)
        {
            return GapHandoverHz(lower, upper, processorSampleRateHz) != null;
        }

        (double lowHz, double highHz) = OverlapBand(GetPairCrossoverHz(lower, upper, processorSampleRateHz));
        return PlaysWithin(lower, lowHz, highHz) && PlaysWithin(upper, lowHz, highHz);
    }

    /// <summary>Whether the channel's nominal band reaches into <paramref name="lowHz"/>-<paramref name="highHz"/>.</summary>
    public static bool PlaysWithin(VirtualCrossoverChannelSettings settings, double lowHz, double highHz)
    {
        (double channelLow, double channelHigh) = GetChannelBand(settings);
        return channelHigh > lowHz && channelLow < highHz;
    }

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
