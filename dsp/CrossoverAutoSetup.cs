using System.Collections.Concurrent;
using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>Declared low to high in frequency, so ordering by this enum orders channels along the spectrum.</summary>
public enum DriverType
{
    Subwoofer,
    Woofer,
    Midbass,
    Midrange,
    Tweeter
}

/// <summary>NoCleanBand (dirty everywhere) must tighten the bounds, never relax to the class heuristic. See docs/tech/crossover-auto-setup.md#distortion-clean-band.</summary>
public enum DistortionBandStatus
{
    Unavailable,
    Unreliable,
    CleanBandFound,
    NoCleanBand
}

public sealed record DriverBandEstimate(
    double LowHz,
    double HighHz,
    double LevelDb,
    DriverType SuggestedType,
    // Lowest high-pass / highest low-pass corner. CleanBandFound: the clean sub-band's edges.
    // NoCleanBand: Low = TOP of the dirty span, High = its BOTTOM (the bound tightens). NaN when Unavailable/Unreliable.
    double DistortionLowHz = double.NaN,
    double DistortionHighHz = double.NaN,
    DistortionBandStatus DistortionStatus = DistortionBandStatus.Unavailable);

/// <summary><see cref="Coherence"/> is optional per-point γ² (0..1), aligned 1:1 with <see cref="MagnitudeDb"/>.</summary>
public sealed record AutoSetupSource(
    IReadOnlyList<SignalPoint> MagnitudeDb,
    DriverType Type,
    IReadOnlyList<double>? Coherence = null,
    // Optional THD (dB re fundamental) vs frequency; bounds the crossover by the driver's distortion-clean band.
    IReadOnlyList<SignalPoint>? DistortionDb = null);

/// <summary><see cref="InvertPolarity"/> is derived from the crossover the channel ends up with, not measured; Auto delay
/// replaces it with its own answer. See docs/tech/crossover-auto-setup.md#polarity.</summary>
public sealed record CrossoverProposal(
    CrossoverKind Kind,
    CrossoverEdge? HighPassEdge,
    CrossoverEdge? LowPassEdge,
    double GainDb,
    bool InvertPolarity = false);

/// <summary>One junction's user-set search window; a null field means the wizard decides that bound.
/// See docs/tech/crossover-auto-setup.md#per-junction-windows.</summary>
public sealed record JunctionSearchWindow(
    double? MinHz = null,
    double? MaxHz = null,
    int? MinSlopeDbPerOctave = null,
    int? MaxSlopeDbPerOctave = null,
    bool AllowSplitCorners = false);

/// <summary>Why a bound sits where it does. <see cref="Summary"/> is the fact, short enough to live beside the row;
/// <see cref="Detail"/> is the reasoning, which belongs in a tooltip and nowhere near the numbers.</summary>
public sealed record JunctionWindowNote(string Summary, string Detail);

/// <summary>What a junction search actually ran with, plus a note for every bound that moved. The dialog prints them;
/// a silently clamped window is what made the old wizard look wilful.</summary>
public sealed record JunctionWindowResolution(
    double LowHz,
    double HighHz,
    int MinSlopeDbPerOctave,
    int MaxSlopeDbPerOctave,
    bool AllowSplitCorners,
    IReadOnlyList<JunctionWindowNote> Notes);

/// <summary><see cref="AchievabilityPenaltyDb"/> is the summed junction loss after the best per-junction delay (null without IRs);
/// <see cref="IsConventional24"/> marks only the dedicated all-24 dB/oct run, not a pool candidate that happens to use 24. Lower score is better.</summary>
public sealed record RankedCrossoverProposal(
    IReadOnlyList<CrossoverProposal> Proposals,
    double MagnitudeScore,
    double? AchievabilityPenaltyDb,
    double TotalScore,
    bool IsConventional24);

/// <summary>With <see cref="IndependentSlopes"/> off, a driver's high-pass and low-pass share one slope. <see cref="SubElevationDb"/>: see <see cref="CrossoverAutoSetup.ApplyTargetCurveGains"/> (null = measured elevation).</summary>
/// <param name="SampleRateHz">The measurement's rate; bounds the analysis grid and crossover window.</param>
/// <param name="ProcessorSampleRateHz">The rate the device realizes filters at; bilinear warping makes scoring at the wrong rate score filters the device will not produce.</param>
/// <param name="MinCrossoverHz">System band limit, NOT a search window: it band-limits the outermost channels.
/// A junction is narrowed through <paramref name="JunctionWindows"/>.</param>
/// <param name="JunctionWindows">One entry per junction (channel count − 1); shorter or null means the wizard decides.</param>
public sealed record CrossoverAutoSetupOptions(
    IReadOnlyList<CrossoverFilterFamily> Families,
    double MinCrossoverHz,
    double MaxCrossoverHz,
    bool IndependentSlopes,
    double SampleRateHz,
    double ProcessorSampleRateHz,
    double? SubElevationDb = null,
    IReadOnlyList<JunctionSearchWindow>? JunctionWindows = null)
{
    public static CrossoverAutoSetupOptions Default(
        double sampleRateHz,
        double processorSampleRateHz) =>
        new(
            [
                CrossoverFilterFamily.LinkwitzRiley,
                CrossoverFilterFamily.Butterworth,
                CrossoverFilterFamily.Bessel
            ],
            20,
            20_000,
            IndependentSlopes: true,
            sampleRateHz,
            processorSampleRateHz);
}

/// <summary>Magnitude-only crossover wizard; delay/polarity alignment is a separate later step. See docs/tech/crossover-auto-setup.md.</summary>
public static class CrossoverAutoSetup
{
    // Between the -6 dB textbook edge and the -10 dB that in-room 1/3-oct ripple needs.
    private const double BandEdgeDropDb = 8.0;

    // Below γ² 0.5 (the phase-unwrap floor) a point cannot anchor a band edge.
    private const double CoherenceFloor = 0.5;

    // Bridges a narrow null inside a band; wider gaps must not let an isolated resonance stretch the band.
    private const double MaxBandGapOctaves = 0.5;

    private const double CrossoverMarginOctaves = 1.0;

    private const int CrossoverSlopeDbPerOctave = 24;

    /// <summary>Slopes whose peak group delay exceeds this are excluded; the family's gentlest practical slope (12 dB/oct) is always admitted. See docs/tech/crossover-auto-setup.md#group-delay-budget.</summary>
    public const double MaxCrossoverGroupDelaySeconds = 0.010;

    /// <summary>Floor for the tweeter Fs estimate; the high-pass must reach <see cref="TweeterFsAttenuationTargetDb"/> at Fs. See docs/tech/crossover-auto-setup.md#tweeter-resonance-floor.</summary>
    public const double TweeterFsFloorHz = 1_200.0;

    /// <summary>Anchored on the Focal TNF datasheet (3.2 kHz @ 18 dB/oct, Fs ~1370 Hz, ~22 dB at Fs).</summary>
    public const double TweeterFsAttenuationTargetDb = 22.0;

    // 3 % THD.
    private const double DistortionCeilingDb = -30.0;

    private const int GridPointsPerOctave = 24;

    private const double MinJunctionSeparationOctaves = 0.5;

    // Cut-only is enforced afterward by referencing the loudest channel.
    private const double GainSearchRangeDb = 8.0;
    private const double GainSearchStepDb = 0.25;

    private const int MaxPasses = 6;
    private const double ConvergenceDb = 0.01;

    // A narrow suckout is more audible than the same energy as ripple.
    private const double DipPenaltyWeight = 0.5;

    /// <summary>A junction bump is what an in-phase Butterworth pair produces; the system-wide flatness term never
    /// looked for one, because an amplitude sum could not make one. Weighted like the dip.</summary>
    private const double BumpPenaltyWeight = 0.5;

    private const int PoolOptionsPerJunction = 4;
    private const int PoolMaxCombinations = 512;

    // Crop of the IRs for the post-check; verified not to change junction losses (the 4096-sample gate sits at the shared peak anchor).
    private const int PostCheckCropLength = 32_768;
    private const int PostCheckCropPrePeakSamples = 8_192;

    // Window half-width scales as 1/fc like filter group delay. See docs/tech/crossover-auto-setup.md#achievability-post-check.
    private const double PostCheckWindowGroupDelayScaleHz = 1_200;
    private const double PostCheckMinHalfWindowMs = 2.0;
    private const double PostCheckMaxHalfWindowMs = 12.0;
    private const double PostCheckMissingJunctionPenaltyDb = 6.0;

    /// <summary>Clamped to 2–12 ms; shared with the junction tuner's after-best-delay reading.</summary>
    public static double PostCheckHalfWindowMs(double junctionHz) =>
        Math.Clamp(
            PostCheckWindowGroupDelayScaleHz / junctionHz,
            PostCheckMinHalfWindowMs,
            PostCheckMaxHalfWindowMs);

    private const double AchievabilityWeight = 0.5;
    private const double Conventional24PreferenceDb = 0.25;

    /// <summary>The user may narrow the slope window, but never out of reach of the car-audio standard: the score's
    /// anchor, the seed slope and the conventional run are all 24 dB/oct, and a window excluding it would leave all
    /// three pulling at a slope the search cannot take. See docs/tech/crossover-auto-setup.md#slope-window.</summary>
    public const int MandatorySlopeDbPerOctave = PreferredSlopeDbPerOctave;

    // Junction flatness is the point of the exercise, so it is scored again locally, on top of the system-wide term.
    private const double JunctionFlatnessWeight = 0.5;

    private const double JunctionBandHalfWidthOctaves = 1.0;

    /// <summary>How far above a safety floor the window opens when that floor has overruled where the drivers
    /// actually overlap. Not a round number: protecting Fs needs fc >= Fs·2^(22/slope), so covering every admissible
    /// slope from the steepest (48) to the gentlest (12) is 22/12 − 22/48 = 1.375 octaves. Wider than that and a
    /// mid-to-tweeter search wanders up towards 20 kHz, which is not a handover anybody would dial.</summary>
    private const double SafetyOverrideSpanOctaves = 1.5;

    /// <summary>Split corners are searched as a SIGNED offset from the junction corner, not as two free
    /// frequencies: a free pair squares the lattice and breaks the coordinate descent. Positive holds the corners
    /// apart, which takes a bump off the junction; negative overlaps them, which fills a dip. Both are searched —
    /// the ask was a junction free of both. See docs/tech/crossover-auto-setup.md#split-corners.</summary>
    internal static readonly IReadOnlyList<double> SplitOffsetOctaves =
    [
        0.0,
        1.0 / 12.0, -1.0 / 12.0,
        1.0 / 6.0, -1.0 / 6.0,
        1.0 / 4.0, -1.0 / 4.0
    ];

    /// <summary>Fixed analysis circle for the minimum-phase cepstrum, deliberately not the measurement rate: the
    /// optimizer grid never passes 20 kHz, and a fixed circle keeps the driver phase identical whether the car was
    /// measured at 44.1 or 192 kHz. 65536 bins put 0.73 Hz under the 20 Hz end of the grid.</summary>
    private const double MinimumPhaseCircleRateHz = 48_000.0;

    private const int MinimumPhaseSpectrumLength = 65_536;

    /// <summary>5 Hz steps below 100 Hz, 10 Hz below 1 kHz, 50 Hz above; the optimizer searches on this lattice directly.</summary>
    public static double RoundToLattice(double frequencyHz)
    {
        double step = LatticeStep(frequencyHz);
        return Math.Max(20, Math.Round(frequencyHz / step) * step);
    }

    private static double LatticeStep(double frequencyHz) =>
        frequencyHz < 100 ? 5 : frequencyHz < 1_000 ? 10 : 50;

    private static double RoundUpToLattice(double frequencyHz)
    {
        double step = LatticeStep(frequencyHz);
        return Math.Max(20, Math.Ceiling(frequencyHz / step) * step);
    }

    /// <summary>A window narrower than one step collapses to its snapped midpoint. Shared with the junction tuner.</summary>
    public static double[] LatticePoints(double low, double high)
    {
        var points = new List<double>();
        double f = RoundToLattice(low);
        if (f < low)
        {
            f += LatticeStep(f);
        }
        while (f <= high + 1e-9)
        {
            points.Add(f);
            f += LatticeStep(f);
        }

        if (points.Count == 0)
        {
            points.Add(Math.Clamp(RoundToLattice(Math.Sqrt(low * high)), low, high));
        }

        return points.ToArray();
    }

    /// <summary>Minimum phase of a driver curve, sampled onto the optimizer grid. The wizard sums channels coherently
    /// with the phase the crossover AND the driver's own roll-off contribute; a flat-phase driver would miss the
    /// roll-off, which is exactly where junctions sit. See docs/tech/crossover-auto-setup.md#ideal-complex-sum.</summary>
    internal static double[] MinimumPhaseOnGrid(
        IReadOnlyList<SignalPoint> curve,
        IReadOnlyList<double> grid)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(grid);

        // Keyed on the curve itself, so a dialog that re-fits on every keystroke pays the cepstrum once per channel
        // and not once per fit. The table holds nothing alive: an entry dies with the curve it belongs to.
        Dictionary<(int Count, double Top), double[]> byGrid =
            minimumPhaseCache.GetValue(curve, _ => new Dictionary<(int, double), double[]>());
        var key = (grid.Count, grid[^1]);
        lock (byGrid)
        {
            if (byGrid.TryGetValue(key, out double[]? hit))
            {
                return hit;
            }
        }

        double[] computed = ComputeMinimumPhaseOnGrid(curve, grid);
        lock (byGrid)
        {
            byGrid[key] = computed;
        }

        return computed;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        IReadOnlyList<SignalPoint>, Dictionary<(int Count, double Top), double[]>>
        minimumPhaseCache = new();

