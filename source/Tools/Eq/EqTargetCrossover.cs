using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The channel's own crossover as part of the target: the goal for a handed-over channel is the target curve inside
/// the passband and the crossover's slope outside it, so the fit can bring the ACOUSTIC slope onto the filter the
/// tune defines. See docs/tech/eq-auto-tuner.md#the-crossover-in-the-target.
/// </summary>
internal static class EqTargetCrossover
{
    /// <summary>How far down a skirt the fit is asked to follow, which is what the window is widened to.</summary>
    public const double SlopeWindowFallDb = 18;

    /// <summary>Below this much fall the skirt may be cut onto the target but never lifted: a boost there fights the filter.</summary>
    public const double NoBoostFallDb = 6;

    // A target diving to minus infinity is no goal at all; past this the skirt is simply "as low as it gets".
    private const double ShapeFloorDb = 40;

    private const int StepsPerOctave = 24;
    private const double SearchOctaves = 6;

    /// <summary>
    /// The crossover the source's channel defines, or null: no chain behind the source, or no crossover in it. This is
    /// the channel's effective crossover — a FIR crossover's design corners included — not the built chain, whose
    /// <c>Crossover</c> is Off while a FIR kernel carries the filter.
    /// </summary>
    public static CrossoverSpec? Of(EqWizardCurveSource? source) =>
        source?.TargetCrossover is { Kind: not CrossoverKind.Off } crossover
            ? crossover
            : null;

    /// <summary>What the crossover adds to the target at one frequency: 0 dB in the passband, negative down a skirt.</summary>
    public static double ShapeDb(CrossoverSpec crossover, double frequencyHz, int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(crossover);
        if (frequencyHz <= 0)
        {
            return -ShapeFloorDb;
        }

        double magnitude = CrossoverFilter.Response(crossover, frequencyHz, sampleRateHz).Magnitude;
        double decibels = magnitude > 0 ? 20 * Math.Log10(magnitude) : -ShapeFloorDb;
        return Math.Clamp(decibels, -ShapeFloorDb, 0);
    }

    /// <summary>
    /// The passband widened to where each skirt has fallen <see cref="SlopeWindowFallDb"/>, so the fit scores the
    /// slope it is now asked to follow. Bounded by the measured band and by the window fields' own range.
    /// </summary>
    public static (double MinHz, double MaxHz) SlopeWindow(
        CrossoverSpec crossover,
        double passbandMinHz,
        double passbandMaxHz,
        int sampleRateHz,
        double? measuredLowHz,
        double? measuredHighHz)
    {
        ArgumentNullException.ThrowIfNull(crossover);
        double lowLimit = Math.Max(
            (double)EqWizardLimits.WindowFrequency.Minimum,
            measuredLowHz ?? 0);
        double highLimit = Math.Min(
            (double)EqWizardLimits.WindowFrequency.Maximum,
            measuredHighHz ?? double.PositiveInfinity);
        return (
            Math.Max(lowLimit, Walk(crossover, passbandMinHz, sampleRateHz, SlopeWindowFallDb, down: true)),
            Math.Min(highLimit, Walk(crossover, passbandMaxHz, sampleRateHz, SlopeWindowFallDb, down: false)));
    }

    /// <summary>
    /// The parts of the window where the skirt has fallen more than <see cref="NoBoostFallDb"/>: there the target's own
    /// fall is the filter's doing, so the fit may cut onto it but never lift it. Empty where neither skirt reaches in.
    /// </summary>
    public static IReadOnlyList<EqNoBoostBand> NoBoostBands(
        CrossoverSpec crossover,
        double windowMinHz,
        double windowMaxHz,
        int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(crossover);
        // Inward from each edge while the skirt is still that far down; a skirt is monotonic, so the first rise ends it.
        double? low = Scan(crossover, windowMinHz, windowMaxHz, sampleRateHz, up: true);
        double? high = Scan(crossover, windowMaxHz, windowMinHz, sampleRateHz, up: false);
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
        CrossoverSpec crossover,
        double fromHz,
        double toHz,
        int sampleRateHz,
        bool up)
    {
        double step = Math.Pow(2, (up ? 1.0 : -1.0) / StepsPerOctave);
        double hz = fromHz;
        while (up ? hz < toHz : hz > toHz)
        {
            if (ShapeDb(crossover, hz, sampleRateHz) > -NoBoostFallDb)
            {
                return hz;
            }

            hz *= step;
        }

        return null;
    }

    // Outward from a passband edge to where the skirt has fallen this far; the edge itself when nothing falls that far.
    private static double Walk(
        CrossoverSpec crossover,
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

            if (ShapeDb(crossover, next, sampleRateHz) <= -fallDb)
            {
                return next;
            }

            hz = next;
        }

        return hz;
    }
}
