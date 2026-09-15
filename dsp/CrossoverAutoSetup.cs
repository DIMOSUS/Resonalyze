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

public sealed record CrossoverProposal(
    CrossoverKind Kind,
    CrossoverEdge? HighPassEdge,
    CrossoverEdge? LowPassEdge,
    double GainDb);

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
public sealed record CrossoverAutoSetupOptions(
    IReadOnlyList<CrossoverFilterFamily> Families,
    double MinCrossoverHz,
    double MaxCrossoverHz,
    bool IndependentSlopes,
    double SampleRateHz,
    double ProcessorSampleRateHz,
    double? SubElevationDb = null)
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

    // Flatness alone rewards wide overlap. See docs/tech/crossover-auto-setup.md#engineering-penalties.
    private const double OverlapPenaltyDbPerOctave = 0.6;

    private const int MinPracticalSlopeDbPerOctave = 12;

    private const int PreferredSlopeDbPerOctave = 24;
    private const double SlopeDeviationPenaltyDb = 0.7;

    private const double NonAdjacentOverlapWeight = 4.0;

    private const double EarSensitivityLowHz = 2_000;
    private const double EarSensitivityHighHz = 4_000;
    private const double EarSensitivitySigmaOctaves = 0.5;
    private const double EarSensitivityWeightDb = 0.5;

    private const double SubHandoverUpBiasWeightDb = 0.6;

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
    private static (double LowHz, double HighHz) JunctionTypeBounds(
        DriverType lower,
        DriverType upper) =>
        (SensibleRange(upper).LowHz, SensibleRange(lower).HighHz);

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

    /// <summary>High-pass-only protection for a driver that crosses with nobody (rear fill, centre, lone sub). See docs/tech/crossover-auto-setup.md#groups-outside-the-chain.</summary>
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
                    priorSigmaMs: half / 2.0);

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

    /// <summary>The plain amplitude sum the optimizer scored, on its log grid; used by the live preview and tests.</summary>
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
        double[] combined = new double[grid.Length];
        for (int channel = 0; channel < channels.Count; channel++)
        {
            CrossoverProposal proposal = proposals[channel];
            double gainLinear = DataHelper.DecibelsToAmplitude(proposal.GainDb);
            var spec = new CrossoverSpec(
                proposal.Kind,
                proposal.LowPassEdge,
                proposal.HighPassEdge);
            for (int k = 0; k < grid.Length; k++)
            {
                double driverDb = InterpolateDb(channels[channel].MagnitudeDb, grid[k]);
                if (double.IsFinite(driverDb))
                {
                    combined[k] += gainLinear
                        * DataHelper.DecibelsToAmplitude(driverDb)
                        * CrossoverFilter.Response(
                            spec, grid[k], processorSampleRateHz).Magnitude;
                }
            }
        }

        var result = new SignalPoint[grid.Length];
        for (int k = 0; k < grid.Length; k++)
        {
            result[k] = new SignalPoint(grid[k], DataHelper.AmplitudeToDecibels(combined[k]));
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

    /// <summary>Coordinate descent over junction frequency/family/slope and channel gain. See docs/tech/crossover-auto-setup.md#optimizer.</summary>
    private sealed class Optimizer
    {
        private readonly CrossoverAutoSetupOptions options;
        private readonly int channelCount;
        private readonly IReadOnlyList<SignalPoint>[] curves;
        private readonly DriverBandEstimate[] bands;
        private readonly DriverType[] types;
        private readonly double[] grid;
        private readonly double[][] driverAmplitude;
        private readonly int evalLow;
        private readonly int evalHigh;

        // Window-edge band limits on the outer channels; not part of the search.
        private readonly CrossoverEdge? lowLimitEdge;
        private readonly CrossoverEdge? highLimitEdge;

        private readonly double[] gainDb;
        private readonly double[] crossoverHz;
        private readonly CrossoverFilterFamily[] junctionFamily;
        private readonly int[] lowerSlope;
        private readonly int[] upperSlope;

        private readonly int? forcedSlope;

        private readonly Dictionary<(CrossoverFilterFamily, int, long, bool), double[]> magnitudeCache =
            new();

        private readonly Dictionary<(CrossoverFilterFamily, int, long), double> groupDelayCache =
            new();

        // Keyed by edge choice; lattice-stable frequencies make it hit on almost every probe after pass one.
        private readonly Dictionary<(int Channel, long HighPassKey, long LowPassKey), double[]> unitCache =
            new();
        private readonly double[][] scratchUnits;
        private readonly double[] scratchCombined;

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
            driverAmplitude = new double[channelCount][];
            for (int i = 0; i < channelCount; i++)
            {
                driverAmplitude[i] = new double[grid.Length];
                for (int k = 0; k < grid.Length; k++)
                {
                    double db = InterpolateDb(curves[i], grid[k]);
                    driverAmplitude[i][k] = double.IsFinite(db)
                        ? DataHelper.DecibelsToAmplitude(db)
                        : 0;
                }
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
            junctionFamily = new CrossoverFilterFamily[channelCount - 1];
            lowerSlope = new int[channelCount - 1];
            upperSlope = new int[channelCount - 1];
            scratchUnits = new double[channelCount][];
            scratchCombined = new double[grid.Length];
        }

        public IReadOnlyList<CrossoverProposal> Solve()
        {
            Descend();
            NormalizeGainsCutOnly();
            return BuildProposals();
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
            if (crossoverHz[j] >= minFc)
            {
                return;
            }

            double raised = Math.Min(options.MaxCrossoverHz, RoundUpToLattice(minFc));
            if (raised >= minFc)
            {
                crossoverHz[j] = Math.Max(crossoverHz[j], raised);
                return;
            }

            int floor = SlopeFloor(last, crossoverHz[j]);
            int? steeper = AllowedSlopes(junctionFamily[j], crossoverHz[j])
                .Where(slope => slope >= floor)
                .Cast<int?>()
                .Min();
            if (steeper is int slope)
            {
                upperSlope[j] = slope;
            }
        }

        /// <summary>Descent winner plus per-junction best options crossed (bounded), one gain pass each. See docs/tech/crossover-auto-setup.md#ranked-search.</summary>
        public List<PoolCandidate> SolvePool(int poolSize)
        {
            Descend();

            var pool = new List<PoolCandidate>();
            var seen = new HashSet<string>();
            void Capture()
            {
                NormalizeGainsCutOnly();
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
                        (option.Family, option.FrequencyHz, option.LowerSlope, option.UpperSlope))
                    .Select(group => group.First())
                    .OrderBy(option => option.Score)
                    .Take(PoolOptionsPerJunction)
                    .ToList();
                if (junctionChoices[j].Count == 0)
                {
                    junctionChoices[j] =
                    [
                        new JunctionOption(
                            junctionFamily[j], crossoverHz[j], lowerSlope[j], upperSlope[j], 0)
                    ];
                }
            }

            var savedGains = (double[])gainDb.Clone();
            var savedFc = (double[])crossoverHz.Clone();
            var savedFamilies = (CrossoverFilterFamily[])junctionFamily.Clone();
            var savedLower = (int[])lowerSlope.Clone();
            var savedUpper = (int[])upperSlope.Clone();
            void Restore()
            {
                savedGains.CopyTo(gainDb, 0);
                savedFc.CopyTo(crossoverHz, 0);
                savedFamilies.CopyTo(junctionFamily, 0);
                savedLower.CopyTo(lowerSlope, 0);
                savedUpper.CopyTo(upperSlope, 0);
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
                    Set(j, choice.Family, choice.FrequencyHz, choice.LowerSlope, choice.UpperSlope);
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
                    $"{Describe(proposal.LowPassEdge)}:{proposal.GainDb:0.0}"));

        private static string Describe(CrossoverEdge? edge) =>
            edge is { } value
                ? $"{value.Family}/{value.FrequencyHz:0}/{value.SlopeDbPerOctave}"
                : "-";

        private void Initialize()
        {
            CrossoverFilterFamily family = PreferredFamily();
            for (int j = 0; j < channelCount - 1; j++)
            {
                double fc = ProposeCrossoverFrequency(
                    curves[j], bands[j], types[j], curves[j + 1], bands[j + 1], types[j + 1]);
                crossoverHz[j] = Math.Clamp(
                    RoundToLattice(fc), options.MinCrossoverHz, options.MaxCrossoverHz);
                junctionFamily[j] = family;
                int slope = forcedSlope ?? SeedSlope(family, crossoverHz[j]);
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

        private int SeedSlope(CrossoverFilterFamily family, double fcHz)
        {
            IReadOnlyList<int> allowed = AllowedSlopes(family, fcHz);
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

        // Crossed bounds (over-tight window, measured/class conflict, neighbour separation) collapse to one pinned frequency.
        private (double Low, double High) JunctionSearchBounds(int j)
        {
            double separation = Math.Pow(2.0, MinJunctionSeparationOctaves);
            double low = Math.Max(options.MinCrossoverHz, bands[j + 1].LowHz);
            double high = Math.Min(options.MaxCrossoverHz, bands[j].HighHz);

            (double typeLow, double typeHigh) = JunctionTypeBounds(types[j], types[j + 1]);

            if (types[j + 1] == DriverType.Tweeter)
            {
                double resonanceHz = TweeterResonanceHz(bands[j + 1].LowHz);
                typeLow = TweeterMinCrossoverHz(resonanceHz, SteepestPracticalSlope());
            }

            // Distortion only tightens: raises a tweeter floor, lowers a lower driver's cap. See docs/tech/crossover-auto-setup.md#distortion-clean-band.
            double distLow = types[j + 1] == DriverType.Tweeter
                ? bands[j + 1].DistortionLowHz
                : double.NaN;
            double distHigh = bands[j].DistortionHighHz;
            double adjLow = double.IsNaN(distLow) ? typeLow : Math.Max(typeLow, distLow);
            double adjHigh = double.IsNaN(distHigh)
                ? typeHigh
                : double.IsNaN(typeHigh) ? distHigh : Math.Min(typeHigh, distHigh);

            if (adjLow <= adjHigh)
            {
                low = Math.Max(low, adjLow);
                high = Math.Min(high, adjHigh);
            }
            else if (typeLow <= typeHigh)
            {
                // Distortion squeezed the window shut: pin to the protective edge instead of relaxing to the class window.
                bool floorRaised = adjLow > typeLow + 1e-9;
                bool capLowered = adjHigh < typeHigh - 1e-9;
                double pinned = floorRaised && !capLowered
                    ? adjLow
                    : capLowered && !floorRaised
                        ? adjHigh
                        : Math.Sqrt(adjLow * adjHigh);
                double clamped = Math.Clamp(
                    pinned, options.MinCrossoverHz, options.MaxCrossoverHz);
                return (clamped, clamped);
            }

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
                double pinned = typeLow <= typeHigh
                    ? Math.Sqrt(typeLow * typeHigh)
                    : crossoverHz[j];
                low = high = Math.Clamp(
                    pinned, options.MinCrossoverHz, options.MaxCrossoverHz);
            }

            return (low, high);
        }

        private void OptimizeJunction(int j)
        {
            (double low, double high) = JunctionSearchBounds(j);

            JunctionOption best = new(
                junctionFamily[j], crossoverHz[j], lowerSlope[j], upperSlope[j], Score());
            foreach (JunctionOption option in EnumerateJunctionOptions(j, low, high))
            {
                if (option.Score < best.Score)
                {
                    best = option;
                }
            }

            Set(j, best.Family, best.FrequencyHz, best.LowerSlope, best.UpperSlope);
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
                int floor = SlopeFloor(i, crossoverHz[junction]);
                List<int> slopes = AllowedSlopes(junctionFamily[junction], crossoverHz[junction])
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
            try
            {
                foreach (double fc in LatticePoints(low, high))
                {
                    int lowerFloor = SlopeFloor(j, fc);
                    int upperFloor = SlopeFloor(j + 1, fc);
                    foreach (CrossoverFilterFamily family in options.Families)
                    {
                        IReadOnlyList<int> slopes = AllowedSlopes(family, fc);
                        if (options.IndependentSlopes)
                        {
                            foreach (int lower in slopes)
                            {
                                if (lower < lowerFloor)
                                {
                                    continue;
                                }

                                foreach (int upper in slopes)
                                {
                                    if (upper < upperFloor)
                                    {
                                        continue;
                                    }

                                    Set(j, family, fc, lower, upper);
                                    yield return new JunctionOption(
                                        family, fc, lower, upper, Score());
                                }
                            }
                        }
                        else if (slopes.Contains(savedLower) && slopes.Contains(savedUpper) &&
                            savedLower >= lowerFloor && savedUpper >= upperFloor)
                        {
                            Set(j, family, fc, savedLower, savedUpper);
                            yield return new JunctionOption(
                                family, fc, savedLower, savedUpper, Score());
                        }
                    }
                }
            }
            finally
            {
                Set(j, savedFamily, savedFc, savedLower, savedUpper);
            }
        }

        private void Set(
            int j,
            CrossoverFilterFamily family,
            double fc,
            int lower,
            int upper)
        {
            junctionFamily[j] = family;
            crossoverHz[j] = fc;
            lowerSlope[j] = lower;
            upperSlope[j] = upper;
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

        // Ideal-alignment amplitude sum.
        private double Score()
        {
            for (int i = 0; i < channelCount; i++)
            {
                scratchUnits[i] = ChannelUnitAmplitude(i);
            }

            Array.Clear(scratchCombined);
            for (int i = 0; i < channelCount; i++)
            {
                double gainLinear = DataHelper.DecibelsToAmplitude(gainDb[i]);
                double[] unit = scratchUnits[i];
                for (int k = 0; k < scratchCombined.Length; k++)
                {
                    scratchCombined[k] += gainLinear * unit[k];
                }
            }

            return Flatness(scratchCombined)
                + OverlapPenalty(scratchUnits)
                + FrequencyPlacementPenalty()
                + SlopeDeviationPenalty();
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

                // Same-class junctions have no class prior; flatness and the post-check decide.
                if (types[j] == types[j + 1])
                {
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

        private double Flatness(double[] combined)
        {
            double mean = 0;
            int count = evalHigh - evalLow + 1;
            for (int k = evalLow; k <= evalHigh; k++)
            {
                mean += DataHelper.AmplitudeToDecibels(combined[k]);
            }

            mean /= count;

            double sumSquares = 0;
            double worstDip = 0;
            for (int k = evalLow; k <= evalHigh; k++)
            {
                double deviation = DataHelper.AmplitudeToDecibels(combined[k]) - mean;
                sumSquares += deviation * deviation;
                if (-deviation > worstDip)
                {
                    worstDip = -deviation;
                }
            }

            return Math.Sqrt(sumSquares / count) + DipPenaltyWeight * worstDip;
        }

        // Overlap = log-frequency integral of peak-normalized responses' product (~1 octave for LR24). See docs/tech/crossover-auto-setup.md#engineering-penalties.
        private double OverlapPenalty(double[][] amplitudes)
        {
            double octavesPerBin = 1.0 / GridPointsPerOctave;
            var peaks = new double[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                double peak = 0;
                double[] amplitude = amplitudes[i];
                for (int k = evalLow; k <= evalHigh; k++)
                {
                    peak = Math.Max(peak, amplitude[k]);
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

                    double[] lower = amplitudes[i];
                    double[] upper = amplitudes[m];
                    double overlap = 0;
                    for (int k = evalLow; k <= evalHigh; k++)
                    {
                        overlap += lower[k] / peaks[i] * (upper[k] / peaks[m]);
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

        private double[] ChannelUnitAmplitude(int i)
        {
            (CrossoverFilterFamily Family, double Fc, int Slope)? highPassEdge = i > 0
                ? (junctionFamily[i - 1], crossoverHz[i - 1], upperSlope[i - 1])
                : lowLimitEdge is { } lowLimit
                    ? (lowLimit.Family, lowLimit.FrequencyHz, lowLimit.SlopeDbPerOctave)
                    : null;
            (CrossoverFilterFamily Family, double Fc, int Slope)? lowPassEdge = i < channelCount - 1
                ? (junctionFamily[i], crossoverHz[i], lowerSlope[i])
                : highLimitEdge is { } highLimit
                    ? (highLimit.Family, highLimit.FrequencyHz, highLimit.SlopeDbPerOctave)
                    : null;

            var key = (i, EdgeKey(highPassEdge), EdgeKey(lowPassEdge));
            if (unitCache.TryGetValue(key, out double[]? cached))
            {
                return cached;
            }

            double[]? highPass = highPassEdge is { } hp
                ? EdgeMagnitude(hp.Family, hp.Fc, hp.Slope, highPass: true)
                : null;
            double[]? lowPass = lowPassEdge is { } lp
                ? EdgeMagnitude(lp.Family, lp.Fc, lp.Slope, highPass: false)
                : null;

            var amplitude = new double[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                double value = driverAmplitude[i][k];
                if (highPass != null)
                {
                    value *= highPass[k];
                }
                if (lowPass != null)
                {
                    value *= lowPass[k];
                }

                amplitude[k] = value;
            }

            unitCache[key] = amplitude;
            return amplitude;
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

        private double[] EdgeMagnitude(
            CrossoverFilterFamily family,
            double frequencyHz,
            int slope,
            bool highPass)
        {
            long frequencyKey = (long)Math.Round(frequencyHz * 1000);
            var key = (family, slope, frequencyKey, highPass);
            if (magnitudeCache.TryGetValue(key, out double[]? cached))
            {
                return cached;
            }

            var edge = new CrossoverEdge(family, frequencyHz, slope);
            CrossoverSpec spec = highPass
                ? new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge)
                : new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge);
            var magnitude = new double[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                magnitude[k] = CrossoverFilter
                    .Response(spec, grid[k], options.ProcessorSampleRateHz).Magnitude;
            }

            magnitudeCache[key] = magnitude;
            return magnitude;
        }

        private IReadOnlyList<CrossoverProposal> BuildProposals()
        {
            var results = new CrossoverProposal[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                CrossoverEdge? highPass = i > 0
                    ? new CrossoverEdge(
                        junctionFamily[i - 1],
                        Math.Round(crossoverHz[i - 1]),
                        upperSlope[i - 1])
                    : lowLimitEdge;
                CrossoverEdge? lowPass = i < channelCount - 1
                    ? new CrossoverEdge(
                        junctionFamily[i],
                        Math.Round(crossoverHz[i]),
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
                    RoundGain(gainDb[i]));
            }

            return results;
        }

    }
}