    private static double[] ComputeMinimumPhaseOnGrid(
        IReadOnlyList<SignalPoint> curve,
        IReadOnlyList<double> grid)
    {
        const int length = MinimumPhaseSpectrumLength;
        const int half = length / 2;
        double binHz = MinimumPhaseCircleRateHz / length;
        var magnitude = new double[length];
        for (int bin = 0; bin <= half; bin++)
        {
            // Holding the endpoints (InterpolateDb clamps) extends the unmeasured skirts flat: a NaN mask or a
            // collapse to silence would put a step into the log magnitude and blow the cepstrum up.
            double db = InterpolateDb(curve, Math.Max(bin * binHz, binHz));
            double value = double.IsFinite(db) ? DataHelper.DecibelsToAmplitude(db) : 0.0;
            magnitude[bin] = value;
            if (bin > 0 && bin < half)
            {
                magnitude[length - bin] = value;
            }
        }

        double[] phase = MinimumPhase.FromMagnitude(magnitude);
        var result = new double[grid.Count];
        for (int k = 0; k < grid.Count; k++)
        {
            double position = grid[k] / binHz;
            int lower = (int)Math.Floor(position);
            if (lower >= half)
            {
                result[k] = phase[half];
                continue;
            }

            double fraction = position - lower;
            result[k] = phase[lower] * (1.0 - fraction) + phase[lower + 1] * fraction;
        }

        return result;
    }

    /// <summary>The window a junction search runs on, and why each bound sits where it does. Safety (the tweeter Fs
    /// floor, the distortion knee, the measured bands) narrows a user window and says so; it never widens one.</summary>
    public static JunctionWindowResolution ResolveJunctionWindow(
        IReadOnlyList<AutoSetupSource> channels,
        int junctionIndex,
        CrossoverAutoSetupOptions options)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(options);
        if (channels.Count < 2)
        {
            throw new ArgumentException(
                "At least two channels are required.",
                nameof(channels));
        }
        if ((uint)junctionIndex >= (uint)(channels.Count - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(junctionIndex));
        }

