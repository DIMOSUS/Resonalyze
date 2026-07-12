using System.Collections.Concurrent;
using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// The driver class a measured response most resembles. Declared low to high in
/// frequency, so ordering channels by this enum orders them along the spectrum.
/// </summary>
public enum DriverType
{
    Subwoofer,
    Woofer,
    Midbass,
    Midrange,
    Tweeter
}

/// <summary>
/// The usable band read from a driver's magnitude response: the outermost
/// frequencies still within the drop threshold of the reference level, the
/// average level inside that band, and the driver class it suggests.
/// </summary>
public sealed record DriverBandEstimate(
    double LowHz,
    double HighHz,
    double LevelDb,
    DriverType SuggestedType);

/// <summary>One wizard input: a channel's raw magnitude curve and its (confirmed) driver type.</summary>
public sealed record AutoSetupSource(
    IReadOnlyList<SignalPoint> MagnitudeDb,
    DriverType Type);

/// <summary>
/// The proposed DSP starting point for one channel: the crossover filters and a
/// cut-only gain. Every field comes out of the magnitude-domain optimizer.
/// </summary>
public sealed record CrossoverProposal(
    CrossoverKind Kind,
    CrossoverEdge? HighPassEdge,
    CrossoverEdge? LowPassEdge,
    double GainDb);

/// <summary>
/// One entry of the ranked wizard search: the per-channel proposals plus the
/// scores that ranked it. <see cref="AchievabilityPenaltyDb"/> is the summed
/// dip-penalized junction loss remaining after the best per-junction delay
/// (measured on the impulse responses; null when no IRs were provided), and
/// <see cref="IsConventional24"/> marks the one candidate built by the dedicated
/// conventional run (every slope forced to 24 dB/oct, Linkwitz-Riley when the
/// user allows it) — the engineering baseline that wins ties. A pool candidate
/// that merely happens to use 24 dB/oct slopes is not conventional. Lower
/// <see cref="TotalScore"/> is better; the list is returned best first.
/// </summary>
public sealed record RankedCrossoverProposal(
    IReadOnlyList<CrossoverProposal> Proposals,
    double MagnitudeScore,
    double? AchievabilityPenaltyDb,
    double TotalScore,
    bool IsConventional24);

/// <summary>
/// The choices the crossover wizard asks for before optimizing: which filter
/// families the optimizer may pick from, the frequency window crossovers must
/// fall inside, and whether the two sides of a junction may take different
/// slopes. With <see cref="IndependentSlopes"/> off, each DRIVER's two shoulders
/// (its high-pass and low-pass) share one slope, so no channel ends up 12 dB/oct
/// on one side and 18 on the other; different drivers stay free to take different
/// slopes. <see cref="SubElevationDb"/> is how far the lowest
/// driver sits above the levelled midrange/tweeter reference in the target-curve
/// gain fit (null uses the measured elevation, i.e. the lowest driver at its raw
/// level); see <see cref="CrossoverAutoSetup.ApplyTargetCurveGains"/>. The sample
/// rate is needed because the optimizer evaluates the exact digital biquad
/// cascades the DSP runs.
/// </summary>
public sealed record CrossoverAutoSetupOptions(
    IReadOnlyList<CrossoverFilterFamily> Families,
    double MinCrossoverHz,
    double MaxCrossoverHz,
    bool IndependentSlopes,
    double SampleRateHz,
    double? SubElevationDb = null)
{
    /// <summary>All families, the full 20 Hz – 20 kHz window, matched slopes.</summary>
    public static CrossoverAutoSetupOptions Default(double sampleRateHz) =>
        new(
            [
                CrossoverFilterFamily.LinkwitzRiley,
                CrossoverFilterFamily.Butterworth,
                CrossoverFilterFamily.Bessel
            ],
            20,
            20_000,
            IndependentSlopes: false,
            sampleRateHz);
}

/// <summary>
/// The analytic part of the crossover wizard. Everything works on smoothed
/// magnitude curves; phase is deliberately ignored — the delay/polarity alignment
/// is a separate step done against the complex sum afterward.
///
/// <para>
/// <see cref="Propose"/> searches per-junction crossover frequency, filter
/// family and slope, plus per-channel cut-only gain, to make the summed magnitude
/// response as flat as the drivers allow. Channels are combined as a plain
/// amplitude sum everywhere — the consistent expression of the design assumption
/// that the later alignment step brings the junction to zero sum loss. How
/// realistic that assumption is for a particular candidate is judged separately:
/// <see cref="ProposeRanked"/> re-ranks the top candidates by the loss actually
/// achievable after the best per-junction delay, measured on the channels'
/// impulse responses with the production alignment search.
/// </para>
/// </summary>
public static class CrossoverAutoSetup
{
    // A band edge is where the response falls this far below the reference
    // level. 8 dB sits between the -6 dB textbook edge and the -10 dB the
    // remaining room ripple of a 1/3-octave-smoothed in-room curve asks for.
    private const double BandEdgeDropDb = 8.0;

    // The proposed crossover keeps at least this margin (octaves) above the
    // upper driver's low edge — the excursion protection — and below the lower
    // driver's high edge.
    private const double CrossoverMarginOctaves = 1.0;

    private const int CrossoverSlopeDbPerOctave = 24;

