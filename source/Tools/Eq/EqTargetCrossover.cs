using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The slope the target follows for one channel: the IIR crossover, a designed FIR crossover's KERNEL (its window and
/// length set the slope, which the design's corners do not describe), or — since a channel may legitimately run both —
/// the two in series, as the chain applies them.
/// </summary>
internal sealed record EqTargetSlope(CrossoverSpec? Crossover, FirFilter? Fir);

/// <summary>
/// The channel's own crossover as part of the target: the goal for a handed-over channel is the target curve inside
/// the passband and the crossover's slope outside it, so the fit can bring the ACOUSTIC slope onto the filter the
/// tune defines. See docs/tech/eq-auto-tuner.md#the-crossover-in-the-target.
/// </summary>
internal static class EqTargetCrossover
{
    /// <summary>How far down a skirt the fit is asked to follow, which is what the window is widened to.</summary>
    public const double SlopeWindowFallDb = 18;

    /// <summary>
    /// Below this much fall no boost is AIMED at the skirt: a boost there fights the filter. What a boost aimed
    /// elsewhere spills in through its own skirt is bounded by <see cref="EqAutoTuner.Options.ForbiddenRegionMaxBoostDb"/>
    /// (0.5 dB), as in a masked-off bin.
    /// </summary>
    public const double NoBoostFallDb = 6;

    // A target diving to minus infinity is no goal at all; past this the skirt is simply "as low as it gets".
    private const double ShapeFloorDb = 40;

    private const int StepsPerOctave = 24;
    private const double SearchOctaves = 6;

    /// <summary>
    /// The slope the source's channel defines, or null: no chain behind the source, or no crossover in it. A designed
    /// FIR crossover answers with its kernel; an IIR one with the channel's effective crossover, which is what the
    /// window's corners read too.
    /// </summary>
    public static EqTargetSlope? Of(EqWizardCurveSource? source)
    {
        CrossoverSpec? crossover = source?.TargetCrossover is { Kind: not CrossoverKind.Off } iir ? iir : null;
        FirFilter? fir = source?.TargetCrossoverFir;
        return crossover == null && fir == null
            ? null
            : new EqTargetSlope(crossover, fir);
    }

    /// <summary>
    /// The slope the chain's ELECTRICAL crossover would give the target, or null where the target already follows it
    /// (no acoustic crossover stated). A FIR crossover kernel is the chain's either way, so it stays in series.
    /// </summary>
    public static EqTargetSlope? ElectricalOf(EqWizardCurveSource? source) =>
        source?.ElectricalCrossover is { Kind: not CrossoverKind.Off } electrical
            ? new EqTargetSlope(electrical, source.TargetCrossoverFir)
            : null;

    /// <summary>What the crossover adds to the target at one frequency: 0 dB in the passband, negative down a skirt.</summary>
    public static double ShapeDb(EqTargetSlope slope, double frequencyHz, int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(slope);
        if (frequencyHz <= 0)
        {
            return -ShapeFloorDb;
        }

        // In series, as the chain applies them: a channel may run a FIR crossover AND an IIR one.
        double magnitude = 1;
        if (slope.Crossover is { } crossover)
        {
            magnitude *= CrossoverFilter.Response(crossover, frequencyHz, sampleRateHz).Magnitude;
        }

        if (slope.Fir is { } fir)
        {
            magnitude *= fir.Response(frequencyHz, sampleRateHz).Magnitude;
        }
        double decibels = magnitude > 0 ? 20 * Math.Log10(magnitude) : -ShapeFloorDb;
        return Math.Clamp(decibels, -ShapeFloorDb, 0);
    }