        var optimizer = new Optimizer(channels, Normalize(options));
        return optimizer.ResolveWindow(junctionIndex);
    }

    /// <summary>Widens a user slope window until it holds <see cref="MandatorySlopeDbPerOctave"/>, and says so.</summary>
    internal static (int Min, int Max) ClampSlopeWindow(
        int? requestedMin,
        int? requestedMax,
        List<JunctionWindowNote>? notes)
    {
        int min = requestedMin ?? MinPracticalSlopeDbPerOctave;
        int max = requestedMax ?? CrossoverFilter.SupportedSlopes(CrossoverFilterFamily.Butterworth).Max();
        if (min > max)
        {
            (min, max) = (max, min);
        }

        string Detail(int requested) =>
            $"{requested} dB/oct would leave {MandatorySlopeDbPerOctave} dB/oct out of the window. " +
            "The score, the seed slope and the baseline candidate the search compares everything against " +
            $"are all anchored on {MandatorySlopeDbPerOctave} dB/oct, so it always stays reachable.";

        if (min > MandatorySlopeDbPerOctave)
        {
            notes?.Add(new JunctionWindowNote(
                $"{min} → {MandatorySlopeDbPerOctave} dB/oct", Detail(min)));
            min = MandatorySlopeDbPerOctave;
        }

        if (max < MandatorySlopeDbPerOctave)
        {
            notes?.Add(new JunctionWindowNote(
                $"{max} → {MandatorySlopeDbPerOctave} dB/oct", Detail(max)));
            max = MandatorySlopeDbPerOctave;
        }

        return (Math.Max(min, MinPracticalSlopeDbPerOctave), max);
    }

    /// <summary>Compact frequency for a note: hertz under a kilohertz, kilohertz above it.</summary>
    private static string NoteHz(double frequencyHz) =>
        frequencyHz >= 1_000
            ? $"{frequencyHz / 1_000:0.##} kHz"
            : $"{frequencyHz:0} Hz";

    // Flatness alone rewards wide overlap. See docs/tech/crossover-auto-setup.md#engineering-penalties.
    private const double OverlapPenaltyDbPerOctave = 0.6;

    /// <summary>Charged against the overlap penalty at its own rate. Parting a junction's corners shrinks the overlap
    /// integral whatever it does to the response, so without this the widest offset on the list scores best every
    /// time and the search stops being one. Charged back, only a real flatness gain survives.</summary>
    private const double SplitPenaltyDbPerOctave = OverlapPenaltyDbPerOctave;

    private const int MinPracticalSlopeDbPerOctave = 12;

    private const int PreferredSlopeDbPerOctave = 24;
    private const double SlopeDeviationPenaltyDb = 0.7;

    private const double NonAdjacentOverlapWeight = 4.0;

    private const double EarSensitivityLowHz = 2_000;
    private const double EarSensitivityHighHz = 4_000;
    private const double EarSensitivitySigmaOctaves = 0.5;
    private const double EarSensitivityWeightDb = 0.5;

    private const double SubHandoverUpBiasWeightDb = 0.6;

    /// <summary>Two drivers of the same class are one class band split between them, and the split belongs in the
    /// middle of what both can produce. Charged per octave AWAY from that middle, so it is a pull and not a
    /// placement. 1.5 is measured, not chosen: at 0.6 it failed to move a junction flatness scored as a tie, so it
    /// was not a prior at all, and above 1.5 the answer stops moving — the middle is an attractor rather than one
    /// side of a tug of war.</summary>
    private const double SharedBandSplitBiasWeightDb = 1.5;

    private const double WideOverlapLowBiasWeightDb = 0.4;

    // Scoped to the midrange handover only; the tweeter junction is governed by the Fs floor.
    private const double MidrangeLocalizationThresholdHz = 250.0;
    private const double MidrangeHandoverLowBiasWeightDb = 2.0;

    /// <summary>Reference is an upper percentile of the curve; coherence (γ², 1:1 with magnitude) discounts untrusted points. See docs/tech/crossover-auto-setup.md#band-estimation.</summary>
    public static DriverBandEstimate EstimateBand(
        IReadOnlyList<SignalPoint> magnitudeDb,
        IReadOnlyList<double>? coherence = null,
        IReadOnlyList<SignalPoint>? distortionDb = null)
    {
        ArgumentNullException.ThrowIfNull(magnitudeDb);

        bool useCoherence = coherence != null && coherence.Count == magnitudeDb.Count;
        double Gamma(int i) => useCoherence ? coherence![i] : 1.0;

        // Percentile over the whole curve: filtering to coherent points shrinks a sub's band toward its peak.
        var levels = magnitudeDb
            .Where(point => double.IsFinite(point.Y))
            .Select(point => point.Y)
            .OrderBy(value => value)
            .ToList();
        if (levels.Count < 2)
        {
            throw new ArgumentException(
                "The magnitude curve is empty.",
                nameof(magnitudeDb));
        }

        double reference = levels[(int)(levels.Count * 0.85)];
        double threshold = reference - BandEdgeDropDb;

        double bestLow = double.NaN;
        double bestHigh = double.NaN;
        double bestArea = double.NegativeInfinity;
        double segLow = double.NaN;
        double segHigh = double.NaN;
        double segArea = 0.0;
        double lastAboveHz = double.NaN;

        void CloseSegment()
        {
            if (!double.IsNaN(segLow) && segHigh > segLow && segArea > bestArea)
            {
                bestArea = segArea;
                bestLow = segLow;
                bestHigh = segHigh;
            }
        }

        for (int i = 0; i < magnitudeDb.Count; i++)
        {
            SignalPoint point = magnitudeDb[i];
            double g2 = Gamma(i);
            if (!double.IsFinite(point.Y) || point.Y < threshold || g2 < CoherenceFloor)
            {
                continue;
            }

            if (!double.IsNaN(lastAboveHz)
                && Math.Log2(point.X / lastAboveHz) > MaxBandGapOctaves)
            {
                CloseSegment();
                segLow = double.NaN;
                segArea = 0.0;
            }

            if (double.IsNaN(segLow))
            {
                segLow = point.X;
            }
            segHigh = point.X;
            segArea += (point.Y - threshold) * g2;
            lastAboveHz = point.X;
        }
        CloseSegment();

        double lowHz = bestLow;
        double highHz = bestHigh;
        if (double.IsNaN(lowHz) || highHz <= lowHz)
        {
            throw new ArgumentException(
                "The magnitude curve has no usable band.",
                nameof(magnitudeDb));
        }

        double level = AverageLevelDb(magnitudeDb, lowHz, highHz);
        (DistortionBandStatus distortionStatus, double distortionLow, double distortionHigh) =
            DistortionCleanBand(distortionDb, lowHz, highHz);
        return new DriverBandEstimate(
            lowHz, highHz, level, Classify(lowHz, highHz),
            distortionLow, distortionHigh, distortionStatus);
    }

    // Status separates no data from dirty everywhere; NoCleanBand returns the dirty span's edges swapped.
    private static (DistortionBandStatus Status, double Low, double High) DistortionCleanBand(
        IReadOnlyList<SignalPoint>? distortionDb,
        double bandLow,
        double bandHigh)
    {
        if (distortionDb == null)
        {
            return (DistortionBandStatus.Unavailable, double.NaN, double.NaN);
        }

        double bestLow = double.NaN;
        double bestHigh = double.NaN;
        double bestSpan = 0.0;
        double segLow = double.NaN;
        double segHigh = double.NaN;
        double lastCleanHz = double.NaN;

        // Separates Unreliable (nothing finite) from NoCleanBand.
        double reliableLow = double.NaN;
        double reliableHigh = double.NaN;

        void CloseSegment()
        {
            if (!double.IsNaN(segLow) && segHigh > segLow)
            {
                double span = Math.Log2(segHigh / segLow);
                if (span > bestSpan)
                {
                    bestSpan = span;
                    bestLow = segLow;
                    bestHigh = segHigh;
                }
            }
        }

        foreach (SignalPoint point in distortionDb)
        {
            if (point.X < bandLow || point.X > bandHigh)
            {
                continue;
            }

            if (double.IsFinite(point.Y))
            {
                reliableLow = double.IsNaN(reliableLow)
                    ? point.X : Math.Min(reliableLow, point.X);
                reliableHigh = double.IsNaN(reliableHigh)
                    ? point.X : Math.Max(reliableHigh, point.X);
            }

            bool clean = double.IsFinite(point.Y) && point.Y <= DistortionCeilingDb;
            if (!clean)
            {
                continue;
            }

            if (!double.IsNaN(lastCleanHz)
                && Math.Log2(point.X / lastCleanHz) > MaxBandGapOctaves)
            {
                CloseSegment();
                segLow = double.NaN;
            }

            if (double.IsNaN(segLow))
            {
                segLow = point.X;
            }
            segHigh = point.X;
            lastCleanHz = point.X;
        }
        CloseSegment();

        if (!double.IsNaN(bestLow))
        {
            return (
                DistortionBandStatus.CleanBandFound,
                Math.Max(bestLow, bandLow),
                Math.Min(bestHigh, bandHigh));
        }

        return double.IsNaN(reliableLow)
            ? (DistortionBandStatus.Unreliable, double.NaN, double.NaN)
            : (DistortionBandStatus.NoCleanBand, reliableHigh, reliableLow);
    }

    // Thresholds are fixed class-centre midpoints, not derived from SensibleRange. See docs/tech/crossover-auto-setup.md#classification-and-sensible-ranges.
    private static DriverType Classify(double lowHz, double highHz)
    {
        double center = Math.Sqrt(lowHz * highHz);
        if (center < 63)
        {
            return DriverType.Subwoofer;
        }
        if (center < 141)
        {
            return DriverType.Woofer;
        }
        if (center < 450)
        {
            return DriverType.Midbass;
        }

        return center < 2_500 ? DriverType.Midrange : DriverType.Tweeter;
    }

    private static (double LowHz, double HighHz) SensibleRange(DriverType type) => type switch
    {
        DriverType.Subwoofer => (20, 80),
        DriverType.Woofer => (40, 250),
        DriverType.Midbass => (80, 500),
        // Still gated by the measured midrange band.
        DriverType.Midrange => (200, 4_000),
        // Low only when the measured tweeter band supports it.
        DriverType.Tweeter => (1_700, 20_000),
        _ => (20, 20_000)
    };

    // When the classes do not overlap (e.g. 2-way woofer + tweeter) low exceeds high: no class band, the measured overlap stands.
    /// <summary>The narrowest a class preference is allowed to be. Adjacent classes ABUT — a subwoofer ends at
    /// 80 Hz exactly where a midbass begins — so their intersection can be a single frequency, and a window of one
    /// frequency is not a preference, it is a pin with nothing for the search to do. One octave is the overlap an
    /// LR24 pair produces by itself: a class window narrower than the crossover's own overlap cannot move the
    /// corner by even one crossover width.</summary>
    private const double MinClassWindowOctaves = 1.0;

    /// <summary>Where the two classes say a junction belongs, widened about its own centre when the two ranges
    /// only touch. An empty intersection is returned as it is: the caller drops the preference entirely.</summary>
    private static (double LowHz, double HighHz) JunctionTypeBounds(
        DriverType lower,
        DriverType upper)
    {
        double low = SensibleRange(upper).LowHz;
        double high = SensibleRange(lower).HighHz;
        if (low > high || high >= low * Math.Pow(2.0, MinClassWindowOctaves))
        {
            return (low, high);
        }

        double centre = Math.Sqrt(low * high);
        double half = Math.Pow(2.0, MinClassWindowOctaves / 2.0);
        return (centre / half, centre * half);
    }

    /// <summary>Estimated from the measured band low edge, floored at <see cref="TweeterFsFloorHz"/>.</summary>
    public static double TweeterResonanceHz(double measuredBandLowHz) =>
        Math.Max(
            TweeterFsFloorHz,
            double.IsFinite(measuredBandLowHz) ? measuredBandLowHz : TweeterFsFloorHz);

    /// <summary>fc = Fs·2^(<see cref="TweeterFsAttenuationTargetDb"/>/slope).</summary>
    public static double TweeterMinCrossoverHz(double resonanceHz, int highPassSlopeDbPerOctave) =>
        resonanceHz * Math.Pow(
            2.0, TweeterFsAttenuationTargetDb / highPassSlopeDbPerOctave);

    /// <summary><paramref name="channels"/> is one chain in spectral order: channel i hands over to i+1 only. Types may repeat,
    /// so order comes from the caller. Non-crossing drivers go through <see cref="ProposeSingle"/>. Results are in input order.</summary>
    public static IReadOnlyList<CrossoverProposal> Propose(
        IReadOnlyList<AutoSetupSource> channels,
        CrossoverAutoSetupOptions options)
    {
        // Arrival reads repeat on identical input across the run; see AlignmentRunMemo.
        using AlignmentRunMemo.Scope runMemo = AlignmentRunMemo.Begin();
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(options);
        if (channels.Count < 2)
        {
            throw new ArgumentException(
                "At least two channels are required.",
                nameof(channels));
        }

        options = Normalize(options);
        IReadOnlyList<CrossoverProposal> proposals =
            new Optimizer(channels, options).Solve();

        // Level-matched gains pick crossovers; the user gets the target-curve fit.
        return ApplyTargetCurveGains(
            channels, proposals, options.SampleRateHz, options.SubElevationDb);
    }

    // 12/18 dB/oct are admitted but penalized, not forbidden; steep slopes are capped by the group-delay budget in AllowedSlopes.
    private static IReadOnlyList<int> PracticalSlopes(CrossoverFilterFamily family) =>
        CrossoverFilter.SupportedSlopes(family)
            .Where(slope => slope >= MinPracticalSlopeDbPerOctave)
            .ToList();

    private readonly record struct TargetCurveContext(
        double[] PassbandLevelDb,
        double[] PassbandCenterHz,
        int BassIndex,
        IReadOnlyList<int> ReferenceIndices,
        int SlopeTopIndex,
        double ReferenceLevelDb,
        double MeasuredElevationDb);

    private static TargetCurveContext BuildTargetCurveContext(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz)
    {
        int n = channels.Count;
        var levels = new double[n];
        var centers = new double[n];
        double ceiling = Math.Min(20_000, sampleRateHz * 0.49);
        for (int i = 0; i < n; i++)
        {
            DriverBandEstimate band = EstimateBand(
                channels[i].MagnitudeDb, channels[i].Coherence, channels[i].DistortionDb);
            double low = proposals[i].HighPassEdge?.FrequencyHz ?? band.LowHz;
            double high = proposals[i].LowPassEdge?.FrequencyHz ?? Math.Min(band.HighHz, ceiling);
            if (high <= low)
            {
                (low, high) = (band.LowHz, band.HighHz);
            }

            levels[i] = AverageLevelDb(channels[i].MagnitudeDb, low, high);
            centers[i] = Math.Sqrt(low * high);
        }

        // The first channel of a class is its lowest (chain order).
        int Find(DriverType type)
        {
            for (int i = 0; i < n; i++)
            {
                if (channels[i].Type == type)
                {
                    return i;
                }
            }

            return -1;
        }

        var reference = Enumerable.Range(0, n)
            .Where(i => channels[i].Type is DriverType.Midrange or DriverType.Tweeter)
            .ToList();

        // Subwoofer, else the lowest woofer/midbass. See docs/tech/crossover-auto-setup.md#target-curve-gains.
        int bass = Find(DriverType.Subwoofer);
        if (bass < 0)
        {
            bass = Find(DriverType.Woofer);
        }
        if (bass < 0)
        {
            bass = Find(DriverType.Midbass);
        }

        // Quietest non-sub driver; every sub is excluded so a quiet sub cannot drag the flat top down.
        double referenceLevel = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            if (channels[i].Type != DriverType.Subwoofer && levels[i] < referenceLevel)
            {
                referenceLevel = levels[i];
            }
        }

        // All subs: no flat top, they level to each other, cut-only.
        bool allSubwoofers = double.IsPositiveInfinity(referenceLevel);
        if (allSubwoofers)
        {
            referenceLevel = levels.Min();
        }

        int slopeTop = reference.Count > 0
            ? reference.MinBy(index => centers[index])
            : Enumerable.Range(0, n).Where(i => i != bass).DefaultIfEmpty(bass).Max();

        // All subs have no elevation; reading levels[bass] would depend on chain direction.
        double measuredElevation = bass < 0 || allSubwoofers
            ? 0
            : Math.Max(0, levels[bass] - referenceLevel);
        return new TargetCurveContext(
            levels, centers, bass, reference, slopeTop, referenceLevel, measuredElevation);
    }

    /// <summary>Default and upper limit of the sub-elevation control: the user may only trim it down.</summary>
    public static double MeasuredSubElevationDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(proposals);
        if (proposals.Count != channels.Count)
        {
            throw new ArgumentException(
                "One proposal per channel is required.", nameof(proposals));
        }

        return BuildTargetCurveContext(channels, proposals, sampleRateHz).MeasuredElevationDb;
    }

    /// <summary>Replaces gains with the car target-curve fit, crossovers untouched; cut-only, input order.
    /// See docs/tech/crossover-auto-setup.md#target-curve-gains.</summary>
    public static IReadOnlyList<CrossoverProposal> ApplyTargetCurveGains(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz,
        double? subElevationDb = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(proposals);
        if (proposals.Count != channels.Count)
        {
            throw new ArgumentException(
                "One proposal per channel is required.", nameof(proposals));
        }

        TargetCurveContext context = BuildTargetCurveContext(channels, proposals, sampleRateHz);
        double[] level = context.PassbandLevelDb;
        double[] center = context.PassbandCenterHz;
        double reference = context.ReferenceLevelDb;
        double elevation = Math.Clamp(
            subElevationDb ?? context.MeasuredElevationDb, 0, context.MeasuredElevationDb);

        double subTarget = reference + elevation;
        bool hasBass = context.BassIndex >= 0;
        double subCenter = hasBass ? center[context.BassIndex] : 0;
        double logSpan = hasBass ? Math.Log(center[context.SlopeTopIndex] / subCenter) : 0;

        double TargetAt(double frequencyHz) => hasBass && logSpan > 1e-9
            ? subTarget - elevation * (Math.Log(frequencyHz / subCenter) / logSpan)
            : reference;

        var gains = new double[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            if (context.ReferenceIndices.Contains(i))
            {
                gains[i] = reference - level[i];
            }
            else if (i == context.BassIndex)
            {
                gains[i] = Math.Min(0, subTarget - level[i]);
            }
            else
            {
                gains[i] = Math.Min(0, TargetAt(center[i]) - level[i]);
            }
        }

        var results = new CrossoverProposal[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            results[i] = proposals[i] with { GainDb = RoundGain(gains[i]) };
        }

        return results;
    }

    /// <summary>The quietest non-sub driver's passband level (quietest overall in an all-sub chain); see <see cref="OffsetToReferenceLevel"/>.</summary>
    public static double ReferenceLevelDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(proposals);
        if (proposals.Count != channels.Count)
        {
            throw new ArgumentException(
                "One proposal per channel is required.", nameof(proposals));
        }

        return BuildTargetCurveContext(channels, proposals, sampleRateHz).ReferenceLevelDb;
    }

    /// <summary>Rigid, cut-only shift of a group's gains onto <paramref name="referenceLevelDb"/>. See docs/tech/crossover-auto-setup.md#groups-outside-the-chain.</summary>
    public static IReadOnlyList<CrossoverProposal> OffsetToReferenceLevel(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz,
        double referenceLevelDb)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(proposals);
        if (proposals.Count != channels.Count)
        {
            throw new ArgumentException(
                "One proposal per channel is required.", nameof(proposals));
        }
        if (channels.Count == 0)
        {
            return proposals;
        }

        double own = BuildTargetCurveContext(channels, proposals, sampleRateHz).ReferenceLevelDb;
        if (!double.IsFinite(own) || !double.IsFinite(referenceLevelDb))
        {
            return proposals;
        }

        double offset = Math.Min(0, referenceLevelDb - own);
        return proposals
            .Select(proposal => proposal with { GainDb = RoundGain(proposal.GainDb + offset) })
            .ToList();
    }

    /// <summary>Protection for a driver that crosses with nobody (rear fill, centre, lone sub): a high-pass, plus a low-pass where the band limit cuts into its top. See docs/tech/crossover-auto-setup.md#groups-outside-the-chain.</summary>
    public static CrossoverProposal ProposeSingle(
        AutoSetupSource channel,
        CrossoverAutoSetupOptions options)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        options = Normalize(options);

        DriverBandEstimate band = EstimateBand(
            channel.MagnitudeDb, channel.Coherence, channel.DistortionDb);
        CrossoverFilterFamily family = PreferredFamily(options.Families);
        double margin = Math.Pow(2.0, CrossoverMarginOctaves);

        double ceiling = Math.Min(
            options.MaxCrossoverHz, Math.Max(options.MinCrossoverHz, band.HighHz / margin));
        double corner = Math.Clamp(band.LowHz * margin, options.MinCrossoverHz, ceiling);

        // Floors only raise the corner, which only admits more slopes, so the re-chosen slope is never invalid.
        int slope = ProtectiveSlope(family, corner, options.ProcessorSampleRateHz);

        // The octave margin is a preference and may be squeezed; Fs and distortion floors are safety: refuse rather than go under them.
        double safetyFloor = 0;
        if (channel.Type == DriverType.Tweeter)
        {
            safetyFloor = TweeterMinCrossoverHz(TweeterResonanceHz(band.LowHz), slope);
        }
        if (double.IsFinite(band.DistortionLowHz))
        {
            safetyFloor = Math.Max(safetyFloor, band.DistortionLowHz);
        }

        if (safetyFloor > ceiling)
        {
            throw new ArgumentException(
                "A protective high-pass for this driver has to sit at " +
                $"{RoundUpToLattice(safetyFloor):0} Hz or above, which the crossover " +
                "window does not reach.",
                nameof(options));
        }

        // Snapped up, not nearest: a step down would be under the floor.
        corner = Math.Clamp(RoundUpToLattice(
            Math.Clamp(Math.Max(corner, safetyFloor), options.MinCrossoverHz, ceiling)),
            options.MinCrossoverHz,
            ceiling);
        slope = ProtectiveSlope(family, corner, options.ProcessorSampleRateHz);
        var highPass = new CrossoverEdge(family, Math.Round(corner), slope);

        // A low-pass at or under the high-pass would be silence, not a band-pass.
        CrossoverEdge? lowPass =
            options.MaxCrossoverHz < band.HighHz / Math.Pow(2.0, 1.0 / 12.0) &&
            options.MaxCrossoverHz > corner
                ? new CrossoverEdge(
                    family,
                    Math.Round(options.MaxCrossoverHz),
                    ProtectiveSlope(
                        family, options.MaxCrossoverHz, options.ProcessorSampleRateHz))
                : null;

        return new CrossoverProposal(
            lowPass is null ? CrossoverKind.HighPass : CrossoverKind.BandPass,
            highPass,
            lowPass,
            0);
    }

    // `+ 0.0` turns negative zero into zero so it never prints "-0.0 dB".
    private static double RoundGain(double gainDb) => Math.Round(gainDb, 1) + 0.0;

    private static CrossoverFilterFamily PreferredFamily(
        IReadOnlyList<CrossoverFilterFamily> families) =>
        families.Contains(CrossoverFilterFamily.LinkwitzRiley)
            ? CrossoverFilterFamily.LinkwitzRiley
            : families.Contains(CrossoverFilterFamily.Bessel)
                ? CrossoverFilterFamily.Bessel
                : families[0];

    // Nearest-24 slope within the GD budget, else the family's gentlest (always admitted).
    private static int ProtectiveSlope(
        CrossoverFilterFamily family,
        double cornerHz,
        double processorSampleRateHz)
    {
        IReadOnlyList<int> slopes = PracticalSlopes(family);
        if (slopes.Count == 0)
        {
            return MinPracticalSlopeDbPerOctave;
        }

        return slopes
            .Where(slope => CrossoverFilter.MaxGroupDelaySeconds(
                    new CrossoverEdge(family, cornerHz, slope),
                    highPass: true,
                    processorSampleRateHz)
                <= MaxCrossoverGroupDelaySeconds)
            .OrderBy(slope => Math.Abs(
                Math.Log2((double)slope / PreferredSlopeDbPerOctave)))
            .DefaultIfEmpty(slopes.Min())
            .First();
    }

    /// <summary>Re-ranks a pool of near-optimal candidates (always including the conventional all-24 one) by achievable junction loss
    /// when IRs are given in chain order. Best first. See docs/tech/crossover-auto-setup.md#ranked-search.</summary>
    public static IReadOnlyList<RankedCrossoverProposal> ProposeRanked(
        IReadOnlyList<AutoSetupSource> channels,
        CrossoverAutoSetupOptions options,
        IReadOnlyList<Complex[]>? impulseResponses = null,
        int candidateCount = 50)
    {
        // Arrival reads repeat on identical input across the run; see AlignmentRunMemo.
        using AlignmentRunMemo.Scope runMemo = AlignmentRunMemo.Begin();
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(options);
        if (channels.Count < 2)
        {
            throw new ArgumentException(
                "At least two channels are required.",
                nameof(channels));
        }
        if (impulseResponses != null &&
            (impulseResponses.Count != channels.Count ||
                impulseResponses.Any(ir => ir == null || ir.Length == 0)))
        {
            throw new ArgumentException(
                "One non-empty impulse response is required per channel.",
                nameof(impulseResponses));
        }
        if (candidateCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        }

        options = Normalize(options);
        List<PoolCandidate> pool = new Optimizer(channels, options).SolvePool(candidateCount);

        CrossoverAutoSetupOptions conventionalOptions =
            options.Families.Contains(CrossoverFilterFamily.LinkwitzRiley)
                ? options with { Families = [CrossoverFilterFamily.LinkwitzRiley] }
                : options;
        PoolCandidate? conventional = new Optimizer(
            channels, conventionalOptions, forcedSlope: CrossoverSlopeDbPerOctave)
            .SolvePool(1)
            .FirstOrDefault();
        if (conventional != null &&
            !pool.Any(entry => entry.Signature == conventional.Signature))
        {
            pool.Add(conventional);
        }
        pool = pool.OrderBy(candidate => candidate.MagnitudeScore).ToList();
        if (pool.Count > candidateCount)
        {
            bool conventionalKept = conventional == null ||
                pool.Take(candidateCount)
                    .Any(entry => entry.Signature == conventional.Signature);
            pool = pool.Take(candidateCount).ToList();
            if (!conventionalKept)
            {
                pool[^1] = conventional!;
            }
        }

        double[]? penalties = null;
        if (impulseResponses != null)
        {
            int sampleRate = (int)Math.Round(options.SampleRateHz);
            int processorRate = (int)Math.Round(options.ProcessorSampleRateHz);
            Complex[][] cropped = CropSharedDirectSoundWindow(impulseResponses.ToArray());
            var arrivalCache =
                new ConcurrentDictionary<(int Channel, long BandKey), (double Ms, bool Valid)>();
            penalties = pool
                .AsParallel().AsOrdered()
                .Select(candidate => AchievabilityPenaltyDb(
                    cropped,
                    candidate.Proposals.ToArray(),
                    arrivalCache,
                    sampleRate,
                    processorRate))
                .ToArray();
        }

        // Matched by signature: a pool candidate that merely landed on 24 dB/oct is not the LR24 baseline.
        string? conventionalSignature = conventional?.Signature;
        var ranked = pool
            .Select((candidate, index) =>
            {
                double? penalty = penalties?[index];
                return new RankedCrossoverProposal(
                    // Ranked on level-matched gains; emitted with target-curve gains.
                    ApplyTargetCurveGains(
                        channels, candidate.Proposals, options.SampleRateHz,
                        options.SubElevationDb),
                    candidate.MagnitudeScore,
                    penalty,
                    candidate.MagnitudeScore + AchievabilityWeight * (penalty ?? 0),
                    candidate.Signature == conventionalSignature);
            })
            .OrderBy(candidate => candidate.TotalScore)
            .ToList();

        RankedCrossoverProposal? preferred = ranked
            .FirstOrDefault(candidate => candidate.IsConventional24);
        if (preferred != null &&
            !ReferenceEquals(ranked[0], preferred) &&
            preferred.TotalScore <= ranked[0].TotalScore + Conventional24PreferenceDb)
        {
            ranked.Remove(preferred);
            ranked.Insert(0, preferred);
        }

        return ranked;
    }

    private static Complex[][] CropSharedDirectSoundWindow(Complex[][] impulseResponses) =>
        VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            impulseResponses, PostCheckCropLength, PostCheckCropPrePeakSamples);

    // Arrivals from different bands are not comparable (own group delay and envelope rise): both sides are read in one shared band.
    private static (double Ms, bool Valid) CachedRawArrival(
        ConcurrentDictionary<(int Channel, long BandKey), (double Ms, bool Valid)> cache,
        Complex[][] croppedOrdered,
        int channel,
        double bandLowHz,
        double bandHighHz,
        int sampleRate)
    {
        long bandKey = ((long)Math.Round(bandLowHz) << 20) | (long)Math.Round(bandHighHz);
        return cache.GetOrAdd((channel, bandKey), _ =>
        {
            TimeAlignmentAnalysisResult arrival =
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    croppedOrdered[channel], sampleRate, bandLowHz, bandHighHz);
            return (arrival.FirstArrivalDelayMilliseconds, arrival.IsValid);
        });
    }

    // Summed dip-penalized loss after the delay production selection would pick. See docs/tech/crossover-auto-setup.md#achievability-post-check.
    private static double AchievabilityPenaltyDb(
        Complex[][] croppedOrdered,
        CrossoverProposal[] orderedProposals,
        ConcurrentDictionary<(int Channel, long BandKey), (double Ms, bool Valid)> arrivalCache,
        int sampleRate,
        int processorSampleRate)
    {
        var processed = new Complex[croppedOrdered.Length][];
        for (int channel = 0; channel < croppedOrdered.Length; channel++)
        {
            CrossoverProposal proposal = orderedProposals[channel];
            processed[channel] = VirtualCrossoverAnalysis.ApplyChain(
                croppedOrdered[channel],
                new DspChannelChain(
                    GainDb: proposal.GainDb,
                    Crossover: new CrossoverSpec(
                        proposal.Kind,
                        proposal.LowPassEdge,
                        proposal.HighPassEdge)),
                sampleRate,
                processorSampleRate);
        }

        double penalty = 0;
        for (int j = 0; j < processed.Length - 1; j++)
        {
            double? lowPassHz = orderedProposals[j].LowPassEdge?.FrequencyHz;
            double? highPassHz = orderedProposals[j + 1].HighPassEdge?.FrequencyHz;
            if (lowPassHz is not { } lp || highPassHz is not { } hp)
            {
                continue;
            }

            double bandLow = Math.Max(20, Math.Min(lp, hp) / 2);
            double bandHigh = Math.Min(20_000, Math.Max(lp, hp) * 2);

            (double lowerMs, bool lowerValid) = CachedRawArrival(
                arrivalCache, croppedOrdered, j, bandLow, bandHigh, sampleRate);
            (double upperMs, bool upperValid) = CachedRawArrival(
                arrivalCache, croppedOrdered, j + 1, bandLow, bandHigh, sampleRate);
            bool anchored = lowerValid && upperValid;
            double center = anchored ? lowerMs - upperMs : 0;
            double halfWindow = anchored
                ? PostCheckHalfWindowMs(Math.Min(lp, hp))
                : PostCheckMaxHalfWindowMs;

            // The chains render without the proposal's polarity: only the relation Auto delay will force is searched.
            bool? forcedFlip = AutoAlignmentEngine.SettledRelativeInversion(
                orderedProposals[j].LowPassEdge, orderedProposals[j + 1].HighPassEdge, processorSampleRate);

            IReadOnlyList<AlignmentCandidate> Search(double half) =>
                VirtualCrossoverAnalysis.FindAlignmentCandidates(
                    processed[j + 1],
                    [processed[j]],
                    sampleRate,
                    bandLow,
                    bandHigh,
                    center - half,
                    center + half,
                    priorDelayMs: anchored ? center : null,
                    priorSigmaMs: half / 2.0,
                    forcedPolarity: forcedFlip);

            IReadOnlyList<AlignmentCandidate> found = Search(halfWindow);
            if (found.Count == 0)
            {
                penalty += PostCheckMissingJunctionPenaltyDb;
                continue;
            }

            AlignmentCandidate chosen = AlignmentSelection.Select(found, center);
            // Retry re-selected through the same rules: the raw best of a widened window is exactly the impostor selection rejects.
            if (Math.Abs(chosen.DelayMs - center) >= halfWindow * 0.9)
            {
                IReadOnlyList<AlignmentCandidate> retried = Search(halfWindow * 2);
                if (retried.Count > 0)
                {
                    chosen = AlignmentSelection.Select(retried, center);
                }
            }

            penalty += -(chosen.LossDb + DipPenaltyWeight * (chosen.DipDb - chosen.LossDb));
        }

        return penalty;
    }

    internal sealed record PoolCandidate(
        IReadOnlyList<CrossoverProposal> Proposals,
        double MagnitudeScore,
        string Signature);

    /// <summary>The ideal complex sum the optimizer scored, on its log grid: measured magnitude with its own minimum
    /// phase, the crossover's real phase, the proposal's polarity, drivers taken as perfectly time-aligned. Used by
    /// the live preview and tests. It is NOT what the panel will measure — see docs/tech/crossover-auto-setup.md.</summary>
    public static IReadOnlyList<SignalPoint> SummedResponseDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz,
        double processorSampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(proposals);
        if (channels.Count != proposals.Count)
        {
            throw new ArgumentException(
                "One proposal is required per channel.",
                nameof(proposals));
        }
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (processorSampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processorSampleRateHz));
        }

        double[] grid = BuildGrid(sampleRateHz);
        var combined = new Complex[grid.Length];
        for (int channel = 0; channel < channels.Count; channel++)
        {
            CrossoverProposal proposal = proposals[channel];
            double scale = DataHelper.DecibelsToAmplitude(proposal.GainDb)
                * (proposal.InvertPolarity ? -1.0 : 1.0);
            var spec = new CrossoverSpec(
                proposal.Kind,
                proposal.LowPassEdge,
                proposal.HighPassEdge);
            double[] phase = MinimumPhaseOnGrid(channels[channel].MagnitudeDb, grid);
            for (int k = 0; k < grid.Length; k++)
            {
                double driverDb = InterpolateDb(channels[channel].MagnitudeDb, grid[k]);
                if (double.IsFinite(driverDb))
                {
                    combined[k] += scale
                        * Complex.FromPolarCoordinates(
                            DataHelper.DecibelsToAmplitude(driverDb), phase[k])
                        * CrossoverFilter.Response(spec, grid[k], processorSampleRateHz);
                }
            }
        }

        var result = new SignalPoint[grid.Length];
        for (int k = 0; k < grid.Length; k++)
        {
            result[k] = new SignalPoint(
                grid[k], DataHelper.AmplitudeToDecibels(combined[k].Magnitude));
        }

        return result;
    }

    private static CrossoverAutoSetupOptions Normalize(CrossoverAutoSetupOptions options)
    {
        if (options.SampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The sample rate must be positive.");
        }
        if (options.ProcessorSampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The processor sample rate must be positive.");
        }

        var families = options.Families
            .Distinct()
            .ToList();
        if (families.Count == 0)
        {
            throw new ArgumentException(
                "At least one filter family must be allowed.",
                nameof(options));
        }

        double ceiling = options.SampleRateHz * 0.49;
        double max = Math.Clamp(options.MaxCrossoverHz, 20, ceiling);
        double min = Math.Clamp(options.MinCrossoverHz, 20, ceiling);
        if (min >= max)
        {
            min = Math.Max(20, max / 2);
        }

        return options with { Families = families, MinCrossoverHz = min, MaxCrossoverHz = max };
    }

    private static double[] BuildGrid(double sampleRateHz)
    {
        double low = 20;
        double high = Math.Min(20_000, sampleRateHz * 0.49);
        if (high <= low)
        {
            high = low * 2;
        }

        int count = Math.Max(
            2,
            (int)Math.Round(Math.Log2(high / low) * GridPointsPerOctave) + 1);
        return EqualizationCurve.LogFrequencyGrid(low, high, count).ToArray();
    }

    // Clamped ends: outside the measured range the driver has rolled off, so hold the endpoint.
    private static double InterpolateDb(IReadOnlyList<SignalPoint> points, double frequencyHz) =>
        CurveSampling.InterpolateDbLog(points, frequencyHz, clampEnds: true);

    private static double AverageLevelDb(
        IReadOnlyList<SignalPoint> curve,
        double fromHz,
        double toHz)
    {
        double sum = 0;
        int count = 0;
        foreach (SignalPoint point in curve)
        {
            if (point.X < fromHz || point.X > toHz || !double.IsFinite(point.Y))
            {
                continue;
            }

            sum += DataHelper.DecibelsToAmplitude(point.Y);
            count++;
        }

        return count > 0
            ? DataHelper.AmplitudeToDecibels(sum / count)
            : double.NegativeInfinity;
    }

    // Seed: intersection of level-aligned curves in the overlap, an octave from band edges; geometric mean if they never cross.
    private static double ProposeCrossoverFrequency(
        IReadOnlyList<SignalPoint> lowerCurve,
        DriverBandEstimate lowerBand,
        DriverType lowerType,
        IReadOnlyList<SignalPoint> upperCurve,
        DriverBandEstimate upperBand,
        DriverType upperType)
    {
        double overlapLow = upperBand.LowHz;
        double overlapHigh = lowerBand.HighHz;
        double crossover = double.NaN;

        if (overlapHigh > overlapLow)
        {
            int count = Math.Min(lowerCurve.Count, upperCurve.Count);
            double previousDiff = double.NaN;
            double previousX = double.NaN;
            for (int i = 0; i < count; i++)
            {
                double frequency = lowerCurve[i].X;
                if (frequency < overlapLow || frequency > overlapHigh)
                {
                    continue;
                }

                double diff =
                    (lowerCurve[i].Y - lowerBand.LevelDb) -
                    (upperCurve[i].Y - upperBand.LevelDb);
                if (!double.IsNaN(previousDiff) && previousDiff > 0 && diff <= 0)
                {
                    double t = previousDiff / (previousDiff - diff);
                    crossover = previousX + (frequency - previousX) * t;
                    break;
                }

                previousDiff = diff;
                previousX = frequency;
            }
        }

        if (double.IsNaN(crossover))
        {
            crossover = Math.Sqrt(
                Math.Max(20, overlapLow) * Math.Min(20_000, Math.Max(overlapLow + 1, overlapHigh)));
        }

        double margin = Math.Pow(2.0, CrossoverMarginOctaves);
        double minimum = upperBand.LowHz * margin;
        double maximum = lowerBand.HighHz / margin;
        crossover = minimum <= maximum
            ? Math.Clamp(crossover, minimum, maximum)
            : Math.Sqrt(upperBand.LowHz * lowerBand.HighHz);

        // Keep the seed out of the lower driver's roll-off skirt (a woofer seeded at 850 Hz).
        (double typeLow, double typeHigh) = JunctionTypeBounds(lowerType, upperType);
        if (typeLow <= typeHigh)
        {
            crossover = Math.Clamp(crossover, typeLow, typeHigh);
        }

        return Math.Clamp(crossover, 20, 20_000);
    }

    /// <summary>Makes junction <paramref name="junction"/> sum inverted or not, leaving every other junction's relation
    /// as it is: the whole stack above turns, since a relation is the XOR of two neighbours' signs.</summary>
    internal static void SetRelativeInversion(bool[] invert, int junction, bool invertRelative)
    {
        if (invert[junction + 1] == (invert[junction] ^ invertRelative))
        {
            return;
        }

        for (int k = junction + 1; k < invert.Length; k++)
        {
            invert[k] = !invert[k];
        }
    }

    /// <summary>Coordinate descent over junction frequency/family/slope and channel gain. See docs/tech/crossover-auto-setup.md#optimizer.</summary>
    private sealed class Optimizer
    {
        private readonly CrossoverAutoSetupOptions options;
        private readonly int channelCount;
        private readonly IReadOnlyList<SignalPoint>[] curves;
        private readonly DriverBandEstimate[] bands;
        private readonly DriverType[] types;
        private readonly double[] grid;
        // Measured magnitude carrying its own minimum phase: the ideal complex sum's driver term. Built on first
        // use, because the cepstrum behind it is two 65536-point transforms per channel and the window resolution
        // the dialog asks for on every keystroke needs none of it.
        private readonly Complex[]?[] driverResponse;
        private readonly double[] psychoacousticHalfWidthBins;
        private readonly int evalLow;
        private readonly int evalHigh;

        // Window-edge band limits on the outer channels; not part of the search.
        private readonly CrossoverEdge? lowLimitEdge;
        private readonly CrossoverEdge? highLimitEdge;

        private readonly double[] gainDb;
        private readonly double[] crossoverHz;
        // Corner separation of a split junction, in octaves: the low-pass sits half of it below the corner, the
        // high-pass half above. Zero unless the junction's window allows a split.
        private readonly double[] splitOctaves;
        private readonly CrossoverFilterFamily[] junctionFamily;
        private readonly int[] lowerSlope;
        private readonly int[] upperSlope;
        // Channel 0 stays upright during the search: flipping every channel is acoustically free, so it is settled
        // once at the end by NormalizePolarity.
        private readonly bool[] invert;

        private readonly int? forcedSlope;
        private readonly JunctionWindowResolution[] windows;
        // The bounds that protect the drivers, before the class priors and the user's own narrowing. The window
        // holds them for the CORNER; an offset moves the edges off it, and they have to clear them where they land.
        private readonly (double Low, double High)[] junctionSafety;

        private readonly Dictionary<(CrossoverFilterFamily, int, long, bool), Complex[]> edgeCache =
            new();

        private readonly Dictionary<(CrossoverFilterFamily, int, long), double> groupDelayCache =
            new();

        // Keyed by edge choice; lattice-stable frequencies make it hit on almost every probe after pass one.
        private readonly Dictionary<(int Channel, long HighPassKey, long LowPassKey), Complex[]> unitCache =
            new();
        private readonly Complex[][] scratchUnits;
        private readonly Complex[] scratchCombined;
        // Score() runs tens of thousands of times per fit, so the level buffers it reads through are fields: an
        // Optimizer is never shared between threads (the ranked search parallelizes over candidates, not inside one).
        private readonly double[] scratchLevels;
        private readonly double[] scratchJunctionLevels;
        private readonly double[] scratchSmoothSource;

        public Optimizer(
            IReadOnlyList<AutoSetupSource> channels,
            CrossoverAutoSetupOptions options,
            int? forcedSlope = null)
        {
            this.options = options;
            this.forcedSlope = forcedSlope;
            channelCount = channels.Count;

            // Caller's order, not a sort by type: two drivers of one class form a legitimate chain.
            curves = channels.Select(channel => channel.MagnitudeDb).ToArray();
            bands = channels
                .Select(channel => EstimateBand(
                    channel.MagnitudeDb, channel.Coherence, channel.DistortionDb))
                .ToArray();
            types = channels.Select(channel => channel.Type).ToArray();

            grid = BuildGrid(options.SampleRateHz);
            driverResponse = new Complex[channelCount][];

            // Half-width in grid bins of the psychoacoustic kernel: 1/3 octave below 100 Hz easing to 1/6 above 1 kHz.
            psychoacousticHalfWidthBins = new double[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                psychoacousticHalfWidthBins[k] =
                    SpectrumSmoothing.PsychoacousticOctaves(grid[k]) * GridPointsPerOctave / 2.0;
            }

            // Only when the limit sits at least a semitone inside the driver edge.
            double margin = Math.Pow(2.0, 1.0 / 12.0);
            CrossoverFilterFamily limitFamily = PreferredFamily();
            lowLimitEdge = options.MinCrossoverHz > bands[0].LowHz * margin
                ? new CrossoverEdge(
                    limitFamily,
                    Math.Round(options.MinCrossoverHz),
                    forcedSlope ?? GentlestAdmissibleSlope(limitFamily, options.MinCrossoverHz))
                : null;
            highLimitEdge = options.MaxCrossoverHz < bands[channelCount - 1].HighHz / margin
                ? new CrossoverEdge(
                    limitFamily,
                    Math.Round(options.MaxCrossoverHz),
                    forcedSlope ?? GentlestAdmissibleSlope(limitFamily, options.MaxCrossoverHz))
                : null;

            // Trim half an octave inside the outer skirts: that constant floor would swamp the crossover ripple.
            double trim = Math.Pow(2.0, 0.5);
            double lowEdge = lowLimitEdge is { } low ? low.FrequencyHz : bands[0].LowHz;
            double highEdge = highLimitEdge is { } high
                ? high.FrequencyHz
                : bands[channelCount - 1].HighHz;
            double sysLow = lowEdge * trim;
            double sysHigh = highEdge / trim;
            evalLow = 0;
            evalHigh = grid.Length - 1;
            for (int k = 0; k < grid.Length; k++)
            {
                if (grid[k] < sysLow)
                {
                    evalLow = k + 1;
                }
            }
            for (int k = grid.Length - 1; k >= 0; k--)
            {
                if (grid[k] > sysHigh)
                {
                    evalHigh = k - 1;
                }
            }
            if (evalHigh <= evalLow)
            {
                evalLow = 0;
                evalHigh = grid.Length - 1;
            }

            gainDb = new double[channelCount];
            crossoverHz = new double[channelCount - 1];
            splitOctaves = new double[channelCount - 1];
            junctionFamily = new CrossoverFilterFamily[channelCount - 1];
            lowerSlope = new int[channelCount - 1];
            upperSlope = new int[channelCount - 1];
            invert = new bool[channelCount];
            scratchUnits = new Complex[channelCount][];
            scratchCombined = new Complex[grid.Length];
            scratchLevels = new double[grid.Length];
            scratchJunctionLevels = new double[grid.Length];
            scratchSmoothSource = new double[grid.Length];
            windows = new JunctionWindowResolution[channelCount - 1];
            junctionSafety = new (double, double)[channelCount - 1];
            for (int j = 0; j < channelCount - 1; j++)
            {
                windows[j] = BuildWindow(j);
            }
        }

        public IReadOnlyList<CrossoverProposal> Solve()
        {
            Descend();
            NormalizeGainsCutOnly();
            NormalizePolarity();
            return BuildProposals();
        }

        /// <summary>Flipping every channel is acoustically free, so the absolute choice is settled here rather than
        /// searched: take the side with fewer inverted channels, and on a tie leave the lowest driver upright.</summary>
        private void NormalizePolarity()
        {
            int inverted = invert.Count(value => value);
            if (inverted * 2 > channelCount || (inverted * 2 == channelCount && invert[0]))
            {
                for (int i = 0; i < channelCount; i++)
                {
                    invert[i] = !invert[i];
                }
            }
        }

        private void Descend()
        {
            Initialize();

            double previous = Score();
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                for (int j = 0; j < channelCount - 1; j++)
                {
                    OptimizeJunction(j);
                    OptimizeJunctionSplit(j);
                }

                if (!options.IndependentSlopes)
                {
                    for (int i = 0; i < channelCount; i++)
                    {
                        OptimizeChannelSlope(i);
                    }
                }

                OptimizeGains();

                double current = Score();
                if (previous - current < ConvergenceDb)
                {
                    break;
                }

                previous = current;
            }

            EnforceTweeterResonanceFloor();
        }

        // The decoupled search can leave the tweeter under its Fs floor; raise fc, else steepen. See docs/tech/crossover-auto-setup.md#tweeter-resonance-floor.
        private void EnforceTweeterResonanceFloor()
        {
            int last = channelCount - 1;
            if (types[last] != DriverType.Tweeter)
            {
                return;
            }

            int j = last - 1;
            double resonanceHz = TweeterResonanceHz(bands[last].LowHz);
            double minFc = TweeterMinCrossoverHz(resonanceHz, upperSlope[j]);
            if (HighPassHz(j) >= minFc)
            {
                return;
            }

            // Fs is safety and a split corner is a preference: give the split up rather than reason about both at once.
            splitOctaves[j] = 0;

            double raised = Math.Min(options.MaxCrossoverHz, RoundUpToLattice(minFc));
            if (raised >= minFc)
            {
                crossoverHz[j] = Math.Max(crossoverHz[j], raised);
                return;
            }

            // Fs is safety and the junction's slope window a preference, so steepening may leave the window. A forced
            // slope defines the conventional baseline: that run keeps it, and SolvePool drops the candidate instead.
            int floor = SlopeFloor(last, crossoverHz[j]);
            int? Gentlest(IEnumerable<int> slopes) =>
                slopes.Where(slope => slope >= floor).Cast<int?>().Min();
            int? steeper = Gentlest(AllowedSlopes(j, junctionFamily[j], crossoverHz[j])) ??
                (forcedSlope is null ? Gentlest(PracticalSlopes(junctionFamily[j])) : null);
            if (steeper is int slope)
            {
                upperSlope[j] = slope;
            }
        }

        private bool TweeterUnderResonanceFloor()
        {
            int last = channelCount - 1;
            return types[last] == DriverType.Tweeter &&
                HighPassHz(last - 1) < TweeterMinCrossoverHz(
                    TweeterResonanceHz(bands[last].LowHz), upperSlope[last - 1]) - 1e-6;
        }

        /// <summary>Descent winner plus per-junction best options crossed (bounded), one gain pass each. See docs/tech/crossover-auto-setup.md#ranked-search.</summary>
        public List<PoolCandidate> SolvePool(int poolSize)
        {
            Descend();

            var pool = new List<PoolCandidate>();
            var seen = new HashSet<string>();
            void Capture()
            {
                // The combination loop composes junction choices that were each cleared on their own; only this
                // states the invariant over the whole chain, and nothing else re-runs it after the crossing.
                EnforceTweeterResonanceFloor();
                if (forcedSlope is not null && TweeterUnderResonanceFloor())
                {
                    return;
                }

                NormalizeGainsCutOnly();
                NormalizePolarity();
                IReadOnlyList<CrossoverProposal> proposals = BuildProposals();
                string signature = SignatureOf(proposals);
                if (seen.Add(signature))
                {
                    pool.Add(new PoolCandidate(proposals, Score(), signature));
                }
            }

            Capture();
            if (poolSize <= 1)
            {
                return pool;
            }

            int junctions = channelCount - 1;
            var junctionChoices = new List<JunctionOption>[junctions];
            for (int j = 0; j < junctions; j++)
            {
                (double low, double high) = JunctionSearchBounds(j);
                junctionChoices[j] = EnumerateJunctionOptions(j, low, high)
                    .GroupBy(option =>
                        (option.Family, option.FrequencyHz, option.LowerSlope, option.UpperSlope,
                            option.SplitOctaves))
                    .Select(group => group.First())
                    .OrderBy(option => option.Score)
                    .Take(PoolOptionsPerJunction)
                    .ToList();
                if (junctionChoices[j].Count == 0)
                {
                    junctionChoices[j] =
                    [
                        new JunctionOption(
                            junctionFamily[j], crossoverHz[j], lowerSlope[j], upperSlope[j],
                            splitOctaves[j], invert[j] ^ invert[j + 1], 0)
                    ];
                }
            }

            var savedGains = (double[])gainDb.Clone();
            var savedFc = (double[])crossoverHz.Clone();
            var savedFamilies = (CrossoverFilterFamily[])junctionFamily.Clone();
            var savedLower = (int[])lowerSlope.Clone();
            var savedUpper = (int[])upperSlope.Clone();
            var savedSplits = (double[])splitOctaves.Clone();
            var savedInvert = (bool[])invert.Clone();
            void Restore()
            {
                savedGains.CopyTo(gainDb, 0);
                savedFc.CopyTo(crossoverHz, 0);
                savedFamilies.CopyTo(junctionFamily, 0);
                savedLower.CopyTo(lowerSlope, 0);
                savedUpper.CopyTo(upperSlope, 0);
                savedSplits.CopyTo(splitOctaves, 0);
                savedInvert.CopyTo(invert, 0);
            }

            long totalCombinations = 1;
            foreach (List<JunctionOption> choices in junctionChoices)
            {
                totalCombinations *= choices.Count;
            }

            long combinations = Math.Min(totalCombinations, PoolMaxCombinations);
            // Options were bounded against the optimum's neighbours, so two moved junctions can jointly break separation.
            double minimumRatio =
                Math.Pow(2.0, MinJunctionSeparationOctaves) * (1 - 1e-9);
            var indices = new int[junctions];
            for (long combo = 0; combo < combinations; combo++)
            {
                long remainder = combo;
                for (int j = 0; j < junctions; j++)
                {
                    indices[j] = (int)(remainder % junctionChoices[j].Count);
                    remainder /= junctionChoices[j].Count;
                }

                Restore();
                for (int j = 0; j < junctions; j++)
                {
                    JunctionOption choice = junctionChoices[j][indices[j]];
                    Set(
                        j, choice.Family, choice.FrequencyHz, choice.LowerSlope, choice.UpperSlope,
                        choice.SplitOctaves, choice.InvertRelative);
                }

                bool separated = true;
                for (int j = 1; j < junctions; j++)
                {
                    if (crossoverHz[j] < crossoverHz[j - 1] * minimumRatio)
                    {
                        separated = false;
                        break;
                    }
                }

                if (!separated)
                {
                    continue;
                }

                OptimizeGains();
                Capture();
            }

            Restore();
            return pool
                .OrderBy(candidate => candidate.MagnitudeScore)
                .Take(poolSize)
                .ToList();
        }

        private static string SignatureOf(IReadOnlyList<CrossoverProposal> proposals) =>
            string.Join(
                "|",
                proposals.Select(proposal =>
                    $"{proposal.Kind}:{Describe(proposal.HighPassEdge)}:" +
                    $"{Describe(proposal.LowPassEdge)}:{proposal.GainDb:0.0}:" +
                    $"{(proposal.InvertPolarity ? "inv" : "-")}"));

        private static string Describe(CrossoverEdge? edge) =>
            edge is { } value
                ? $"{value.Family}/{value.FrequencyHz:0}/{value.SlopeDbPerOctave}"
                : "-";

        private void Initialize()
        {
            CrossoverFilterFamily family = PreferredFamily();
            Array.Clear(invert);
            Array.Clear(splitOctaves);
            for (int j = 0; j < channelCount - 1; j++)
            {
                double fc = ProposeCrossoverFrequency(
                    curves[j], bands[j], types[j], curves[j + 1], bands[j + 1], types[j + 1]);
                // Seeded inside the junction's own window, so a narrowed window starts the descent where it can search.
                crossoverHz[j] = Math.Clamp(
                    RoundToLattice(fc), windows[j].LowHz, windows[j].HighHz);
                junctionFamily[j] = family;
                int slope = forcedSlope ?? SeedSlope(j, family, crossoverHz[j]);
                lowerSlope[j] = slope;
                upperSlope[j] = slope;
            }

            double separation = Math.Pow(2.0, MinJunctionSeparationOctaves);
            for (int j = 1; j < channelCount - 1; j++)
            {
                if (crossoverHz[j] <= crossoverHz[j - 1])
                {
                    crossoverHz[j] = Math.Min(
                        options.MaxCrossoverHz,
                        crossoverHz[j - 1] * separation);
                }
            }

            InitializeGains();
        }

        private CrossoverFilterFamily PreferredFamily() =>
            CrossoverAutoSetup.PreferredFamily(options.Families);

        /// <summary>Whether a junction's REAL edges clear the bounds that protect the drivers: the tweeter's
        /// distortion knee below the high-pass, the lower driver's breakup onset above the low-pass. The window
        /// enforces both, but it enforces them on the CORNER, and an offset moves the edges off it — at the widest
        /// offset the high-pass sits at 0.917 of the corner and the low-pass at 1.091. With matched corners the
        /// edges ARE the corner, so this can only agree with the window that already placed it.</summary>
        private bool EdgesClearSafetyBounds(int j, double lowPassHz, double highPassHz)
        {
            // Whichever bound the window had to give up is not in here: this asks the window's own question again
            // at the frequency the edge landed on, not a stricter one the corner was never held to.
            (double low, double high) = junctionSafety[j];
            return highPassHz >= low - 1e-9 && lowPassHz <= high + 1e-9;
        }

        /// <summary>The family's admissible slopes narrowed to the junction's slope window. The window always holds
        /// 24 dB/oct, but the group-delay budget may still exclude everything in it, so an empty intersection falls
        /// back to the unrestricted set rather than stranding the search.</summary>
        private IReadOnlyList<int> AllowedSlopes(int junction, CrossoverFilterFamily family, double fcHz)
        {
            IReadOnlyList<int> admissible = AllowedSlopes(family, fcHz);
            JunctionWindowResolution window = windows[junction];
            List<int> inWindow = admissible
                .Where(slope => slope >= window.MinSlopeDbPerOctave &&
                    slope <= window.MaxSlopeDbPerOctave)
                .ToList();
            return inWindow.Count > 0 ? inWindow : admissible;
        }

        private IReadOnlyList<int> AllowedSlopes(CrossoverFilterFamily family, double fcHz)
        {
            IReadOnlyList<int> slopes = PracticalSlopes(family);
            if (slopes.Count == 0)
            {
                return slopes;
            }

            int floor = slopes.Min();
            bool WithinBudget(int slope) =>
                GroupDelaySeconds(family, slope, fcHz) <= MaxCrossoverGroupDelaySeconds;

            if (forcedSlope is int locked)
            {
                return slopes.Contains(locked) && (WithinBudget(locked) || locked == floor)
                    ? [locked]
                    : [];
            }

            List<int> withinBudget = slopes.Where(WithinBudget).ToList();
            // See docs/tech/crossover-auto-setup.md#group-delay-budget.
            return withinBudget.Count > 0 ? withinBudget : [floor];
        }

        private int GentlestAdmissibleSlope(CrossoverFilterFamily family, double fcHz)
        {
            IReadOnlyList<int> allowed = AllowedSlopes(family, fcHz);
            return allowed.Count > 0 ? allowed.Min() : PracticalSlopes(family).Min();
        }

        private int SeedSlope(int junction, CrossoverFilterFamily family, double fcHz)
        {
            IReadOnlyList<int> allowed = AllowedSlopes(junction, family, fcHz);
            if (allowed.Count == 0)
            {
                return PracticalSlopes(family).Min();
            }

            return allowed
                .OrderBy(slope => Math.Abs(
                    Math.Log2((double)slope / PreferredSlopeDbPerOctave)))
                .First();
        }

        // Ignores the GD budget: it sets how low the tweeter window may open.
        private int SteepestPracticalSlope() =>
            options.Families
                .SelectMany(PracticalSlopes)
                .DefaultIfEmpty(MinPracticalSlopeDbPerOctave)
                .Max();

        private double GroupDelaySeconds(CrossoverFilterFamily family, int slope, double fcHz)
        {
            var key = (family, slope, (long)Math.Round(fcHz));
            if (!groupDelayCache.TryGetValue(key, out double delay))
            {
                delay = CrossoverFilter.MaxGroupDelaySeconds(
                    new CrossoverEdge(family, fcHz, slope),
                    highPass: false,
                    options.ProcessorSampleRateHz);
                groupDelayCache[key] = delay;
            }

            return delay;
        }

        // slope >= target / log2(fc / Fs) for a tweeter; at or below Fs nothing qualifies, which pushes the search off that fc.
        private int SlopeFloor(int driverIndex, double fcHz)
        {
            if (types[driverIndex] != DriverType.Tweeter)
            {
                return MinPracticalSlopeDbPerOctave;
            }

            double resonanceHz = TweeterResonanceHz(bands[driverIndex].LowHz);
            if (fcHz <= resonanceHz)
            {
                return int.MaxValue;
            }

            int needed = (int)Math.Ceiling(
                TweeterFsAttenuationTargetDb / Math.Log2(fcHz / resonanceHz));
            return Math.Max(MinPracticalSlopeDbPerOctave, needed);
        }

        private void InitializeGains()
        {
            var level = new double[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                double from = i == 0 ? bands[i].LowHz : crossoverHz[i - 1];
                double to = i == channelCount - 1 ? bands[i].HighHz : crossoverHz[i];
                if (to <= from)
                {
                    from = bands[i].LowHz;
                    to = bands[i].HighHz;
                }

                level[i] = AverageLevelDb(curves[i], from, to);
            }

            double target = level.Min();
            for (int i = 0; i < channelCount; i++)
            {
                gainDb[i] = target - level[i];
            }
        }

        /// <summary>The static part of a junction's window: measured bands, class bounds, the tweeter Fs floor, the
        /// distortion-clean band and the user's own request, with a note for every bound the user's numbers lost to.
        /// Neighbour separation is NOT here — it moves as the descent moves the junctions either side.</summary>
        public JunctionWindowResolution ResolveWindow(int j) => windows[j];

        private static JunctionWindowNote Moved(double requested, double applied, string reason) =>
            new(
                $"{NoteHz(requested)} → {NoteHz(applied)}",
                $"{NoteHz(requested)} is outside what this junction can take: {reason} puts the bound " +
                $"at {NoteHz(applied)}.");

        private JunctionWindowResolution BuildWindow(int j)
        {
            var notes = new List<JunctionWindowNote>();
            JunctionSearchWindow? requested =
                options.JunctionWindows is { } list && j < list.Count ? list[j] : null;

            double autoLow = bands[j + 1].LowHz;
            string lowReason = "the upper driver's measured band";
            double autoHigh = bands[j].HighHz;
            string highReason = "the lower driver's measured band";

            void RaiseLow(double value, string reason)
            {
                if (double.IsFinite(value) && value > autoLow)
                {
                    autoLow = value;
                    lowReason = reason;
                }
            }

            void LowerHigh(double value, string reason)
            {
                if (double.IsFinite(value) && value < autoHigh)
                {
                    autoHigh = value;
                    highReason = reason;
                }
            }

            RaiseLow(options.MinCrossoverHz, "the system band limit");
            LowerHigh(options.MaxCrossoverHz, "the system band limit");

            // Three strengths, weakest first. A class bound says which class SHOULD own a region, and the drivers
            // have already said what they CAN do, so a class bound that empties the window is dropped rather than
            // obeyed — silently, because it is a preference losing to a measurement and there is nothing to warn
            // about. See docs/tech/crossover-auto-setup.md#per-junction-windows.
            double measuredLow = autoLow;
            double measuredHigh = autoHigh;
            string measuredLowReason = lowReason;
            string measuredHighReason = highReason;
            (double typeLow, double typeHigh) = JunctionTypeBounds(types[j], types[j + 1]);
            if (typeLow <= typeHigh)
            {
                RaiseLow(typeLow, "the upper driver's class");
                LowerHigh(typeHigh, "the lower driver's class");
            }

            if (autoHigh < autoLow)
            {
                autoLow = measuredLow;
                autoHigh = measuredHigh;
                lowReason = measuredLowReason;
                highReason = measuredHighReason;
            }

            // Safety is NOT a preference, so it is applied even where the class bounds had to be dropped. Bundling
            // the two together is what once let a mid measuring to 736 Hz hand over to a tweeter measuring from
            // 712 Hz inside its own resonance: the window read 712-736 and only the after-the-fact floor saved it.
            double safetyLow = 0;
            string safetyLowReason = string.Empty;
            if (types[j + 1] == DriverType.Tweeter)
            {
                // Opened only to where the steepest available slope still protects Fs.
                safetyLow = TweeterMinCrossoverHz(
                    TweeterResonanceHz(bands[j + 1].LowHz), SteepestPracticalSlope());
                safetyLowReason = "the tweeter's Fs floor";
                // Distortion only tightens. See docs/tech/crossover-auto-setup.md#distortion-clean-band.
                double knee = bands[j + 1].DistortionLowHz;
                if (double.IsFinite(knee) && knee > safetyLow)
                {
                    safetyLow = knee;
                    safetyLowReason = "the tweeter's distortion knee";
                }
            }

            double safetyHigh = double.PositiveInfinity;
            string safetyHighReason = string.Empty;
            if (double.IsFinite(bands[j].DistortionHighHz))
            {
                safetyHigh = bands[j].DistortionHighHz;
                safetyHighReason = "the lower driver's breakup onset";
            }

            // The drivers may not overlap at all, and that is NOT a safety conflict: the window is then the gap
            // between them, which is where a handover has to sit anyway. Deciding this before safety is applied is
            // the point — afterwards the two are indistinguishable, and the safety branch would open a window
            // 1.5 octaves above a lower driver that stopped playing long before it.
            if (autoHigh < autoLow)
            {
                (autoLow, autoHigh) = (autoHigh, autoLow);
                (lowReason, highReason) = ("the lower driver's measured band", lowReason);
                notes.Add(new JunctionWindowNote(
                    $"Gap {NoteHz(autoLow)}–{NoteHz(autoHigh)}",
                    $"The two drivers do not overlap: the lower one is down by {NoteHz(autoLow)} and the " +
                    $"upper one does not reach {NoteHz(autoHigh)}. The handover can only sit in the gap " +
                    "between them, so that is the window — and the sum through it is the one number worth " +
                    "reading on this chain."));
            }

            junctionSafety[j] = (safetyLow, safetyHigh);
            double wantedLow = autoLow;
            double wantedHigh = autoHigh;
            RaiseLow(safetyLow, safetyLowReason);
            LowerHigh(safetyHigh, safetyHighReason);

            bool overridden = false;
            if (autoHigh < autoLow)
            {
                // Safety and the drivers disagree, and after the gap swap above it can only be safety that did it.
                // A floor protects hardware — a tweeter crossed under its resonance overexcurts — so it stands and
                // the window opens UPWARD from it; collapsing onto it is what left the search nothing to do and
                // handed the corner to the after-the-fact floor instead. A cap protects the lower driver from its
                // own breakup, which is one-sided the other way, so the window opens DOWNWARD from the cap. Where
                // both crossed, the floor wins: overexcursion is damage and breakup is only a worse sound.
                string blocked = lowReason;
                string yielded = highReason;
                // Two questions, and only the first can make a safety bound give way. The drivers, the classes and
                // the user are PREFERENCES and yield to safety. One safety bound yields to the other only when the
                // two cannot both be met — not merely because one of them is what emptied the window.
                bool safetyConflict = safetyLow > safetyHigh;
                // Not just "did the floor clear the top of the window": a floor and a cap can each sit inside
                // the window and still cross EACH OTHER, and that is the case the policy is actually about.
                bool floorWon = safetyConflict || safetyLow > wantedHigh;
                double span = Math.Pow(2.0, SafetyOverrideSpanOctaves);
                if (floorWon)
                {
                    autoLow = Math.Clamp(
                        Math.Max(safetyLow, wantedLow),
                        options.MinCrossoverHz,
                        options.MaxCrossoverHz);
                    double reach = safetyConflict
                        ? autoLow * span
                        : Math.Min(autoLow * span, safetyHigh);
                    autoHigh = Math.Clamp(reach, autoLow, options.MaxCrossoverHz);
                    highReason = safetyConflict || autoLow * span <= safetyHigh
                        ? "the span that bound leaves"
                        : safetyHighReason;
                    junctionSafety[j] = safetyConflict
                        ? (safetyLow, double.PositiveInfinity)
                        : (safetyLow, safetyHigh);
                }
                else
                {
                    autoHigh = Math.Clamp(
                        Math.Min(safetyHigh, wantedHigh),
                        options.MinCrossoverHz,
                        options.MaxCrossoverHz);
                    // safetyConflict cannot hold here: it would have made the floor win.
                    autoLow = Math.Clamp(
                        Math.Max(autoHigh / span, safetyLow),
                        options.MinCrossoverHz,
                        autoHigh);
                    blocked = highReason;
                    yielded = lowReason;
                    lowReason = autoHigh / span >= safetyLow
                        ? "the span that bound leaves"
                        : safetyLowReason;
                    highReason = blocked;
                    junctionSafety[j] = (safetyLow, safetyHigh);
                }

                notes.Add(new JunctionWindowNote(
                    floorWon && types[j + 1] == DriverType.Tweeter
                        ? $"Estimated tweeter Fs {NoteHz(TweeterResonanceHz(bands[j + 1].LowHz))}"
                        : floorWon
                            ? $"Moved up to {NoteHz(autoLow)}"
                            : $"Moved down to {NoteHz(autoHigh)}",
                    $"The drivers only meet at {NoteHz(wantedLow)}–{NoteHz(wantedHigh)}, on the " +
                    $"wrong side of {blocked}. Protecting the driver outranks {yielded}, so the " +
                    $"window moved to {NoteHz(autoLow)}–{NoteHz(autoHigh)} and the search runs " +
                    "there instead of being dragged there afterwards."));
                overridden = true;
            }

            bool pinned = overridden && autoHigh <= autoLow;

            double low = autoLow;
            double high = autoHigh;
            if (!pinned)
            {
                // The user may narrow, never widen; where safety disagrees, safety wins and says why.
                if (requested?.MinHz is { } userLow)
                {
                    if (userLow > high)
                    {
                        notes.Add(Moved(userLow, high, highReason));
                    }
                    else if (userLow < autoLow - 1e-6)
                    {
                        notes.Add(Moved(userLow, autoLow, lowReason));
                    }
                    else
                    {
                        low = userLow;
                    }
                }

                if (requested?.MaxHz is { } userHigh)
                {
                    if (userHigh < low)
                    {
                        notes.Add(Moved(userHigh, low, lowReason));
                    }
                    else if (userHigh > autoHigh + 1e-6)
                    {
                        notes.Add(Moved(userHigh, autoHigh, highReason));
                    }
                    else
                    {
                        high = userHigh;
                    }
                }

                if (high < low)
                {
                    (low, high) = (autoLow, autoHigh);
                }
            }

            // A window of one frequency is a real answer (the classes touch, or a floor met a cap), but it looks like
            // a broken field unless it says so.
            if (!pinned && high <= low + 1e-6)
            {
                notes.Add(new JunctionWindowNote(
                    $"Pinned to {NoteHz(low)}",
                    $"{lowReason} and {highReason} meet at {NoteHz(low)}, so there is one frequency " +
                    "this junction can take and nothing for the search to choose between."));
            }

            (int minSlope, int maxSlope) = ClampSlopeWindow(
                requested?.MinSlopeDbPerOctave, requested?.MaxSlopeDbPerOctave, notes);
            return new JunctionWindowResolution(
                low, high, minSlope, maxSlope, requested?.AllowSplitCorners ?? false, notes);
        }

        // The window, plus the separation the neighbours demand where the descent has already placed them.
        private (double Low, double High) JunctionSearchBounds(int j)
        {
            JunctionWindowResolution window = windows[j];
            double separation = Math.Pow(2.0, MinJunctionSeparationOctaves);
            double low = window.LowHz;
            double high = window.HighHz;
            if (j > 0)
            {
                low = Math.Max(low, crossoverHz[j - 1] * separation);
            }

            if (j < channelCount - 2)
            {
                high = Math.Min(high, crossoverHz[j + 1] / separation);
            }

            if (high < low)
            {
                low = high = Math.Clamp(
                    Math.Sqrt(window.LowHz * window.HighHz),
                    options.MinCrossoverHz,
                    options.MaxCrossoverHz);
            }

            return (low, high);
        }

        private void OptimizeJunction(int j)
        {
            (double low, double high) = JunctionSearchBounds(j);

            JunctionOption best = new(
                junctionFamily[j], crossoverHz[j], lowerSlope[j], upperSlope[j],
                splitOctaves[j], invert[j] ^ invert[j + 1], Score());
            foreach (JunctionOption option in EnumerateJunctionOptions(j, low, high))
            {
                if (option.Score < best.Score)
                {
                    best = option;
                }
            }

            Set(
                j, best.Family, best.FrequencyHz, best.LowerSlope, best.UpperSlope,
                best.SplitOctaves, best.InvertRelative);
        }

        /// <summary>The split offset, refined on the corner the junction sweep just settled. A coordinate of its
        /// own rather than a factor on every other one: crossed with frequency, family and slope it multiplied the
        /// sweep by the length of the offset list for a lever that moves one number. Offset 0 is on the list, so a
        /// junction that gains nothing from a split keeps the matched corner it already had.</summary>
        private void OptimizeJunctionSplit(int j)
        {
            if (!windows[j].AllowSplitCorners)
            {
                return;
            }

            JunctionOption best = new(
                junctionFamily[j], crossoverHz[j], lowerSlope[j], upperSlope[j],
                splitOctaves[j], invert[j] ^ invert[j + 1], Score());
            foreach (double split in SplitOffsetOctaves)
            {
                // An offset moves the edges off the corner the sweep cleared, so everything that made the
                // corner admissible — the safety bounds, the Fs floor, the group-delay budget — is asked again
                // at the two frequencies the edges really land on.
                double lowPassHz = SplitCornerOf(crossoverHz[j], split, -1);
                double highPassHz = SplitCornerOf(crossoverHz[j], split, +1);
                if (!EdgesClearSafetyBounds(j, lowPassHz, highPassHz) ||
                    lowerSlope[j] < SlopeFloor(j, lowPassHz) ||
                    upperSlope[j] < SlopeFloor(j + 1, highPassHz) ||
                    !AllowedSlopes(j, junctionFamily[j], lowPassHz).Contains(lowerSlope[j]) ||
                    !AllowedSlopes(j, junctionFamily[j], highPassHz).Contains(upperSlope[j]))
                {
                    continue;
                }

                JunctionOption option = BestPolarity(
                    j, junctionFamily[j], crossoverHz[j], lowerSlope[j], upperSlope[j], split);
                if (option.Score < best.Score)
                {
                    best = option;
                }
            }

            Set(
                j, best.Family, best.FrequencyHz, best.LowerSlope, best.UpperSlope,
                best.SplitOctaves, best.InvertRelative);
        }

        private int ChannelSlope(int i) =>
            i < channelCount - 1 ? lowerSlope[i] : upperSlope[i - 1];

        // Invariant: upperSlope[i-1] == lowerSlope[i].
        private void SetChannelSlope(int i, int slope)
        {
            if (i < channelCount - 1)
            {
                lowerSlope[i] = slope;
            }

            if (i > 0)
            {
                upperSlope[i - 1] = slope;
            }
        }

        private IReadOnlyList<int> AllowedChannelSlopes(int i)
        {
            List<int>? allowed = null;
            void Intersect(int junction)
            {
                // The channel's OWN edge at that junction: its low-pass below it, its high-pass above it.
                double cornerHz = junction == i ? LowPassHz(junction) : HighPassHz(junction);
                int floor = SlopeFloor(i, cornerHz);
                List<int> slopes = AllowedSlopes(junction, junctionFamily[junction], cornerHz)
                    .Where(slope => slope >= floor)
                    .ToList();
                allowed = allowed == null
                    ? slopes
                    : allowed.Where(slopes.Contains).ToList();
            }

            if (i > 0)
            {
                Intersect(i - 1);
            }

            if (i < channelCount - 1)
            {
                Intersect(i);
            }

            return allowed ?? [];
        }

        private void OptimizeChannelSlope(int i)
        {
            IReadOnlyList<int> allowed = AllowedChannelSlopes(i);
            if (allowed.Count == 0)
            {
                return;
            }

            // Start from an allowed slope: the current one may now sit under the resonance floor.
            int best = allowed.Contains(ChannelSlope(i)) ? ChannelSlope(i) : allowed[0];
            SetChannelSlope(i, best);
            double bestScore = Score();
            foreach (int slope in allowed)
            {
                SetChannelSlope(i, slope);
                double score = Score();
                if (score < bestScore - 1e-9)
                {
                    bestScore = score;
                    best = slope;
                }
            }

            SetChannelSlope(i, best);
        }

        internal readonly record struct JunctionOption(
            CrossoverFilterFamily Family,
            double FrequencyHz,
            int LowerSlope,
            int UpperSlope,
            double SplitOctaves,
            // The RELATION across the junction, not the upper channel's absolute sign. The pool crosses junction
            // options that were each scored against a different upper-channel state, so an absolute sign composed
            // into a combination means a different relative polarity than the one that was measured.
            bool InvertRelative,
            double Score);

        // With independent slopes off the slope belongs to the channel (OptimizeChannelSlope); here only frequency and family vary.
        private IEnumerable<JunctionOption> EnumerateJunctionOptions(
            int j,
            double low,
            double high)
        {
            CrossoverFilterFamily savedFamily = junctionFamily[j];
            double savedFc = crossoverHz[j];
            int savedLower = lowerSlope[j];
            int savedUpper = upperSlope[j];
            double savedSplit = splitOctaves[j];
            bool savedInvert = invert[j] ^ invert[j + 1];
            // The split is NOT crossed with frequency, family and slope here: it is its own coordinate, refined by
            // OptimizeJunctionSplit once this sweep has settled the rest. Crossed, it multiplied the whole sweep by
            // the length of the offset list, and a four-way with every junction split took ten seconds.
            try
            {
                foreach (double fc in LatticePoints(low, high))
                {
                    double lowPassHz = SplitCornerOf(fc, savedSplit, -1);
                    double highPassHz = SplitCornerOf(fc, savedSplit, +1);
                    if (!EdgesClearSafetyBounds(j, lowPassHz, highPassHz))
                    {
                        continue;
                    }

                    int lowerFloor = SlopeFloor(j, lowPassHz);
                    int upperFloor = SlopeFloor(j + 1, highPassHz);
                    foreach (CrossoverFilterFamily family in options.Families)
                    {
                        // Group delay runs as 1/fc, so the two edges of a split junction do not share a budget:
                        // the lower one carries more of it than the corner the window was drawn around.
                        IReadOnlyList<int> lowerSlopes = AllowedSlopes(j, family, lowPassHz);
                        IReadOnlyList<int> upperSlopes = savedSplit == 0
                            ? lowerSlopes
                            : AllowedSlopes(j, family, highPassHz);
                        if (options.IndependentSlopes)
                        {
                            foreach (int lower in lowerSlopes)
                            {
                                if (lower < lowerFloor)
                                {
                                    continue;
                                }

                                foreach (int upper in upperSlopes)
                                {
                                    if (upper < upperFloor)
                                    {
                                        continue;
                                    }

                                    yield return BestPolarity(
                                        j, family, fc, lower, upper, savedSplit);
                                }
                            }
                        }
                        else if (lowerSlopes.Contains(savedLower) &&
                            upperSlopes.Contains(savedUpper) &&
                            savedLower >= lowerFloor && savedUpper >= upperFloor)
                        {
                            yield return BestPolarity(
                                j, family, fc, savedLower, savedUpper, savedSplit);
                        }
                    }
                }
            }
            finally
            {
                Set(j, savedFamily, savedFc, savedLower, savedUpper, savedSplit, savedInvert);
            }
        }

        /// <summary>Scores one crossover both ways round and reports the better. Polarity is derived, not tabulated:
        /// order parity describes a matched-corner Linkwitz-Riley and nothing else — Bessel does not obey it, odd
        /// Butterworth orders are in quadrature where it cannot matter, independent slopes have no parity at all,
        /// and a split corner leaves the rule with nothing to say.</summary>
        private JunctionOption BestPolarity(
            int j,
            CrossoverFilterFamily family,
            double fc,
            int lower,
            int upper,
            double split)
        {
            Set(j, family, fc, lower, upper, split, invertRelative: false);
            double upright = Score();
            Set(j, family, fc, lower, upper, split, invertRelative: true);
            double flipped = Score();
            return flipped < upright
                ? new JunctionOption(family, fc, lower, upper, split, true, flipped)
                : new JunctionOption(family, fc, lower, upper, split, false, upright);
        }

        private void Set(
            int j,
            CrossoverFilterFamily family,
            double fc,
            int lower,
            int upper,
            double split,
            bool invertRelative)
        {
            junctionFamily[j] = family;
            crossoverHz[j] = fc;
            lowerSlope[j] = lower;
            upperSlope[j] = upper;
            splitOctaves[j] = split;
            // Composed onto the lower channel, which the pool's ascending loop has already settled; every channel above
            // turns with it, so the relations of the junctions above stand.
            SetRelativeInversion(invert, j, invertRelative);
        }

        private void OptimizeGains()
        {
            for (int i = 0; i < channelCount; i++)
            {
                double bestGain = gainDb[i];
                double bestScore = Score();
                double start = gainDb[i] - GainSearchRangeDb;
                double end = gainDb[i] + GainSearchRangeDb;
                for (double gain = start; gain <= end + 1e-9; gain += GainSearchStepDb)
                {
                    gainDb[i] = gain;
                    double score = Score();
                    if (score < bestScore - 1e-9)
                    {
                        bestScore = score;
                        bestGain = gain;
                    }
                }

                gainDb[i] = bestGain;
            }
        }

        private void NormalizeGainsCutOnly()
        {
            double max = gainDb.Max();
            for (int i = 0; i < channelCount; i++)
            {
                gainDb[i] -= max;
            }
        }

        /// <summary>Ideal complex sum: each driver contributes its measured magnitude with its own minimum phase, the
        /// crossover contributes the phase it really has, and polarity is a sign. The drivers are taken as perfectly
        /// time-aligned — what the later alignment step is for. See docs/tech/crossover-auto-setup.md#ideal-complex-sum.</summary>
        private double Score()
        {
            for (int i = 0; i < channelCount; i++)
            {
                scratchUnits[i] = ChannelUnitResponse(i);
            }

            Array.Clear(scratchCombined);
            for (int i = 0; i < channelCount; i++)
            {
                double scale = DataHelper.DecibelsToAmplitude(gainDb[i]) * (invert[i] ? -1.0 : 1.0);
                Complex[] unit = scratchUnits[i];
                for (int k = 0; k < scratchCombined.Length; k++)
                {
                    scratchCombined[k] += scale * unit[k];
                }
            }

            return Flatness(scratchCombined)
                + JunctionFlatnessWeight * JunctionPenalty()
                + OverlapPenalty(scratchUnits)
                + SplitPenalty()
                + FrequencyPlacementPenalty()
                + SlopeDeviationPenalty();
        }

        /// <summary>The junction term: how flat the two adjacent channels sum an octave either side of the corner,
        /// read through the psychoacoustic kernel. Deliberately NOT the tuner's summation loss |A+B| / (|A| + |B|):
        /// that asks whether the two add coherently, and under ideal alignment they nearly always do once polarity is
        /// right — a Butterworth pair in phase scores a perfect zero loss while putting a 3 dB bump on the response.
        /// Flatness is the goal, so flatness is what is scored, bumps as well as dips.</summary>
        private double JunctionPenalty()
        {
            double total = 0;
            double[] levels = scratchJunctionLevels;
            for (int j = 0; j < channelCount - 1; j++)
            {
                double fc = crossoverHz[j];
                double low = fc / Math.Pow(2.0, JunctionBandHalfWidthOctaves);
                double high = fc * Math.Pow(2.0, JunctionBandHalfWidthOctaves);
                double lowerScale =
                    DataHelper.DecibelsToAmplitude(gainDb[j]) * (invert[j] ? -1.0 : 1.0);
                double upperScale =
                    DataHelper.DecibelsToAmplitude(gainDb[j + 1]) * (invert[j + 1] ? -1.0 : 1.0);
                Complex[] lowerUnit = scratchUnits[j];
                Complex[] upperUnit = scratchUnits[j + 1];

                int first = -1;
                int last = -1;
                for (int k = evalLow; k <= evalHigh; k++)
                {
                    if (grid[k] < low || grid[k] > high)
                    {
                        continue;
                    }

                    Complex sum = lowerScale * lowerUnit[k] + upperScale * upperUnit[k];
                    levels[k] = DataHelper.AmplitudeToDecibels(sum.Magnitude);
                    first = first < 0 ? k : first;
                    last = k;
                }

                if (first < 0 || last <= first)
                {
                    continue;
                }

                PsychoacousticSmooth(levels, first, last);
                int count = last - first + 1;

                // Referenced to the band's own straight trend in log frequency, NOT to its mean. A mean reference
                // charges the junction for the drivers' tilt through the band, which is largest where the handover
                // is most needed, so the term would quietly become a placement force and fight the class priors.
                // A tilt is free; a bump or a suckout is not, and that is what "flat at the junction" means.
                double sumX = 0;
                double sumY = 0;
                double sumXx = 0;
                double sumXy = 0;
                for (int k = first; k <= last; k++)
                {
                    double x = Math.Log2(grid[k] / fc);
                    sumX += x;
                    sumY += levels[k];
                    sumXx += x * x;
                    sumXy += x * levels[k];
                }

                double denominator = count * sumXx - sumX * sumX;
                double slope = Math.Abs(denominator) > 1e-12
                    ? (count * sumXy - sumX * sumY) / denominator
                    : 0.0;
                double intercept = (sumY - slope * sumX) / count;

                double sumSquares = 0;
                double worstDip = 0;
                double worstBump = 0;
                for (int k = first; k <= last; k++)
                {
                    double deviation =
                        levels[k] - (intercept + slope * Math.Log2(grid[k] / fc));
                    sumSquares += deviation * deviation;
                    worstDip = Math.Max(worstDip, -deviation);
                    worstBump = Math.Max(worstBump, deviation);
                }

                total += Math.Sqrt(sumSquares / count)
                    + DipPenaltyWeight * worstDip
                    + BumpPenaltyWeight * worstBump;
            }

            return total;
        }

        /// <summary>What a split costs before it has done anything. See <see cref="SplitPenaltyDbPerOctave"/>.</summary>
        private double SplitPenalty()
        {
            double total = 0;
            for (int j = 0; j < channelCount - 1; j++)
            {
                total += Math.Abs(splitOctaves[j]);
            }

            return SplitPenaltyDbPerOctave * total;
        }

        /// <summary>In-place moving average over the psychoacoustic width: 1/3 octave below 100 Hz easing to 1/6 above
        /// 1 kHz. It is what decides how much of a narrow notch counts as a dip.</summary>
        private void PsychoacousticSmooth(double[] values, int first, int last)
        {
            double[] source = scratchSmoothSource;
            Array.Copy(values, first, source, 0, last - first + 1);
            for (int k = first; k <= last; k++)
            {
                int half = Math.Max(1, (int)Math.Round(psychoacousticHalfWidthBins[k]));
                int from = Math.Max(first, k - half);
                int to = Math.Min(last, k + half);
                double sum = 0;
                for (int m = from; m <= to; m++)
                {
                    sum += source[m - first];
                }

                values[k] = sum / (to - from + 1);
            }
        }

        private double SlopeDeviationPenalty()
        {
            double total = 0;
            for (int j = 0; j < channelCount - 1; j++)
            {
                total += SlopeDeviationWeight(lowerSlope[j])
                    + SlopeDeviationWeight(upperSlope[j]);
            }

            return SlopeDeviationPenaltyDb * total;
        }

        private static double SlopeDeviationWeight(int slopeDbPerOctave) =>
            Math.Abs(Math.Log2((double)slopeDbPerOctave / PreferredSlopeDbPerOctave));

        private double FrequencyPlacementPenalty()
        {
            double total = 0;
            for (int j = 0; j < channelCount - 1; j++)
            {
                double fc = crossoverHz[j];
                total += EarSensitivityWeightDb * EarSensitivityBump(fc);

                if (types[j] == types[j + 1])
                {
                    // No CLASS prior here — that one answers which class owns a region and has nothing to say
                    // between two drivers doing the same job. What it does say is that the two of them divide
                    // the band they share, and the division belongs in its middle: left to flatness alone the
                    // lower driver gets squeezed into a sliver at the bottom of its own range.
                    double sharedLow = bands[j + 1].LowHz;
                    double sharedHigh = bands[j].HighHz;
                    if (double.IsFinite(sharedLow) && double.IsFinite(sharedHigh) &&
                        sharedHigh > sharedLow)
                    {
                        total += SharedBandSplitBiasWeightDb
                            * Math.Abs(Math.Log2(fc / Math.Sqrt(sharedLow * sharedHigh)));
                    }

                    continue;
                }

                if (types[j] == DriverType.Subwoofer)
                {
                    double subTop = SensibleRange(DriverType.Subwoofer).HighHz;
                    if (fc < subTop)
                    {
                        total += SubHandoverUpBiasWeightDb * Math.Log2(subTop / fc);
                    }

                    continue;
                }

                double overlapLow = bands[j + 1].LowHz;
                double overlapHigh = bands[j].HighHz;
                if (overlapHigh > overlapLow && fc > overlapLow)
                {
                    double overlapOctaves = Math.Log2(overlapHigh / overlapLow);
                    double octavesAbove = Math.Log2(fc / overlapLow);
                    total += WideOverlapLowBiasWeightDb * overlapOctaves * octavesAbove;
                }

                if (types[j + 1] == DriverType.Midrange &&
                    fc > MidrangeLocalizationThresholdHz)
                {
                    total += MidrangeHandoverLowBiasWeightDb
                        * Math.Log2(fc / MidrangeLocalizationThresholdHz);
                }
            }

            return total;
        }

        private static double EarSensitivityBump(double frequencyHz)
        {
            double center = Math.Sqrt(EarSensitivityLowHz * EarSensitivityHighHz);
            double octavesFromCenter = Math.Log2(frequencyHz / center);
            double z = octavesFromCenter / EarSensitivitySigmaOctaves;
            return Math.Exp(-0.5 * z * z);
        }

        private double Flatness(Complex[] combined)
        {
            int count = evalHigh - evalLow + 1;
            double[] levels = scratchLevels;
            for (int k = evalLow; k <= evalHigh; k++)
            {
                levels[k] = DataHelper.AmplitudeToDecibels(combined[k].Magnitude);
            }

            // Smoothed before anything is read: a coherent sum can put a single-bin notch anywhere and the ear does
            // not hear one. Mean, RMS and dip all come off the same smoothed curve.
            PsychoacousticSmooth(levels, evalLow, evalHigh);
            double mean = 0;
            for (int k = evalLow; k <= evalHigh; k++)
            {
                mean += levels[k];
            }

            mean /= count;
            double sumSquares = 0;
            double worstDip = 0;
            for (int k = evalLow; k <= evalHigh; k++)
            {
                double deviation = levels[k] - mean;
                sumSquares += deviation * deviation;
                if (-deviation > worstDip)
                {
                    worstDip = -deviation;
                }
            }

            return Math.Sqrt(sumSquares / count) + DipPenaltyWeight * worstDip;
        }

        // Overlap = log-frequency integral of peak-normalized responses' product (~1 octave for LR24). See docs/tech/crossover-auto-setup.md#engineering-penalties.
        private double OverlapPenalty(Complex[][] responses)
        {
            double octavesPerBin = 1.0 / GridPointsPerOctave;
            var peaks = new double[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                double peak = 0;
                Complex[] response = responses[i];
                for (int k = evalLow; k <= evalHigh; k++)
                {
                    peak = Math.Max(peak, response[k].Magnitude);
                }

                peaks[i] = peak;
            }

            double total = 0;
            for (int i = 0; i < channelCount; i++)
            {
                if (peaks[i] <= 0)
                {
                    continue;
                }

                for (int m = i + 1; m < channelCount; m++)
                {
                    if (peaks[m] <= 0)
                    {
                        continue;
                    }

                    Complex[] lower = responses[i];
                    Complex[] upper = responses[m];
                    double overlap = 0;
                    for (int k = evalLow; k <= evalHigh; k++)
                    {
                        overlap += lower[k].Magnitude / peaks[i]
                            * (upper[k].Magnitude / peaks[m]);
                    }

                    int distance = m - i;
                    double weight = distance == 1
                        ? 1.0
                        : NonAdjacentOverlapWeight * (distance - 1);
                    total += weight * overlap * octavesPerBin;
                }
            }

            return OverlapPenaltyDbPerOctave * total;
        }

        /// <summary>The low-pass corner of junction <paramref name="j"/>: the corner itself, or half the split below it.</summary>
        private double LowPassHz(int j) => SplitCorner(j, -1);

        /// <summary>The high-pass corner of junction <paramref name="j"/>: the corner itself, or half the split above it.</summary>
        private double HighPassHz(int j) => SplitCorner(j, +1);

        private Complex[] DriverResponse(int i)
        {
            if (driverResponse[i] is { } cached)
            {
                return cached;
            }

            double[] phase = MinimumPhaseOnGrid(curves[i], grid);
            var response = new Complex[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                double db = InterpolateDb(curves[i], grid[k]);
                response[k] = double.IsFinite(db)
                    ? Complex.FromPolarCoordinates(DataHelper.DecibelsToAmplitude(db), phase[k])
                    : Complex.Zero;
            }

            driverResponse[i] = response;
            return response;
        }

        private double SplitCorner(int j, int direction) =>
            SplitCornerOf(crossoverHz[j], splitOctaves[j], direction);

        /// <summary>Where an edge really lands for a corner and an offset. Taken as a pure function because the
        /// safety floors have to be read for a candidate the junction has not been Set to yet: reading them at the
        /// CORNER while a negative offset puts the high-pass an eighth of an octave below it is how a tweeter ends
        /// up crossed 3 dB (24 dB/oct) or 6 dB (48) further into its resonance than the floor believes.</summary>
        private static double SplitCornerOf(double fcHz, double splitOctaves, int direction) =>
            splitOctaves == 0
                ? fcHz
                : RoundToLattice(fcHz * Math.Pow(2.0, direction * splitOctaves / 2.0));

        // Polarity is NOT folded in here: it belongs to the channel, and keeping it out leaves the cache key on the edges alone.
        private Complex[] ChannelUnitResponse(int i)
        {
            (CrossoverFilterFamily Family, double Fc, int Slope)? highPassEdge = i > 0
                ? (junctionFamily[i - 1], HighPassHz(i - 1), upperSlope[i - 1])
                : lowLimitEdge is { } lowLimit
                    ? (lowLimit.Family, lowLimit.FrequencyHz, lowLimit.SlopeDbPerOctave)
                    : null;
            (CrossoverFilterFamily Family, double Fc, int Slope)? lowPassEdge = i < channelCount - 1
                ? (junctionFamily[i], LowPassHz(i), lowerSlope[i])
                : highLimitEdge is { } highLimit
                    ? (highLimit.Family, highLimit.FrequencyHz, highLimit.SlopeDbPerOctave)
                    : null;

            var key = (i, EdgeKey(highPassEdge), EdgeKey(lowPassEdge));
            if (unitCache.TryGetValue(key, out Complex[]? cached))
            {
                return cached;
            }

            Complex[]? highPass = highPassEdge is { } hp
                ? EdgeResponse(hp.Family, hp.Fc, hp.Slope, highPass: true)
                : null;
            Complex[]? lowPass = lowPassEdge is { } lp
                ? EdgeResponse(lp.Family, lp.Fc, lp.Slope, highPass: false)
                : null;

            Complex[] driver = DriverResponse(i);
            var response = new Complex[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                Complex value = driver[k];
                if (highPass != null)
                {
                    value *= highPass[k];
                }
                if (lowPass != null)
                {
                    value *= lowPass[k];
                }

                response[k] = value;
            }

            unitCache[key] = response;
            return response;
        }

        private static long EdgeKey(
            (CrossoverFilterFamily Family, double Fc, int Slope)? edge)
        {
            if (edge is not { } value)
            {
                return -1;
            }

            long frequencyKey = (long)Math.Round(value.Fc * 1000);
            return frequencyKey * 1000 + value.Slope * 10 + (int)value.Family;
        }

        // The filter's own phase, kept rather than discarded: it is what decides a junction, and it is the only thing
        // a split corner changes. Evaluated at the PROCESSOR rate, so the bilinear warp matches the device.
        private Complex[] EdgeResponse(
            CrossoverFilterFamily family,
            double frequencyHz,
            int slope,
            bool highPass)
        {
            long frequencyKey = (long)Math.Round(frequencyHz * 1000);
            var key = (family, slope, frequencyKey, highPass);
            if (edgeCache.TryGetValue(key, out Complex[]? cached))
            {
                return cached;
            }

            var edge = new CrossoverEdge(family, frequencyHz, slope);
            CrossoverSpec spec = highPass
                ? new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge)
                : new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge);
            var response = new Complex[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                response[k] = CrossoverFilter
                    .Response(spec, grid[k], options.ProcessorSampleRateHz);
            }

            edgeCache[key] = response;
            return response;
        }

        private IReadOnlyList<CrossoverProposal> BuildProposals()
        {
            var results = new CrossoverProposal[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                CrossoverEdge? highPass = i > 0
                    ? new CrossoverEdge(
                        junctionFamily[i - 1],
                        Math.Round(HighPassHz(i - 1)),
                        upperSlope[i - 1])
                    : lowLimitEdge;
                CrossoverEdge? lowPass = i < channelCount - 1
                    ? new CrossoverEdge(
                        junctionFamily[i],
                        Math.Round(LowPassHz(i)),
                        lowerSlope[i])
                    : highLimitEdge;
                CrossoverKind kind = (highPass, lowPass) switch
                {
                    (not null, not null) => CrossoverKind.BandPass,
                    (not null, null) => CrossoverKind.HighPass,
                    _ => CrossoverKind.LowPass
                };

                results[i] = new CrossoverProposal(
                    kind,
                    highPass,
                    lowPass,
                    RoundGain(gainDb[i]),
                    invert[i]);
            }

            return results;
        }

    }
}