    /// <summary>
    /// Below this junction frequency, slopes steeper than
    /// <see cref="LowJunctionMaxSlopeDbPerOctave"/> are excluded from the
    /// search: a steep low-frequency crossover carries a large group delay
    /// (many periods of ringing at an already-long period), which is far more
    /// audible than the protection it buys.
    /// </summary>
    public const double SteepSlopeMinimumJunctionHz = 300;

    /// <summary>The steepest slope allowed for junctions below <see cref="SteepSlopeMinimumJunctionHz"/>.</summary>
    public const int LowJunctionMaxSlopeDbPerOctave = 24;

    /// <summary>
    /// The mirror of the low-bass cap, for tweeters: a tweeter crossed below this
    /// frequency must use at least <see cref="CrossoverSlopeDbPerOctave"/> dB/oct,
    /// or a shallow filter lets it play too far down and overexcurt. The placement
    /// heuristics push a capable tweeter's handover low for a better soundstage,
    /// so the steep-slope floor keeps that safe.
    /// </summary>
    public const double TweeterProtectionHz = 2_500;

    // The log-frequency grid the optimizer scores flatness on.
    private const int GridPointsPerOctave = 24;

    // Adjacent crossovers keep at least this separation so a three-way search
    // cannot collapse two junctions onto the same frequency.
    private const double MinJunctionSeparationOctaves = 0.5;

    // Gain refinement: how far around the current value each channel is searched,
    // and the resolution. Cut-only is enforced afterward by referencing the loudest.
    private const double GainSearchRangeDb = 8.0;
    private const double GainSearchStepDb = 0.25;

    // Coordinate-descent passes; it converges well within this on two- and
    // three-way systems, and stops early once a pass stops helping.
    private const int MaxPasses = 6;
    private const double ConvergenceDb = 0.01;

    // A narrow suckout is far more audible than the same energy spread as ripple,
    // so the deepest dip below the mean is added to the RMS flatness score.
    private const double DipPenaltyWeight = 0.5;

    // Ranked search (ProposeRanked): per junction this many of the best
    // (frequency, family, slope) options seed the candidate pool; their cross
    // combinations are scored (bounded) and the top of the pool goes to the
    // impulse-response post-check.
    private const int PoolOptionsPerJunction = 4;
    private const int PoolMaxCombinations = 512;

    // The achievability post-check works on the gated direct sound, so the
    // chains run on a shared crop of the measured IRs instead of the full
    // capture (verified against full-length IRs on real measurements: the
    // 4096-sample evaluation gate sits at the shared peak anchor, so the crop
    // does not change the junction losses).
    private const int PostCheckCropLength = 32_768;
    private const int PostCheckCropPrePeakSamples = 8_192;

    // Per junction the alignment search runs in a window around the RAW
    // channels' band-limited arrival difference (computed once — it is
    // candidate-independent). The half-window absorbs the filter group delay
    // any candidate can add, which scales as 1/fc (an LR24 at 40 Hz rings for
    // ~10 ms, at 4 kHz for ~0.1 ms), so the window shrinks with the junction
    // frequency — a wide window at a high junction would cost thousands of
    // probe deltas across its short periods for nothing. A junction where the
    // search finds no candidate at all is scored with a flat penalty instead
    // of silently winning by absence.
    private const double PostCheckWindowGroupDelayScaleHz = 1_200;
    private const double PostCheckMinHalfWindowMs = 2.0;
    private const double PostCheckMaxHalfWindowMs = 12.0;
    private const double PostCheckMissingJunctionPenaltyDb = 6.0;

    private static double PostCheckHalfWindowMs(double junctionHz) =>
        Math.Clamp(
            PostCheckWindowGroupDelayScaleHz / junctionHz,
            PostCheckMinHalfWindowMs,
            PostCheckMaxHalfWindowMs);

    // How strongly the achievable post-alignment loss weighs against the
    // magnitude flatness score in the final ranking, and how much worse (dB)
    // a challenger must be before it loses to the conventional all-24 dB/oct
    // candidate.
    private const double AchievabilityWeight = 0.5;
    private const double Conventional24PreferenceDb = 0.25;

    /// <summary>
    /// Snaps a crossover frequency to the lattice the wizard proposes on:
    /// 5 Hz steps below 100 Hz, 10 Hz steps below 1 kHz, 50 Hz steps above.
    /// The optimizer searches directly on this lattice, so the scored
    /// frequency IS the proposed frequency.
    /// </summary>
    public static double RoundToLattice(double frequencyHz)
    {
        double step = LatticeStep(frequencyHz);
        return Math.Max(20, Math.Round(frequencyHz / step) * step);
    }

    private static double LatticeStep(double frequencyHz) =>
        frequencyHz < 100 ? 5 : frequencyHz < 1_000 ? 10 : 50;

    // Every lattice frequency inside [low, high]; a window narrower than one
    // lattice step collapses to its clamped, snapped midpoint so degenerate
    // junction bounds still yield exactly one probe.
    private static double[] LatticePoints(double low, double high)
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

    // Pure magnitude flatness is blind to band overlap: shallow filters let
    // adjacent drivers overlap widely, which averages out each other's ripple and
    // reads flat, but an engineer would never do it — wide overlap means lobing,
    // intermodulation and out-of-band excursion. So the score also penalizes how
    // many octaves two adjacent drivers meaningfully overlap (steeper filters =
    // narrower overlap), and the search never goes below a practical slope: a
    // first-order (6 dB/oct) filter protects nothing.
    private const double OverlapPenaltyDbPerOctave = 0.6;
    private const int MinPracticalSlopeDbPerOctave = 24;

