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

    /// <summary>Where the slopes of a low-pass and a high-pass set an octave or more apart cross, as the processor
    /// realizes them; null where the corners stand closer, or the slopes meet below <see cref="GapHandoverFloorDb"/>.</summary>
    public static double? GapHandoverHz(
        VirtualCrossoverChannelSettings lower,
        VirtualCrossoverChannelSettings upper,
        int? processorSampleRateHz = null)
    {
        CrossoverSpec lowerSpec = lower.EffectiveCrossover;
        CrossoverSpec upperSpec = upper.EffectiveCrossover;
        int rateHz = processorSampleRateHz is > 0 ? processorSampleRateHz.Value : UnwarpedRateHz;
        if (lowerSpec.LowPassHz is not { } lowPassHz ||
            upperSpec.HighPassHz is not { } highPassHz ||
            highPassHz < 2 * lowPassHz ||
            highPassHz >= rateHz / 2.0)
        {
            return null;
        }

        var lowPass = new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: lowerSpec.LowPassEdge);
        var highPass = new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: upperSpec.HighPassEdge);
        double crossingHz = lowPassHz;
        double crossingLevel = 0;
        for (int step = 0; step <= GapHandoverSteps; step++)
        {
            double hz = lowPassHz * Math.Pow(highPassHz / lowPassHz, (double)step / GapHandoverSteps);
            double level = Math.Min(
                CrossoverFilter.Response(lowPass, hz, rateHz).Magnitude,
                CrossoverFilter.Response(highPass, hz, rateHz).Magnitude);
            if (level > crossingLevel)
            {
                crossingLevel = level;
                crossingHz = hz;
            }
        }

        return 20 * Math.Log10(crossingLevel) >= GapHandoverFloorDb ? crossingHz : null;
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