    /// <summary>
    /// The passband widened to where each skirt has fallen <see cref="SlopeWindowFallDb"/>, so the fit scores the
    /// slope it is now asked to follow. Bounded by the measured band and by the window fields' own range.
    /// </summary>
    public static (double MinHz, double MaxHz) SlopeWindow(
        EqTargetSlope slope,
        double passbandMinHz,
        double passbandMaxHz,
        int sampleRateHz,
        double? measuredLowHz,
        double? measuredHighHz)
    {
        ArgumentNullException.ThrowIfNull(slope);
        double lowLimit = Math.Max(
            (double)EqWizardLimits.WindowFrequency.Minimum,
            measuredLowHz ?? 0);
        double highLimit = Math.Min(
            (double)EqWizardLimits.WindowFrequency.Maximum,
            measuredHighHz ?? double.PositiveInfinity);
        double low = Math.Max(lowLimit, Walk(slope, passbandMinHz, sampleRateHz, SlopeWindowFallDb, down: true));
        double high = Math.Min(highLimit, Walk(slope, passbandMaxHz, sampleRateHz, SlopeWindowFallDb, down: false));
        if (low < high)
        {
            return (low, high);
        }

        // Nothing to widen into: a crossover outside the band that was actually measured. Keep the passband, clipped to
        // what the record covers while that still leaves a window — and where even that is empty the fit refuses, since
        // a window with no measured point in it is missing data, not a window (EqWizardFit.NoMeasuredDataRefusal).
        double passLow = Math.Min(passbandMinHz, passbandMaxHz);
        double passHigh = Math.Max(passbandMinHz, passbandMaxHz);
        double clippedLow = Math.Max(passLow, lowLimit);
        double clippedHigh = Math.Min(passHigh, highLimit);
        return clippedLow < clippedHigh ? (clippedLow, clippedHigh) : (passLow, passHigh);
    }

    /// <summary>
    /// The parts of the window where the skirt has fallen more than <see cref="NoBoostFallDb"/>: there the target's own
    /// fall is the filter's doing, so the fit may cut onto it but never lift it. Empty where neither skirt reaches in.
    /// </summary>
    public static IReadOnlyList<EqNoBoostBand> NoBoostBands(
        EqTargetSlope slope,
        double windowMinHz,
        double windowMaxHz,
        int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(slope);
        // Inward from each edge while the skirt is still that far down; a skirt is monotonic, so the first rise ends it.
        double? low = Scan(slope, windowMinHz, windowMaxHz, sampleRateHz, up: true);
        double? high = Scan(slope, windowMaxHz, windowMinHz, sampleRateHz, up: false);
        if (low == null || high == null)
        {
            // Nowhere in the window is the skirt less than that far down: the whole window is somebody's slope.
            return [new EqNoBoostBand(0, double.PositiveInfinity)];
        }

        var bands = new List<EqNoBoostBand>(2);
        if (low > windowMinHz)
        {
            bands.Add(new EqNoBoostBand(0, low.Value));
        }

        if (high < windowMaxHz)
        {
            bands.Add(new EqNoBoostBand(high.Value, double.PositiveInfinity));
        }

        return bands;
    }

    /// <returns>The first frequency whose skirt is less than <see cref="NoBoostFallDb"/> down, or null: there is none.</returns>
    private static double? Scan(
        EqTargetSlope slope,
        double fromHz,
        double toHz,
        int sampleRateHz,
        bool up)
    {
        double step = Math.Pow(2, (up ? 1.0 : -1.0) / StepsPerOctave);
        double hz = fromHz;
        while (up ? hz < toHz : hz > toHz)
        {
            if (ShapeDb(slope, hz, sampleRateHz) > -NoBoostFallDb)
            {
                return hz;
            }

            hz *= step;
        }

        return null;
    }

    // Outward from a passband edge to where the skirt has fallen this far; the edge itself when nothing falls that far.
    private static double Walk(
        EqTargetSlope slope,
        double fromHz,
        int sampleRateHz,
        double fallDb,
        bool down)
    {
        double step = Math.Pow(2, (down ? -1.0 : 1.0) / StepsPerOctave);
        double hz = fromHz;
        for (int i = 0; i < StepsPerOctave * SearchOctaves; i++)
        {
            double next = hz * step;
            if (next <= 0)
            {
                break;
            }

            if (ShapeDb(slope, next, sampleRateHz) <= -fallDb)
            {
                return next;
            }

            hz = next;
        }

        return hz;
    }
}