    // A driver whose roll-off reaches past its neighbour into a non-adjacent
    // driver's band overlaps where it never should; that overlap is weighted this
    // much heavier per band of distance than an unavoidable adjacent handover, so
    // a too-shallow filter (a 12 dB/oct woofer bleeding up to the tweeter) is
    // pushed to a steeper slope.
    private const double NonAdjacentOverlapWeight = 4.0;

    // A handover in the ear's most sensitive band (2–4 kHz) puts the crossover's
    // phase wobble, lobing and any residual dip right where they are most
    // audible, so a junction landing there is penalized: a soft bump centred on
    // the band's log-centre (~2.83 kHz), full inside and tapering ~an octave to
    // each side. Gentle — a tie-breaker that steers a free handover out of the
    // band, not an override of a genuinely flatter split.
    private const double EarSensitivityLowHz = 2_000;
    private const double EarSensitivityHighHz = 4_000;
    private const double EarSensitivitySigmaOctaves = 0.5;
    private const double EarSensitivityWeightDb = 0.5;

    // A subwoofer wants to hand over where it stops being localizable (~80 Hz),
    // not as low as the flatness search would drag it — a sub crossed at 45 Hz
    // leaves the woofer carrying real bass. So the sub handover is nudged up
    // toward the top of its sensible range.
    private const double SubHandoverUpBiasWeightDb = 0.6;

    // When two adjacent drivers share a wide band, the handover can sit anywhere
    // across it; an engineer crosses low, letting the upper (smaller) driver take
    // over as early as it cleanly can (better dispersion up top, less excursion
    // and breakup demand on the lower driver). So a junction is nudged toward the
    // bottom of the drivers' shared band, the pull scaled by how wide that band
    // is — negligible for a narrow overlap, firm for a broad one.
    private const double WideOverlapLowBiasWeightDb = 0.4;

    /// <summary>
    /// Reads the usable band from a (smoothed) magnitude curve and suggests the
    /// driver class. The reference is an upper percentile of the curve, robust
    /// against both narrow room dips and single peaks.
    /// </summary>
    public static DriverBandEstimate EstimateBand(IReadOnlyList<SignalPoint> magnitudeDb)
    {
        ArgumentNullException.ThrowIfNull(magnitudeDb);

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

        double lowHz = double.NaN;
        double highHz = double.NaN;
        foreach (SignalPoint point in magnitudeDb)
        {
            if (!double.IsFinite(point.Y) || point.Y < threshold)
            {
                continue;
            }

            if (double.IsNaN(lowHz))
            {
                lowHz = point.X;
            }
            highHz = point.X;
        }

        if (double.IsNaN(lowHz) || highHz <= lowHz)
        {
            throw new ArgumentException(
                "The magnitude curve has no usable band.",
                nameof(magnitudeDb));
        }

        double level = AverageLevelDb(magnitudeDb, lowHz, highHz);
        return new DriverBandEstimate(lowHz, highHz, level, Classify(lowHz, highHz));
    }

    // Classifies by the band's log-center, using the geometric midpoints between
    // the neighbouring driver classes' own centers as thresholds. The class only
    // seeds the wizard's suggestion; the user confirms it before optimizing.
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

    // The frequency range a driver of this class may sensibly play — and so the
    // band any crossover involving it must stay inside. A woofer measured in-room
    // still shows output near 850 Hz, but nobody crosses a woofer there; the class
    // caps the search to musically sane handovers.
    private static (double LowHz, double HighHz) SensibleRange(DriverType type) => type switch
    {
        DriverType.Subwoofer => (20, 80),
        DriverType.Woofer => (40, 250),
        DriverType.Midbass => (80, 500),
        // The 200 Hz floor (down from 250) lets the woofer/midbass hand over
        // lower — before its cone-breakup region — when the midrange measures
        // headroom down there; a wide overlap higher up interferes badly, and a
        // midrange crossed low with a steep filter cleans the handover. Still
        // gated by the measured midrange band (one rolled off by 300 Hz crosses
        // no lower).
        DriverType.Midrange => (200, 4_000),
        // A quality tweeter crossed low (with a steep filter) covers more of the
        // critical midrange for a better soundstage; the 1.7 kHz floor lets the
        // search go there, but only when the measured tweeter band supports it —
        // a tweeter that has rolled off by 2.5 kHz still crosses no lower.
        DriverType.Tweeter => (1_700, 20_000),
        _ => (20, 20_000)
    };

    // The band a handover between two adjacent driver classes may sit in: at or
    // below the lower driver's sensible top and at or above the upper driver's
    // sensible bottom. When the classes do not overlap (a "skipped" pairing such
    // as a 2-way woofer + tweeter) the returned low exceeds the high, signalling
    // that no class-sensible band exists and the measured overlap should stand.
    private static (double LowHz, double HighHz) JunctionTypeBounds(
        DriverType lower,
        DriverType upper) =>
        (SensibleRange(upper).LowHz, SensibleRange(lower).HighHz);

    /// <summary>
    /// Builds the crossover proposal for the given channels, honouring the wizard
    /// <paramref name="options"/>. Results are in the input order. Every channel
    /// must carry a distinct driver type — with two identical drivers there is no
    /// crossover to propose.
    /// </summary>
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
        if (channels.Select(channel => channel.Type).Distinct().Count() != channels.Count)
        {
            throw new ArgumentException(
                "Every channel needs a distinct driver type.",
                nameof(channels));
        }

        options = Normalize(options);
        IReadOnlyList<CrossoverProposal> proposals =
            new Optimizer(channels, options).Solve();

        // The optimizer level-matched the drivers to flatten the sum, which is
        // right for choosing the crossovers but not the gains the user wants.
        // Replace them with the car target-curve fit.
        return ApplyTargetCurveGains(
            channels, proposals, options.SampleRateHz, options.SubElevationDb);
    }

    // The slopes a family offers above the practical floor. A shallow crossover
    // (12/18 dB/oct) leaves adjacent drivers overlapping over a wide span where
    // they interfere; on a real system the summed dip that leaves is worse than
    // any group-delay a steeper filter costs, and Auto delay cannot align it
    // away — so the floor is 24 dB/oct, matching how these systems are tuned by
    // hand. (Below SteepSlopeMinimumJunctionHz the slope is also capped at 24 for
    // group delay, pinning low junctions to exactly 24.)
    private static IReadOnlyList<int> PracticalSlopes(CrossoverFilterFamily family) =>
        CrossoverFilter.SupportedSlopes(family)
            .Where(slope => slope >= MinPracticalSlopeDbPerOctave)
            .ToList();

    // The per-driver context the target-curve gain fit needs: each channel's
    // level over its assigned passband (between its crossovers), the reference
    // (levelled midrange/tweeter) level, the bass-anchor channel, and the
    // measured elevation of the bass over the reference.
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
            DriverBandEstimate band = EstimateBand(channels[i].MagnitudeDb);
            double low = proposals[i].HighPassEdge?.FrequencyHz ?? band.LowHz;
            double high = proposals[i].LowPassEdge?.FrequencyHz ?? Math.Min(band.HighHz, ceiling);
            if (high <= low)
            {
                (low, high) = (band.LowHz, band.HighHz);
            }

            levels[i] = AverageLevelDb(channels[i].MagnitudeDb, low, high);
            centers[i] = Math.Sqrt(low * high);
        }

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

        int mid = Find(DriverType.Midrange);
        int tweeter = Find(DriverType.Tweeter);
        var reference = new List<int>();
        if (mid >= 0)
        {
            reference.Add(mid);
        }
        if (tweeter >= 0)
        {
            reference.Add(tweeter);
        }

        // Only a subwoofer is the elevated bass anchor. A woofer/midbass that
        // happens to be the lowest driver (a 2-way without a sub) is a normal
        // driver levelled into the system, not a hot sub to lift.
        int bass = Find(DriverType.Subwoofer);

        // The reference (flat-top) level is the quietest driver apart from the
        // sub, so the whole system is cut to it and the sub is lifted on top.
        // With the sub excluded, a hot woofer never drags the reference up.
        double referenceLevel = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            if (i != bass && levels[i] < referenceLevel)
            {
                referenceLevel = levels[i];
            }
        }

        // The slope runs up to where the flat top begins — the lowest reference
        // member (the midrange when present, else the tweeter); with neither, the
        // highest non-sub driver.
        int slopeTop = reference.Count > 0
            ? reference.MinBy(index => centers[index])
            : Enumerable.Range(0, n)
                .Where(i => i != bass)
                .MaxBy(i => channels[i].Type);

        double measuredElevation = bass < 0
            ? 0
            : Math.Max(0, levels[bass] - referenceLevel);
        return new TargetCurveContext(
            levels, centers, bass, reference, slopeTop, referenceLevel, measuredElevation);
    }

    /// <summary>
    /// The elevation (dB) of the lowest driver over the levelled midrange/tweeter
    /// reference, measured on the given proposal's passbands. This is the default
    /// and the upper limit of the sub-elevation control: the user may only trim it
    /// down (flattening the bottom), never boost past what was measured.
    /// </summary>
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

    /// <summary>
    /// Replaces the gains of an existing proposal with the car target-curve fit,
    /// keeping the crossovers untouched. The midrange and tweeter are levelled to
    /// each other (the louder attenuated); the lowest driver anchors the bass at
    /// <paramref name="subElevationDb"/> above that reference (null = the measured
    /// elevation, i.e. the lowest driver kept at its raw level); the remaining
    /// drivers are fit onto the log-frequency line between those anchors, cut-only
    /// — a driver already below the target keeps its level, so no measured dip is
    /// filled with gain. Every gain is a cut (0 dB on the reference), so the result
    /// is headroom-safe. Proposals are returned in the input order.
    /// </summary>
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

        // With no sub the target is flat at the reference; otherwise it descends
        // from the sub anchor to the reference across log-frequency.
        double TargetAt(double frequencyHz) => hasBass && logSpan > 1e-9
            ? subTarget - elevation * (Math.Log(frequencyHz / subCenter) / logSpan)
            : reference;

        var gains = new double[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            if (context.ReferenceIndices.Contains(i))
            {
                // Level the midrange/tweeter to their quieter member.
                gains[i] = reference - level[i];
            }
            else if (i == context.BassIndex)
            {
                // The bass anchor sits at reference + elevation; cut-only so a
                // sub measured quieter than the reference is never boosted.
                gains[i] = Math.Min(0, subTarget - level[i]);
            }
            else
            {
                // An intermediate driver: onto the target line, cut-only.
                gains[i] = Math.Min(0, TargetAt(center[i]) - level[i]);
            }
        }

        var results = new CrossoverProposal[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            results[i] = proposals[i] with { GainDb = Math.Round(gains[i], 1) };
        }

        return results;
    }

    /// <summary>
    /// The ranked wizard search: expands a pool of up to
    /// <paramref name="candidateCount"/> near-optimal candidates (always
    /// including a conventional all-24 dB/oct one), and — when the channels'
    /// measured impulse responses are provided in the same order — re-ranks
    /// them by the junction loss actually achievable after the best
    /// per-junction delay, using the production alignment search on a shared
    /// crop of the IRs. The conventional candidate wins unless a challenger
    /// beats it by more than a small margin. Returns the candidates best
    /// first; <c>[0].Proposals</c> is the recommended setup.
    /// </summary>
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
        if (channels.Select(channel => channel.Type).Distinct().Count() != channels.Count)
        {
            throw new ArgumentException(
                "Every channel needs a distinct driver type.",
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

        // The conventional candidate: every slope locked to 24 dB/oct,
        // Linkwitz-Riley when the user allows it — what an engineer reaches
        // for first, and the reference the challengers must beat.
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
                // The conventional reference must always reach the post-check;
                // it replaces the worst pool entry when truncation dropped it.
                pool[^1] = conventional!;
            }
        }

        double[]? penalties = null;
        if (impulseResponses != null)
        {
            int sampleRate = (int)Math.Round(options.SampleRateHz);
            int[] orderedIndex = Enumerable.Range(0, channels.Count)
                .OrderBy(index => channels[index].Type)
                .ToArray();
            Complex[][] cropped = CropSharedDirectSoundWindow(
                orderedIndex.Select(index => impulseResponses[index]).ToArray());
            var arrivalCache =
                new ConcurrentDictionary<(int Channel, long BandKey), (double Ms, bool Valid)>();
            penalties = pool
                .AsParallel().AsOrdered()
                .Select(candidate => AchievabilityPenaltyDb(
                    cropped,
                    orderedIndex.Select(index => candidate.Proposals[index]).ToArray(),
                    arrivalCache,
                    sampleRate))
                .ToArray();
        }

        // Only the dedicated conventional run's candidate carries the flag —
        // matched by signature, because a pool candidate that merely landed on
        // all-24 dB/oct slopes (or a Butterworth/Bessel 24 mix) is not the
        // LR24 baseline the tie preference is meant to protect.
        string? conventionalSignature = conventional?.Signature;
        var ranked = pool
            .Select((candidate, index) =>
            {
                double? penalty = penalties?[index];
                return new RankedCrossoverProposal(
                    // The pool ranked with the optimizer's level-matched gains
                    // (right for comparing crossovers); the emitted proposal
                    // carries the car target-curve gains the user applies.
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

        // Ties (within the preference margin) go to the conventional candidate.
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

    // The channels' measured IRs cut to one shared direct-sound window: the
    // post-check only ever evaluates the gated direct sound, so the candidate
    // chains do not need the full capture.
    private static Complex[][] CropSharedDirectSoundWindow(Complex[][] impulseResponses) =>
        VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            impulseResponses, PostCheckCropLength, PostCheckCropPrePeakSamples);

    // The raw channel's band-limited arrival in the given SHARED junction
    // band, cached across candidates. Arrivals from different measuring bands
    // are NOT comparable (each band carries its own driver group delay and
    // envelope rise — the same lesson the Auto delay engine and the stereo Δ
    // metric already encode), so both sides of a junction must be measured in
    // one band; the cache keeps the Hilbert-envelope cost bounded because the
    // pool only ever probes a handful of lattice frequencies per junction.
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

    // The summed dip-penalized loss (positive dB, 0 = perfect handovers) that
    // remains after the delay the production selection policy would pick for
    // this candidate: each channel is processed with the candidate's filters
    // and gain, then every adjacent junction runs the production alignment
    // search — arrival-anchored prior, AlignmentSelection tie-breaks (an
    // inverted half-period impostor must not fake achievability the real
    // Auto delay would refuse) and a widened retry when the pick lands on the
    // window edge. Deliberate simplifications versus the full engine, judged
    // acceptable for RANKING: no PHAT-seeded timeline and no cascade
    // reprocessing of already-settled neighbors (junction deltas of a mono
    // N-way compose independently).
    private static double AchievabilityPenaltyDb(
        Complex[][] croppedOrdered,
        CrossoverProposal[] orderedProposals,
        ConcurrentDictionary<(int Channel, long BandKey), (double Ms, bool Valid)> arrivalCache,
        int sampleRate)
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
                sampleRate);
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

            // Both sides measured in the SAME shared band; unreadable arrivals
            // fall back to an unanchored search over the widest window.
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
            // A pick at the window edge means the true lobe may be cut off;
            // one widened retry, re-selected through the same rules — taking
            // the retried best raw would hand the widened window to exactly
            // the impostor the selection exists to reject.
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

    /// <summary>One entry of the optimizer's candidate pool.</summary>
    internal sealed record PoolCandidate(
        IReadOnlyList<CrossoverProposal> Proposals,
        double MagnitudeScore,
        string Signature);

    /// <summary>
    /// The magnitude-domain summed response the wizard predicts for a proposal,
    /// on the optimizer's own log grid: a plain amplitude sum of the filtered
    /// channels — exactly the curve the optimizer scored, under the same
    /// ideal-alignment assumption. Used for the live preview and by the tests.
    /// </summary>
    public static IReadOnlyList<SignalPoint> SummedResponseDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz)
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
                        * CrossoverFilter.Response(spec, grid[k], sampleRateHz).Magnitude;
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

    // Clamps the wizard options into a usable range: at least one family, a
    // positive window inside the Nyquist limit, min strictly below max.
    private static CrossoverAutoSetupOptions Normalize(CrossoverAutoSetupOptions options)
    {
        if (options.SampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The sample rate must be positive.");
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

    // The shared log-frequency grid: 20 Hz up to 20 kHz (or just under Nyquist),
    // sampled at GridPointsPerOctave.
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

    // Endpoint-clamped interpolation: measured driver curves span the whole audio
    // band, so a grid point outside the measured range means the driver has simply
    // rolled off — holding the endpoint value is the right behaviour, not dropping
    // the point.
    private static double InterpolateDb(IReadOnlyList<SignalPoint> points, double frequencyHz) =>
        CurveSampling.InterpolateDbLog(points, frequencyHz, clampEnds: true);

    // Average level (dB over linear amplitude) of the curve inside a band.
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

    // The crossover for one adjacent pair: where the level-aligned curves
    // intersect inside the overlap region, clamped an octave away from both
    // drivers' band edges; the geometric mean when they never cross. Used to seed
    // the optimizer.
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
            // The lower driver falls while the upper rises, so the aligned
            // difference decreases; the first sign change is the natural handover.
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
                    // Interpolate the zero crossing between the two grid points.
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

        // Excursion protection for the upper driver and headroom for the lower
        // one; contradictory clamps fall back to the plain geometric mean.
        double margin = Math.Pow(2.0, CrossoverMarginOctaves);
        double minimum = upperBand.LowHz * margin;
        double maximum = lowerBand.HighHz / margin;
        crossover = minimum <= maximum
            ? Math.Clamp(crossover, minimum, maximum)
            : Math.Sqrt(upperBand.LowHz * lowerBand.HighHz);

        // Keep the seed inside the range sensible for both driver classes when
        // they overlap, so the initial handover is not up in the lower driver's
        // roll-off skirt (a woofer seeded at 850 Hz). Non-overlapping classes have
        // no such range and keep the measured seed.
        (double typeLow, double typeHigh) = JunctionTypeBounds(lowerType, upperType);
        if (typeLow <= typeHigh)
        {
            crossover = Math.Clamp(crossover, typeLow, typeHigh);
        }

        return Math.Clamp(crossover, 20, 20_000);
    }

    /// <summary>
    /// Coordinate-descent optimizer over one ordered set of drivers. State is the
    /// per-junction crossover frequency + family + slopes and the per-channel gain;
    /// each pass re-tunes every junction then every gain, scoring the summed
    /// magnitude flatness, until a pass stops improving.
    /// </summary>
    private sealed class Optimizer
    {
        private readonly CrossoverAutoSetupOptions options;
        private readonly int channelCount;
        private readonly int[] inputIndex;
        private readonly IReadOnlyList<SignalPoint>[] curves;
        private readonly DriverBandEstimate[] bands;
        private readonly DriverType[] types;
        private readonly double[] grid;
        private readonly double[][] driverAmplitude;
        private readonly int evalLow;
        private readonly int evalHigh;

        // Band-limit edges on the outer channels: a subsonic high-pass on the
        // lowest driver and a brickwall low-pass on the highest, added only where
        // the user narrowed the window past a driver that plays into it. They sit
        // at the window edges and are not part of the search.
        private readonly CrossoverEdge? lowLimitEdge;
        private readonly CrossoverEdge? highLimitEdge;

        private readonly double[] gainDb;
        private readonly double[] crossoverHz;
        private readonly CrossoverFilterFamily[] junctionFamily;
        private readonly int[] lowerSlope;
        private readonly int[] upperSlope;

        // When set, every junction is locked to this slope (the conventional
        // all-24 dB/oct candidate of the ranked search).
        private readonly int? forcedSlope;

        private readonly Dictionary<(CrossoverFilterFamily, int, long, bool), double[]> magnitudeCache =
            new();

        // Unit-gain channel amplitudes (driver × its current edges, gain
        // excluded) keyed by the edge choice, plus scratch buffers: scoring a
        // trial allocates nothing, and the lattice-stable frequencies make the
        // cache hit on almost every probe after the first pass.
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

            var ordered = channels
                .Select((channel, index) => (channel, index))
                .OrderBy(item => item.channel.Type)
                .ToArray();
            inputIndex = ordered.Select(item => item.index).ToArray();
            curves = ordered.Select(item => item.channel.MagnitudeDb).ToArray();
            bands = ordered.Select(item => EstimateBand(item.channel.MagnitudeDb)).ToArray();
            types = ordered.Select(item => item.channel.Type).ToArray();

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

            // A subsonic high-pass / brickwall low-pass is added to the outer
            // channels when the user narrowed the window inside a driver that still
            // plays there — e.g. a 75 Hz lower limit on a woofer that reaches lower
            // gets a 75 Hz high-pass. Leaving the window at the full band adds
            // nothing.
            // The limit only counts when it sits at least a semitone inside the
            // driver edge, so float noise on the band edges and negligible cuts do
            // not sprout a filter.
            double margin = Math.Pow(2.0, 1.0 / 12.0);
            CrossoverFilterFamily limitFamily = PreferredFamily();
            int limitSlope = PreferredSlope(limitFamily);
            lowLimitEdge = options.MinCrossoverHz > bands[0].LowHz * margin
                ? new CrossoverEdge(limitFamily, Math.Round(options.MinCrossoverHz), limitSlope)
                : null;
            highLimitEdge = options.MaxCrossoverHz < bands[channelCount - 1].HighHz / margin
                ? new CrossoverEdge(limitFamily, Math.Round(options.MaxCrossoverHz), limitSlope)
                : null;

            // Flatness is only judged over the interior passband. The outermost
            // drivers' own roll-off skirts (or the band-limit edges, when set) are
            // unavoidable and identical for every candidate, so trimming half an
            // octave inside them keeps that constant floor from swamping the
            // crossover-region ripple the optimizer can actually change.
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
                    // Junction passes hold each channel's slope; this pass tunes
                    // it (both shoulders together), so one driver never ends up
                    // with a 12/18 split while the drivers still differ freely.
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
        }

        /// <summary>
        /// Runs the descent, then expands a pool of near-optimal states: per
        /// junction the best few (frequency, family, slope) options with the
        /// rest of the optimum fixed, crossed over the junctions (bounded),
        /// each combination given one gain re-tune pass. Combinations whose
        /// junctions jointly land closer than the minimum separation are
        /// rejected. Sorted by magnitude score, deduplicated, at most
        /// <paramref name="poolSize"/> entries; the descent winner is always
        /// included.
        /// </summary>
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

            // Mixed-radix enumeration over the per-junction choices; when the
            // full product exceeds the cap, the earliest (best-ranked) choices
            // are covered first.
            long combinations = Math.Min(totalCombinations, PoolMaxCombinations);
            // Each junction's options were bounded against the descent optimum's
            // NEIGHBOURS, so two junctions moved toward each other can jointly
            // land closer than the minimum separation (or even swap order) —
            // e.g. a peaked middle driver pulls both of its junctions inward.
            // The small relative slack keeps float noise from rejecting a combo
            // that sits exactly on a bound.
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
            int slope = forcedSlope ?? PreferredSlope(family);
            for (int j = 0; j < channelCount - 1; j++)
            {
                double fc = ProposeCrossoverFrequency(
                    curves[j], bands[j], types[j], curves[j + 1], bands[j + 1], types[j + 1]);
                crossoverHz[j] = Math.Clamp(
                    RoundToLattice(fc), options.MinCrossoverHz, options.MaxCrossoverHz);
                junctionFamily[j] = family;
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

        // Prefer the family an engineer would reach for first: Linkwitz-Riley, then
        // Bessel, then whatever is left.
        private CrossoverFilterFamily PreferredFamily()
        {
            if (options.Families.Contains(CrossoverFilterFamily.LinkwitzRiley))
            {
                return CrossoverFilterFamily.LinkwitzRiley;
            }

            return options.Families.Contains(CrossoverFilterFamily.Bessel)
                ? CrossoverFilterFamily.Bessel
                : options.Families[0];
        }

        private static int PreferredSlope(CrossoverFilterFamily family)
        {
            IReadOnlyList<int> slopes = PracticalSlopes(family);
            return slopes.Contains(CrossoverSlopeDbPerOctave)
                ? CrossoverSlopeDbPerOctave
                : slopes.MinBy(slope => Math.Abs(slope - CrossoverSlopeDbPerOctave));
        }

        // The slopes the search may actually try at this junction frequency:
        // the family's practical slopes, capped at 24 dB/oct below
        // SteepSlopeMinimumJunctionHz (group delay), or pinned to the forced
        // slope of the conventional-candidate run.
        private IReadOnlyList<int> AllowedSlopes(CrossoverFilterFamily family, double fcHz)
        {
            IReadOnlyList<int> slopes = PracticalSlopes(family);
            if (forcedSlope is int locked)
            {
                return slopes.Contains(locked) ? [locked] : [];
            }

            return fcHz < SteepSlopeMinimumJunctionHz
                ? slopes.Where(slope => slope <= LowJunctionMaxSlopeDbPerOctave).ToList()
                : slopes;
        }

        // The lowest slope a driver of this class may take at a handover of this
        // frequency: a tweeter crossed below TweeterProtectionHz is held to at
        // least 24 dB/oct so it does not play too far down; every other driver
        // keeps the practical floor.
        private static int SlopeFloor(DriverType sideType, double fcHz) =>
            sideType == DriverType.Tweeter && fcHz < TweeterProtectionHz
                ? CrossoverSlopeDbPerOctave
                : MinPracticalSlopeDbPerOctave;

        // Cut-only seed: bring every band down to the quietest, measured over the
        // band it will actually cover with the seeded crossovers.
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

        // The frequency window junction j may search: where both of its drivers
        // actually produce output (inside the requested window), constrained to
        // a band sensible for BOTH driver classes when their ranges overlap (a
        // woofer must not cross up in its roll-off skirt at 850 Hz), and
        // separated from the neighbouring junctions. Crossed bounds (an
        // over-tight user window, a measured/class conflict, or neighbour
        // separation) collapse to one sensible pinned frequency.
        private (double Low, double High) JunctionSearchBounds(int j)
        {
            double separation = Math.Pow(2.0, MinJunctionSeparationOctaves);
            double low = Math.Max(options.MinCrossoverHz, bands[j + 1].LowHz);
            double high = Math.Min(options.MaxCrossoverHz, bands[j].HighHz);

            (double typeLow, double typeHigh) = JunctionTypeBounds(types[j], types[j + 1]);
            if (typeLow <= typeHigh)
            {
                low = Math.Max(low, typeLow);
                high = Math.Min(high, typeHigh);
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

        // Channel i's single slope (both shoulders): its low-pass is the lower
        // side of junction i, its high-pass the upper side of junction i-1.
        private int ChannelSlope(int i) =>
            i < channelCount - 1 ? lowerSlope[i] : upperSlope[i - 1];

        // Writes one slope to both of channel i's shoulders, keeping the
        // per-channel invariant (upperSlope[i-1] == lowerSlope[i]).
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

        // The slopes channel i may take: allowed at every junction it touches
        // (each junction's family and the low-frequency cap). An outer channel
        // has one junction; an interior channel must satisfy both, so a slope
        // one family offers but the neighbour's does not is excluded.
        private IReadOnlyList<int> AllowedChannelSlopes(int i)
        {
            List<int>? allowed = null;
            void Intersect(int junction)
            {
                // Channel i sits on this junction; its own class sets the steep
                // floor (a tweeter crossed low), while the junction frequency and
                // family set the rest.
                int floor = SlopeFloor(types[i], crossoverHz[junction]);
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

        // Coordinate step for one channel's slope (independent slopes off): the
        // low-pass and high-pass move together, so the two shoulders can never
        // disagree. Different channels remain free to pick different slopes.
        private void OptimizeChannelSlope(int i)
        {
            int best = ChannelSlope(i);
            double bestScore = Score();
            foreach (int slope in AllowedChannelSlopes(i))
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

        // Scores the (lattice frequency × family × slope) choices for junction j
        // with the rest of the state fixed. With independent slopes ON, both
        // sides of the junction are free. With it OFF, the slope is a property of
        // the CHANNEL (its two shoulders share one slope, tuned separately in
        // OptimizeChannelSlope), so the junction only varies frequency and
        // family here and holds its two channels' current slopes — a family that
        // cannot supply either held slope is skipped. Restores state afterward.
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
                    // Steep-slope floors for this handover's two sides: a tweeter
                    // crossed low must stay steep (its own class), the lower
                    // driver keeps the practical floor.
                    int lowerFloor = SlopeFloor(types[j], fc);
                    int upperFloor = SlopeFloor(types[j + 1], fc);
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

        // The flatness score is invariant to a global level shift, so the search
        // fixes only relative gains; reference them to the loudest for a cut-only,
        // headroom-safe result.
        private void NormalizeGainsCutOnly()
        {
            double max = gainDb.Max();
            for (int i = 0; i < channelCount; i++)
            {
                gainDb[i] -= max;
            }
        }

        // Plain amplitude sum of the (unit-gain cached) channel responses with
        // the gains applied inline — the ideal-alignment assumption, allocation
        // free. The overlap penalty normalizes per channel, so it reads the
        // unit responses directly and the gains drop out.
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
                + FrequencyPlacementPenalty();
        }

        // Frequency-placement heuristics that depend only on where the junctions
        // sit (not on the summed magnitude): keep handovers out of the ear's
        // 2–4 kHz sensitivity band, and cross low when two drivers share a wide
        // band. Both are gentle nudges added to the flatness score.
        private double FrequencyPlacementPenalty()
        {
            double total = 0;
            for (int j = 0; j < channelCount - 1; j++)
            {
                double fc = crossoverHz[j];
                total += EarSensitivityWeightDb * EarSensitivityBump(fc);

                // The band the two drivers share, and how far above its bottom
                // this junction sits — both in octaves. A narrow overlap barely
                // pulls; a wide one pulls firmly toward the low edge. Skipped for
                // the subwoofer handover: a sub wants to hand over where it stops
                // being localizable (~80 Hz), not as low as it can play.
                if (types[j] == DriverType.Subwoofer)
                {
                    // The sub hands over UP toward the top of its sensible range,
                    // never pulled low.
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
            }

            return total;
        }

        // A soft bump, full over 2–4 kHz and tapering ~an octave to each side,
        // centred on the band's log-centre.
        private static double EarSensitivityBump(double frequencyHz)
        {
            double center = Math.Sqrt(EarSensitivityLowHz * EarSensitivityHighHz);
            double octavesFromCenter = Math.Log2(frequencyHz / center);
            double z = octavesFromCenter / EarSensitivitySigmaOctaves;
            return Math.Exp(-0.5 * z * z);
        }

        // RMS deviation of the summed magnitude from its own mean over the interior
        // passband, plus a heavier weight on the deepest suckout.
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

        // How many octaves drivers meaningfully overlap, summed over every pair.
        // Each driver is normalized to its own passband peak (so gains and levels
        // drop out) and the overlap is the log-frequency integral of the two
        // normalized responses' product — near an octave for a clean LR24
        // handover of adjacent drivers, several octaves for shallow filters.
        // Adjacent overlap is unavoidable at a handover; a shallow roll-off that
        // reaches PAST the neighbour into a non-adjacent driver's band (a 12 dB/oct
        // woofer still audible up at the tweeter) is far worse, so that overlap is
        // weighted much heavier — which pushes such a driver to a steeper slope.
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

        // The channel's driver response × its current edges, WITHOUT the gain:
        // memoized by the edge choice, so re-probing a lattice frequency (and
        // every step of the gain search) reuses the array instead of
        // recomputing the product.
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
                magnitude[k] = CrossoverFilter.Response(spec, grid[k], options.SampleRateHz).Magnitude;
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

                results[inputIndex[i]] = new CrossoverProposal(
                    kind,
                    highPass,
                    lowPass,
                    Math.Round(gainDb[i], 1));
            }

            return results;
        }

    }
}
