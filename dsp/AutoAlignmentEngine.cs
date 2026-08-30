using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// The delay/polarity proposal Auto delay computes for one channel: the
/// absolute delay to apply and whether the channel's polarity should flip.
/// </summary>
public readonly record struct AlignmentOverride(
    double DelayMs,
    bool InvertPolarity);

/// <summary>
/// Qualitative confidence of one automatically decided setting, for the user
/// report. Ordered so that Math.Min/Max express "cap at" / "floor at".
/// </summary>
public enum AlignmentConfidence
{
    Low,
    Medium,
    High
}

/// <summary>
/// What kind of decision set a channel's delay: a free junction search
/// (confidence = the rival margin), a pick pinned by an onset/scene lock
/// (a constraint of the physics or the task — NOT a measure of how the
/// acoustics voted, so it carries no confidence), the fixed reference
/// (nothing was chosen at all), or the stereo bridge (an arrival fit,
/// confidence = the weaker side's SNR).
/// </summary>
public enum AlignmentDecisionKind
{
    Search,
    Locked,
    Reference,
    Bridge
}

/// <summary>
/// How one channel's delay/polarity decision was reached, for the user
/// report: the decision kind, the qualitative confidence where one is
/// meaningful (free searches and the bridge; null for locked and reference
/// channels) and a short human-readable summary (the rival margin and the
/// gates that shaped the pick). Distinct from the diagnostic log, which
/// records everything.
/// </summary>
public sealed record AlignmentDecision(
    AlignmentDecisionKind Kind,
    AlignmentConfidence? Confidence,
    string Detail);

/// <summary>
/// A channel as the alignment engine sees it: an identity (reference
/// equality keys the override maps), a display name for the diagnostic log
/// and a sample rate. The caller's channel model implements this.
/// </summary>
public interface IAlignmentChannel
{
    string Name { get; }

    /// <summary>The MEASUREMENT's rate — the grid every impulse response here lives on.</summary>
    int SampleRate { get; }

    /// <summary>
    /// The rate the simulated processor runs its filters at, which need not be
    /// the measurement's (see <see cref="PreparedDspResponse"/>). The engine
    /// only needs it where it pushes a chain through
    /// <see cref="VirtualCrossoverAnalysis.ApplyChain(System.Numerics.Complex[], DspChannelChain, int, int)"/>;
    /// every other read here is of measured content and belongs to
    /// <see cref="SampleRate"/>.
    /// </summary>
    int ProcessorSampleRate { get; }
}

/// <summary>
/// One channel's processed impulse response for an alignment round, ready for
/// arrival detection and correlation.
/// <see cref="ValidRange"/> is where the MEASURED content sits inside the
/// (delay-shifted, FFT-length-padded) record — the range the
/// <see cref="VirtualCrossoverAnalysis.ApplyChain(System.Numerics.Complex[], DspChannelChain, int, int, out ValidSampleRange)"/>
/// overload reports — so envelope/SNR analyses skip both the delay prefix and
/// the manufactured tail. Empty means unknown: the analyses then fall back to
/// the padding-signature heuristic.
///
/// <see cref="BypassedImpulseResponse"/> is the SAME measurement with no chain
/// (the bare driver in the room) and <see cref="ProcessingChain"/> turns one
/// into the other, so the engine can RE-DERIVE what this channel's front must
/// look like after its own processing (see
/// <see cref="AutoAlignmentEngine.PredictedFrontArrivalMs"/>): a steep
/// crossover concentrates a junction band's energy in the room's modal region
/// and makes the PROCESSED arrival latch onto a mode, while the same band read
/// off the full-range driver still finds the front. Both are immutable for the
/// run — the search may not read live model state — and null when the caller
/// has none, which degrades every path to the upper-half probe alone.
/// </summary>
public sealed record AlignmentSnapshot(
    IAlignmentChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex,
    ValidSampleRange ValidRange = default,
    DspChannelChain? ProcessingChain = null,
    Complex[]? BypassedImpulseResponse = null,
    ValidSampleRange BypassedValidRange = default);

/// <summary>
/// Adjacent channels along the spectrum with their shared junction: the pair
/// crossover frequency and the band (an octave to each side) where the two
/// drivers genuinely overlap. This band is where coarse arrivals are
/// compared and where the fine delay search correlates.
/// </summary>
public sealed record AlignmentJunction(
    AlignmentSnapshot Lower,
    AlignmentSnapshot Upper,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz);

/// <summary>
/// The inputs of a stereo alignment run (see
/// <see cref="AutoAlignmentEngine.ComputeStereo"/>). Mono channels appear in
/// BOTH by-band lists as the same <see cref="IAlignmentChannel"/> instance and
/// are tuned once, by the left pass. The bridge is the highest-frequency pair
/// with sources on both sides; its band is the top channels' own playing band.
/// Left and Right are ROLES, not cabin sides: Left is the reference (the
/// driver's side, settled first), Right the far side fitted to it, and
/// <see cref="SceneOffsetMs"/> is positive when that far side should LEAD
/// (arrive earlier at the microphone) by that much — the "image toward the
/// dash center" convention. A left-hand-drive cabin maps its sides onto the
/// roles directly; a right-hand-drive one hands the plan MIRRORED (its right
/// side as Left), so the same positive offset makes its left side lead.
/// </summary>
public sealed record StereoAlignmentPlan(
    IReadOnlyList<AlignmentSnapshot> LeftChannelsByBand,
    IReadOnlyList<AlignmentJunction> LeftPairs,
    IReadOnlyList<AlignmentSnapshot> RightChannelsByBand,
    IReadOnlyList<AlignmentJunction> RightPairs,
    IReadOnlyCollection<IAlignmentChannel> MonoChannels,
    IAlignmentChannel BridgeLeft,
    IAlignmentChannel BridgeRight,
    double BridgeBandLowHz,
    double BridgeBandHighHz,
    double SceneOffsetMs,
    IReadOnlyList<StereoPairLink>? PairLinks = null);

/// <summary>
/// One L/R driver pair below the bridge, with the band both sides actually
/// share (the intersection of their playing bands). The right descent uses it
/// to aim its gentle prior at the delay that would land the right driver's
/// arrival exactly the scene offset ahead of the left one's — the "Δ" the
/// metric panel verifies afterwards — so a lobe that is a whole period off
/// the other side pays the prior penalty even when its own-side junction sum
/// looks perfect.
/// </summary>
public sealed record StereoPairLink(
    IAlignmentChannel Left,
    IAlignmentChannel Right,
    double BandLowHz,
    double BandHighHz);

/// <summary>
/// Re-runs the caller's channel processing with the given delay/polarity
/// overrides applied (a channel absent from the map processes with zero
/// delay and normal polarity) and returns fresh snapshots. Called from the
/// engine's search loops, typically on a background thread — implementations
/// must not touch shared mutable state.
/// </summary>
public delegate IReadOnlyList<AlignmentSnapshot> AlignmentReprocessor(
    IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides);

/// <summary>
/// Two-stage automatic time alignment of a multi-way system. Stage 1:
/// Time-Alignment-style band-limited first arrivals (PHAT-refined where the
/// peak is trustworthy) give a coarse delay per channel. Stage 2: walking
/// pair by pair outward from the reference, a summation-loss search
/// fine-tunes each channel against its settled neighbor inside their shared
/// pair band, also deciding whether its polarity should flip. At sharp-front
/// junctions the stage-2 window is locked to the drivers' broadband IR
/// onsets (see the onset-lock constants), so the loss metric can only polish
/// within the physically correct lobe. The latest-arriving channel is the
/// fixed reference, so the proposed delays stay non-negative by
/// construction.
/// </summary>
public static class AutoAlignmentEngine
{
    // Bounds of the stage-2 fine-search span. The span scales with the
    // crossover frequency (half its period) because the coarse arrival error
    // grows with the period, but never drops below half a millisecond: arrival
    // estimates carry a floor of error (filter group-delay asymmetry, driver
    // rise time) that does not shrink with the junction period, so at a high
    // split half a period would regularly miss the true optimum. The extra
    // lobes a wide window admits are handled by the candidate list, the arrival
    // prior, and the physical tie-break in AlignmentSelection.
    private const double MinFineAlignmentRangeMs = 0.5;
    // The fixed cap suffices at short-period (mid/high) junctions. At a LOW
    // junction a whitened correlation with too few in-band periods can seed the
    // window a half period off, parking it on a (flip + half-period) impostor
    // whose true opposite-polarity partner then sits beyond this reach — hence
    // LowJunctionReachFraction lifts the effective cap there.
    private const double MaxFineAlignmentRangeMs = 2.5;

    // The fraction of a half period the fine window may reach at a low junction
    // (where a half period exceeds the fixed cap). Just under 1 so the window
    // captures the half-period-away flip partner without spanning the
    // full-period same-polarity lobe on the far side; the arrival prior and the
    // AlignmentSelection tie-breaks resolve what remains.
    private const double LowJunctionReachFraction = 0.97;

    /// <summary>
    /// How far past the MEASURED distance to the seed's opposite-polarity
    /// partner a trusted junction's fine window may reach, so the partner's own
    /// loss optimum lands inside it as an interior point rather than pinned to
    /// the edge (an edge pin triggers the retry path instead of a comparison).
    /// </summary>
    private const double SeedPartnerReachFactor = 1.2;

    /// <summary>
    /// And the ceiling on that reach, in crossover periods: three quarters sits
    /// between the polarity partner a half period out — which the window must
    /// contain — and the same-polarity rival a full period out, which it must
    /// not. The same bound, for the same reason, as
    /// <see cref="OnsetLockReachPeriods"/>.
    /// </summary>
    private const double SeedPartnerMaxReachPeriods = 0.75;

    // The delay ceiling an AUTO DELAY proposal may reach — tighter than the
    // manual UI range (100 ms, since the Virtual DSP may model any hardware).
    // Car processors cap per-channel delay in the tens of milliseconds (~17 m
    // of path here), so a proposal past this could never be transferred to a
    // device. Real cabin spans run well under 10 ms: this is the feasibility
    // gate, not an operating region.
    private const double MaxDelayMs = 50;

    // A deliberately wide fine-search window (many periods at a high crossover,
    // ~one at a low one). Its candidates are always logged, surfacing summation
    // optima several lobes outside the working window; at a junction the onset
    // lock does not govern, the promotion path below may also CHOOSE from them.
    private const double DiagnosticFineRangeMs = 3.0;
    private const double DiagnosticCorrelationRangeMs = 3.0;

    // The wide diagnostic sweep must reach past the flip partner half a period
    // out even at a LOW junction, where the fixed millisecond span above is
    // sub-period — otherwise the [diag] line (and the promotion pool at
    // un-locked junctions) cannot contain the optimum it exists to surface.
    // 1.25 half periods clears that partner with margin; mid/high junctions
    // keep the fixed span, already many periods there.
    private const double DiagnosticFineReachHalfPeriods = 1.25;

    // The stage-1 correlation window in periods of the pair crossover. The
    // peak-vs-trough dominance gate below is only meaningful when BOTH polarity
    // partners are complete lobes inside the window, and the arrival estimate
    // the window centers on can itself sit up to a half period off at a low
    // junction — so the window must hold at least a full period to each side. A
    // window edge that cuts the rival lobe understates it, and the dominance
    // gate then passes on a truncated number. Mid/high junctions stay on the
    // fixed floor (±3 ms already spans several periods there).
    private const double SeedCorrelationWindowPeriods = 1.25;

    // The stage-1 / diagnostic correlation half-window for a junction: the
    // fixed floor, grown with the crossover period at low junctions so both
    // polarity partners fit as whole lobes.
    private static double SeedCorrelationRangeMs(double crossoverHz) =>
        Math.Max(
            DiagnosticCorrelationRangeMs,
            SeedCorrelationWindowPeriods * 1000.0 / crossoverHz);

    // How far from the arrival estimate a trusted seed extremum may sit: half a
    // period — the next same-polarity lobe is a full period out, so that is the
    // no-cycle-skip bound — floored at the FIXED ±3 ms span rather than the
    // grown window. At a low junction the grown window may SEE farther lobes
    // but must never hand one to the timeline.
    private static double SeedReachMs(double crossoverHz) =>
        Math.Max(DiagnosticCorrelationRangeMs, 500.0 / crossoverHz);

    // The minimum |r| of the dominant PHAT extremum (peak or trough — the seed
    // uses only its POSITION, polarity stays with the loss search) for it to
    // seed the stage-2 window instead of the arrival envelope. Below it the
    // extremum is noise (a low junction with too few in-band periods) and the
    // arrival estimate stands. Deliberately low: even a modest genuine extremum
    // beats the arrival envelope, and a seed that lands a little off is
    // recovered downstream — by the onset lock at sharp junctions, by the loss
    // search and the wide-window promotion below it.
    private const double PhatSeedMinCoefficient = 0.15;

    /// <summary>
    /// How far below its own band's energy a first arrival may be picked and
    /// still be allowed to VETO a whitened extremum for disagreeing with it
    /// (<see cref="TimeAlignmentAnalysisResult.FirstArrivalProminenceDecibels"/>,
    /// which is 0 dB when the arrival IS the band's strongest peak).
    /// <para>
    /// The arrival detector searches 25 dB below the band maximum on purpose:
    /// a soft direct rise sitting under a strong in-room modal build-up is a
    /// real front, and reading the mode instead would time the channel whole
    /// periods late. But a pick that deep is not the same KIND of feature the
    /// whitened correlation reads. The correlation is driven by where the
    /// band's ENERGY is; an arrival 23.5 dB down sits 14.5 ms ahead of it (a
    /// field 50-110 Hz subwoofer, its front at 8.7 ms against a body at
    /// 23.2 ms). Comparing that front against a neighbour whose own arrival IS
    /// its band peak measures the difference between two questions, not a
    /// delay — and the reach veto then refuses an extremum at r 0.95, which the
    /// direct-sound cut confirms at r 0.93, for disagreeing with it.
    /// </para>
    /// <para>
    /// Half the detector's own search depth: a pick in the upper half of the
    /// range is a shoulder on the band's energy and speaks for it, one in the
    /// lower half is a separate feature. The gate only ever WITHDRAWS the
    /// anchor's veto, never moves it — the seed, the window centre, the prior
    /// and the fallback are untouched, and the extremum still has to pass its
    /// own trust gates.
    /// </para>
    /// </summary>
    private const double SeedVetoMinProminenceDb = -12.5;

    /// <summary>
    /// The margin by which the seed extremum must beat the SAME-POLARITY rival
    /// one period over before its position is trusted. This is the whole-period
    /// cycle skip — the error the fine window cannot undo, since its reach is
    /// capped below a period — so it keeps a gate of its own.
    ///
    /// The peak-vs-trough margin does NOT. It used to gate the seed by the same
    /// figure, and measurement killed that: on a PERFECT synthetic junction —
    /// two filters off one impulse, no room, no noise, perfectly aligned — the
    /// peak/trough margin is 0.167 at a two-octave band and falls to 0.100 /
    /// 0.049 / 0.012 at 1.5 / 1.0 / 0.5 octaves, at every fc, family and slope.
    /// It measures how wide the analysed band is, not whether the extremum can
    /// be believed, and the old 0.1 therefore demanded 60 % of what a flawless
    /// junction can even produce: across the archived cabins it refused 34 of
    /// 40 junctions, including every one whose extremum the owner's hand tune
    /// then landed on. What it flags — a half-period ambiguity — is also the
    /// one the fine window spans and the loss search settles by polarity, so
    /// the peak-vs-trough figure now only informs the log.
    ///
    /// The rival margin is a different statistic on the same scale: the perfect
    /// junction shows 0.534 of it, so 0.05 is a tenth of the ideal rather than
    /// the old 0.1 = a fifth. Field: the archived cabins' true rivals sit
    /// 0.09-0.53 apart, and the junctions that fail this gate are the ones with
    /// no separated structure at all.
    /// </summary>
    private const double PhatSeedMinRivalDominance = 0.05;

    /// <summary>
    /// The direct-cut seed witness runs at junctions this high. The full-record
    /// PHAT correlates everything the record holds, and at a mid/tweeter junction
    /// most of that is the cabin: across the archived cabins its dominant extremum
    /// sat 3.4-4.7 periods from the owner's hand tune in HALF of the 1.3-2.9 kHz
    /// junction cells — while passing every trust gate above (r, dominance, reach),
    /// so the gates cannot catch it. The same PHAT on the direct-sound cuts
    /// (<see cref="VirtualCrossoverAnalysis.CutDirectSoundPair"/>: 1-2 periods
    /// behind each front — the drivers, not the room) read r 0.58-0.96 with zero
    /// catastrophic misses on the same cells, reproduced two owner tunes to 6 us
    /// on the junction that exposed this, and held its position to ~10 us across
    /// record rates. Below ~1 kHz the cut does not isolate a wavefront (the same
    /// physics as <see cref="DirectCoherenceMinCrossoverHz"/>) and the witness
    /// stays out — the sub/bass junctions belong to the arrival envelope and the
    /// modal-latch machinery, which the same bench measured as the better seeds
    /// there.
    /// </summary>
    private const double DirectSeedMinCrossoverHz = 1000;

    /// <summary>
    /// The minimum |r| of the direct-cut extremum for the witness to speak at all.
    /// Far above <see cref="PhatSeedMinCoefficient"/> deliberately: the cuts hold a
    /// couple of periods of wavefront, so an honest pair correlates strongly there
    /// (0.58 was the weakest field value) and a middling coefficient means the cut
    /// caught reflections after all.
    /// </summary>
    private const double DirectSeedMinCoefficient = 0.5;

    /// <summary>
    /// When the trusted full-record extremum and the direct-cut extremum disagree
    /// by more than half a period, the seed goes to whichever position carries the
    /// higher JOINT support — the smaller of the two surfaces' |r| within a quarter
    /// period — by at least this margin; otherwise the full-record extremum stands
    /// (the least change). Field calibration: the four contested cells split 0.24
    /// vs 0.02, 0.32 vs 0.02, 0.28 vs 0.20 and 0.13 vs 0.02 toward the owner's
    /// lobe, while the one cell where the full extremum was right read 0.51 vs
    /// 0.35 the other way — and the near-ties (0.03, 0.05) sat within one lobe
    /// pair, where either pick's stage-2 window covers the truth.
    /// </summary>
    private const double DirectSeedJointTieMarginR = 0.05;

    /// <summary>
    /// How far from the arrival estimate the FULL-RECORD extremum may sit, in
    /// junction periods, while the direct-cut witness offers a usable seed of its
    /// own. <see cref="SeedReachMs"/>'s fixed 3 ms floor is sized for low
    /// junctions and amounts to four or five periods at a mid/tweeter split,
    /// where it therefore vetoes nothing. Field: across the archived cabins the
    /// full-record extrema that agreed with the owner's tune sit within 1.15
    /// periods of the arrival, while the phantoms grown by correlated cabin
    /// reflections sit from 1.66 out (to 3.9) — so a period and a half separates
    /// them with room on both sides.
    /// </summary>
    private const double DirectSeedTrustReachPeriods = 1.5;

    /// <summary>
    /// How far two corners may sit apart (Hz) and still count as ONE crossover
    /// for <see cref="FilterPolarityPreferenceDb"/>. A hair of tolerance only —
    /// the corners come from the same UI fields and are typed, not measured, so
    /// they either match or the split was deliberately staggered.
    /// </summary>
    private const double MatchedSplitToleranceHz = 0.5;

    /// <summary>
    /// How much better the inverted sum of the two channels' FILTERS must be,
    /// across the junction band, before the search expects the pair to be
    /// relatively inverted (see <see cref="ExpectsRelativeInversion"/>). The
    /// question is not close for the shapes that matter — an odd-order
    /// Linkwitz-Riley (LR12, LR36) or a Butterworth 12 puts the two filters
    /// exactly 180° apart at the corner, so one polarity nulls where the other
    /// sums — and the margin exists only so that a junction whose filters say
    /// nothing (Butterworth 18's 90°, a channel with no crossover at all, an
    /// asymmetric pair of corners) keeps the historical in-phase expectation
    /// rather than flipping on rounding noise.
    /// </summary>
    private const double ExpectedInversionMarginDb = 1.0;

    // The sub-precedence margin: at a junction with the shared mono sub, a
    // near-tie between the comb lobe that leaves the sub TRAILING the stack and
    // the one that leaves it LEADING is not acoustically resolvable, but it is
    // perceptually one-sided. The first wavefront binds the bass to the
    // localizable midbass transient (precedence effect), so a slightly leading
    // sub reads as "bass up front" while a trailing one reads as sluggish and
    // detached. The margin sits above the near-tie scale and just under the
    // ~1.4 dB comb-noise ceiling measured between real lobes: within that
    // ceiling an in-room mode can flatter either side by as much, so the
    // psychoacoustics decide; beyond it the summation stands. Owner-calibrated
    // on the v3 cabin, where the leading lobe scored 0.66-0.73 dB under the
    // trailing pick yet was the one that localized the bass to the front stage.
    private const double SubPrecedenceMarginDb = 1.0;

    // Candidates within this of the envelope anchor count as neither leading
    // nor trailing: the preference re-decides genuine lobe choices, not the
    // sub-millisecond polish around the envelope-aligned point.
    private const double SubPrecedenceSlackMs = 0.5;

    // How much better (in score dB) a wide-window optimum must be before it
    // unseats the arrival-anchored fine pick, at a junction the onset lock does
    // not govern (below its frequency gate, or a smeared front): there the
    // window is still centered on the coarse arrival, which can sit a whole
    // lobe off. The margin keeps the physically-minimal arrival pick unless a
    // distinctly better summation exists elsewhere. Field-calibrated: comb noise
    // between real lobes runs up to ~1.4 dB (a false hop offered 1.40 dB) while
    // a real envelope error shows as ~2 dB across the whole basin (a genuine
    // recovery offered 1.91 dB). A distance-scaled ramp cannot separate those
    // two points at any slope; this flat threshold does.
    private const double WideWindowPromotionMarginDb = 1.6;

    // The gain above which a declined promotion is worth a log line: below it
    // the wide window merely confirmed the arrival pick.
    private const double PromotionNoteworthyGainDb = 0.2;

    // How far (in crossover periods) the promotion may move the pick away from
    // the arrival-anchored fine result. It exists to recover a coarse arrival
    // that landed a lobe or two off at a degenerate junction (a spectral gap
    // between the corners degrades the whitened correlation into near-equal
    // lobes), which needs up to ~2 periods of reach. Beyond that the summation
    // surface is a comb of near-equal minima one period apart, differing by
    // fractions of a dB: which lobe is physically correct is set by the arrival,
    // NOT by the sum, and an uncapped window lets a marginally-better ALIAS
    // ~3.9 periods out win on a 0.25 dB "gain". 2.5 periods clears the
    // legitimate recovery and rejects the alias. It also bounds the far-alias
    // inflation from the wide window's weaker arrival prior.
    private const double PromotionReachPeriods = 2.5;

    // ---- The onset lock -----------------------------------------------------
    // At a high junction the summation surface is a comb of near-equal minima
    // and fractions of a dB cannot choose a lobe; the band-limited arrival that
    // anchors the search marks the first PEAK of an octave-band envelope, and
    // the two drivers occupy opposite halves of that shared band, so their
    // peak times lag their true fronts by different rise times — a measured
    // ~0.3-0.4 ms systematic bias (0.45-0.8 periods at 1.5-2.3 kHz) that
    // regularly parks the anchor between lobes for the sum to finish the miss.
    // The broadband threshold onset (EstimateBroadbandOnset) marks the front
    // itself — the same feature a human validates on the IR plot — so where
    // the front is sharp the search is LOCKED to it: the window IS
    // onset-anchor ± the reach below, every escape hatch (edge retry, wide
    // promotion) stays shut, and the sum's only job is polishing inside the
    // correct lobe and choosing polarity.

    // The slowest junction whose fronts are still sharp enough to lock on.
    // Field data: at 1.5-2.3 kHz the 10-vs-50 % onset spread is ~0.3 period
    // (locks engage); at 220 Hz it is milliseconds (thresholds land on modal
    // build-up, not a front) and at 80 Hz there is no front at all — those
    // junctions keep the arrival-anchored search unchanged.
    private const double OnsetLockMinCrossoverHz = 700;

    // The lock's half-window in crossover periods. It must admit the true lobe
    // given the onset estimate's own error (~0.3 period) plus the crossover's
    // legitimate per-driver group-delay split (fractions of a period), and the
    // flip partner half a period out so the polarity decision stays with the
    // invert rules — while excluding the next same-polarity lobe a full period
    // out. 0.75 sits between those bounds.
    private const double OnsetLockReachPeriods = 0.75;

    // The honesty gate: the onset DIFFERENCE between the two drivers is read at
    // 10/25/50 % thresholds, and the lock engages only when those three
    // readings agree within this many periods. A sharp direct front keeps them
    // within ~0.3 period; a smeared or reflection-led front (off-axis driver,
    // modal bass) spreads them and the lock stands down rather than pin the
    // search to a guess.
    private const double OnsetLockMaxSpreadPeriods = 0.5;

    // Junction measurements no longer share one gate anchor: every response
    // is windowed at its own band-limited front and the cuts meet in one
    // absolute-time frame (see BuildAlignmentBins in
    // VirtualCrossoverAnalysis, and the gate remarks there for the full
    // placement history — system peak, pair peak, pair front, chain-free
    // front were each measured on the archived cabins before per-channel
    // traveling windows made the shared choice moot).

    /// <summary>
    /// The direct-coherence witness: at a junction where the summation score
    /// cannot separate a lobe from its POLARITY partner (thin spectral
    /// overlap ties them within hundredths of a dB — split corners like
    /// 1500/1700 at 48 dB/oct leave half an octave of usable overlap), the
    /// whitened correlation of the two channels' DIRECT sound
    /// (<see cref="VirtualCrossoverAnalysis.CutDirectSound"/>) still
    /// separates them: r at the lobe measures how well the direct
    /// wavefronts' phase SLOPE matches across the whole overlap, which the
    /// flip partner — phase-equivalent at the corner only — cannot fake. The
    /// ear sides with this witness at these junctions: fusion and
    /// localization above the bass follow the direct wavefront (precedence),
    /// and on the reference car the owner's hand tune sits on the lobe this
    /// figure prefers (r 0.89 against 0.81) while the summation score reads
    /// the two as a 0.04 dB tie.
    ///
    /// The witness only arbitrates a TIE between polarity partners; a score
    /// preference beyond <see cref="DirectCoherenceTieMarginDb"/> stands,
    /// because the score reads the whole windowed sum the cabin will
    /// actually produce. It stands down where "direct sound" is not a
    /// measurable notion (below <see cref="DirectCoherenceMinCrossoverHz"/>
    /// two crossover periods of cut span the room's own build-up — the
    /// archived sub junctions read high r at delays whole periods away, room
    /// geometry wearing a coherence figure), where a lock or policy already
    /// pinned the lobe by stronger evidence, and where the coherence itself
    /// is weak or the advantage inside its own noise. Field calibration
    /// (2026-08-20, six cabins): genuine mid/tweeter discriminations read
    /// |r| 0.74-0.96 with advantages from 0.08 up; 0.6 / 0.05 sit under
    /// those with room to spare while a coherence-free junction (Passat C/D
    /// reads r 0.07 at its current lobe) cannot vote at all.
    /// </summary>
    private const double DirectCoherenceMinCrossoverHz = 120;
    private const double DirectCoherenceTieMarginDb = 0.3;
    private const double DirectCoherenceMinR = 0.6;
    private const double DirectCoherenceMinAdvantage = 0.05;

    /// <summary>
    /// The second opinion on a mid/tweeter lobe: where the direct-sound
    /// correlation's advantage is itself slim, the arrival-coherence ladder
    /// votes, and a decisive vote for the STANDING lobe vetoes the swap.
    /// <para>
    /// The two witnesses read different things. The correlation is one
    /// whitened comb over the pair's whole band, so it carries the polarity,
    /// but its neighbouring lobes differ by very little; the ladder resolves
    /// the band into sub-band probes, each re-cutting the direct sound at its
    /// own scale, so it cannot see polarity at all (see the remarks by
    /// <see cref="VirtualCrossoverAnalysis.ArrivalCoherencePoint"/>) but it
    /// can say how many bands actually want the upper channel where a
    /// candidate puts it. A slim correlation advantage together with a clear
    /// disagreement across the bands is the one combination where the comb is
    /// deciding on noise.
    /// </para>
    /// <para>
    /// Only above <see cref="LadderVetoMinCrossoverHz"/>, where the ladder's
    /// windows are short enough that a band's optimum is its driver's
    /// wavefront rather than a cabin mode — the ladder's own remarks disclaim
    /// low junctions for exactly that reason. Only bands the ladder itself
    /// calls coherent vote, and the vote must carry
    /// <see cref="LadderVetoMinBandMargin"/> bands: field calibration
    /// (2026-08-27) has the archive's high junctions splitting 4 bands to 1
    /// where the veto belongs (v3 L, whose swap the correlation asked for on a
    /// 0.07 advantage while the summation score reads the two lobes 0.05 dB
    /// apart) and 3 to 2 where it does not (v6 L, where the correlation is
    /// decisive at 0.11 and never reaches this gate).
    /// </para>
    /// </summary>
    private const double LadderVetoMaxAdvantage = 0.10;
    private const double LadderVetoMinCrossoverHz = 1000;
    private const int LadderVetoMinBandMargin = 2;

    /// <summary>
    /// The minimum envelope peak-to-noise grade (dB) both channels' onset
    /// estimates must carry before the lock trusts them. The spread gate alone
    /// cannot refuse a noise-only record: random crossings can look stable
    /// across the three thresholds. A pure-noise Hilbert envelope grades its
    /// strongest excursion ~13-14 dB over the record's quiet quarter (the
    /// Rayleigh peak factor at this crop length), while real loopback
    /// measurements run 40 dB and far beyond — 20 dB separates the two with
    /// margin on both sides. Public so tests assert against the same figure.
    /// </summary>
    public const double OnsetLockMinimumSnrDb = 20;

    /// <summary>
    /// Runs the two-stage alignment. <paramref name="channelsByBand"/> holds
    /// the initial snapshots ordered along the spectrum;
    /// <paramref name="pairs"/>[i] joins channels i and i+1 of that order.
    /// Results land in <paramref name="alignment"/>; the run's diagnostic
    /// trace is appended to <paramref name="log"/>. Previous delay/polarity
    /// settings play no part: the caller produces the initial snapshots with
    /// zero overrides and the engine computes an absolute proposal.
    /// </summary>
    public static void Compute(
        IReadOnlyList<AlignmentSnapshot> channelsByBand,
        IReadOnlyList<AlignmentJunction> pairs,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        ArgumentNullException.ThrowIfNull(channelsByBand);
        ArgumentNullException.ThrowIfNull(alignment);
        RequireOneSampleRate(channelsByBand);
        // An absolute proposal, per the contract: stale entries would otherwise
        // leak into the neighbor-base and reprocess reads and skew the result.
        alignment.Clear();
        decisions?.Clear();
        Compute(channelsByBand, pairs, reprocess, alignment, log,
            onsetLocks: null, decisions);
        NormalizeAndVerifyFeasibility(channelsByBand.ToList(), alignment, log);
        NormalizePolarityPresentation(channelsByBand, alignment, log);
    }

    // Presentation-only normalization: a GLOBAL polarity flip changes no
    // relation — every junction, the scene and the sum see both of their ends
    // flipped together — so when a proposal inverts MORE channels than it
    // keeps, the whole field is flipped and the same physics reads as the
    // minimal set of Invert switches. An inverted sub/stack relation is thus
    // presented as ONE inverted sub, not three inverted stack channels.
    private static void NormalizePolarityPresentation(
        IReadOnlyList<AlignmentSnapshot> scope,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
    {
        List<IAlignmentChannel> channels = scope
            .Select(item => item.Channel)
            .Distinct()
            .ToList();
        int inverted = channels.Count(
            channel => alignment.GetValueOrDefault(channel).InvertPolarity);
        if (inverted * 2 <= channels.Count)
        {
            return;
        }

        foreach (IAlignmentChannel channel in channels)
        {
            AlignmentOverride over = alignment.GetValueOrDefault(channel);
            alignment[channel] = over with
            {
                InvertPolarity = !over.InvertPolarity
            };
        }
        log.AppendLine(
            $"  polarity presentation: flipped every channel ({inverted} of " +
            $"{channels.Count} were inverted) — a global flip changes no relation.");
    }

    /// <summary>
    /// The verdict of the arrival honesty probe: a full-band read judged
    /// against the same record's upper-half read. LATCHED — the full band times
    /// a feature far LATER than its own upper half (a modal latch), so the read
    /// is garbage. UNVERIFIED — no certificate either way: a read is
    /// unmeasurable/low-SNR, or the PROBE itself timed a far later feature
    /// (weak in-band HF leaves it blind to the front); the full read stays
    /// usable but earns no tight scene lock. VERIFIED — the two agree within
    /// the dispersion one wavefront can show. One classification shared by the
    /// cross-side links, the donor certificates and the stereo bridge, so the
    /// three cannot drift apart. Public because the manual Time Alignment mode
    /// surfaces the same verdict on its bandpass-windowed reads (see
    /// <see cref="TimeAlignmentAnalysis.ProbeArrivalHonesty"/>).
    /// </summary>
    public enum ArrivalCertificate
    {
        Unverified,
        Latched,
        Verified
    }

    /// <summary>
    /// An ESTIMATE of where this channel's processed arrival should land if
    /// it timed the direct front: its arrival read off the BYPASSED
    /// (chain-free) response, plus the shift its own chain applies to a flat
    /// reference impulse in the same band. Null when the caller supplied no
    /// bypassed response or no chain, or when the bypassed read is
    /// unmeasurable.
    ///
    /// An estimate, not an identity. The arrival detector is a nonlinear
    /// envelope search, so a chain's shift measured on an impulse does not
    /// transfer exactly to a shaped front: across a source matrix the error
    /// stays inside a quarter of the conviction threshold for realistic driver
    /// roll-offs but reaches 1.2 allowances where the source has strong
    /// structure INSIDE the band — a steep low-pass leaving the channel barely
    /// radiating there, or an all-pass twisting its phase. That is why nothing
    /// here convicts on its own: see
    /// <see cref="PredictedArrivalConvictionFactor"/>, which keeps a shaping
    /// error out of the LATCHED verdict, and
    /// <see cref="PredictionState.Inconsistent"/>, which withdraws a pair from
    /// the predictor rather than trusting a disagreement it cannot explain.
    ///
    /// The bypassed read is what makes this see through a modal latch. A steep
    /// crossover leaves a junction band's energy concentrated in the room's
    /// modal region, so the PROCESSED envelope can front on a mode; off the
    /// full-range driver the same band still finds the real front. The bypassed
    /// response is deliberately NOT gated: at a low junction one period is
    /// 6-12 ms, so any gate short enough to exclude a mode 10 ms out also
    /// truncates the driver's own front and biases the read by milliseconds
    /// (measured 2.5 ms on the archived midbass — enough to hand the junction
    /// to the flip impostor).
    ///
    /// The chain term is MEASURED, not derived: a reference impulse is pushed
    /// through the real ApplyChain and read with the real detector (see
    /// ChainArrivalShiftMs). An analytic band-averaged group delay misses by
    /// 3.8 ms on a steep 80 Hz high-pass and 2.3 ms on a 330 Hz all-pass, and
    /// its error is not common-mode across a junction, so it would inject a
    /// differential error into the very difference the timeline stores.
    /// </summary>
    internal static double? PredictedFrontArrivalMs(
        AlignmentSnapshot side,
        double bandLowHz,
        double bandHighHz)
    {
        ArgumentNullException.ThrowIfNull(side);
        if (side.BypassedImpulseResponse is not { } bypassed ||
            side.ProcessingChain is not { } chain)
        {
            return null;
        }

        int sampleRate = side.Channel.SampleRate;
        int processorRate = side.Channel.ProcessorSampleRate;
        TimeAlignmentAnalysisResult bare =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                bypassed, sampleRate, bandLowHz, bandHighHz,
                side.BypassedValidRange);
        if (!bare.IsValid || bare.SignalToNoiseDecibels < MinimumArrivalSnrDb)
        {
            return null;
        }

        // The reference impulse is as long as the MEASURED content, not as long
        // as the padded bypassed array: that array is itself an ApplyChain
        // output, so its length is already double the crop, and handing it on
        // would run the shift through a window twice the one the real reads
        // use. At this length ApplyChain reports exactly the ranges production
        // sees.
        int contentLength = side.BypassedValidRange.IsKnown
            ? side.BypassedValidRange.EndSample - side.BypassedValidRange.StartSample
            : bypassed.Length;
        return bare.FirstArrivalDelayMilliseconds +
            ChainArrivalShiftMs(chain, sampleRate, processorRate, bandLowHz,
                bandHighHz, contentLength, side.PeakIndex);
    }

    // How much later the band-limited arrival detector reads a signal once
    // this chain is applied — measured by pushing a reference impulse through
    // the REAL ApplyChain and reading it with the REAL detector, exactly as
    // the processed response is read. The chain WITHOUT its bulk delay and
    // polarity: the snapshots the timeline reads are the override-free
    // reprocess, where both are neutral, and neither moves an envelope
    // arrival anyway.
    private static double ChainArrivalShiftMs(
        DspChannelChain chain,
        int sampleRate,
        int processorSampleRate,
        double bandLowHz,
        double bandHighHz,
        int length,
        int peakIndex)
    {
        var impulse = new Complex[length];
        impulse[Math.Clamp(peakIndex, 0, length - 1)] = Complex.One;
        // BOTH reads go through ApplyChain and carry the ValidSampleRange it
        // reports, exactly as the measured responses do. Analyzing the bare
        // impulse raw would hand the detector a different window — it crops to
        // the reported range when given one and falls back to a padding
        // heuristic when not — so the two ends of this subtraction would not be
        // measured alike.
        Complex[] bareResponse = VirtualCrossoverAnalysis.ApplyChain(
            impulse, DspChannelChain.Identity, sampleRate, processorSampleRate,
            out ValidSampleRange bareRange);
        Complex[] filteredResponse = VirtualCrossoverAnalysis.ApplyChain(
            impulse,
            chain with { DelayMs = 0, InvertPolarity = false },
            sampleRate,
            processorSampleRate,
            out ValidSampleRange filteredRange);
        double bare = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            bareResponse, sampleRate, bandLowHz, bandHighHz, bareRange)
            .FirstArrivalDelayMilliseconds;
        double filtered = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            filteredResponse, sampleRate, bandLowHz, bandHighHz, filteredRange)
            .FirstArrivalDelayMilliseconds;
        return filtered - bare;
    }

    /// <summary>
    /// The arrival honesty probe's dispersion allowance for ONE side: how much
    /// later than its own upper-half read the full-band read may sit before
    /// <see cref="ClassifyArrival"/> convicts it of a modal latch.
    ///
    /// Two terms. The first is generic physics — half a period at the probe's
    /// lower edge, never tighter than 1 ms: the smear one wavefront can show
    /// across the band. The second is the channel's OWN chain: a steep low-pass
    /// puts the full band's energy BELOW its corner, where the chain runs
    /// milliseconds slower than in the probe band above it, so the two envelope
    /// fronts differ with no room mode anywhere. A field midbass under
    /// LP 200 Hz/36 read 13.04 ms in 100-400 Hz against 10.16 ms in its
    /// 200-400 Hz half — a 2.88 ms skew a flat 2.5 ms allowance would convict.
    ///
    /// That second term is the PREDICTED SKEW, the difference of two
    /// <see cref="PredictedFrontArrivalMs"/> readings, so it credits how much
    /// later the full band should read for BOTH reasons — the chain's
    /// dispersion and the driver's own band-dependence (a woofer reads later at
    /// 100-200 Hz than at 200-400 Hz on its own account). The probe's question
    /// is how much later the full band should read absent a mode, and the
    /// driver is part of the answer; an averaged group delay (the chain term
    /// alone) over-credits an HP- or PEQ-fed channel by more than a
    /// millisecond, and over-crediting an allowance is how a real latch slips
    /// through. Clamped at zero: a response FASTER in the full band than in the
    /// probe band earns no credit, it just cannot tighten the floor.
    /// </summary>
    internal static double ArrivalProbeToleranceMs(
        AlignmentSnapshot side,
        double measuredMs,
        double probeMeasuredMs,
        double bandLowHz,
        double probeLowHz,
        double bandHighHz)
    {
        ArgumentNullException.ThrowIfNull(side);
        double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
        // The credit is a DIFFERENCE of two predictions, so BOTH have to have
        // earned it against their own measured read. Grading only one leaves
        // the other free to be arbitrarily wrong while the difference still
        // looks plausible: a 220 Hz all-pass source verified in 40-160 Hz and
        // still earned the whole clamped credit against an honest 0.04 ms skew.
        // A side graded INCONSISTENT is withdrawn from the predictor for
        // anchoring and must be withdrawn here too, or the fallback is not
        // independent of the estimator it fell back from — at 100-400 Hz an
        // uncredited 4 ms skew is a latch and a fully credited one is not. A
        // LATCHED side earns nothing either: its own read is under suspicion.
        // Both predictions come back through the out parameter — each costs an
        // ApplyChain, an FFT and an arrival analysis.
        if (GradeAgainstPrediction(
                side, measuredMs, bandLowHz, bandHighHz, out double full) !=
            PredictionState.Verified ||
            GradeAgainstPrediction(
                side, probeMeasuredMs, probeLowHz, bandHighHz,
                out double probe) != PredictionState.Verified)
        {
            return toleranceMs;
        }

        // Capped at the generic allowance, so the ESTIMATE can at most double
        // the physics. Both predictions read a bypassed response that may itself
        // carry the mode, so an uncapped credit grows with exactly the feature
        // the probe is trying to convict — measured uncapped, a 220 Hz all-pass
        // source earned 7.79 ms against an honest skew of 0.04 ms and swallowed
        // a genuine late mode whole. Nothing else guards this: the credit is
        // added to the tolerance directly, with no conviction factor.
        return toleranceMs + Math.Clamp(full - probe, 0, toleranceMs);
    }

    /// <summary>
    /// What the predicted-arrival probe concluded about one side.
    /// UNAVAILABLE — no prediction could be formed (no bypassed response, no
    /// chain, or an unmeasurable read), so the side carries no certificate at
    /// all. LATCHED — the processed read sits LATER than the prediction by
    /// more than the allowance: it is timing something the direct front cannot
    /// explain. INCONSISTENT — the read is EARLIER than the prediction by more
    /// than the allowance, which the prediction cannot account for either (a
    /// truncated front, a mis-captured bypassed response); the read is not
    /// convicted, but neither is it certified. VERIFIED — the two agree, and
    /// only then does the anchor count as independently confirmed.
    /// </summary>
    internal enum PredictionState { Unavailable, Verified, Latched, Inconsistent }

    /// <summary>
    /// The prediction's own accuracy floor (ms) — the disagreement an honest
    /// read may still show, from the estimator's imperfect transfer to a
    /// shaped front and from the detector's own resolution. Measured across
    /// two cabins, honest reads land well inside it while the field latches
    /// run 6.7 ms and up.
    /// </summary>
    private const double PredictedArrivalAccuracyMs = 2.5;

    /// <summary>
    /// How far PAST the allowance a read must sit before the prediction may
    /// convict it. The prediction measures the chain's contribution on a flat
    /// reference impulse, but a driver worked well below its own passband does
    /// not present the chain with a flat input — a midbass playing a 40-160 Hz
    /// junction band sits past its own rolloff, and its high-pass then costs
    /// several milliseconds more than the same filter costs an impulse. The
    /// error is systematic, one-signed (the prediction reads early) and does not
    /// vanish with a better estimator, so a marginal exceedance is not evidence.
    ///
    /// The field separates cleanly: across both cabins and every crossover
    /// corner the FALSE convictions landed between 1.01 and 1.17 allowances and
    /// every true modal latch between 2.49 and 3.89. Two allowances sits in that
    /// gap with room on both sides — the same "plainly, not marginally" standard
    /// the lobe gates apply. A read in between is INCONSISTENT: not convicted,
    /// but not trusted to certify an anchor either.
    /// </summary>
    private const double PredictedArrivalConvictionFactor = 2.0;

    /// <summary>
    /// The comb arbitration for the conviction factor's DEAD ZONE. The factor
    /// exists because a prediction's own shaping error can reach 1.2
    /// allowances, so a single witness may not convict below 2.0 — but at a
    /// bass junction the allowance is ~half the crossover period, which puts
    /// conviction at a FULL period: exactly where a modal latch lands. A read
    /// in that zone (later than its prediction by more than one allowance,
    /// short of two) used to withdraw the pair silently, and the upper-half
    /// probe cannot examine a steeply low-passed reference that has nothing
    /// above the corner — the archived Passat v2 sub read 26.8 ms against a
    /// predicted 13.6 (1.7 allowances) and its latch anchored the whole
    /// junction a period late. The whitened correlation of the pair breaks
    /// the tie: it reads WHERE the two channels' shared content actually
    /// aligns, so its strongest lobe within half a period of the
    /// prediction-implied lag, beating the strongest within half a period of
    /// the measured-implied lag by a real margin, is the second independent
    /// witness a sub-conviction-strength discrepancy needs (on the Passat v2
    /// junction: r 0.91 at the predicted family against 0.81 at the measured
    /// one). The floor and the advantage mirror the direct-coherence
    /// witness's field calibration; both lags must be measurable or the
    /// arbitration stands down and the zone withdraws the pair as before.
    /// </summary>
    private const double LatchArbitrationMinR = 0.6;
    private const double LatchArbitrationMinAdvantage = 0.05;

    /// <summary>
    /// How far a processed read may sit from
    /// <see cref="PredictedFrontArrivalMs"/> before the side loses its
    /// certificate: half a period at the band's geometric center — one
    /// wavefront's dispersion, stated where the band's energy actually is —
    /// never below the prediction's own accuracy.
    /// </summary>
    internal static double PredictedArrivalAllowanceMs(
        double bandLowHz, double bandHighHz) =>
        Math.Max(
            PredictedArrivalAccuracyMs,
            500.0 / Math.Sqrt(bandLowHz * bandHighHz));

    /// <summary>
    /// Grades one side's processed arrival against what its own front, read
    /// through its own chain, estimates it must be. Two-sided on purpose: a read
    /// EARLIER than the prediction is not a modal latch, but it is not a
    /// confirmation either, and treating it as one would hand a tightened seed
    /// reach to an anchor that nothing verified.
    /// </summary>
    internal static PredictionState GradeAgainstPrediction(
        AlignmentSnapshot side,
        double measuredMs,
        double bandLowHz,
        double bandHighHz,
        out double predictedMs)
    {
        predictedMs = double.NaN;
        if (PredictedFrontArrivalMs(side, bandLowHz, bandHighHz)
            is not { } predicted)
        {
            return PredictionState.Unavailable;
        }

        predictedMs = predicted;
        double errorMs = measuredMs - predicted;
        double allowanceMs = PredictedArrivalAllowanceMs(bandLowHz, bandHighHz);
        if (errorMs > allowanceMs * PredictedArrivalConvictionFactor)
        {
            return PredictionState.Latched;
        }

        return Math.Abs(errorMs) <= allowanceMs
            ? PredictionState.Verified
            : PredictionState.Inconsistent;
    }

    internal static ArrivalCertificate ClassifyArrival(
        TimeAlignmentAnalysisResult full,
        TimeAlignmentAnalysisResult probe,
        double toleranceMs)
    {
        if (!full.IsValid ||
            full.SignalToNoiseDecibels < MinimumArrivalSnrDb ||
            !probe.IsValid ||
            probe.SignalToNoiseDecibels < MinimumArrivalSnrDb)
        {
            return ArrivalCertificate.Unverified;
        }

        double skewMs = full.FirstArrivalDelayMilliseconds
            - probe.FirstArrivalDelayMilliseconds;
        if (skewMs > toleranceMs)
        {
            return ArrivalCertificate.Latched;
        }
        if (-skewMs > toleranceMs)
        {
            return ArrivalCertificate.Unverified;
        }
        return ArrivalCertificate.Verified;
    }

    // Every cross-channel figure in the engine — correlation lags, junction
    // bands, per-sample delays — assumes ONE sample rate: the searches read a
    // neighbor's IR with the searched channel's rate, so mixed rates would
    // silently misscale frequencies and delays rather than fail.
    private static void RequireOneSampleRate(
        IEnumerable<AlignmentSnapshot> channels)
    {
        int? sampleRate = null;
        foreach (AlignmentSnapshot snapshot in channels)
        {
            if (sampleRate == null)
            {
                sampleRate = snapshot.Channel.SampleRate;
            }
            else if (snapshot.Channel.SampleRate != sampleRate)
            {
                throw new ArgumentException(
                    "All channels must share one sample rate; " +
                    $"found {sampleRate} and {snapshot.Channel.SampleRate} Hz. " +
                    "Resample the measurements to a common rate first.");
            }
        }
    }

    // One onset-locked junction: which channel the lock was applied to during
    // its fine search, how far the chosen delay landed from the onset-aligned
    // anchor, and the lock's half-window. The co-move consumes this so its
    // shared per-pair delta cannot push a locked junction's front gap past the
    // cap the fine search honored.
    private sealed record OnsetLockState(
        IAlignmentChannel SearchedChannel,
        double GapMs,
        double CapMs);

    private static void Compute(
        IReadOnlyList<AlignmentSnapshot> channelsByBand,
        IReadOnlyList<AlignmentJunction> pairs,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<AlignmentJunction, OnsetLockState>? onsetLocks,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null,
        IReadOnlyCollection<IAlignmentChannel>? monoChannels = null)
    {
        ArgumentNullException.ThrowIfNull(channelsByBand);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(reprocess);
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(log);
        if (channelsByBand.Count < 2)
        {
            throw new ArgumentException(
                "At least two channels are required.",
                nameof(channelsByBand));
        }
        if (pairs.Count != channelsByBand.Count - 1)
        {
            throw new ArgumentException(
                "One junction is required between each adjacent channel pair.",
                nameof(pairs));
        }

        List<AlignmentSnapshot> byBand = channelsByBand.ToList();
        AppendCorrelationAlignmentDiagnostics(log, pairs);

        Dictionary<IAlignmentChannel, double> timeline =
            BuildArrivalTimeline(
                byBand, pairs, log,
                out HashSet<AlignmentJunction> untrustedSeeds,
                out Dictionary<AlignmentJunction, double> seedPartnerReach);

        // The relatively latest channel is the fixed reference; everyone else is
        // delayed toward it, so the coarse deltas are non-negative.
        double latest = timeline.Values.Max();
        IAlignmentChannel reference =
            timeline.First(pair => pair.Value == latest).Key;
        log.AppendLine($"Reference: {reference.Name}");
        if (decisions != null)
        {
            // The latest-arriving channel is the fixed anchor everyone else
            // aligns to — there is no search whose robustness could be judged.
            decisions[reference] = new AlignmentDecision(
                AlignmentDecisionKind.Reference, Confidence: null,
                "reference (others align to it)");
        }

        // Stage 2: sequential pairwise fine alignment, walking outward from the
        // reference along the band order. Each channel is phase-correlated
        // against its already-settled neighbor only, inside their shared pair
        // band, so the search window is sized by THAT junction — a mid channel
        // must not have its low-junction window squeezed to the period of its
        // high junction. An arrival error at a low junction then propagates
        // through the chain and moves the whole upper group together, which a
        // per-channel search against all fixed channels at once cannot do.
        int referenceIndex = byBand.FindIndex(item => item.Channel == reference);
        for (int i = referenceIndex - 1; i >= 0; i--)
        {
            AlignChannelAtJunction(
                byBand[i].Channel, byBand[i + 1].Channel, pairs[i],
                timeline, byBand, reprocess, alignment, log,
                untrustedSeedJunctions: untrustedSeeds,
                seedPartnerDistanceMs: seedPartnerReach,
                onsetLocks: onsetLocks,
                decisions: decisions,
                monoChannels: monoChannels);
        }
        for (int i = referenceIndex + 1; i < byBand.Count; i++)
        {
            AlignChannelAtJunction(
                byBand[i].Channel, byBand[i - 1].Channel, pairs[i - 1],
                timeline, byBand, reprocess, alignment, log,
                untrustedSeedJunctions: untrustedSeeds,
                seedPartnerDistanceMs: seedPartnerReach,
                onsetLocks: onsetLocks,
                decisions: decisions,
                monoChannels: monoChannels);
        }
    }

    // Stage 1: coarse offsets from band-limited first arrivals, refined by the
    // GCC-PHAT peak where it is trustworthy. Arrivals of different drivers are
    // only comparable inside a SHARED band — a woofer's envelope in its own low
    // band rises milliseconds later than a tweeter's in its high band. So each
    // adjacent pair is measured around its own crossover frequency, and the
    // pairwise differences chain into one relative timeline. Only the
    // differences matter downstream, so the anchor value of the first channel
    // is arbitrary (zero).
    /// <summary>
    /// Whether the junction's own FILTERS ask for the two channels to be
    /// relatively inverted — computed from the crossover settings alone, with no
    /// measurement involved: the two chains' responses are summed across the
    /// junction band in both relative polarities, and the inverted sum has to
    /// win by <see cref="ExpectedInversionMarginDb"/>.
    /// <para>
    /// This is the piece the search could not see before. A crossover's summing
    /// polarity is a property of its order: LR24 and LR48 sum in phase, LR12 and
    /// LR36 sum INVERTED (their filters sit 180° apart at the corner), Butterworth
    /// 12 likewise. Where the filters are that explicit, an inverted junction is
    /// the crossover working as designed, and the invert preference in
    /// <see cref="AlignmentSelection"/> — written for "a flipped driver is a
    /// wiring fault worth several dB" — has to defend the OTHER polarity, or it
    /// spends its 0.5 dB defending the null. Measured on the v6 cabin with the
    /// mid/tweeter split set to LR36: on the correct alignment the in-phase sum
    /// runs 4.8-5.8 dB below the inverted one, yet once each polarity is allowed
    /// its own optimum delay the gap between them is only 0.28-0.49 dB, so the
    /// undefended margin decided it — one side of the same cabin came out right
    /// and the other wrong.
    /// </para>
    /// <para>
    /// The gain, delay and polarity of each chain are neutralized first: gain
    /// would weight one channel's filter over the other's, and delay and polarity
    /// are exactly what the search is about to decide. What remains is the
    /// crossover (and any PEQ, which does bend phase and belongs here).
    /// </para>
    /// </summary>
    private static bool? ExpectsRelativeInversion(AlignmentJunction pair)
    {
        double preferenceDb = FilterPolarityPreferenceDb(pair);
        if (double.IsNaN(preferenceDb) ||
            Math.Abs(preferenceDb) <= ExpectedInversionMarginDb)
        {
            // Either the filters do not pose the question (a staggered split,
            // a channel with no crossover) or they answer it with a shrug
            // (Butterworth 18's 90°, where neither polarity nulls). Both are
            // the search's to decide, and must not be confused with a matched
            // split that genuinely asks for IN PHASE.
            return null;
        }

        return preferenceDb > 0;
    }

    /// <summary>
    /// <see cref="ExpectsRelativeInversion"/>'s figure: how much better (dB) the
    /// junction's own two filters sum AT THE CORNER when one channel is inverted.
    /// Positive means the crossover asks for the flip. NaN — "the filters say
    /// nothing" — whenever the question is not well posed.
    /// <para>
    /// It is well posed only for a MATCHED split: the lower channel's low-pass
    /// and the upper channel's high-pass sharing family, corner and slope. Then
    /// the pair is one crossover, its summing polarity is a designed property of
    /// the order (LR12 and LR36 sum inverted, LR24 and LR48 in phase; Butterworth
    /// 12 and 36 inverted, 24 and 48 in phase, 18 indifferent at 90°), and it is
    /// read at the corner where both halves are at −6 dB and the answer is exact.
    /// </para>
    /// <para>
    /// Any other arrangement returns NaN, and deliberately so. A split with two
    /// different corners or slopes has no single phase relation to state: its
    /// filters overlap across a region rather than crossing at a point, and its
    /// best relative delay is not zero — so summing them AS THEY STAND answers a
    /// question nobody asked. Measured: the v2 cabin's 1500/1900 Hz Butterworth
    /// 36 split reads "inverted, +1 dB" that way, while the real junction, each
    /// polarity allowed its own optimum delay, prefers IN PHASE by 3.3 dB at the
    /// owner's setting. Same for the LR24-against-LR48 splits in the 3RC and
    /// Passat cabins. Those junctions keep the historical in-phase expectation
    /// and are decided, as before, by the summation search.
    /// </para>
    /// </summary>
    private static double FilterPolarityPreferenceDb(AlignmentJunction pair)
    {
        if (pair.Lower.ProcessingChain?.Crossover is not { } lowerCrossover ||
            pair.Upper.ProcessingChain?.Crossover is not { } upperCrossover ||
            lowerCrossover.LowPassEdge is not { } lowPass ||
            upperCrossover.HighPassEdge is not { } highPass ||
            lowerCrossover.Kind is not (CrossoverKind.LowPass or CrossoverKind.BandPass) ||
            upperCrossover.Kind is not (CrossoverKind.HighPass or CrossoverKind.BandPass))
        {
            return double.NaN;
        }

        // One crossover, or two filters that merely meet? Only the first has a
        // polarity to speak of.
        if (lowPass.Family != highPass.Family ||
            lowPass.SlopeDbPerOctave != highPass.SlopeDbPerOctave ||
            Math.Abs(lowPass.FrequencyHz - highPass.FrequencyHz) >
                MatchedSplitToleranceHz)
        {
            return double.NaN;
        }

        int rate = pair.Lower.Channel.ProcessorSampleRate;
        double cornerHz = 0.5 * (lowPass.FrequencyHz + highPass.FrequencyHz);
        Complex low = CrossoverFilter.Response(
            new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: lowPass),
            cornerHz,
            rate);
        Complex high = CrossoverFilter.Response(
            new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: highPass),
            cornerHz,
            rate);
        double same = (low + high).Magnitude;
        double inverted = (low - high).Magnitude;
        return same > 0 && inverted > 0
            ? 20.0 * Math.Log10(inverted / same)
            : double.NaN;
    }

    private static Dictionary<IAlignmentChannel, double> BuildArrivalTimeline(
        IReadOnlyList<AlignmentSnapshot> byBand,
        IReadOnlyList<AlignmentJunction> pairs,
        StringBuilder log,
        out HashSet<AlignmentJunction> untrustedSeedJunctions,
        out Dictionary<AlignmentJunction, double> seedPartnerDistanceMs)
    {
        // Junctions whose coarse seed fell back to the arrival envelope because
        // the PHAT peak was untrusted (a low junction with too few in-band
        // periods): the coarse offset ACROSS such a junction can be a half period
        // off, so aligning its two channels is allowed a half-period window (see
        // LowJunctionReachFraction) to admit the true lobe. Keyed by junction,
        // not channel — the uncertainty is a property of the lower/upper
        // RELATION, so the wider window fires whichever side the walk arrives
        // from and never leaks onto a channel's OTHER, phat-trusted junction.
        untrustedSeedJunctions =
            new HashSet<AlignmentJunction>(ReferenceEqualityComparer.Instance);
        // And, for the TRUSTED ones, how far the seed's opposite-polarity
        // partner was measured to sit — the reach the fine window owes it.
        seedPartnerDistanceMs =
            new Dictionary<AlignmentJunction, double>(
                ReferenceEqualityComparer.Instance);
        var timeline = new Dictionary<IAlignmentChannel, double>
        {
            [byBand[0].Channel] = 0
        };
        foreach (AlignmentJunction pair in pairs)
        {
            TimeAlignmentAnalysisResult lowerRead =
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    pair.Lower.ImpulseResponse,
                    pair.Lower.Channel.SampleRate,
                    pair.BandLowHz,
                    pair.BandHighHz,
                    pair.Lower.ValidRange);
            TimeAlignmentAnalysisResult upperRead =
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    pair.Upper.ImpulseResponse,
                    pair.Upper.Channel.SampleRate,
                    pair.BandLowHz,
                    pair.BandHighHz,
                    pair.Upper.ValidRange);

            // An invalid or near-noise arrival is NOT a time — and, unlike a
            // mis-TIMED one, it is not rescuable either: the envelope SNR
            // grades the whole band against the record's own noise floor, so
            // failing it means the channel has no measurable signal THERE at
            // all — the junction search downstream would only shape a loss
            // surface out of noise phases and let the prior pick a delay. The
            // wide window rescues uncertain timing of a strong signal, never
            // the absence of one. Refuse the run and point at the channel:
            // this is a dead driver, a wrong source or a mis-set crossover,
            // and the user must see that, not a proposal that pretends.
            bool arrivalsMeasured =
                lowerRead.IsValid && upperRead.IsValid &&
                lowerRead.SignalToNoiseDecibels >= MinimumArrivalSnrDb &&
                upperRead.SignalToNoiseDecibels >= MinimumArrivalSnrDb;
            if (!arrivalsMeasured)
            {
                string Describe(TimeAlignmentAnalysisResult read) =>
                    !read.IsValid
                        ? "unmeasurable"
                        : $"near-noise (SNR {read.SignalToNoiseDecibels:0.0} dB, " +
                          $"minimum {MinimumArrivalSnrDb:0})";
                string lowerDescription = Describe(lowerRead);
                string upperDescription = Describe(upperRead);
                log.AppendLine(
                    $"Pair {pair.Lower.Channel.Name}/" +
                    $"{pair.Upper.Channel.Name}: " +
                    $"band {pair.BandLowHz:0}-{pair.BandHighHz:0} Hz, " +
                    $"arrivals {lowerDescription} / {upperDescription} — refusing the run");
                throw new InvalidOperationException(
                    $"No junction evidence between {pair.Lower.Channel.Name} " +
                    $"and {pair.Upper.Channel.Name} in " +
                    $"{pair.BandLowHz:0}-{pair.BandHighHz:0} Hz: " +
                    $"{pair.Lower.Channel.Name} is {lowerDescription}, " +
                    $"{pair.Upper.Channel.Name} is {upperDescription}. " +
                    "Check the channels' sources and crossover settings.");
            }

            double lowerArrival = lowerRead.FirstArrivalDelayMilliseconds;
            double upperArrival = upperRead.FirstArrivalDelayMilliseconds;

            // The pair-band arrival honesty probe: the same full-band-vs-upper-
            // half re-read (ClassifyArrival) the cross-side links, the donor
            // certificates and the stereo bridge already run. A steep low-pass
            // can concentrate the pair band's energy below the corner, where a
            // channel's direct front hides under a late in-room modal build-up
            // deeper than the envelope search depth, and the full-band read then
            // times the mode whole periods late (a field midbass under
            // LP 180 Hz/36 read 21.96 ms in 90-360 Hz against a 10.99 ms front
            // in its own upper half). A convicted latch does two things below:
            // the pair re-anchors on the half-band reads where both measure (the
            // same ladder the cross-side links climb), and the seed-reach veto
            // is lifted — the reach rule measures the PHAT extremum against the
            // arrival, so with the arrival convicted it would only enforce the
            // very cycle skip it exists to prevent. The half-band read is a
            // mushier anchor than an honest full-band one (an octave of HF mush
            // can drag a woofer 6 ms off on the cross-side ladder), so it only
            // recenters the correlation window and the fallback diff; a
            // trustworthy PHAT extremum still wins the seed. The
            // predicted-arrival probe runs first (see PredictedFrontArrivalMs):
            // it convicts the latches the upper-half probe cannot see, and its
            // replacement anchor is stated in the junction's own band.
            //
            // BOTH sides must be gradeable for it to speak at all. The timeline
            // stores a DIFFERENCE, and the two estimators do not read the same
            // thing to the same precision — the prediction estimates a front,
            // the measurement reads the processed response — so their residuals
            // cancel between two predictions and between two measurements, never
            // between one of each. Mixing them injects the residual as a real
            // delay (the v4 cabin's 160 Hz corner: 2.45 ms of base error that
            // held the mid on the flip impostor). A pair with one ungradeable
            // side therefore falls through to the upper-half probe entirely,
            // which then still examines BOTH sides rather than leaving one
            // unchecked.
            PredictionState lowerState = GradeAgainstPrediction(
                pair.Lower, lowerArrival, pair.BandLowHz, pair.BandHighHz,
                out double lowerPrediction);
            PredictionState upperState = GradeAgainstPrediction(
                pair.Upper, upperArrival, pair.BandLowHz, pair.BandHighHz,
                out double upperPrediction);
            // INCONSISTENT counts as ungradeable too. A read sitting far EARLIER
            // than its own predicted front is not a latch, but the two disagree
            // in a way the prediction cannot explain (a truncated front, a
            // mis-captured bypassed response) — and a prediction that cannot
            // explain the read it is graded against may not replace it.
            static bool Gradeable(PredictionState state) =>
                state is PredictionState.Verified or PredictionState.Latched;
            bool pairGradeable = Gradeable(lowerState) && Gradeable(upperState);
            bool lowerLatchedByPrediction =
                pairGradeable && lowerState == PredictionState.Latched;
            bool upperLatchedByPrediction =
                pairGradeable && upperState == PredictionState.Latched;

            // The dead zone (see LatchArbitrationMinR): a side LATER than its
            // prediction by more than one allowance but short of the
            // conviction factor, with both predictions in hand and the other
            // side not itself refusing the grade. The whitened comb decides
            // whether the pair's shared content sits with the predictions or
            // with the measured arrivals.
            bool lowerLateInZone =
                lowerState == PredictionState.Inconsistent &&
                lowerArrival > lowerPrediction;
            bool upperLateInZone =
                upperState == PredictionState.Inconsistent &&
                upperArrival > upperPrediction;
            if (!lowerLatchedByPrediction && !upperLatchedByPrediction &&
                (lowerLateInZone || upperLateInZone) &&
                lowerState != PredictionState.Unavailable &&
                upperState != PredictionState.Unavailable &&
                (lowerLateInZone || lowerState == PredictionState.Verified) &&
                (upperLateInZone || upperState == PredictionState.Verified))
            {
                double periodMs = 1_000.0 / pair.CrossoverHz;
                double measuredLagMs = lowerArrival - upperArrival;
                double predictedLagMs = lowerPrediction - upperPrediction;
                List<SignalPoint> comb =
                    VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                        pair.Lower.ImpulseResponse,
                        pair.Upper.ImpulseResponse,
                        pair.Lower.Channel.SampleRate,
                        pair.CrossoverHz,
                        Math.Log2(pair.BandHighHz / pair.BandLowHz),
                        Math.Abs(measuredLagMs - predictedLagMs) / 2.0
                            + periodMs / 2.0,
                        (measuredLagMs + predictedLagMs) / 2.0,
                        phaseTransform: true);
                double StrongestNear(double lagMs) => comb
                    .Where(point => Math.Abs(point.X - lagMs) <= periodMs / 2.0)
                    .Select(point => Math.Abs(point.Y))
                    .DefaultIfEmpty(0.0)
                    .Max();
                double nearPredicted = StrongestNear(predictedLagMs);
                double nearMeasured = StrongestNear(measuredLagMs);
                if (nearPredicted >= LatchArbitrationMinR &&
                    nearPredicted >= nearMeasured + LatchArbitrationMinAdvantage)
                {
                    log.AppendLine(
                        $"  {(lowerLateInZone ? pair.Lower : pair.Upper).Channel.Name}: " +
                        $"read sits in the conviction dead zone and the " +
                        $"whitened comb sides with the prediction " +
                        $"(r {nearPredicted:0.00} at the predicted family vs " +
                        $"{nearMeasured:0.00} at the measured) — convicted by " +
                        "arbitration");
                    lowerLatchedByPrediction = lowerLateInZone;
                    upperLatchedByPrediction = upperLateInZone;
                }
                else
                {
                    log.AppendLine(
                        $"  latch arbitration stood down for " +
                        $"{pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}: " +
                        $"comb r {nearPredicted:0.00} at the predicted family " +
                        $"vs {nearMeasured:0.00} at the measured — no second " +
                        "witness, the pair withdraws from the predictor.");
                }
            }
            if (lowerLatchedByPrediction || upperLatchedByPrediction)
            {
                void LogConviction(
                    AlignmentSnapshot side, double measuredMs, double predictedMs)
                {
                    double allowances = (measuredMs - predictedMs) /
                        PredictedArrivalAllowanceMs(pair.BandLowHz, pair.BandHighHz);
                    // Below the factor the predictor did not convict alone —
                    // the comb arbitration above supplied the second witness
                    // — and the line must not read as if it had.
                    string basis = allowances >= PredictedArrivalConvictionFactor
                        ? $"conviction needs {PredictedArrivalConvictionFactor:0.0}"
                        : $"short of the predictor's own " +
                          $"{PredictedArrivalConvictionFactor:0.0}, convicted by " +
                          $"the comb's second witness";
                    log.AppendLine(
                        $"  {side.Channel.Name}: {measuredMs:0.000} ms in " +
                        $"{pair.BandLowHz:0}-{pair.BandHighHz:0} Hz but its " +
                        $"un-crossovered front, read through its own " +
                        $"chain, predicts {predictedMs:0.000} ms there (modal " +
                        $"latch behind the crossover; " +
                        $"{allowances:0.0} allowances, {basis}) — re-anchored");
                }
                if (lowerLatchedByPrediction)
                {
                    LogConviction(pair.Lower, lowerArrival, lowerPrediction);
                }
                if (upperLatchedByPrediction)
                {
                    LogConviction(pair.Upper, upperArrival, upperPrediction);
                }

                // Both sides move to the prediction, not just the convicted
                // one — see the estimator-mixing note above.
                lowerArrival = lowerPrediction;
                upperArrival = upperPrediction;
            }

            // How much the pair anchor DISAGREES with its own prediction. NOT a
            // bound on the anchor's error, and nothing downstream may treat it
            // as one: the measurement and the prediction share a nonlinear
            // envelope detector, a room, and the same bypassed response, so a
            // bias common to both leaves this figure at zero while the true
            // error is whatever the bias is. It only decides whether to apply an
            // EXTRA restriction below, never to certify one — nothing here can
            // make the seed gate more permissive than it was without it.
            //
            // Where both sides merely VERIFIED, only the DIFFERENCE of the
            // residuals enters, since the timeline stores a difference and two
            // sides erring alike cost it nothing. Where a side was convicted and
            // replaced, its residual is unknowable by construction, so the
            // stand-in is TWICE the per-side allowance: two sides each within A
            // bound their difference by 2A, not by A.
            double predictionDisagreementMs =
                lowerLatchedByPrediction || upperLatchedByPrediction
                    ? 2.0 * PredictedArrivalAllowanceMs(
                        pair.BandLowHz, pair.BandHighHz)
                    : Math.Abs(
                        (lowerArrival - lowerPrediction) -
                        (upperArrival - upperPrediction));

            // Whether the pair can be GRADED against its prediction at all — not
            // whether the anchor is confirmed by it. Their agreement is not
            // evidence of accuracy (see the disagreement figure above).
            // Gradeable means every side either agreed with its own predicted
            // front or was replaced by it; a merely AVAILABLE prediction proves
            // less, since the conviction test is one-sided and a read far
            // EARLIER than its prediction would sail through it (INCONSISTENT,
            // excluded above).
            bool pairPredictionGradeable = pairGradeable;

            double probeLowHz = Math.Sqrt(pair.BandLowHz * pair.BandHighHz);
            bool arrivalReanchored = false;
            if (!lowerLatchedByPrediction && !upperLatchedByPrediction &&
                pair.BandHighHz >=
                probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
            {
                TimeAlignmentAnalysisResult lowerProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        pair.Lower.ImpulseResponse,
                        pair.Lower.Channel.SampleRate,
                        probeLowHz,
                        pair.BandHighHz,
                        pair.Lower.ValidRange);
                TimeAlignmentAnalysisResult upperProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        pair.Upper.ImpulseResponse,
                        pair.Upper.Channel.SampleRate,
                        probeLowHz,
                        pair.BandHighHz,
                        pair.Upper.ValidRange);
                // Per channel, not per junction: the two sides of a junction
                // run different filters, so the smear each one's own chain
                // explains differs (see ArrivalProbeToleranceMs).
                ArrivalCertificate lowerCertificate = ClassifyArrival(
                    lowerRead, lowerProbe,
                    ArrivalProbeToleranceMs(
                        pair.Lower, lowerArrival,
                        lowerProbe.FirstArrivalDelayMilliseconds,
                        pair.BandLowHz, probeLowHz, pair.BandHighHz));
                ArrivalCertificate upperCertificate = ClassifyArrival(
                    upperRead, upperProbe,
                    ArrivalProbeToleranceMs(
                        pair.Upper, upperArrival,
                        upperProbe.FirstArrivalDelayMilliseconds,
                        pair.BandLowHz, probeLowHz, pair.BandHighHz));
                bool lowerLatched =
                    lowerCertificate == ArrivalCertificate.Latched;
                bool upperLatched =
                    upperCertificate == ArrivalCertificate.Latched;
                if (lowerLatched || upperLatched)
                {
                    TimeAlignmentAnalysisResult latchedRead =
                        lowerLatched ? lowerRead : upperRead;
                    TimeAlignmentAnalysisResult latchedProbe =
                        lowerLatched ? lowerProbe : upperProbe;
                    log.AppendLine(
                        $"  {(lowerLatched ? pair.Lower : pair.Upper).Channel.Name}: " +
                        $"{latchedRead.FirstArrivalDelayMilliseconds:0.000} ms in " +
                        $"{pair.BandLowHz:0}-{pair.BandHighHz:0} Hz but " +
                        $"{latchedProbe.FirstArrivalDelayMilliseconds:0.000} ms in its " +
                        $"{probeLowHz:0}-{pair.BandHighHz:0} Hz half (modal latch)");
                    // Re-anchor only when BOTH probes read the same physics —
                    // judged by the CERTIFICATES, not by bare validity: an
                    // UNVERIFIED side either failed to measure or its probe
                    // timed a far LATER feature than its own full band (a late
                    // reflection the half band mistook for the front), and
                    // either way its probe read is not the wavefront the latched
                    // side's probe found. A conviction WITHOUT a comparable
                    // replacement anchor changes nothing below: the corrupted
                    // diff keeps centering the window and the reach veto stays
                    // armed, since lifting it would trust an extremum measured
                    // around the very anchor the probe just convicted.
                    if (lowerCertificate != ArrivalCertificate.Unverified &&
                        upperCertificate != ArrivalCertificate.Unverified)
                    {
                        lowerArrival = lowerProbe.FirstArrivalDelayMilliseconds;
                        upperArrival = upperProbe.FirstArrivalDelayMilliseconds;
                        arrivalReanchored = true;
                    }
                }
            }

            // Refine the coarse offset with the DOMINANT GCC-PHAT extremum of
            // either sign — position only; polarity and the final lobe stay with
            // the loss search. At a mid/high junction it lands the stage-2
            // window on the correct lobe directly, sparing the wide-window
            // recovery; where the extremum is not the honest winner of its
            // window, the arrival envelope stands. A trough seed matters as much
            // as a peak: at an inverted junction (the cabin sub/woofer pair,
            // whitened trough r −0.97) the arrival fallback plus a period-wide
            // window leaves the true lobe and a non-inverted rival a third of a
            // period out competing within fractions of a dB — a coin flip worth
            // 3.5-5 ms of sub misalignment. Distrust rules, ordered because each
            // corrupts the next one's inputs:
            //  - EDGE-PINNED: a lobe cut by the window boundary, so its position
            //    and magnitude (and every comparison below) are artifacts;
            //  - WEAK or BARELY-DOMINANT: lobe ambiguity decided by noise (see
            //    the two constants);
            //  - near-tie against the SAME-SIGN rival one period over (which
            //    Confidence, being peak-vs-trough, cannot see): the lobe choice,
            //    and with it a whole-period cycle skip, would fall to whichever
            //    reflection ran slightly hotter;
            //  - FARTHER FROM THE ARRIVAL than max(half period, fixed window
            //    floor): a cycle-skip candidate the period-wide window must not
            //    hand to the timeline.
            // The timeline stores arrivals as (upper - lower); the extremum is
            // the delay to add to the upper channel, i.e. that quantity negated.
            double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);
            double centerLagMs = lowerArrival - upperArrival;
            CorrelationAlignmentResult Correlate(double centerMs) =>
                VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                    pair.Lower.ImpulseResponse,
                    pair.Upper.ImpulseResponse,
                    pair.Lower.Channel.SampleRate,
                    pair.CrossoverHz,
                    passOctaves,
                    SeedCorrelationRangeMs(pair.CrossoverHz),
                    centerMs,
                    phaseTransform: true);
            CorrelationAlignmentResult phat = Correlate(centerLagMs);

            // The lobe-boundary conviction. Where the pair anchor disagrees
            // with its own predicted fronts by more than the measured half
            // lobe spacing, it cannot say WHICH lobe the junction sits on —
            // and an anchor that cannot resolve lobes must not be the thing
            // that resolves this one. It used to only VETO the extremum here
            // and then seed the timeline from that same anchor, prior and
            // all: the field case that exposed the asymmetry (a 55 Hz sub
            // junction whose envelope read sat 8.7 ms past its predicted
            // front, 0.96 of an allowance — under every conviction bar,
            // because the allowance IS half a period at fc and therefore
            // cannot be tighter than the lobe spacing it would have to
            // resolve) parked the whole stack a half period late while a
            // whitened extremum at r 0.99 stood refused. So convict the
            // anchor instead: move both sides to their predicted fronts, like
            // the upper-half probe's conviction does, re-center the
            // correlation on the corrected lag, and let the extremum be
            // judged on its own quality below.
            double LobeBoundaryMs(CorrelationAlignmentResult result)
            {
                CorrelationDelayCandidate best = result.BestByMagnitude;
                CorrelationDelayCandidate? adjacent = best.InvertPolarity
                    ? result.NegativeOppositeNeighbor
                    : result.PositiveOppositeNeighbor;
                return adjacent is { EdgePinned: false } neighbour
                    ? Math.Abs(neighbour.DelayMs - best.DelayMs) / 2.0
                    : 0;
            }

            // Only on MEASURED lobe geometry, only where the anchor is still
            // the RAW read, and only on a disagreement the predictor can call
            // real:
            //  - no separated opposite-sign structure in the window (or an
            //    edge-pinned one) means the spacing this conviction reasons
            //    from was never measured, and absence of evidence may not
            //    license re-anchoring plus the lifting of the seed-reach veto.
            //    Those junctions keep the conservative path: the reach rule
            //    below already refuses an extremum there, since a zero
            //    boundary floors its reach at zero;
            //  - a pair the prediction already convicted upstream carries its
            //    replacement anchor and a stand-in disagreement figure (twice
            //    the allowance, not a measurement);
            //  - a disagreement inside the predictor's own accuracy floor is
            //    estimator noise.
            if (!arrivalReanchored && pairPredictionGradeable &&
                !lowerLatchedByPrediction && !upperLatchedByPrediction &&
                predictionDisagreementMs >= PredictedArrivalAccuracyMs)
            {
                double boundaryMs = LobeBoundaryMs(phat);
                if (boundaryMs > 0 && predictionDisagreementMs >= boundaryMs)
                {
                    log.AppendLine(
                        $"  {pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}: " +
                        $"the arrival anchor disagrees with the predicted fronts " +
                        $"by {predictionDisagreementMs:0.000} ms against a " +
                        $"{boundaryMs:0.000} ms lobe boundary — it cannot place " +
                        $"the junction inside a lobe, so both sides re-anchor on " +
                        $"their predicted fronts " +
                        $"({lowerArrival:0.000}/{upperArrival:0.000} -> " +
                        $"{lowerPrediction:0.000}/{upperPrediction:0.000} ms)");
                    lowerArrival = lowerPrediction;
                    upperArrival = upperPrediction;
                    arrivalReanchored = true;
                    centerLagMs = lowerArrival - upperArrival;
                    // Re-centered ONCE, never iterated: the window is period-wide
                    // and the corrected lag is where the extremum should be read
                    // from, but a second conviction pass off the new reading would
                    // be a loop with no fixed point.
                    phat = Correlate(centerLagMs);
                }
            }

            CorrelationDelayCandidate seed = phat.BestByMagnitude;
            CorrelationDelayCandidate? sameSignRival =
                seed.InvertPolarity ? phat.NegativeRival : phat.PositiveRival;
            string seedLabel = seed.InvertPolarity ? "trough" : "peak";
            double seedOffsetMs = seed.DelayMs - centerLagMs;
            // Declared ahead of the trust gate below, which reads the witness
            // (a local function cannot capture a variable declared after it).
            CorrelationAlignmentResult? directPhat = null;
            CorrelationDelayCandidate? directSeed = null;
            // The weaker of the two sides' picks: the anchor is their
            // DIFFERENCE, so one side reading a feature the other does not is
            // enough to make it one.
            double anchorProminenceDb = Math.Min(
                lowerRead.FirstArrivalProminenceDecibels,
                upperRead.FirstArrivalProminenceDecibels);
            Complex[] lowerDirectCut = [];
            Complex[] upperDirectCut = [];

            string? Distrust()
            {
                if (phat.PositivePeak.EdgePinned || phat.NegativeTrough.EdgePinned)
                {
                    return "edge-pinned extremum";
                }
                if (Math.Abs(seed.Coefficient) < PhatSeedMinCoefficient)
                {
                    return $"{seedLabel} too weak";
                }
                // There is deliberately NO peak-vs-trough gate here: that margin
                // measures the analysed band's width, not the extremum's
                // credibility (see PhatSeedMinRivalDominance), and the half
                // period it leaves ambiguous is the one the fine window spans
                // and the loss search settles by polarity.
                if (sameSignRival is { } rival &&
                    Math.Abs(seed.Coefficient) - Math.Abs(rival.Coefficient) <
                        PhatSeedMinRivalDominance)
                {
                    return "same-polarity rival near-tie";
                }
                // The reach veto grades the extremum against the arrival, so it
                // is lifted only once the pair is RE-ANCHORED on honest
                // half-band reads: against a convicted-and-replaced anchor it
                // would enforce the very cycle skip it exists to prevent. A
                // conviction without a replacement anchor keeps the veto — the
                // window is still centered on the corrupted diff, and a strong
                // distant modal peak found there is exactly the skip candidate
                // the veto guards.
                //
                // A pair whose anchor could not place it inside a lobe has
                // already been convicted and re-anchored above, which clears
                // this arrival-relative test: measuring the extremum against an
                // anchor just found incapable of resolving lobes (or against a
                // discarded one, after the upper-half probe's or the predicted
                // front's conviction) would veto by a number that refers to
                // nothing.
                //
                // A prediction-GRADEABLE pair used to have its reach clamped
                // further, to the measured half lobe spacing. The arithmetic
                // retired that clamp. The certificate it rested on resolves
                // PredictedArrivalAllowanceMs — max(2.5 ms, half a period at the
                // band centre) — while the boundary it clamped to is half the
                // peak-trough spacing, a QUARTER period. A quarter period is
                // never as coarse as either term of that maximum, so the
                // certificate could not resolve the reach it was certifying: at
                // the v5 cabin's 1500 Hz junction a ±2.5 ms "verified" read
                // enforced a 0.167 ms reach and refused an extremum (r 0.587)
                // that the panel then measured 0.13 dB better on average and
                // 2.7 dB shallower in the dip than the lobe the arrival chose.
                // A gate must not be tightened by evidence fifteen times coarser
                // than the distance it decides.
                double reachMs = SeedReachMs(pair.CrossoverHz);
                // Where the DIRECT-CUT witness produced a usable extremum, that
                // reach is tightened to a period and a half. The fixed 3 ms floor
                // above is sized for low junctions; at a mid/tweeter split it is
                // four to five periods, so it admits anything — which is exactly
                // how the full-record extremum passed every gate while sitting
                // 1.7-3.9 periods from the arrival on five of the archived
                // cabins. The honest full-record extrema measured there sit
                // within 1.15 periods, the phantoms from 1.66 out, so the bound
                // separates them; and it only applies when there is a measured
                // direct front to seed from instead, never leaving the junction
                // with nothing but the envelope.
                if (directSeed != null)
                {
                    reachMs = Math.Min(
                        reachMs, DirectSeedTrustReachPeriods * 1000.0 / pair.CrossoverHz);
                }

                // At the boundary itself the two lobes are equidistant, so
                // the extremum is refused rather than admitted.
                if (!arrivalReanchored && Math.Abs(seedOffsetMs) >= reachMs)
                {
                    // Unless the anchor is not the same kind of read as the
                    // extremum it would be vetoing (see
                    // SeedVetoMinProminenceDb). One side picked deep under its
                    // own band's energy and the other on top of it: their
                    // difference is not a delay the reach can grade.
                    if (anchorProminenceDb >= SeedVetoMinProminenceDb)
                    {
                        return $"{seedLabel} beyond the arrival's reach";
                    }

                    log.AppendLine(
                        $"  {pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}: " +
                        $"the {seedLabel} sits {Math.Abs(seedOffsetMs):0.000} ms " +
                        $"from the arrival anchor, past its {reachMs:0.000} ms " +
                        $"reach — but that anchor was picked " +
                        $"{-anchorProminenceDb:0.0} dB under its own band's " +
                        $"energy, so it is not the read the extremum disagrees " +
                        $"with and cannot veto it");
                }
                return null;
            }
            // The direct-cut witness (see DirectSeedMinCrossoverHz): the same
            // whitened correlation on the two channels' direct-sound cuts, so the
            // reflections that grow the full record's phantom lobes never enter
            // it. It is subject to its own gates — an edge-pinned cut, a weak
            // coefficient (the cuts caught reflections after all) or a position
            // past the arrival's reach silence it, and the full-record path then
            // behaves exactly as before this witness existed.
            if (pair.CrossoverHz >= DirectSeedMinCrossoverHz)
            {
                (lowerDirectCut, upperDirectCut) =
                    VirtualCrossoverAnalysis.CutDirectSoundPair(
                        pair.Lower.ImpulseResponse,
                        pair.Upper.ImpulseResponse,
                        pair.Lower.Channel.SampleRate,
                        pair.BandLowHz,
                        pair.BandHighHz,
                        pair.CrossoverHz,
                        SeedCorrelationRangeMs(pair.CrossoverHz),
                        pair.Lower.ValidRange,
                        pair.Upper.ValidRange);
                directPhat = VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                    lowerDirectCut,
                    upperDirectCut,
                    pair.Lower.Channel.SampleRate,
                    pair.CrossoverHz,
                    passOctaves,
                    SeedCorrelationRangeMs(pair.CrossoverHz),
                    centerLagMs,
                    phaseTransform: true);
                CorrelationDelayCandidate directBest = directPhat.BestByMagnitude;
                CorrelationDelayCandidate? directRival = directBest.InvertPolarity
                    ? directPhat.NegativeRival
                    : directPhat.PositiveRival;
                // The same-sign rival gate matters here as much as on the full
                // record: a cut holding a genuinely periodic front (an echo one
                // period out at near-equal strength) ties its own lobes, and a
                // witness deciding that by half a percent of r would hand the
                // timeline a coin flip. Field margins of honest direct seeds
                // run 0.10-0.47.
                if (!directPhat.PositivePeak.EdgePinned &&
                    !directPhat.NegativeTrough.EdgePinned &&
                    Math.Abs(directBest.Coefficient) >= DirectSeedMinCoefficient &&
                    (directRival == null ||
                        Math.Abs(directBest.Coefficient) -
                            Math.Abs(directRival.Coefficient) >=
                        PhatSeedMinRivalDominance) &&
                    Math.Abs(directBest.DelayMs - centerLagMs) <
                        SeedReachMs(pair.CrossoverHz))
                {
                    directSeed = directBest;
                }
            }

            string? distrust = Distrust();
            bool trustPhat = distrust == null;

            // A trusted seed fixes WHERE the pair of adjacent lobes sits, not
            // WHICH of them is right: the peak-vs-trough margin is a statement
            // about the band's width, so the polarity partner a half period away
            // is still a live candidate and the loss search is what settles it
            // (see PhatSeedMinRivalDominance). That only holds if the fine
            // window can reach the partner, and the fixed ±2.5 ms cap cannot
            // below ~200 Hz: measured across the archived cabins, 10 of 13
            // junctions under 400 Hz put their partner OUTSIDE it (7.57 ms out
            // at 60 Hz, 3.18 at 150 against a 2.5 ms reach). So every
            // non-arrival seed records how far its partner actually sits,
            // measured on the SURFACE that produced the seed.
            // A local alias: an out parameter cannot be captured by a local
            // function, and the dictionary instance is what matters.
            Dictionary<AlignmentJunction, double> partnerReachByPair =
                seedPartnerDistanceMs;
            void RecordPartnerReach(CorrelationAlignmentResult source)
            {
                if (LobeBoundaryMs(source) is > 0 and { } halfSpacingMs)
                {
                    partnerReachByPair[pair] = 2.0 * halfSpacingMs;
                }
            }

            double halfPeriodAtFcMs = 500.0 / pair.CrossoverHz;
            double increment;
            string seedSource;
            bool arrivalSeeded = false;
            if (directSeed is { } adjudicated && trustPhat &&
                Math.Abs(adjudicated.DelayMs - seed.DelayMs) > halfPeriodAtFcMs)
            {
                // Two trusted extrema on different lobes: adjudicate by JOINT
                // support — the smaller of what the two surfaces read within a
                // quarter period of each candidate. A phantom lobe is strong on
                // the surface that manufactured it and near-zero on the other
                // (0.02 against 0.20+ in every catastrophic field cell), while
                // the true lobe carries both drivers' wavefronts and so shows on
                // both. See DirectSeedJointTieMarginR for the calibration.
                List<SignalPoint> fullCurve =
                    VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                        pair.Lower.ImpulseResponse,
                        pair.Upper.ImpulseResponse,
                        pair.Lower.Channel.SampleRate,
                        pair.CrossoverHz,
                        passOctaves,
                        SeedCorrelationRangeMs(pair.CrossoverHz),
                        centerLagMs,
                        phaseTransform: true);
                List<SignalPoint> directCurve =
                    VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                        lowerDirectCut,
                        upperDirectCut,
                        pair.Lower.Channel.SampleRate,
                        pair.CrossoverHz,
                        passOctaves,
                        SeedCorrelationRangeMs(pair.CrossoverHz),
                        centerLagMs,
                        phaseTransform: true);
                double SupportNear(List<SignalPoint> curve, double positionMs)
                {
                    double best = 0;
                    foreach (SignalPoint point in curve)
                    {
                        if (Math.Abs(point.X - positionMs) <= halfPeriodAtFcMs / 2.0)
                        {
                            best = Math.Max(best, Math.Abs(point.Y));
                        }
                    }

                    return best;
                }
                double fullJoint = Math.Min(
                    SupportNear(fullCurve, seed.DelayMs),
                    SupportNear(directCurve, seed.DelayMs));
                double directJoint = Math.Min(
                    SupportNear(fullCurve, adjudicated.DelayMs),
                    SupportNear(directCurve, adjudicated.DelayMs));
                if (directJoint > fullJoint + DirectSeedJointTieMarginR)
                {
                    increment = -adjudicated.DelayMs;
                    seedSource = FormattableString.Invariant(
                        $"direct-cut over phat (joint {directJoint:0.00} vs {fullJoint:0.00})");
                    RecordPartnerReach(directPhat!);
                }
                else
                {
                    increment = -seed.DelayMs;
                    seedSource = FormattableString.Invariant(
                        $"phat (joint {fullJoint:0.00} vs direct {directJoint:0.00})");
                    RecordPartnerReach(phat);
                }
            }
            else if (trustPhat)
            {
                increment = -seed.DelayMs;
                seedSource = directSeed != null ? "phat (direct-cut concurs)" : "phat";
                RecordPartnerReach(phat);
            }
            else if (directSeed is { } rescue)
            {
                // The full-record extremum failed its own gates; before this
                // witness the arrival envelope seeded here, and at these
                // junctions it sat 0.6-1.2 periods off the owner's tunes in
                // most field cells — recoverable only by luck of the onset
                // lock. The direct front is the honest read.
                increment = -rescue.DelayMs;
                seedSource = FormattableString.Invariant(
                    $"direct-cut (phat: {distrust})");
                RecordPartnerReach(directPhat!);
            }
            else
            {
                increment = upperArrival - lowerArrival;
                seedSource = FormattableString.Invariant($"arrival ({distrust})");
                arrivalSeeded = true;
            }
            timeline[pair.Upper.Channel] = timeline[pair.Lower.Channel] + increment;
            if (arrivalSeeded)
            {
                untrustedSeedJunctions.Add(pair);
            }

            // Full-band processed-IR peak times, a detector-independent arrival
            // proxy: a band-limited arrival that sits many ms LATER than its own
            // channel's energy peak is a detector artifact (a late in-band lobe),
            // not a real arrival.
            double lowerPeakMs =
                pair.Lower.PeakIndex * 1000.0 / pair.Lower.Channel.SampleRate;
            double upperPeakMs =
                pair.Upper.PeakIndex * 1000.0 / pair.Upper.Channel.SampleRate;

            log.AppendLine(
                $"Pair {pair.Lower.Channel.Name}/" +
                $"{pair.Upper.Channel.Name}: " +
                $"fc {pair.CrossoverHz:0} Hz, " +
                $"band {pair.BandLowHz:0}-{pair.BandHighHz:0} Hz, " +
                $"arrivals {lowerArrival:0.000} / {upperArrival:0.000} ms " +
                $"(peaks {lowerPeakMs:0.000} / {upperPeakMs:0.000} ms), " +
                $"diff {upperArrival - lowerArrival:+0.000;-0.000} ms, " +
                $"phat {seedLabel} {seed.DelayMs:+0.000;-0.000} ms " +
                $"(r {seed.Coefficient:+0.000;-0.000}, " +
                $"dom {phat.Confidence:0.000})" +
                (directPhat is { } directForLog
                    ? $", direct-cut {directForLog.BestByMagnitude.DelayMs:+0.000;-0.000} ms " +
                      $"(r {directForLog.BestByMagnitude.Coefficient:+0.000;-0.000}" +
                      $"{(directSeed == null ? ", unusable" : "")})"
                    : "") +
                $" -> seed {seedSource}");
        }

        return timeline;
    }

    // Fine-aligns one channel against its settled neighbor(s) and writes the
    // result into the alignment map: the stage-2 body shared by the mono walk
    // and the stereo right-side descent. The search window has three
    // authorities, strongest first: a scene lock (the image pin IS the
    // window), the onset lock (sharp-front junctions pin to the broadband
    // onset anchor), and otherwise the coarse base(s) ± the period-scaled
    // range. With a SECONDARY settled neighbor (the shared mono subwoofer
    // below a descent channel) the search optimizes BOTH junctions at once:
    // both neighbors join the fixed set, the band spans both junctions, and
    // the window covers both junctions' coarse bases — otherwise the channel
    // buys a perfect upper junction while parking a whole period off its
    // lower one. An external prior (the cross-side Δ-consistent delay)
    // replaces the base as the gentle tie-break when supplied. A physically
    // impossible negative delay is converted into a uniform shift of every
    // OTHER channel in <paramref name="shiftScope"/> (a uniform shift
    // preserves the alignment) — in a stereo run the scope must span BOTH
    // sides, or the shift would silently break the inter-side scene offset.
    private static void AlignChannelAtJunction(
        IAlignmentChannel channel,
        IAlignmentChannel neighborChannel,
        AlignmentJunction pair,
        IReadOnlyDictionary<IAlignmentChannel, double> timeline,
        IReadOnlyList<AlignmentSnapshot> shiftScope,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        IAlignmentChannel? secondaryNeighbor = null,
        AlignmentJunction? secondaryPair = null,
        double? priorOverrideMs = null,
        double? sceneLockToleranceMs = null,
        bool? forcedPolarity = null,
        IReadOnlySet<AlignmentJunction>? untrustedSeedJunctions = null,
        Dictionary<AlignmentJunction, OnsetLockState>? onsetLocks = null,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null,
        IReadOnlyCollection<IAlignmentChannel>? monoChannels = null,
        IReadOnlyDictionary<AlignmentJunction, double>? seedPartnerDistanceMs = null)
    {
        // Widen the window when the coarse seed ACROSS this junction (or its
        // secondary, for a joint two-neighbour search) was the untrusted arrival
        // fallback — the base can sit a half period off. Junction-keyed, so it
        // fires the same whether the walk reached this junction from below or
        // above, and only for the untrusted junction itself.
        bool wideSeed = untrustedSeedJunctions != null &&
            (untrustedSeedJunctions.Contains(pair) ||
                (secondaryPair != null && untrustedSeedJunctions.Contains(secondaryPair)));

        double primaryBase = alignment.GetValueOrDefault(neighborChannel).DelayMs
            + timeline[neighborChannel] - timeline[channel];
        double secondaryBase = secondaryNeighbor != null
            ? alignment.GetValueOrDefault(secondaryNeighbor).DelayMs
                + timeline[secondaryNeighbor] - timeline[channel]
            : primaryBase;
        double bandLowHz = Math.Min(
            pair.BandLowHz, secondaryPair?.BandLowHz ?? pair.BandLowHz);
        double bandHighHz = Math.Max(
            pair.BandHighHz, secondaryPair?.BandHighHz ?? pair.BandHighHz);
        double halfPeriodMs = Math.Max(
            500.0 / pair.CrossoverHz,
            secondaryPair != null ? 500.0 / secondaryPair.CrossoverHz : 0);
        // The anchor of the near-tie selection and (absent an external prior)
        // of the quadratic lobe deterrent: between the two coarse bases when
        // both junctions constrain the channel.
        double anchorMs = priorOverrideMs ?? (primaryBase + secondaryBase) / 2.0;

        // A matched odd-order split (LR12/LR36, Butterworth 12/36) sums to a NULL
        // at its corner unless one channel is flipped — the crossover is designed
        // that way, and nothing measured can overrule arithmetic. So the polarity
        // is settled here, from the settings, and the search below is left to do
        // only what it is good at: find the delay. Measured on the v6 cabin with
        // its mid/tweeter split set to LR36, letting the summation decide instead
        // gave the two sides opposite answers, each on a 0.2-0.5 dB margin — the
        // in-phase option buys nearly all of the null back by sliding a quarter
        // period, so the score cannot see what the corner makes obvious.
        // A caller-supplied polarity (the stereo descent inheriting its
        // counterpart's) outranks this: there the whole point is that the two
        // sides of one driver never differ.
        // ... and only where the search knows WHICH lobe it is on. Under a wide
        // seed the coarse offset can be half a period out, the window spans
        // several lobes, and the recovery machinery (the edge retry, the
        // wide-window promotion) navigates by comparing candidates of both
        // polarities; removing half of them there does not fix the polarity, it
        // strands the delay — measured on the v4 cabin's wide-seeded 180 Hz
        // Butterworth 36 junction, forcing the (correct) flip moved the channel
        // a whole period off the lobe the free search had found.
        // ... and only above DirectSeedMinCrossoverHz. Lower down the cabin's
        // modes, not the crossover, shape the junction band: the sum there is a
        // room response with a filter in it, the arrivals need the modal-latch
        // machinery to be read at all, and the archived cabins' two matched
        // Butterworth 36 splits (70 and 180 Hz) answer the polarity question by
        // moving up to a period rather than by flipping. Above 1 kHz the
        // crossover dominates its own band, which is where this rule can be
        // believed — and where the owner's LR36 test lives.
        bool? filterPolarity = pair.CrossoverHz >= DirectSeedMinCrossoverHz
            ? ExpectsRelativeInversion(pair)
            : null;
        bool expectsInversion = filterPolarity == true;
        if (forcedPolarity == null && filterPolarity is bool expectedInversion &&
            !wideSeed && secondaryNeighbor == null)
        {
            // Both answers are forced, not just the flip: a matched LR24 sums
            // in phase as decisively as a matched LR36 nulls there, and leaving
            // the in-phase case to the search would let a comb lobe half a
            // period away flip a pair whose crossover has already settled the
            // question. Only the shrug (null) is the search's.
            forcedPolarity =
                alignment.GetValueOrDefault(neighborChannel).InvertPolarity ^
                expectedInversion;
            log.AppendLine(
                $"  the matched {pair.CrossoverHz:0} Hz split sums " +
                (expectedInversion ? "only inverted" : "in phase") +
                $" — {channel.Name} takes the " +
                (expectedInversion ? "opposite" : "same") +
                $" polarity as {neighborChannel.Name} by construction; the " +
                "search settles the delay alone");
        }

        // Reprocess so the settled neighbors participate with their new delays
        // and polarities. The searched channel is dropped from the override map
        // so its response is the raw, undelayed IR — the search provides the
        // delay, and chosen.DelayMs is then the absolute delay to assign.
        // Without this reset, a uniform shift applied earlier to a
        // not-yet-searched channel (the negative-delay branch below) would bake
        // a stray offset into variableIr that the reported delay does not
        // account for. Hoisted out of SearchJunction so the fine, wide and retry
        // searches and the onset lock all read the same settled state.
        var searchAlignment =
            new Dictionary<IAlignmentChannel, AlignmentOverride>(alignment);
        searchAlignment.Remove(channel);
        IReadOnlyList<AlignmentSnapshot> current = reprocess(searchAlignment);
        AlignmentSnapshot variableSnapshot =
            current.First(item => item.Channel == channel);
        AlignmentSnapshot primaryNeighborSnapshot =
            current.First(item => item.Channel == neighborChannel);
        Complex[] variableIr = variableSnapshot.ImpulseResponse;
        AlignmentSnapshot? secondaryNeighborSnapshot = secondaryNeighbor != null
            ? current.First(item => item.Channel == secondaryNeighbor)
            : null;
        // Every loss measurement below windows each response at its OWN
        // band-limited front and rotates the probes through those cuts (see
        // BuildAlignmentBins) — the fronts are detected once per bins build,
        // from the responses of THIS `current` render, so every candidate of
        // this search is scored through the same windows, however far the
        // candidate moves the channel. No shared anchor is needed, and none
        // could serve: mid-cascade a settled neighbor's assigned delay can
        // put it further from the searched channel than a band-sized window
        // spans.
        var neighborIrs = new List<Complex[]>
        {
            primaryNeighborSnapshot.ImpulseResponse
        };
        // The snapshots' valid ranges travel with the responses into every
        // bins build below: the per-channel front detection must not read a
        // chain delay's silent prefix as arrival SNR.
        var neighborRanges = new List<ValidSampleRange>
        {
            primaryNeighborSnapshot.ValidRange
        };
        if (secondaryNeighborSnapshot != null)
        {
            neighborIrs.Add(secondaryNeighborSnapshot.ImpulseResponse);
            neighborRanges.Add(secondaryNeighborSnapshot.ValidRange);
        }

        // The level match saturates at its cap; past it the residual
        // imbalance shrinks the junction contrast again, so the user should
        // level the gains and re-run — say so instead of degrading silently.
        if (VirtualCrossoverAnalysis.MeasureInBandImbalanceDb(
                variableIr, neighborIrs, channel.SampleRate, bandLowHz, bandHighHz,
                variableSnapshot.ValidRange, neighborRanges)
            is { } imbalanceDb &&
            Math.Abs(imbalanceDb) > VirtualCrossoverAnalysis.LevelMatchCapDb)
        {
            log.AppendLine(
                $"  WARNING: {channel.Name} sits " +
                $"{Math.Abs(imbalanceDb):0} dB " +
                $"{(imbalanceDb > 0 ? "under" : "over")} its neighbor(s) in " +
                $"{bandLowHz:0}-{bandHighHz:0} Hz — past the " +
                $"{VirtualCrossoverAnalysis.LevelMatchCapDb:0} dB level-match " +
                "cap; level the channel gains and re-run for a trustworthy " +
                "junction read.");
        }

        // The onset lock (see the constants block): at a sharp-front junction
        // the search window is pinned to the broadband onset-aligned delay and
        // the arrival-anchored machinery below only polishes inside it. A
        // second neighbor means a joint two-junction search whose window spans
        // both bases — no single onset anchor exists there (those are the low,
        // mono-adjacent junctions the frequency gate excludes anyway), and a
        // scene lock outranks: the image pin already IS the window.
        double? onsetAnchorMs = null;
        double onsetCapMs = 0;
        // ... and only where the ANCHOR is the arrival envelope, which is what
        // the lock exists to replace (see the onset-lock constants). A trusted
        // whitened extremum is the better witness of the two — it times the
        // two responses against each other in the junction's own band, where a
        // threshold onset reads each driver's broadband front separately and
        // differences their rise times: at the v5 cabin's 1500 Hz junction the
        // mid's front rises over 0.274 ms and the tweeter's over 0.037, so the
        // onset difference moved 0.237 ms (0.4 period) across the 10/25/50 %
        // thresholds and its anchor landed on the weakest lobe of the comb.
        // Pinning a trusted seed to that would overrule the stronger evidence
        // with the weaker.
        if (secondaryNeighbor == null &&
            sceneLockToleranceMs == null &&
            wideSeed &&
            pair.CrossoverHz >= OnsetLockMinCrossoverHz)
        {
            BroadbandOnsetEstimate own =
                VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                    variableIr, channel.SampleRate,
                    variableSnapshot.ValidRange);
            BroadbandOnsetEstimate other =
                VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                    neighborIrs[0], neighborChannel.SampleRate,
                    primaryNeighborSnapshot.ValidRange);
            if (own.IsValid && other.IsValid &&
                (own.SnrDb < OnsetLockMinimumSnrDb ||
                 other.SnrDb < OnsetLockMinimumSnrDb))
            {
                log.AppendLine(
                    $"  onset lock declined for {channel.Name}: envelope SNR " +
                    $"{own.SnrDb:0.0} / {other.SnrDb:0.0} dB below the " +
                    $"{OnsetLockMinimumSnrDb:0} dB floor — the fronts are not " +
                    "measured, so nothing honest to pin to.");
            }
            else if (own.IsValid && other.IsValid)
            {
                double periodMs = 2.0 * halfPeriodMs;
                // The spread of the onset DIFFERENCE across the thresholds —
                // per-channel spreads partially cancel (both fronts widen with
                // the threshold together), and the difference is the quantity
                // the anchor actually uses.
                double early = other.EarlyMs - own.EarlyMs;
                double mid = other.OnsetMs - own.OnsetMs;
                double late = other.LateMs - own.LateMs;
                double spreadMs =
                    Math.Max(early, Math.Max(mid, late)) -
                    Math.Min(early, Math.Min(mid, late));
                if (spreadMs <= OnsetLockMaxSpreadPeriods * periodMs)
                {
                    onsetAnchorMs = mid;
                    onsetCapMs = OnsetLockReachPeriods * periodMs;
                    anchorMs = mid;
                }
                else
                {
                    log.AppendLine(
                        $"  onset lock declined for {channel.Name}: threshold " +
                        $"spread {spreadMs:0.000} ms exceeds " +
                        $"{OnsetLockMaxSpreadPeriods:0.00} of the " +
                        $"{pair.CrossoverHz:0} Hz period — the front is not " +
                        "sharp enough to pin.");
                }
            }
        }

        // One junction search: candidates of the prior-penalized loss score in
        // a window spanning the coarse base(s) (the PHAT-seeded timeline,
        // arrival envelope where PHAT was untrusted) plus half a period of the
        // slowest involved crossover — wide enough to absorb the coarse error
        // (which grows with the period), narrow enough not to span two
        // same-polarity lobes of one base.
        (IReadOnlyList<AlignmentCandidate> Candidates,
            IReadOnlyList<AlignmentCandidate> AllOptima,
            double WindowLowMs, double WindowHighMs)
            SearchJunction(double? windowOverrideMs = null)
        {
            // Where the coarse seed was untrusted (arrival fallback at a low
            // junction) the cap grows toward a half period so the window can
            // reach a half-period-away flip partner the fixed cap would hide.
            //
            // A TRUSTED seed needs the same reach, for a different reason. It
            // fixes where the pair of adjacent lobes sits but not which of them
            // is right — the peak-vs-trough margin measures the band's width,
            // not the polarity (see PhatSeedMinRivalDominance) — so the loss
            // search is what settles that, and it can only settle what the
            // window contains. The reach is the MEASURED distance to the
            // partner rather than half a period: the real correlation's extrema
            // do not sit where a monochromatic comb says (3.18 ms at the v5
            // cabin's 150 Hz junction against a nominal 3.33). Without it the
            // fixed 2.5 ms cap excluded the partner at 10 of the 13 junctions
            // under 400 Hz across the archived cabins, and a hundredth of PHAT
            // coefficient would have decided the polarity with no way back:
            // the wide diagnostic sweep sees the partner, but reaching it there
            // costs the 1.6 dB promotion margin a near-tie cannot pay.
            double partnerReachMs =
                !wideSeed &&
                seedPartnerDistanceMs?.TryGetValue(pair, out double partnerMs) == true
                    ? Math.Min(
                        SeedPartnerReachFactor * partnerMs,
                        SeedPartnerMaxReachPeriods * 2.0 * halfPeriodMs)
                    : 0;
            double maxRangeMs = wideSeed
                ? Math.Max(MaxFineAlignmentRangeMs, LowJunctionReachFraction * halfPeriodMs)
                : Math.Max(MaxFineAlignmentRangeMs, partnerReachMs);
            double rangeMs = windowOverrideMs ?? Math.Clamp(
                halfPeriodMs, MinFineAlignmentRangeMs, maxRangeMs);
            double windowLowMs = Math.Min(primaryBase, secondaryBase) - rangeMs;
            double windowHighMs = Math.Max(primaryBase, secondaryBase) + rangeMs;
            if (sceneLockToleranceMs is { } lockTolerance && windowOverrideMs == null)
            {
                // The scene mandate: the window IS the tolerance around the
                // cross-side target — the search only fine-tunes the junction
                // sum (and decides polarity) inside it.
                windowLowMs = anchorMs - lockTolerance;
                windowHighMs = anchorMs + lockTolerance;
            }
            else if (onsetAnchorMs is { } onsetAnchor && windowOverrideMs == null)
            {
                // The onset lock: same principle as the scene lock — the
                // window IS the constraint. The wide DIAGNOSTIC sweep (a
                // windowOverrideMs caller) still sees past it, so the log
                // keeps showing what the lock excluded.
                windowLowMs = onsetAnchor - onsetCapMs;
                windowHighMs = onsetAnchor + onsetCapMs;
            }
            IReadOnlyList<AlignmentCandidate> candidates =
                VirtualCrossoverAnalysis.FindAlignmentCandidates(
                    variableIr,
                    neighborIrs,
                    channel.SampleRate,
                    bandLowHz,
                    bandHighHz,
                    windowLowMs,
                    windowHighMs,
                    priorDelayMs: anchorMs,
                    priorSigmaMs: (windowHighMs - windowLowMs) / 4.0,
                    forcedPolarity: forcedPolarity,
                    // Search-side level match: the lobe choice must not depend
                    // on the channels' playback gains (see BuildAlignmentBins).
                    levelMatch: true,
                    out IReadOnlyList<AlignmentCandidate> allOptima,
                    gateAnchorSample: null,
                    variableSnapshot.ValidRange,
                    neighborRanges);
            return (candidates, allOptima, windowLowMs, windowHighMs);
        }

        {
            (IReadOnlyList<AlignmentCandidate> candidates,
                IReadOnlyList<AlignmentCandidate> fineOptima,
                double windowLow, double windowHigh) = SearchJunction();
            log.AppendLine(
                $"Channel {channel.Name}: " +
                $"vs {neighborChannel.Name}" +
                (secondaryNeighbor != null ? $" + {secondaryNeighbor.Name}" : "") +
                $" in {bandLowHz:0}-{bandHighHz:0} Hz, " +
                $"base {primaryBase:0.000}" +
                (secondaryNeighbor != null ? $" / {secondaryBase:0.000}" : "") +
                $" ms, prior {anchorMs:0.000} ms" +
                (priorOverrideMs != null ? " (cross-side)" : "") +
                (wideSeed ? ", WIDE SEED" : "") +
                (sceneLockToleranceMs is { } tol
                    ? $", SCENE-LOCKED \u00b1{tol:0.00} ms"
                    : "") +
                (onsetAnchorMs is { } onsetForLog
                    ? $", ONSET-LOCKED {onsetForLog:0.000} \u00b1{onsetCapMs:0.000} ms"
                    : "") +
                // The window itself, not just its inputs: which lobes the loss
                // search could even compare is the first thing to check when a
                // junction settles on a surprising one, and it is not derivable
                // from the base and the crossover alone (a trusted seed's reach
                // follows the MEASURED polarity-partner distance).
                $", window {windowLow:0.000}..{windowHigh:0.000} ms" +
                ", candidates " +
                string.Join("; ", candidates.Select(item =>
                    $"{item.DelayMs:0.000} ms" +
                    $"{(item.InvertPolarity ? " inv" : "")} " +
                    $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                    $"dip {item.DipDb:0.0} dB)")));

            // Polarity purity is judged against the SETTLED primary neighbor:
            // with an inverted neighbor the pure pair is the equally-inverted
            // candidate. An absolute-flag preference would "rescue" a mixed pair
            // at the cost of a quarter period of delay, sliding a tweeter off
            // the onset line its inverted twin sits on.
            bool neighborInverted =
                alignment.GetValueOrDefault(neighborChannel).InvertPolarity;
            // ... and "pure" itself is what the junction's FILTERS ask for: an
            // odd-order Linkwitz-Riley (LR12, LR36) or a Butterworth 12 sums
            // INVERTED, so there the preference must defend the flipped pair
            // instead (see ExpectsRelativeInversion).

            AlignmentCandidate? selected = candidates.Count > 0
                ? AlignmentSelection.Select(candidates, anchorMs,
                    neighborInverted: neighborInverted,
                    expectedRelativeInversion: expectsInversion)
                : null;
            if (selected is { } fineSelected && fineSelected != candidates[0])
            {
                log.AppendLine(
                    $"  preferred {fineSelected.DelayMs:0.000} ms" +
                    $"{(fineSelected.InvertPolarity ? " inv" : "")} over " +
                    $"{candidates[0].DelayMs:0.000} ms" +
                    $"{(candidates[0].InvertPolarity ? " inv" : "")} " +
                    $"(margin {candidates[0].ScoreDb - fineSelected.ScoreDb:0.00} dB)");
            }
            else if (selected is { } keptPick &&
                !expectsInversion &&
                keptPick.InvertPolarity != neighborInverted &&
                AlignmentSelection.DeclinedInvertRescue(candidates, anchorMs,
                    neighborInverted: neighborInverted,
                    expectedRelativeInversion: expectsInversion)
                    is { } rescue)
            {
                log.AppendLine(
                    $"  kept {keptPick.DelayMs:0.000} ms" +
                    $"{(keptPick.InvertPolarity ? " inv" : "")}: rescue " +
                    $"{rescue.DelayMs:0.000} ms " +
                    $"(margin {keptPick.ScoreDb - rescue.ScoreDb:0.00} dB) is " +
                    $"{Math.Abs(rescue.DelayMs - anchorMs) - Math.Abs(keptPick.DelayMs - anchorMs):0.000} ms " +
                    "farther from the arrival (reach " +
                    $"{AlignmentSelection.DefaultInvertPreferenceReachMs:0.00} ms)");
            }

            // Wide sweep: the same junction searched across a much wider
            // window so lobes beyond the working range appear in the log.
            // At an un-locked junction the promotion below may adopt its
            // winner; under a scene or onset lock it is log-only. At a low
            // junction the fixed span is sub-period, so it grows to reach the
            // flip partner half a period out (see DiagnosticFineReachHalfPeriods).
            (IReadOnlyList<AlignmentCandidate> wide,
                IReadOnlyList<AlignmentCandidate> wideOptima,
                double wideLow, double wideHigh) =
                SearchJunction(windowOverrideMs: Math.Max(
                    DiagnosticFineRangeMs,
                    DiagnosticFineReachHalfPeriods * halfPeriodMs));
            log.AppendLine(
                $"  [diag] wide {wideLow:0.000}..{wideHigh:0.000} ms: " +
                (wide.Count > 0
                    ? string.Join("; ", wide.Select(item =>
                        $"{item.DelayMs:0.000} ms" +
                        $"{(item.InvertPolarity ? " inv" : "")} " +
                        $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                        $"dip {item.DipDb:0.0} dB)"))
                    : "none"));

            // An empty fine window is not yet "no evidence": the wide sweep
            // covers several periods, so adopt its selection when it found
            // structure the narrow window missed.
            if (selected == null && wide.Count > 0)
            {
                selected = AlignmentSelection.Select(wide, anchorMs,
                    neighborInverted: neighborInverted,
                    expectedRelativeInversion: expectsInversion);
                log.AppendLine(
                    $"  fine window empty — adopted {selected.DelayMs:0.000} ms" +
                    $"{(selected.InvertPolarity ? " inv" : "")} from the wide sweep");
            }

            // NO usable junction evidence at all (a channel silent or buried in
            // the band — the evidence gate returned no candidates in either
            // window). Fabricating a candidate at the coarse anchor would apply
            // a delay built on an unmeasured or invalid arrival as if it were a
            // result, and a partial "skip this channel" is no better: earlier
            // uniform shifts may already have written a delay into its override,
            // later passes would shift it again, and the walk would align
            // further channels against an unaligned neighbor. The only honest
            // outcome is refusing the RUN with the reason — an unmeasurable
            // channel needs the user's attention (a dead driver, a wrong source,
            // a mis-set crossover), not a proposal that pretends.
            if (selected == null)
            {
                log.AppendLine(
                    $"  NO junction evidence in {bandLowHz:0}-{bandHighHz:0} Hz — " +
                    "refusing the run");
                throw new InvalidOperationException(
                    $"No junction evidence between {channel.Name} and " +
                    $"{neighborChannel.Name} in " +
                    $"{bandLowHz:0}-{bandHighHz:0} Hz: one of them is silent or " +
                    "buried in the shared band, so no delay can be measured " +
                    "there. Check the channel's source and crossover settings.");
            }

            AlignmentCandidate chosen = selected;

            // The arrival-anchored pick, captured BEFORE the edge-retry can move
            // it: the promotion reach is measured from here, so a retry that
            // legitimately widened the window (up to ~0.9 period) cannot stack
            // with the promotion cap to let a comb alias land >2.5 periods off
            // the envelope.
            AlignmentCandidate arrivalPick = chosen;

            // A result pinned to the window edge means the optimum lies beyond
            // the coarse estimate's reach — retry once, widened but still short
            // of a full period so the search cannot land on the next lobe. The
            // edge hit means the base itself is suspect, so the retry relaxes
            // the prior along with the window.
            double retryRangeMs = Math.Min(1.8 * halfPeriodMs, 3.0);
            bool atEdge = chosen.DelayMs <= windowLow + 0.02 ||
                chosen.DelayMs >= windowHigh - 0.02;
            bool edgeRetry = false;
            IReadOnlyList<AlignmentCandidate> retriedOptima = [];
            if (sceneLockToleranceMs == null && onsetAnchorMs == null &&
                retryRangeMs > (windowHigh - windowLow) / 2.0 && atEdge)
            {
                edgeRetry = true;
                (IReadOnlyList<AlignmentCandidate> retried,
                    IReadOnlyList<AlignmentCandidate> retriedAll, _, _) =
                    SearchJunction(windowOverrideMs: retryRangeMs);
                retriedOptima = retriedAll;
                if (retried.Count > 0)
                {
                    // Through the same selection rules as the primary pick:
                    // taking retried[0] raw would let the widened window hand
                    // the result to a (flip + half-period) impostor that the
                    // invert margin and the arrival tie-break exist to reject.
                    chosen = AlignmentSelection.Select(retried, anchorMs,
                        neighborInverted: neighborInverted,
                        expectedRelativeInversion: expectsInversion);
                }

                log.AppendLine(
                    $"  WARNING: fine result at the search edge; widened to " +
                    $"±{retryRangeMs:0.000} ms -> {chosen.DelayMs:0.000} ms, " +
                    $"invert {(chosen.InvertPolarity ? "yes" : "no")}");
            }

            // The wide-seed window reaches comb lobes a trusted seed's window
            // never admits, and inside one window only the soft prior and the
            // 0.1 dB tie-break defend the arrival — fractions of a dB overrun
            // both. Hold any pick beyond the trusted window's own reach to the
            // promotion standard: a lobe hop must be plainly better on the
            // prior-free acoustic score, or the best arrival-adjacent
            // candidate stands (see AlignmentSelection.GateWideSeedLobe).
            if (wideSeed && sceneLockToleranceMs == null && onsetAnchorMs == null)
            {
                double trustedReachMs = Math.Clamp(
                    halfPeriodMs, MinFineAlignmentRangeMs, MaxFineAlignmentRangeMs);
                AlignmentCandidate gated = AlignmentSelection.GateWideSeedLobe(
                    candidates, chosen, AcousticScore, anchorMs,
                    trustedReachMs, WideWindowPromotionMarginDb,
                    neighborInverted, expectsInversion);
                if (gated != chosen)
                {
                    log.AppendLine(
                        $"  wide-seed lobe gate: kept {gated.DelayMs:0.000} ms" +
                        $"{(gated.InvertPolarity ? " inv" : "")} near the arrival — " +
                        $"{chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} gains only " +
                        $"{AcousticScore(chosen) - AcousticScore(gated):0.00} dB " +
                        $"(a lobe hop needs {WideWindowPromotionMarginDb:0.00} dB).");
                    chosen = gated;
                    arrivalPick = gated;
                }
            }

            // Promote the wide-window optimum when it clearly beats the
            // arrival-anchored pick — the un-locked junctions' recovery from a
            // coarse arrival that sat a whole lobe off, where the narrow
            // window cannot reach the true summation optimum a few periods
            // away. AlignmentSelection applies the same flip/tie rules to the
            // wide set, and the margin ensures a mere lobe/flip impostor
            // cannot pull the result off the arrival — and with a cross-side
            // prior in the scores, a promotion that walks away from the other
            // side's timing pays for that distance too. An onset-locked
            // junction never promotes: the wide window's deeper sums are
            // exactly the comb aliases the lock exists to refuse — they stay
            // in the [diag] log line only.
            bool promoted = false;
            if (wide.Count > 0 && sceneLockToleranceMs == null &&
                onsetAnchorMs == null)
            {
                AlignmentCandidate wideChosen =
                    AlignmentSelection.Select(wide, anchorMs,
                        neighborInverted: neighborInverted,
                        expectedRelativeInversion: expectsInversion);
                // Only a lobe's reach from the arrival pick: past that the "better"
                // score is a comb alias the summation cannot distinguish, so the
                // envelope stays authoritative (see PromotionReachPeriods). Inside
                // the reach, a hop onto another comb lobe must be plainly, not
                // marginally, better (see WideWindowPromotionMarginDb). Both the
                // reach (from the pre-retry arrival pick) and the gain (a
                // prior-free acoustic score) are measured on quantities that do
                // NOT depend on the search-window width — the wide diagnostic
                // window carries a weaker arrival prior than the fine window, so
                // comparing raw ScoreDb would credit a promotion for the prior
                // relaxation alone.
                double periodMs = 2.0 * halfPeriodMs;
                double promotionReachMs = PromotionReachPeriods * periodMs;
                double promotionStepMs =
                    Math.Abs(wideChosen.DelayMs - arrivalPick.DelayMs);
                double periodsMoved = promotionStepMs / periodMs;
                double fineScore = AcousticScore(chosen);
                double gainDb = AcousticScore(wideChosen) - fineScore;
                if (gainDb > WideWindowPromotionMarginDb &&
                    promotionStepMs <= promotionReachMs)
                {
                    // The gate above decides THAT a promotion happens, and the
                    // deepest-summing wide lobe is what tripped it. But inside a
                    // comb basin the promotion-worthy lobes differ by fractions
                    // of a dB, and the deepest sum is not necessarily the
                    // physically correct cycle — the arrival is (the same
                    // envelope-first rule as the fine tie-break, one comb over).
                    // At a 1500 Hz mid/tweeter split two adjacent same-polarity
                    // lobes both cleared the gate and the 0.14 dB-deeper one sat
                    // a full period past the correct alignment. So snap to the
                    // arrival-nearest lobe that still clears the gate; wideChosen
                    // itself qualifies, so this only pulls the pick closer to the
                    // arrival, never onto a declined junction.
                    AlignmentCandidate promotedPick = AlignmentSelection.SelectPromotionLobe(
                        wide,
                        wideChosen,
                        AcousticScore,
                        fineScore,
                        WideWindowPromotionMarginDb,
                        arrivalPick.DelayMs,
                        anchorMs,
                        promotionReachMs);
                    promotionStepMs = Math.Abs(promotedPick.DelayMs - arrivalPick.DelayMs);
                    periodsMoved = promotionStepMs / periodMs;
                    gainDb = AcousticScore(promotedPick) - fineScore;
                    log.AppendLine(
                        $"  promoted {promotedPick.DelayMs:0.000} ms" +
                        $"{(promotedPick.InvertPolarity ? " inv" : "")} " +
                        $"over {chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} " +
                        $"(gain {gainDb:0.00} dB at {periodsMoved:0.0} periods)");
                    chosen = promotedPick;
                    promoted = true;
                }
                else if (gainDb > PromotionNoteworthyGainDb &&
                    promotionStepMs > promotionReachMs)
                {
                    log.AppendLine(
                        $"  promotion declined: {wideChosen.DelayMs:0.000} ms is " +
                        $"{promotionStepMs:0.000} ms ({periodsMoved:0.0} " +
                        $"periods) from the arrival pick {arrivalPick.DelayMs:0.000} ms — " +
                        "a comb alias beyond the envelope's reach.");
                }
                else if (gainDb > PromotionNoteworthyGainDb)
                {
                    log.AppendLine(
                        $"  promotion declined: {wideChosen.DelayMs:0.000} ms" +
                        $"{(wideChosen.InvertPolarity ? " inv" : "")} gains only " +
                        $"{gainDb:0.00} dB over {chosen.DelayMs:0.000} ms — " +
                        $"a lobe hop needs {WideWindowPromotionMarginDb:0.00} dB.");
                }
            }

            double? subPrecedenceBehindDb = null;
            // The sub-precedence preference (see SubPrecedenceMarginDb): at a
            // junction with the shared mono sub, a pick that leaves the sub
            // TRAILING the stack yields to the nearest candidate on the
            // LEADING side of the envelope anchor when the prior-free scores
            // are within the precedence margin. The pool spans the fine and
            // wide sets on the prior-free score — the arrival prior is
            // exactly what keeps parking the result on the trailing lobe
            // when the envelope-aligned point falls between two lobes.
            // Bounded to one period past the anchor: a sub leading by whole
            // periods is not "up front", it is detached the other way.
            if (monoChannels != null &&
                sceneLockToleranceMs == null && onsetAnchorMs == null)
            {
                bool subSearched = monoChannels.Contains(channel);
                bool subNeighbor = secondaryNeighbor == null &&
                    monoChannels.Contains(neighborChannel);
                // With the STACK searched, delaying it beyond the anchor
                // leaves the sub leading (+1); with the SUB searched the
                // directions swap (-1).
                double leadSign = subNeighbor ? 1.0 : -1.0;
                if (subSearched ^ subNeighbor)
                {
                    AlignmentCandidate leading = AlignmentSelection.PreferSubLeading(
                        candidates.Concat(wide),
                        chosen,
                        AcousticScore,
                        anchorMs,
                        leadSign,
                        SubPrecedenceMarginDb,
                        SubPrecedenceSlackMs,
                        reachMs: 2.0 * halfPeriodMs);
                    if (leading != chosen)
                    {
                        subPrecedenceBehindDb =
                            AcousticScore(chosen) - AcousticScore(leading);
                        log.AppendLine(
                            $"  sub precedence: preferred {leading.DelayMs:0.000} ms" +
                            $"{(leading.InvertPolarity ? " inv" : "")} (the sub " +
                            $"leads the stack) over {chosen.DelayMs:0.000} ms" +
                            $"{(chosen.InvertPolarity ? " inv" : "")} — behind by " +
                            $"{subPrecedenceBehindDb:0.00} dB, " +
                            $"within the {SubPrecedenceMarginDb:0.00} dB precedence margin.");
                        chosen = leading;
                    }
                }
            }

            // The direct-coherence witness (see the DirectCoherence*
            // constants): a polarity partner within the tie margin is
            // re-judged on the whitened correlation of the two channels'
            // direct sound. Guarded off wherever the lobe is already pinned
            // by stronger evidence — a scene or onset lock, a forced
            // polarity, a joint two-junction search whose combined band has
            // no single junction to read.
            string? directCoherenceDetail = null;
            if (secondaryNeighbor == null &&
                sceneLockToleranceMs == null &&
                onsetAnchorMs == null &&
                forcedPolarity == null &&
                pair.CrossoverHz >= DirectCoherenceMinCrossoverHz)
            {
                AlignmentCandidate? rival = fineOptima
                    .Concat(wideOptima)
                    .Concat(retriedOptima)
                    .Where(item => item.InvertPolarity != chosen.InvertPolarity &&
                        Math.Abs(item.DelayMs - chosen.DelayMs)
                            <= 1.5 * halfPeriodMs)
                    .OrderByDescending(AcousticScore)
                    .FirstOrDefault();
                if (rival != null &&
                    Math.Abs(AcousticScore(chosen) - AcousticScore(rival))
                        <= DirectCoherenceTieMarginDb)
                {
                    // One curve serves both candidates: lag is the delay
                    // added to the VARIABLE channel, the frame every
                    // candidate's DelayMs lives in.
                    double centerMs = (chosen.DelayMs + rival.DelayMs) / 2.0;
                    List<SignalPoint> coherence =
                        VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
                            VirtualCrossoverAnalysis.CutDirectSound(
                                neighborIrs[0], channel.SampleRate,
                                bandLowHz, bandHighHz, pair.CrossoverHz,
                                primaryNeighborSnapshot.ValidRange),
                            VirtualCrossoverAnalysis.CutDirectSound(
                                variableIr, channel.SampleRate,
                                bandLowHz, bandHighHz, pair.CrossoverHz,
                                variableSnapshot.ValidRange),
                            channel.SampleRate,
                            pair.CrossoverHz,
                            Math.Log2(bandHighHz / bandLowHz),
                            Math.Abs(chosen.DelayMs - rival.DelayMs) / 2.0
                                + halfPeriodMs,
                            centerMs,
                            phaseTransform: true);

                    // A candidate's coherence: the best sign-consistent value
                    // within a quarter period of its delay — its own lobe,
                    // never the partner's half a period away.
                    double CoherenceOf(AlignmentCandidate candidate)
                    {
                        double best = double.NegativeInfinity;
                        foreach (SignalPoint point in coherence)
                        {
                            if (Math.Abs(point.X - candidate.DelayMs)
                                <= 0.5 * halfPeriodMs)
                            {
                                best = Math.Max(best, candidate.InvertPolarity
                                    ? -point.Y
                                    : point.Y);
                            }
                        }

                        return best;
                    }

                    double chosenR = CoherenceOf(chosen);
                    double rivalR = CoherenceOf(rival);

                    // The ladder's veto (see the LadderVeto* constants): a swap
                    // the comb asks for by a hair, at a junction high enough for
                    // the sub-band probes to read wavefronts, is refused when the
                    // coherent bands decisively want the standing lobe instead.
                    string? ladderVetoDetail = null;
                    if (rivalR - chosenR < LadderVetoMaxAdvantage &&
                        pair.CrossoverHz >= LadderVetoMinCrossoverHz)
                    {
                        // The ladder reads a pair AT its applied alignment: its
                        // lag axis spans a few periods around zero, while the
                        // variable channel here is still un-delayed against a
                        // neighbor carrying its settled delay. So the pair is
                        // first placed at the standing candidate — a whole
                        // number of samples, whose rounding is a fraction of the
                        // quarter period the vote asks about — and every lag the
                        // ladder reports is then a correction to THAT placement.
                        // A candidate may be NEGATIVE (the cascade's own delays
                        // are rebased later, so the search does not stop at
                        // zero), and a channel cannot be slid earlier: the
                        // NEIGHBOR is slid later instead, which is the same
                        // relative placement and the same thing normalization
                        // would do to the pair afterwards.
                        int slideSamples = (int)Math.Round(
                            chosen.DelayMs * channel.SampleRate / 1000.0);
                        double slideMs = slideSamples * 1000.0 / channel.SampleRate;
                        (Complex[] placedNeighbor, Complex[] placedVariable) =
                            PlacePairAt(
                                neighborIrs[0], variableIr, slideSamples);
                        (ValidSampleRange neighborRange,
                            ValidSampleRange variableRange) = PlacePairAt(
                                primaryNeighborSnapshot.ValidRange,
                                variableSnapshot.ValidRange,
                                slideSamples);
                        IReadOnlyList<VirtualCrossoverAnalysis.ArrivalCoherencePoint>
                            ladder = VirtualCrossoverAnalysis.ArrivalCoherenceLadder(
                                placedNeighbor,
                                placedVariable,
                                channel.SampleRate,
                                bandLowHz,
                                bandHighHz,
                                pair.CrossoverHz,
                                neighborRange,
                                variableRange);
                        int chosenBands = VirtualCrossoverAnalysis.CountLadderAgreement(
                            ladder, chosen.DelayMs - slideMs, halfPeriodMs / 2.0,
                            DirectCoherenceMinR);
                        int rivalBands = VirtualCrossoverAnalysis.CountLadderAgreement(
                            ladder, rival.DelayMs - slideMs, halfPeriodMs / 2.0,
                            DirectCoherenceMinR);
                        log.AppendLine(
                            $"  [diag] coherence ladder: {chosen.DelayMs:0.000} ms " +
                            $"holds {chosenBands} bands, {rival.DelayMs:0.000} ms " +
                            $"holds {rivalBands}, of " +
                            $"{ladder.Count(point => point.PeakR >= DirectCoherenceMinR)} " +
                            $"coherent of {ladder.Count} probed.");
                        if (chosenBands - rivalBands >= LadderVetoMinBandMargin)
                        {
                            ladderVetoDetail = string.Create(
                                System.Globalization.CultureInfo.InvariantCulture,
                                $"the coherence ladder holds the lobe " +
                                $"{chosenBands} bands to {rivalBands}");
                            log.AppendLine(
                                $"  direct coherence: the swap to " +
                                $"{rival.DelayMs:0.000} ms" +
                                $"{(rival.InvertPolarity ? " inv" : "")} is " +
                                $"refused — r gains only " +
                                $"{rivalR - chosenR:0.00}, and the coherence " +
                                $"ladder wants {chosen.DelayMs:0.000} ms by " +
                                $"{chosenBands} coherent bands to {rivalBands}.");
                        }
                    }

                    if (ladderVetoDetail == null &&
                        rivalR >= DirectCoherenceMinR &&
                        rivalR >= chosenR + DirectCoherenceMinAdvantage)
                    {
                        log.AppendLine(
                            $"  direct coherence: preferred " +
                            $"{rival.DelayMs:0.000} ms" +
                            $"{(rival.InvertPolarity ? " inv" : "")} " +
                            $"(direct r {rivalR:0.00}) over " +
                            $"{chosen.DelayMs:0.000} ms" +
                            $"{(chosen.InvertPolarity ? " inv" : "")} " +
                            $"(r {chosenR:0.00}) — scores tied within " +
                            $"{DirectCoherenceTieMarginDb:0.00} dB, the " +
                            "direct wavefronts decide the polarity.");
                        directCoherenceDetail = string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"direct coherence r {rivalR:0.00} vs " +
                            $"{chosenR:0.00} decided the polarity tie");
                        chosen = rival;
                    }
                    else if (ladderVetoDetail == null &&
                        chosenR > double.NegativeInfinity)
                    {
                        log.AppendLine(
                            $"  direct coherence: {chosen.DelayMs:0.000} ms" +
                            $" stands (r {chosenR:0.00} vs rival " +
                            $"{rivalR:0.00})");
                    }

                    // A veto is a decision too: the report should say the lobe
                    // was held by the bands, not leave the swap unexplained.
                    directCoherenceDetail ??= ladderVetoDetail;
                }
            }

            double newDelay = chosen.DelayMs;
            if (newDelay < 0)
            {
                // A physically impossible negative delay: push every channel by
                // the deficit instead — a uniform shift preserves the alignment.
                ShiftAllExcept(shiftScope, channel, -newDelay, alignment, log);
                newDelay = 0;
            }

            // Unclamped above zero: a value past MaxDelayMs stays honest here —
            // relations are what matter mid-run, and the final feasibility
            // check refuses the proposal if the span truly does not fit.
            alignment[channel] = new AlignmentOverride(
                Math.Max(0, Math.Round(newDelay, 2)),
                chosen.InvertPolarity);

            if (decisions != null)
            {
                string versus = neighborChannel.Name +
                    (secondaryNeighbor != null ? $" + {secondaryNeighbor.Name}" : "");
                // The rival pool is the UNCAPPED optimum sets: the selection
                // lists are truncated (six candidates, 1.5 dB gap, judged on the
                // prior-laden score), so a margin over them could read
                // "unrivaled" only because the rival was cut first.
                //
                // Confidence overlap: the WEAKER of the per-neighbor overlaps (a
                // two-neighbor search's good overlap with one settled neighbor
                // must not mask near-none with the other; summing the fixed IRs
                // would also let them cancel in-band). Each neighbor is measured
                // in ITS OWN junction band and normalized to that band's width —
                // not the union both searches span — so a low-junction partner
                // (say 50-150 Hz) is not judged over five octaves it never
                // overlaps. A fraction, not an octave count, so the threshold
                // means the same across narrow and wide bands.
                double OverlapFractionAgainst(
                    AlignmentJunction junction,
                    Complex[] neighborIr,
                    ValidSampleRange neighborRange)
                {
                    double nominal = Math.Log2(junction.BandHighHz / junction.BandLowHz);
                    double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
                        variableIr, [neighborIr], channel.SampleRate,
                        junction.BandLowHz, junction.BandHighHz,
                        variableSnapshot.ValidRange, [neighborRange]);
                    return nominal > 0 ? octaves / nominal : 0;
                }
                double overlapFraction = OverlapFractionAgainst(
                    pair, neighborIrs[0], neighborRanges[0]);
                if (secondaryPair != null && neighborIrs.Count > 1)
                {
                    overlapFraction = Math.Min(
                        overlapFraction,
                        OverlapFractionAgainst(
                            secondaryPair, neighborIrs[1], neighborRanges[1]));
                }
                decisions[channel] = BuildDecision(
                    chosen,
                    fineOptima.Concat(wideOptima).Concat(retriedOptima).ToList(),
                    halfPeriodMs, versus, wideSeed, edgeRetry, promoted,
                    onsetLocked: onsetAnchorMs != null,
                    sceneLocked: sceneLockToleranceMs != null,
                    overlapFraction);
                if (directCoherenceDetail != null)
                {
                    AmendDecision(decisions, channel, directCoherenceDetail);
                }
                if (subPrecedenceBehindDb is { } precedenceBehindDb)
                {
                    // The report must say the objective was deliberately
                    // overridden, or a negative rival margin reads as an
                    // algorithm error rather than a policy. The figure is
                    // relative to the PRE-POLICY pick — itself already shaped by
                    // the prior and the lobe gates, not the global acoustic best.
                    AmendDecision(
                        decisions, channel,
                        "sub-precedence policy: the sub-leading lobe stands " +
                        $"{precedenceBehindDb:0.00} dB behind the pre-policy " +
                        "pick by design");
                }
            }

            if (onsetAnchorMs is { } settledAnchor)
            {
                // The gap is relative (chosen minus anchor), so the uniform
                // shifts that follow — negative-delay recovery, the bridge
                // advance, the final normalization — leave it intact: they
                // move both ends of the junction equally.
                double gapMs = chosen.DelayMs - settledAnchor;
                log.AppendLine(
                    $"  onset gap after: {gapMs:+0.000;-0.000} ms " +
                    $"({gapMs / (2.0 * halfPeriodMs):+0.00;-0.00}T)");
                if (onsetLocks != null)
                {
                    onsetLocks[pair] = new OnsetLockState(channel, gapMs, onsetCapMs);
                }
            }
        }
    }

    // A candidate's summation quality WITHOUT the arrival-prior penalty: the
    // raw in-band average plus the same dip-excess term the candidate scores
    // carry. The stored ScoreDb is this minus a prior penalty whose strength
    // scales with the search window (priorSigma = window / 4), so ScoreDb is
    // only comparable WITHIN one window; cross-window comparisons (the wide
    // promotion vs the fine pick) must use this prior-free figure.
    private static double AcousticScore(AlignmentCandidate candidate) =>
        candidate.LossDb +
        VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
        (candidate.DipDb - candidate.LossDb);

    // The rival margin below which a junction decision reads as ambiguous:
    // comb noise between real lobes runs a few tenths of a dB, so a pick
    // holding less than this over its best rival was effectively decided by
    // the arrival prior and the tie-breaks, not by the acoustics.
    private const double DecisionMediumMarginDb = 0.4;

    // The genuine two-driver overlap (see
    // VirtualCrossoverAnalysis.EffectiveOverlapOctaves) as a FRACTION of the
    // pair band's nominal width below which a junction delay, however cleanly
    // its lobe won, rests on too little shared band to trust: precise but barely
    // observed. A fraction, not an absolute octave count, so it means the same
    // on a narrow steep-crossover band and a wide one. Healthy junctions share
    // ~19-25% of the band once the reliability gate is applied (measured on the
    // v3 cabin), so this only fires on a degenerate hand-over where the drivers
    // barely share a band — it caps a Search pick's reported confidence at Low
    // and annotates any decision (even a locked one) that hits it.
    private const double MinTrustedOverlapFraction = 0.12;

    // Condenses one junction search into the user-report decision: the
    // prior-free score margin of the chosen candidate over its best RIVAL —
    // another lobe (a quarter period away or more) or the opposite polarity;
    // fine, wide and retry candidates are pooled, which the prior-free score
    // keeps on one scale. An onset/scene lock is a CONSTRAINT (of the
    // measured physics or the stereo mandate), not a measure of how the
    // acoustics voted — a locked pick reports the Locked kind with no
    // confidence, and the margin stays in the detail for the curious. For a
    // free search the gates temper the mapping: a wide (untrusted) seed and
    // a window-edge retry mean the coarse base itself was suspect.
    private static AlignmentDecision BuildDecision(
        AlignmentCandidate chosen,
        IReadOnlyList<AlignmentCandidate> pool,
        double halfPeriodMs,
        string versus,
        bool wideSeed,
        bool edgeRetry,
        bool promoted,
        bool onsetLocked,
        bool sceneLocked,
        double overlapFraction)
    {
        double rivalDistanceMs = 0.5 * halfPeriodMs;
        double chosenScore = AcousticScore(chosen);
        double margin = double.PositiveInfinity;
        foreach (AlignmentCandidate candidate in pool)
        {
            bool rival = candidate.InvertPolarity != chosen.InvertPolarity ||
                Math.Abs(candidate.DelayMs - chosen.DelayMs) > rivalDistanceMs;
            if (rival)
            {
                margin = Math.Min(margin, chosenScore - AcousticScore(candidate));
            }
        }

        AlignmentDecisionKind kind = onsetLocked || sceneLocked
            ? AlignmentDecisionKind.Locked
            : AlignmentDecisionKind.Search;
        AlignmentConfidence? confidence = null;
        if (kind == AlignmentDecisionKind.Search)
        {
            AlignmentConfidence level =
                margin >= WideWindowPromotionMarginDb ? AlignmentConfidence.High
                : margin >= DecisionMediumMarginDb ? AlignmentConfidence.Medium
                : AlignmentConfidence.Low;
            if (wideSeed)
            {
                level = (AlignmentConfidence)Math.Min(
                    (int)level, (int)AlignmentConfidence.Medium);
            }
            if (edgeRetry)
            {
                level = AlignmentConfidence.Low;
            }
            if (overlapFraction < MinTrustedOverlapFraction)
            {
                // A clean lobe over almost no shared band is precision without
                // evidence: the rival margin cannot see how little the two
                // drivers actually overlapped, so cap the trust here.
                level = AlignmentConfidence.Low;
            }

            confidence = level;
        }

        // Invariant: the detail feeds the user report, which must read the
        // same regardless of the OS locale (the diagnostic log stays on the
        // current culture, as everywhere else).
        string detail = double.IsPositiveInfinity(margin)
            ? $"vs {versus}: unrivaled"
            : FormattableString.Invariant($"vs {versus}: margin {margin:0.0} dB");
        if (onsetLocked)
        {
            detail += ", onset-locked";
        }
        if (sceneLocked)
        {
            detail += ", scene-locked";
        }
        if (wideSeed)
        {
            detail += ", wide seed";
        }
        if (promoted)
        {
            detail += ", lobe promoted";
        }
        if (edgeRetry)
        {
            detail += ", window-edge retry";
        }
        // The low-overlap note is a diagnostic about the DATA, so it shows on
        // every decision that hits it — including a locked one, whose confidence
        // there is nothing to lower but whose junction is still barely observed.
        if (overlapFraction < MinTrustedOverlapFraction)
        {
            detail += FormattableString.Invariant(
                $", low overlap ({overlapFraction * 100:0}% of band)");
        }

        return new AlignmentDecision(kind, confidence, detail);
    }

    // A uniform delay shift of every channel in the scope but one: the standard
    // way to "advance" a channel that would otherwise need a negative delay.
    // Uniformity is what preserves the alignment, so the scope must cover every
    // channel whose relative timing has already been settled.
    /// <summary>
    /// A junction's two responses placed at a candidate's relative timing: the
    /// upper channel <paramref name="slideSamples"/> later than the lower.
    /// Neither response can be slid EARLIER without cutting its own front off,
    /// so a negative placement moves the lower one later instead — the same
    /// relative timing, and the same thing the cascade's own normalization does
    /// to a pair that ended up with negative delays.
    /// <para>
    /// The distinction matters because a witness reading a pair "at its applied
    /// alignment" states its findings relative to THIS placement; getting the
    /// sign wrong leaves the responses where they were while the caller
    /// believes they moved, and every lag it then reads is offset by the whole
    /// candidate delay.
    /// </para>
    /// </summary>
    internal static (Complex[] Lower, Complex[] Upper) PlacePairAt(
        Complex[] lower, Complex[] upper, int slideSamples) =>
        slideSamples >= 0
            ? (lower, SlideBySamples(upper, slideSamples))
            : (SlideBySamples(lower, -slideSamples), upper);

    /// <summary>The same placement applied to the pair's valid ranges.</summary>
    private static (ValidSampleRange Lower, ValidSampleRange Upper) PlacePairAt(
        ValidSampleRange lower, ValidSampleRange upper, int slideSamples) =>
        slideSamples >= 0
            ? (lower, SlideBySamples(upper, slideSamples))
            : (SlideBySamples(lower, -slideSamples), upper);
    /// <summary>
    /// A response slid LATER by a whole number of samples, keeping its length:
    /// the cheapest honest way to place an un-delayed channel at a candidate's
    /// timing for a witness that reads a pair at its applied alignment. Whole
    /// samples only — a fractional shift would need a resampling kernel, and
    /// the readers that use this ask questions coarser than one sample.
    /// </summary>
    private static Complex[] SlideBySamples(Complex[] response, int samples)
    {
        if (samples <= 0)
        {
            return response;
        }

        var slid = new Complex[response.Length];
        int copied = Math.Max(0, response.Length - samples);
        Array.Copy(response, 0, slid, samples, copied);
        return slid;
    }

    /// <summary>The same slide applied to a response's valid range.</summary>
    private static ValidSampleRange SlideBySamples(
        ValidSampleRange range, int samples) =>
        range.IsKnown && samples > 0
            ? new ValidSampleRange(
                range.StartSample + samples, range.EndSample + samples)
            : range;
    private static void ShiftAllExcept(
        IReadOnlyList<AlignmentSnapshot> scope,
        IAlignmentChannel except,
        double shiftMs,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
    {
        foreach (AlignmentSnapshot item in scope)
        {
            if (item.Channel != except)
            {
                AlignmentOverride currentAlignment =
                    alignment.GetValueOrDefault(item.Channel);
                // NO clamping here: the shift is only alignment-preserving
                // while it is uniform, and a channel pinned at the ceiling
                // would break the relative delays (and the stereo scene)
                // SILENTLY. Transient out-of-range values are legal mid-run —
                // only relations matter until the final normalization, and the
                // feasibility check after it refuses a proposal whose span
                // genuinely does not fit the DSP's delay range.
                alignment[item.Channel] = currentAlignment with
                {
                    DelayMs = currentAlignment.DelayMs + shiftMs
                };
            }
        }
    }

    /// <summary>
    /// Stereo alignment cascade over two sides that never meet at a crossover:
    /// (1) the left side aligns exactly like <see cref="Compute"/> (any mono
    /// channels — typically the shared subwoofer — are part of that walk and
    /// are FINAL afterwards); (2) the bridge fits the right top channel to the
    /// settled left top by band-limited envelope arrivals in the top band,
    /// honoring <see cref="StereoAlignmentPlan.SceneOffsetMs"/>; (3) the right
    /// side descends junction by junction from the bridged top, skipping mono
    /// channels (their right-side junction is measured and logged, not tuned);
    /// (4) the union of both sides is shifted so the minimum delay is exactly
    /// zero. Every uniform shift spans BOTH sides, preserving the scene offset
    /// the bridge established.
    /// </summary>
    public static void ComputeStereo(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reprocess);
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(log);
        List<AlignmentSnapshot> rightByBand = plan.RightChannelsByBand.ToList();
        if (rightByBand.Count == 0 ||
            plan.RightPairs.Count != rightByBand.Count - 1)
        {
            throw new ArgumentException(
                "One junction is required between each adjacent right channel pair.",
                nameof(plan));
        }
        int bridgeIndex = rightByBand.FindIndex(
            item => item.Channel == plan.BridgeRight);
        if (bridgeIndex < 0 ||
            plan.LeftChannelsByBand.All(item => item.Channel != plan.BridgeLeft))
        {
            throw new ArgumentException(
                "The bridge channels must be members of their side's channel list.",
                nameof(plan));
        }
        if (plan.MonoChannels.Contains(plan.BridgeRight))
        {
            throw new ArgumentException(
                "A mono channel cannot be the stereo bridge.",
                nameof(plan));
        }
        if (plan.MonoChannels.Any(mono =>
            plan.LeftChannelsByBand.All(item => item.Channel != mono)))
        {
            throw new ArgumentException(
                "Every mono channel must be part of the left walk that tunes it.",
                nameof(plan));
        }
        RequireOneSampleRate(plan.LeftChannelsByBand.Concat(rightByBand));
        // An absolute proposal, per the contract (see Compute): stage L below
        // uses the PRIVATE overload, so clear here.
        alignment.Clear();
        decisions?.Clear();

        // Onset-locked junctions accumulated across both sides: the co-move
        // must respect the front pins the fine searches honored.
        var onsetLocks = new Dictionary<AlignmentJunction, OnsetLockState>(
            ReferenceEqualityComparer.Instance);

        // Stage L: the left side, exactly like a mono run — plus the mono-set
        // knowledge the sub-precedence preference needs.
        Compute(
            plan.LeftChannelsByBand, plan.LeftPairs, reprocess, alignment, log,
            onsetLocks, decisions, plan.MonoChannels);

        // The union of both sides: the scope of every uniform shift from here
        // on. Shifting one side alone would silently break the inter-side
        // offset the bridge establishes.
        var allChannels = new List<AlignmentSnapshot>(plan.LeftChannelsByBand);
        foreach (AlignmentSnapshot item in rightByBand)
        {
            if (allChannels.All(existing => existing.Channel != item.Channel))
            {
                allChannels.Add(item);
            }
        }

        // Stage bridge: envelope arrivals in the top band, NOT a cross-
        // correlation — same-band L/R drivers sit in different spots with
        // different room paths, and their cross-correlation at high
        // frequencies is lobe-ambiguous noise (probed on real car
        // measurements: r ~0.3, dominance ~0.01), while the envelope arrival
        // is the quantity the stereo image follows up there. A positive scene
        // offset makes the plan's right side — the far side — LEAD (arrive
        // earlier), pulling the image toward the dash center; a right-hand-
        // drive caller hands the plan mirrored, so the same rule makes its
        // actual left side lead.
        IReadOnlyList<AlignmentSnapshot> settled = reprocess(alignment);
        AlignmentSnapshot leftBridgeSnapshot =
            settled.First(item => item.Channel == plan.BridgeLeft);
        AlignmentSnapshot rightBridgeSnapshot =
            settled.First(item => item.Channel == plan.BridgeRight);
        TimeAlignmentAnalysisResult leftBridge =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                leftBridgeSnapshot.ImpulseResponse,
                plan.BridgeLeft.SampleRate,
                plan.BridgeBandLowHz,
                plan.BridgeBandHighHz,
                leftBridgeSnapshot.ValidRange);
        TimeAlignmentAnalysisResult rightBridge =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                rightBridgeSnapshot.ImpulseResponse,
                plan.BridgeRight.SampleRate,
                plan.BridgeBandLowHz,
                plan.BridgeBandHighHz,
                rightBridgeSnapshot.ValidRange);

        // The bridge is the SINGLE link between the sides, so its arrivals are
        // gated instead of trusted: a silent band reports zeros (IsValid off),
        // and a near-noise arrival would time one whole side by garbage —
        // either way the honest outcome is a refusal with the reason, not a
        // plausible-looking wrong alignment.
        if (!leftBridge.IsValid || !rightBridge.IsValid ||
            leftBridge.SignalToNoiseDecibels < MinimumArrivalSnrDb ||
            rightBridge.SignalToNoiseDecibels < MinimumArrivalSnrDb)
        {
            throw new InvalidOperationException(
                "The stereo bridge could not be measured in " +
                $"{plan.BridgeBandLowHz:0}-{plan.BridgeBandHighHz:0} Hz: " +
                $"{plan.BridgeLeft.Name} " +
                (leftBridge.IsValid
                    ? $"SNR {leftBridge.SignalToNoiseDecibels:0.0} dB"
                    : "has no energy in the band") +
                $", {plan.BridgeRight.Name} " +
                (rightBridge.IsValid
                    ? $"SNR {rightBridge.SignalToNoiseDecibels:0.0} dB"
                    : "has no energy in the band") +
                $" (minimum {MinimumArrivalSnrDb:0} dB). " +
                "Check the top pair's sources and crossover band.");
        }

        // The same honesty certificate every cross-side read gets, on the one
        // read that times a WHOLE side: each side's full-band arrival must
        // agree with its own upper-half read to within the dispersion one
        // wavefront can show. SNR alone proves a strong signal, not that both
        // sides timed the same physical event — a strong early reflection on
        // one side passes the SNR gate and would skew the entire right side.
        // Disagreement is positive evidence of that and refuses the bridge; an
        // unmeasurable upper half (a heavily rolled-off top end) cannot
        // certify either way, so the bridge proceeds but its confidence is
        // capped at Low below.
        bool bridgeVerified = true;
        double bridgeProbeLowHz =
            Math.Sqrt(plan.BridgeBandLowHz * plan.BridgeBandHighHz);
        if (plan.BridgeBandHighHz >= bridgeProbeLowHz *
            VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
        {
            TimeAlignmentAnalysisResult leftProbe =
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    leftBridgeSnapshot.ImpulseResponse,
                    plan.BridgeLeft.SampleRate,
                    bridgeProbeLowHz,
                    plan.BridgeBandHighHz,
                    leftBridgeSnapshot.ValidRange);
            TimeAlignmentAnalysisResult rightProbe =
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    rightBridgeSnapshot.ImpulseResponse,
                    plan.BridgeRight.SampleRate,
                    bridgeProbeLowHz,
                    plan.BridgeBandHighHz,
                    rightBridgeSnapshot.ValidRange);
            void Certify(
                TimeAlignmentAnalysisResult full,
                TimeAlignmentAnalysisResult probe,
                AlignmentSnapshot snapshot)
            {
                IAlignmentChannel channel = snapshot.Channel;
                switch (ClassifyArrival(full, probe, ArrivalProbeToleranceMs(
                    snapshot, full.FirstArrivalDelayMilliseconds,
                    probe.FirstArrivalDelayMilliseconds,
                    plan.BridgeBandLowHz, bridgeProbeLowHz,
                    plan.BridgeBandHighHz)))
                {
                    case ArrivalCertificate.Latched:
                        // The full band times a LATER feature than its own
                        // upper half — the arrival is not the direct front.
                        throw new InvalidOperationException(
                            "The stereo bridge reads two different features on " +
                            $"{channel.Name}: {full.FirstArrivalDelayMilliseconds:0.000} ms " +
                            $"in {plan.BridgeBandLowHz:0}-{plan.BridgeBandHighHz:0} Hz but " +
                            $"{probe.FirstArrivalDelayMilliseconds:0.000} ms in its " +
                            $"{bridgeProbeLowHz:0}-{plan.BridgeBandHighHz:0} Hz half. " +
                            "The arrival is not a clean direct front, so timing the " +
                            "whole far side from it would be unreliable. Check the " +
                            "top pair's measurements for early reflections.");
                    case ArrivalCertificate.Unverified:
                        bridgeVerified = false;
                        break;
                }
            }
            Certify(leftBridge, leftProbe, leftBridgeSnapshot);
            Certify(rightBridge, rightProbe, rightBridgeSnapshot);
        }
        else
        {
            bridgeVerified = false;
        }

        double leftArrival = leftBridge.FirstArrivalDelayMilliseconds;
        double rightArrival = rightBridge.FirstArrivalDelayMilliseconds;
        double bridgeDelay = leftArrival - rightArrival - plan.SceneOffsetMs;
        log.AppendLine(
            $"Bridge {plan.BridgeLeft.Name} -> {plan.BridgeRight.Name}: " +
            $"band {plan.BridgeBandLowHz:0}-{plan.BridgeBandHighHz:0} Hz, " +
            $"arrivals ref {leftArrival:0.000} / far {rightArrival:0.000} ms " +
            $"(SNR {leftBridge.SignalToNoiseDecibels:0.0} / " +
            $"{rightBridge.SignalToNoiseDecibels:0.0} dB), " +
            $"scene offset {plan.SceneOffsetMs:+0.000;-0.000} ms " +
            $"(positive: the far side leads) -> far-top delay {bridgeDelay:0.000} ms");
        if (bridgeDelay < 0)
        {
            // The right top must be ADVANCED — typical when the right side is
            // the far one. Impossible directly, so everything settled so far
            // is delayed by the deficit and the right top starts at zero.
            double shift = -bridgeDelay;
            ShiftAllExcept(allChannels, plan.BridgeRight, shift, alignment, log);
            bridgeDelay = 0;
            log.AppendLine(
                $"  advanced via a uniform +{shift:0.000} ms shift " +
                "of every settled channel");
        }
        // Unclamped above zero (see the result write): the final feasibility
        // check owns the delay-range verdict.
        alignment[plan.BridgeRight] = new AlignmentOverride(
            Math.Max(0, Math.Round(bridgeDelay, 2)), false);
        if (decisions != null)
        {
            // The bridge is an envelope-arrival fit, not a candidate search:
            // its robustness is the arrival SNR of the weaker side (clean
            // measurements run 40-70 dB; the hard refusal floor is
            // MinimumArrivalSnrDb).
            double bridgeSnrDb = Math.Min(
                leftBridge.SignalToNoiseDecibels,
                rightBridge.SignalToNoiseDecibels);
            AlignmentConfidence bridgeConfidence =
                bridgeSnrDb >= BridgeHighSnrDb ? AlignmentConfidence.High
                : bridgeSnrDb >= BridgeMediumSnrDb ? AlignmentConfidence.Medium
                : AlignmentConfidence.Low;
            if (!bridgeVerified)
            {
                // The honesty probe could not certify the arrivals as clean
                // direct fronts — the bridge stands, but not with high trust.
                bridgeConfidence = AlignmentConfidence.Low;
            }
            string bridgeSnrText = FormattableString.Invariant(
                $"{leftBridge.SignalToNoiseDecibels:0} / {rightBridge.SignalToNoiseDecibels:0} dB");
            decisions[plan.BridgeRight] = new AlignmentDecision(
                AlignmentDecisionKind.Bridge,
                bridgeConfidence,
                $"bridge to {plan.BridgeLeft.Name}: arrival SNR {bridgeSnrText}" +
                (bridgeVerified
                    ? ""
                    : ", arrival not certified by the upper-half probe"));
        }

        // Polarity is a property of the DRIVER, not the side, and automatic delay
        // never inverts one side of a pair alone: the right top INHERITS the left
        // top's sign (which the left walk may have flipped for its own cascade),
        // just as every lower right driver inherits its left counterpart's. Set it
        // before the right walk so the right lowers align against a correctly-signed
        // top. A genuinely reverse-wired driver is left for a MANUAL flip in the UI,
        // not an asymmetric automatic one.
        InheritBridgePolarity(plan, alignment, log, decisions);

        // Stage R: descent from the bridged top toward the low end (and up
        // from it, if the caller had to bridge a non-top pair) — the same walk
        // as stage 2, referenced to the bridge. Mono channels are final from
        // the left pass: never searched again, their right-side junction is
        // only measured.
        Dictionary<IAlignmentChannel, double> rightTimeline =
            BuildArrivalTimeline(
                rightByBand, plan.RightPairs, log,
                out HashSet<AlignmentJunction> rightUntrustedSeeds,
                out Dictionary<AlignmentJunction, double> rightSeedPartnerReach);

        // The delay that Δ-aligns this right channel to its settled left
        // counterpart — landing its arrival exactly the scene offset ahead,
        // measured by envelope arrivals in the given band. Used as the search
        // prior (a gentle, polarity-blind pull toward the other side's timing
        // that breaks near-ties between lobes the junction sum cannot
        // distinguish) and as the pin of a scene lock, which measures the
        // LOCALIZATION sub-band of the pair's shared band rather than the full
        // intersection.
        //
        // COARSE means good only to a fraction of a millisecond: it may pin a
        // lobe but never the tight scene tolerance. TIGHTLOCK (quarter-period)
        // is set only by the last rung, when the pair's own arrivals were
        // unmeasurable and CORROBORATED donor geometry replaced them, so the
        // mode-shaped junction sum gets less in-lobe authority. Null when no
        // target could be trusted (silent band, low SNR) and the search keeps
        // its own-side anchor.
        (double TargetMs, bool Coarse, bool TightLock)? CrossSideTargetMs(
            IAlignmentChannel rightChannel,
            StereoPairLink link,
            double bandLowHz,
            double bandHighHz,
            double fallbackLowHz,
            double fallbackHighHz)
        {
            var searchAlignment =
                new Dictionary<IAlignmentChannel, AlignmentOverride>(alignment);
            searchAlignment.Remove(rightChannel);
            IReadOnlyList<AlignmentSnapshot> current = reprocess(searchAlignment);
            AlignmentSnapshot leftSnapshot =
                current.First(item => item.Channel == link.Left);
            AlignmentSnapshot rightSnapshot =
                current.First(item => item.Channel == rightChannel);
            Complex[] leftIr = leftSnapshot.ImpulseResponse;
            Complex[] rightIr = rightSnapshot.ImpulseResponse;

            // Both sides measured in one band, with a modal-latch guard. The
            // Latched flag separates "one side timed the wrong feature" (worth
            // retrying one band up) from "the band cannot be measured at all"
            // (silence or a too-narrow intersection — the link then stays
            // without a target).
            //
            // The SAME driver measured in the band's upper half must agree with
            // the full-band read to within the dispersion one direct wave packet
            // can show (half a period at the probe's low edge). A full-band read
            // landing far BEHIND its own upper-half read means the detector
            // latched that side onto the in-room modal build-up instead of the
            // direct rise (an under-seat midbass: 21.2 ms in 80-200 Hz against
            // 13.9 ms one band up); one landing far AHEAD means the upper half
            // timed some later feature — either way the two bands are not
            // looking at one wavefront and the certification fails. The narrow
            // upper half is NOT a substitute (at a low band it is an octave of
            // mush that can drag a woofer 6 ms off); it only votes on the full
            // band's honesty. Verified is the positive certificate: reads whose
            // probe was unmeasurable stay usable but UNVERIFIED, and the caller
            // must not grant them the tight scene lock.
            ((TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                Reads, bool Latched, bool Verified,
                bool LeftLatched, bool RightLatched,
                (TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                    FullReads) MeasureConsistent(
                    double lowHz, double highHz)
            {
                TimeAlignmentAnalysisResult left =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        leftIr, link.Left.SampleRate, lowHz, highHz,
                        leftSnapshot.ValidRange);
                TimeAlignmentAnalysisResult right =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        rightIr, rightChannel.SampleRate, lowHz, highHz,
                        rightSnapshot.ValidRange);
                if (!left.IsValid || !right.IsValid ||
                    left.SignalToNoiseDecibels < MinimumArrivalSnrDb ||
                    right.SignalToNoiseDecibels < MinimumArrivalSnrDb)
                {
                    return (null, false, false, false, false, null);
                }

                double probeLowHz = Math.Sqrt(lowHz * highHz);
                if (highHz <
                    probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
                {
                    return ((left, right), false, false, false, false, (left, right));
                }

                TimeAlignmentAnalysisResult leftProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        leftIr, link.Left.SampleRate, probeLowHz, highHz,
                        leftSnapshot.ValidRange);
                TimeAlignmentAnalysisResult rightProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        rightIr, rightChannel.SampleRate, probeLowHz, highHz,
                        rightSnapshot.ValidRange);

                // See ClassifyArrival for the direction semantics: LATCHED
                // poisons the read (ladder/donors), UNVERIFIED keeps it usable
                // without the certificate, VERIFIED on both sides earns it.
                ArrivalCertificate leftCertificate = ClassifyArrival(
                    left, leftProbe,
                    ArrivalProbeToleranceMs(
                        leftSnapshot, left.FirstArrivalDelayMilliseconds,
                        leftProbe.FirstArrivalDelayMilliseconds,
                        lowHz, probeLowHz, highHz));
                ArrivalCertificate rightCertificate = ClassifyArrival(
                    right, rightProbe,
                    ArrivalProbeToleranceMs(
                        rightSnapshot, right.FirstArrivalDelayMilliseconds,
                        rightProbe.FirstArrivalDelayMilliseconds,
                        lowHz, probeLowHz, highHz));
                if (leftCertificate == ArrivalCertificate.Latched ||
                    rightCertificate == ArrivalCertificate.Latched)
                {
                    bool leftLatched = leftCertificate == ArrivalCertificate.Latched;
                    log.AppendLine(
                        $"  cross-side link {rightChannel.Name}: " +
                        $"{(leftLatched ? link.Left.Name : rightChannel.Name)}" +
                        $" reads {(leftLatched ? left : right).FirstArrivalDelayMilliseconds:0.000} ms" +
                        $" in {lowHz:0}-{highHz:0} Hz but " +
                        $"{(leftLatched ? leftProbe : rightProbe).FirstArrivalDelayMilliseconds:0.000} ms" +
                        $" in its {probeLowHz:0}-{highHz:0} Hz half " +
                        "(modal latch: the sides time different features)");
                    return (null, true, false,
                        leftLatched,
                        rightCertificate == ArrivalCertificate.Latched,
                        (left, right));
                }

                return ((left, right), false,
                    leftCertificate == ArrivalCertificate.Verified &&
                    rightCertificate == ArrivalCertificate.Verified,
                    false, false, (left, right));
            }

            // The consistency ladder: the pair's own shared band first; when a
            // side LATCHED there, the channel's junction band — the engine
            // already trusts it for junction work, and the direct rise that hid
            // under a mode in the low link band is usually plain one octave up.
            // An unmeasurable link band (silence, too narrow) does NOT ladder:
            // the link was inadmissible, not mis-read. Only when both bands are
            // poisoned is the prior withdrawn and the search keeps its own-side
            // junction anchor.
            double usedLowHz = bandLowHz;
            double usedHighHz = bandHighHz;
            bool anyLatch;
            ((TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                Reads, bool Latched, bool Verified,
                bool LeftLatched, bool RightLatched,
                (TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                    FullReads) measured =
                MeasureConsistent(bandLowHz, bandHighHz);
            anyLatch = measured.Latched;
            bool fallbackDiffers =
                fallbackLowHz != bandLowHz || fallbackHighHz != bandHighHz;
            // The junction band doubles as the LINK CERTIFICATE'S WITNESS,
            // because the link certificate can self-verify inside the modal
            // region: the link band's upper half may sit under the same mode, so
            // full and probe agree on the mode and Verified is issued for a
            // latched read (a midbass link read 22.2 ms in 80-200 Hz, its
            // 126-200 Hz half saw the same hump, and the resulting -8.4 ms split
            // — no cabin geometry produces one — was scene-locked onto the right
            // midbass). The conviction does not transfer wholesale: the witness
            // only proves a mode somewhere below its own probe half, which may
            // lie below the link band entirely. So it convicts the link read
            // only when the SAME-FEATURE test holds — the latched side's link
            // read timed the very feature the witness convicted (the two reads
            // agree within the wavefront tolerance; the bands overlap and both
            // sit on one mode, so dispersion between them is small). A link read
            // that timed an earlier, different feature stands: the mode is
            // outside its band's reach. On conviction the link's split must not
            // reach the scene lock, and the pair descends the same ladder a
            // latched link band would.
            ((TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                Reads, bool Latched, bool Verified,
                bool LeftLatched, bool RightLatched,
                (TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                    FullReads)? junctionRung =
                fallbackDiffers && (measured.Reads != null || measured.Latched)
                    ? MeasureConsistent(fallbackLowHz, fallbackHighHz)
                    : null;
            if (measured.Reads == null && measured.Latched &&
                junctionRung is { } rung)
            {
                measured = rung;
                anyLatch |= rung.Latched;
                if (measured.Reads != null)
                {
                    usedLowHz = fallbackLowHz;
                    usedHighHz = fallbackHighHz;
                }
            }
            else if (measured.Reads is { } linkReads &&
                junctionRung is { Latched: true, FullReads: { } witnessReads } witness)
            {
                double witnessProbeLowHz = Math.Sqrt(fallbackLowHz * fallbackHighHz);
                double witnessToleranceMs = Math.Max(1.0, 500.0 / witnessProbeLowHz);
                bool linkTimedTheConvictedFeature =
                    (witness.LeftLatched && Math.Abs(
                        linkReads.Left.FirstArrivalDelayMilliseconds -
                        witnessReads.Left.FirstArrivalDelayMilliseconds) <=
                            witnessToleranceMs) ||
                    (witness.RightLatched && Math.Abs(
                        linkReads.Right.FirstArrivalDelayMilliseconds -
                        witnessReads.Right.FirstArrivalDelayMilliseconds) <=
                            witnessToleranceMs);
                if (linkTimedTheConvictedFeature)
                {
                    log.AppendLine(
                        $"  cross-side link {rightChannel.Name}: " +
                        $"{bandLowHz:0}-{bandHighHz:0} Hz read discarded — it timed " +
                        $"the same feature the junction band " +
                        $"{fallbackLowHz:0}-{fallbackHighHz:0} Hz convicts as a " +
                        "modal latch the link band's own probe cannot see");
                    measured = (null, true, false, false, false, null);
                    anyLatch = true;
                }
            }
            if (measured.Reads is not { } arrivals)
            {
                if (!anyLatch)
                {
                    // The link band itself was inadmissible (silent or too
                    // narrow): no target.
                    return null;
                }

                // The ladder's last rung: no band read both DIRECT rises, so
                // this pair's direct arrivals are UNMEASURABLE (a latch on at
                // least one side under a room mode). Neither side's energy peak
                // substitutes — the two can latch onto DIFFERENT modes and
                // fabricate a path no cabin geometry produces (measured 23.5 vs
                // 17.9 ms, a 5.6 ms "split" that dragged the right midbass past
                // the scene onto a junction notch). The pair is poisoned, but
                // the cabin's L/R GEOMETRY is often measurable on OTHER linked
                // pairs: each pair whose both sides read a clean direct arrival
                // gives an L/R split, and where several agree (mids +1.37 ms,
                // tweeters +1.41) that split is the cabin's L/R offset, so aim
                // the right delay at the settled left twin's minus it and the
                // scene. Not clean geometry in isolation, though — each split
                // also carries that donor pair's own L/R filter/driver asymmetry
                // — which is why corroboration across pairs (not one nearest
                // donor) earns the tight lock, a lone donor only a soft one, and
                // no agreement no pin at all.
                var donorSplits = new List<(double SplitMs, string Names)>();
                if (plan.PairLinks != null)
                {
                    static double LinkCenterHz(StereoPairLink item) =>
                        Math.Sqrt(item.BandLowHz * item.BandHighHz);
                    double centerHz = LinkCenterHz(link);
                    foreach (StereoPairLink other in plan.PairLinks
                        .Where(item => item != link)
                        .OrderBy(item =>
                            Math.Abs(Math.Log(LinkCenterHz(item) / centerHz))))
                    {
                        AlignmentSnapshot? otherLeft = current.FirstOrDefault(
                            item => item.Channel == other.Left);
                        AlignmentSnapshot? otherRight = current.FirstOrDefault(
                            item => item.Channel == other.Right);
                        double lowHz2 = other.BandLowHz;
                        double highHz2 = other.BandHighHz;
                        double probeLow2 = Math.Sqrt(lowHz2 * highHz2);
                        if (otherLeft == null || otherRight == null ||
                            highHz2 < probeLow2 *
                                VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
                        {
                            continue;
                        }

                        TimeAlignmentAnalysisResult Read(
                            AlignmentSnapshot side, double lo, double hi) =>
                            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                                side.ImpulseResponse, side.Channel.SampleRate,
                                lo, hi, side.ValidRange);

                        // A donor earns trust only if BOTH sides POSITIVELY read a
                        // clean direct arrival — full band AND its upper half
                        // valid, SNR-qualified, and agreeing. Absence of a proven
                        // latch is NOT proof of a direct read: an unmeasurable or
                        // low-SNR upper half leaves the split unverified, so skip.
                        bool CleanDirect(AlignmentSnapshot side, out double rawMs)
                        {
                            TimeAlignmentAnalysisResult full = Read(side, lowHz2, highHz2);
                            TimeAlignmentAnalysisResult probe = Read(side, probeLow2, highHz2);
                            double tolerance2 = ArrivalProbeToleranceMs(
                                side, full.FirstArrivalDelayMilliseconds,
                                probe.FirstArrivalDelayMilliseconds,
                                lowHz2, probeLow2, highHz2);
                            rawMs = full.FirstArrivalDelayMilliseconds
                                - alignment.GetValueOrDefault(side.Channel).DelayMs;
                            // A donor must be POSITIVELY clean: only a VERIFIED
                            // certificate counts (unverified or latched reads
                            // contribute no geometry).
                            return full.IsValid &&
                                full.SignalToNoiseDecibels >= MinimumArrivalSnrDb &&
                                ClassifyArrival(full, probe, tolerance2) ==
                                    ArrivalCertificate.Verified;
                        }

                        if (CleanDirect(otherLeft, out double rawLeft) &&
                            CleanDirect(otherRight, out double rawRight))
                        {
                            donorSplits.Add((rawRight - rawLeft,
                                $"{other.Left.Name}/{other.Right.Name}"));
                        }
                    }
                }

                (double PathSplitMs, CrossSideLockTier Tier, int Corroborating,
                    double ClusterLowMs, double ClusterHighMs) resolved =
                    ResolveLatchedPathSplit(
                        donorSplits.Select(item => item.SplitMs).ToList(),
                        CrossSideDonorAgreementMs);
                if (resolved.Tier == CrossSideLockTier.None)
                {
                    // No corroborated geometry: fabricating one (symmetry or a
                    // lone contradicted donor) and hard-locking to it is exactly
                    // the confidently-wrong pin this rung must not create. Drop
                    // the prior — the channel keeps its own-side junction search.
                    log.AppendLine(
                        $"  cross-side prior {rightChannel.Name}: withdrawn — " +
                        "direct arrivals unmeasurable and no linked pair gives a " +
                        (donorSplits.Count == 0
                            ? "clean L/R geometry reference"
                            : $"corroborated one ({donorSplits.Count} donor(s) disagree)"));
                    return null;
                }

                double leftDelayMs =
                    alignment.GetValueOrDefault(link.Left).DelayMs;
                double latchedTarget =
                    leftDelayMs - resolved.PathSplitMs - plan.SceneOffsetMs;
                bool tight = resolved.Tier == CrossSideLockTier.Tight;
                // Name only the donors in the WINNING cluster — the resolver
                // returns its [low, high] span, and a contiguous sorted window
                // holds exactly the donors in that range, so this excludes the
                // outliers the resolver rejected (unlike a distance-to-median
                // test, which can catch a split just outside the cluster).
                string donorNames = string.Join(", ", donorSplits
                    .Where(item => item.SplitMs >= resolved.ClusterLowMs &&
                        item.SplitMs <= resolved.ClusterHighMs)
                    .Select(item => item.Names));
                log.AppendLine(
                    $"  cross-side prior {rightChannel.Name}: target " +
                    $"{latchedTarget:0.000} ms — settled {link.Left.Name} shifted " +
                    $"by the {(tight ? $"{resolved.Corroborating}-pair corroborated" : "lone")} " +
                    $"L/R arrival split {resolved.PathSplitMs:+0.000;-0.000} ms from " +
                    $"{donorNames} (direct arrivals unmeasurable; " +
                    $"{(tight ? "quarter" : "half")}-period lock)");
                return (latchedTarget, true, tight);
            }

            // An UNVERIFIED read (the honesty probe could not run — band too
            // narrow, silent or low-SNR upper half) is still the best estimate
            // but no certificate: it pins only the LOBE (Coarse), never the
            // tight scene tolerance — the same standard the donor rung applies.
            double target = arrivals.Left.FirstArrivalDelayMilliseconds
                - plan.SceneOffsetMs
                - arrivals.Right.FirstArrivalDelayMilliseconds;
            log.AppendLine(
                $"  cross-side prior {rightChannel.Name}: target {target:0.000} ms " +
                $"(L arrival {arrivals.Left.FirstArrivalDelayMilliseconds:0.000}, " +
                $"raw R {arrivals.Right.FirstArrivalDelayMilliseconds:0.000} ms " +
                $"in {usedLowHz:0}-{usedHighHz:0} Hz" +
                $"{(measured.Verified ? "" : "; arrival not certified by the upper-half probe — lobe pin only")})");
            return (target, !measured.Verified, false);
        }

        void AlignRight(int index, int neighborIndex, AlignmentJunction pair)
        {
            IAlignmentChannel channel = rightByBand[index].Channel;
            IAlignmentChannel neighbor = rightByBand[neighborIndex].Channel;
            if (plan.MonoChannels.Contains(channel))
            {
                MeasureFixedJunction(pair, channel, neighbor, reprocess, alignment, log);
                return;
            }

            // The neighbor on the FAR side of the walk joins the search as a
            // second fixed reference when it is already final — during the
            // descent that is the shared mono channel below. Without it the
            // channel optimizes its junction toward the bridge and can park a
            // whole period off the junction it shares with the settled mono —
            // a perfect upper sum bought with a ruined subwoofer handover.
            IAlignmentChannel? secondary = null;
            AlignmentJunction? secondaryPair = null;
            int otherIndex = index + (index - neighborIndex);
            if (otherIndex >= 0 && otherIndex < rightByBand.Count &&
                plan.MonoChannels.Contains(rightByBand[otherIndex].Channel))
            {
                secondary = rightByBand[otherIndex].Channel;
                secondaryPair = plan.RightPairs[Math.Min(index, otherIndex)];
            }

            // The scene mandate: pairs reaching the localization region are
            // pinned to the cross-side target, measured in the localization
            // sub-band alone — the low end of a wide shared band (soft
            // envelopes, no localization) must not smear the pin. A pure
            // low-frequency pair is pinned too, but only to the LOBE: an
            // identical L/R driver pair's delay split is physical (path
            // difference), and a junction comb whose lobes differ by a dB must
            // not choose it — left unchecked it put one under-seat midbass at 0
            // and the other at 10.85 ms. The lock tolerance is half the period
            // of the tightest junction the channel searches against, so the sum
            // keeps full authority inside the arrival's lobe and none across.
            StereoPairLink? channelLink = plan.PairLinks?.FirstOrDefault(
                item => item.Right == channel);
            bool lockable = channelLink != null && IsSceneLockable(channelLink);
            (double TargetMs, bool Coarse, bool TightLock)? cross = channelLink == null
                ? null
                : CrossSideTargetMs(
                    channel,
                    channelLink,
                    lockable
                        ? Math.Max(channelLink.BandLowHz, SceneLockLocalizationLowHz)
                        : channelLink.BandLowHz,
                    channelLink.BandHighHz,
                    pair.BandLowHz,
                    pair.BandHighHz);
            double? crossTarget = cross?.TargetMs;
            // A corroborated-geometry latched target (TightLock) gets a QUARTER-
            // period lock instead of the usual half: the in-lobe authority the
            // wide lock grants the junction sum assumes the sum measures
            // direct-field summation, but where the pair's own direct arrivals
            // were unmeasurable the same room modes shape the sum too (measured:
            // its in-band optimum sat 0.6 ms past every geometry-consistent
            // point). There the multi-donor geometry deserves the larger say and
            // the sum still fine-tunes inside ±T/4. A lone (uncorroborated) donor
            // keeps the ordinary ±T/2.
            double? sceneLock = cross is not { } resolved
                ? null
                : lockable && !resolved.Coarse
                    ? SceneLockToleranceMs
                    : (resolved.TightLock ? 250.0 : 500.0) / Math.Max(
                        pair.CrossoverHz,
                        secondaryPair?.CrossoverHz ?? pair.CrossoverHz);

            // Polarity is a property of the DRIVER, not the side: a right channel
            // inherits the sign its left counterpart settled on (the two are the
            // same driver, wired the same), and only searches the delay. This makes
            // an asymmetric per-driver inversion — left mid flipped while right mid
            // is not — structurally impossible. The right top's sign is the one
            // exception: it is set by the bridge, the single global L/R link.
            bool? inheritedPolarity = channelLink == null
                ? null
                : alignment.TryGetValue(channelLink.Left, out AlignmentOverride leftSide)
                    ? leftSide.InvertPolarity
                    : false;

            AlignChannelAtJunction(
                channel, neighbor, pair,
                rightTimeline, allChannels, reprocess, alignment, log,
                secondary, secondaryPair, crossTarget, sceneLock, inheritedPolarity,
                rightUntrustedSeeds, onsetLocks, decisions, plan.MonoChannels,
                rightSeedPartnerReach);
        }
        for (int i = bridgeIndex - 1; i >= 0; i--)
        {
            AlignRight(i, i + 1, plan.RightPairs[i]);
        }
        for (int i = bridgeIndex + 1; i < rightByBand.Count; i++)
        {
            AlignRight(i, i - 1, plan.RightPairs[i - 1]);
        }

        // Scene-preserving re-balance: with right channels pinned to the
        // scene, their junction sums pay the price — moving BOTH sides of a
        // pair by one shared delta keeps the pair's L-R timing (the scene)
        // untouched while trading junction loss between the sides.
        RebalancePairsKeepingScene(
            plan, reprocess, alignment, log, onsetLocks, decisions);

        // A mono channel's own final polish: its delay (and polarity) is
        // scene-invariant by construction — one shared channel moves both
        // sides' handovers identically — so with everything else settled, the
        // best compromise across its left AND right junctions is searched
        // directly. This is the only pass where the right junction gets a
        // vote on the mono channel at all.
        ComoveMonoChannels(plan, reprocess, alignment, log, allChannels, decisions);

        // The far side's own last word: every far channel may leave its scene
        // position by at most FarSideJunctionPolishMs to buy its own junction
        // summation back — see the method for why the scene can afford it.
        PolishFarSideJunctions(
            plan, rightByBand, allChannels, reprocess, alignment, log, decisions);

        NormalizeAndVerifyFeasibility(allChannels, alignment, log);

        // The invariant the user requires of automatic delay: no driver is ever
        // inverted on one side of a pair alone.
        EnforcePolaritySymmetry(plan, alignment, log, decisions);

        NormalizePolarityPresentation(allChannels, alignment, log);
    }

    // Final normalization + the single feasibility gate. Normalization: the
    // smallest total latency that preserves every relation — the minimum
    // proposed delay lands exactly at zero; lifting a NEGATIVE minimum is as
    // legal as trimming a positive one (both are uniform), so transient
    // out-of-range values from the shift passes settle here. Feasibility: a
    // maximum past the DSP's delay ceiling after that means the proposal
    // PHYSICALLY does not fit — clamping one channel would silently break the
    // relative alignment (and the stereo scene), so the whole run refuses with
    // the reason instead.
    internal static void NormalizeAndVerifyFeasibility(
        IReadOnlyList<AlignmentSnapshot> scope,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
    {
        // Always rebase (even a sub-hundredth minimum: a tiny negative left in
        // the map would be an unrealizable delay), round onto the DSP's 0.01 ms
        // grid, and only then judge the range on the values actually proposed.
        // A channel with no entry and a zero result keeps NO entry — absence
        // means "nothing proposed" (the reference), and the rebase must not
        // manufacture zero-delay proposals for it.
        double minimum = scope.Min(
            item => alignment.GetValueOrDefault(item.Channel).DelayMs);
        foreach (AlignmentSnapshot item in scope)
        {
            bool hasEntry = alignment.TryGetValue(
                item.Channel, out AlignmentOverride current);
            double rebasedMs = Math.Round(current.DelayMs - minimum, 2);
            if (!hasEntry && rebasedMs == 0.0)
            {
                continue;
            }
            alignment[item.Channel] = current with { DelayMs = rebasedMs };
        }
        if (Math.Abs(minimum) > 0.005)
        {
            log.AppendLine(
                $"Normalized: {-minimum:+0.000;-0.000} ms to every channel " +
                "(minimum delay back to zero)");
        }

        AlignmentSnapshot widest = scope.MaxBy(
            item => alignment.GetValueOrDefault(item.Channel).DelayMs)!;
        double widestDelayMs = alignment.GetValueOrDefault(widest.Channel).DelayMs;
        if (widestDelayMs > MaxDelayMs + 0.005)
        {
            throw new InvalidOperationException(
                "The proposed alignment does not fit the DSP delay range: " +
                $"{widest.Channel.Name} needs {widestDelayMs:0.00} ms with the " +
                $"earliest channel at 0, but the limit is {MaxDelayMs:0} ms. " +
                "The measured spread between the earliest and latest channels " +
                "is wider than the DSP can realize.");
        }
    }

    // Post-pass bookkeeping for the user report: a pass that changes a
    // channel AFTER its decision was recorded appends what it did, so the
    // report's notes always describe the FINAL delay and polarity rather
    // than the intermediate walk result.
    private static void AmendDecision(
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions,
        IAlignmentChannel channel,
        string amendment)
    {
        if (decisions == null)
        {
            return;
        }

        AlignmentDecision existing = decisions.GetValueOrDefault(channel)
            ?? new AlignmentDecision(
                AlignmentDecisionKind.Search, Confidence: null, string.Empty);
        decisions[channel] = existing with
        {
            Detail = existing.Detail.Length > 0
                ? $"{existing.Detail}; {amendment}"
                : amendment
        };
    }

    /// <summary>
    /// The minimum record signal-to-noise (dB) a band-limited arrival must
    /// carry before an inter-side decision trusts it: the bridge arrivals,
    /// the cross-side descent targets, and the panel's final Δ L−R read-out
    /// all gate on this. Clean measurements run 40-70 dB; a figure below this
    /// is a mis-picked band or a broken capture, and the bridge is the one
    /// number that times a whole side.
    /// </summary>
    public const double MinimumArrivalSnrDb = 12;

    // The bridge-decision confidence bands over the weaker side's arrival
    // SNR: clean field measurements run 40-70 dB, so 30 dB still marks a
    // comfortably measured bridge, while anything within ~6 dB of the hard
    // refusal floor above deserves a wary Low.
    private const double BridgeHighSnrDb = 30;
    private const double BridgeMediumSnrDb = 18;

    // The scene mandate for the right descent: a channel whose pair band
    // reaches into the localization region is PINNED to the cross-side target
    // (the left counterpart's settled arrival minus the scene offset) and may
    // fine-tune its junction sum only within this tolerance — the stereo
    // image outranks the junction handover. Pairs living entirely below the
    // localization region (the woofers) are pinned more loosely, to the
    // arrival's LOBE (half the tightest adjacent junction period): the ear
    // does not localize there, but an identical driver pair's delay split is
    // still physical, and the junction comb must polish within that lobe,
    // not choose one. With no reliable cross-side arrival at all the free
    // joint-junction search remains.
    private const double SceneLockToleranceMs = 0.05;

    // How close two donor pairs' L/R arrival splits must sit to corroborate one
    // another as the cabin's L/R path offset. Drivers at different positions do
    // differ, so this is generous — the point is to reject a lone anomaly (a
    // donor whose split is dominated by ITS filter/driver asymmetry, not the
    // cabin geometry) while accepting the consistent offset genuinely shared
    // pairs read (v3: mids +1.37, tweeters +1.41).
    private const double CrossSideDonorAgreementMs = 0.6;

    // How a latched pair's cross-side target is resolved from the clean donor
    // pairs' L/R arrival splits (nearest-frequency first). Corroboration decides
    // trust: two-plus splits agreeing within CrossSideDonorAgreementMs are the
    // cabin's real L/R offset (tight quarter-period lock); a lone donor is one
    // estimate carrying its own DSP asymmetry (loose half-period lock); zero
    // donors or several that disagree mean the geometry is unknown and the pair
    // must NOT be pinned (no target — the free own-side search stands). Pure and
    // deterministic so it is unit-tested directly, unlike the arrival detectors.
    internal enum CrossSideLockTier { None, Loose, Tight }

    internal static (double PathSplitMs, CrossSideLockTier Tier, int Corroborating,
        double ClusterLowMs, double ClusterHighMs)
        ResolveLatchedPathSplit(
            IReadOnlyList<double> donorSplits, double agreementToleranceMs)
    {
        ArgumentNullException.ThrowIfNull(donorSplits);
        if (donorSplits.Count == 0)
        {
            return (0.0, CrossSideLockTier.None, 0, 0.0, 0.0);
        }
        if (donorSplits.Count == 1)
        {
            return (donorSplits[0], CrossSideLockTier.Loose, 1,
                donorSplits[0], donorSplits[0]);
        }

        // The largest window of splits MUTUALLY within tolerance (max − min ≤
        // tolerance), not merely within tolerance of one anchor: a chain like
        // 0.45 / 1.00 / 1.55 all sits within 0.6 of the middle yet spans 1.10,
        // and must not read as one agreeing cluster. A two-pointer sweep over
        // the sorted splits finds it in one pass.
        double[] sorted = donorSplits.OrderBy(split => split).ToArray();
        int bestCount = 0;
        int bestStart = 0;
        int windowsAtBest = 0;
        int end = 0;
        for (int start = 0; start < sorted.Length; start++)
        {
            if (end < start)
            {
                end = start;
            }
            while (end + 1 < sorted.Length &&
                sorted[end + 1] - sorted[start] <= agreementToleranceMs)
            {
                end++;
            }

            int count = end - start + 1;
            if (count > bestCount)
            {
                bestCount = count;
                bestStart = start;
                windowsAtBest = 1;
            }
            else if (count == bestCount)
            {
                windowsAtBest++;
            }
        }

        // A lone corroborated cluster is the geometry; a lone unpaired split, or
        // two equally-large clusters that disagree (windowsAtBest > 1), is not.
        if (bestCount < 2 || windowsAtBest > 1)
        {
            return (0.0, CrossSideLockTier.None, 0, 0.0, 0.0);
        }

        double[] cluster = sorted[bestStart..(bestStart + bestCount)];
        double median = cluster.Length % 2 == 1
            ? cluster[cluster.Length / 2]
            : 0.5 * (cluster[cluster.Length / 2 - 1] + cluster[cluster.Length / 2]);
        // The window is contiguous in the sorted splits, so [min, max] names its
        // members exactly — the caller filters donors by this range, not by
        // distance to the median (which can catch an out-of-cluster outlier).
        return (median, CrossSideLockTier.Tight, bestCount, cluster[0], cluster[^1]);
    }

    // The lower edge of the localization region. Only the part of a pair's
    // shared band ABOVE this edge carries scene information, so the lock's
    // cross-side target is measured in that sub-band — and a pair whose band
    // merely pokes past the edge (e.g. 80-310 Hz) has too little localizable
    // content to pin: the lock requires at least a third of an octave above
    // the edge, the same admission rule the arrival analysis itself applies.
    private const double SceneLockLocalizationLowHz = 300;

    // Whether a linked pair reaches far enough into the localization region
    // for the scene to outrank its junction sums: locked in the descent,
    // co-moved by the re-balance pass.
    private static bool IsSceneLockable(StereoPairLink link) =>
        link.BandHighHz >=
        Math.Max(link.BandLowHz, SceneLockLocalizationLowHz) *
        VirtualCrossoverAnalysis.MinimumArrivalBandRatio;

    // The scene-preserving re-balance pass: both sides of a pair may move by
    // the SAME delta (which leaves the pair's L-R timing untouched) to trade
    // junction loss between the sides. Bounded search, and a move must buy at
    // least the minimum gain in the mean adjacent-junction loss to apply.
    // This range is additionally capped per pair to half the period of its
    // tightest adjacent junction (see RebalancePairsKeepingScene): the flat
    // window alone let fraction-of-a-dB "gains" walk a tweeter pair a whole
    // comb lobe off its mid at a high junction.
    private const double PairComoveSearchRangeMs = 1.2;
    private const double PairComoveMinimumGainDb = 0.05;

    // The far-side junction polish (see PolishFarSideJunctions): how far a far
    // channel may leave the position the scene machinery assigned it, and the
    // least mean dip-penalized gain that buys a move. The budget is a phase
    // trim — 0.03 ms is a twentieth of a period at a 1.6 kHz junction and 17°
    // of phase, an order of magnitude under interaural blur — so the scene
    // stays perceptually intact while the far side's handovers stop paying
    // full price for it. The gain threshold sits below the co-move's 0.05 dB
    // deliberately: a 0.03 ms trim can only ever buy fractions of a dB, and a
    // threshold sized for half-period moves would leave the stage dead —
    // measured on the v6 cabin the honest gains ran 0.01-0.03 dB, so even
    // 0.02 dB refused every one of them.
    private const double FarSideJunctionPolishMs = 0.03;
    private const double FarSidePolishMinimumGainDb = 0.01;

    // The mono-channel co-move (see ComoveMonoChannels): the search spans a
    // full half period of the mono channel's tightest junction to each side, in
    // BOTH polarities. Unlike the pair co-move this deliberately reaches other
    // comb lobes, because a mono channel is timed by the left pass alone and the
    // walk's lobe choice never heard the right junction's vote — in the field
    // the sub/midbass junctions were near-tied on the left while the right
    // junction clearly preferred the flip partner a third of a period away. With
    // the polarity dimension the half-period window covers every lobe family
    // exactly once, judged by the mean of the two junctions.
    private const double MonoComoveSearchHalfPeriods = 1.0;

    // A mono lobe/polarity hop must be plainly better than the best IN-LOBE
    // polish: the window above deliberately opens the neighboring lobe family,
    // and the pair co-move's 0.05 dB application threshold is noise scale for
    // that decision — a false hop at a sub junction costs up to half a period
    // (~6 ms at 80 Hz) of bass attack. Field anchors: near-tied co-moves measure
    // 0.01-0.02 dB of "gain", while the two genuine two-sided lobe recoveries
    // measured 0.20 dB (v3 BW36, matching the owner's hand-tuned compromise) and
    // 1.36 dB (v2). 0.1 sits an order of magnitude above the noise ties and half
    // the smallest genuine recovery. Within the current lobe (same polarity,
    // inside the pair co-move's polish reach) the plain 0.05 dB still applies.
    private const double MonoComoveLobeHopMarginDb = 0.1;

    // The sub-band deficit that vetoes a mono lobe/polarity hop even after it
    // cleared the margin above. The full-band mean cannot tell a genuine
    // recovery from a comb impostor flattered by an in-room mode: a lobe a whole
    // period off fits the phase only where its period matches the frequency, and
    // a mode INSIDE the band can put more summed energy behind that impostor
    // than behind the direct sound. The cross-check every narrow-band ranging
    // discipline converges on (sub-band GCC, multi-scale FWI, GPS widelane
    // ambiguity resolution) is consistency ACROSS sub-bands: the true alignment
    // holds in every half of the junction band, the impostor wins one half and
    // loses the other. Field anchors (v3, 80 Hz sub junctions): the false
    // full-period hop "gained" 1.43 dB full-band while losing the clean
    // 40-80 Hz half by 0.29 dB; the genuine recovery's worst half-band deficit
    // was 0.02 dB, the co-move's own noise-tie scale. 0.1 sits between.
    private const double MonoComoveSubBandVetoMarginDb = 0.1;

    // The right bridge top inherits the left top's sign (set before the right
    // walk, so the right lowers align against a correctly-signed top). Automatic
    // delay never inverts one side of a pair alone: a driver's polarity is a
    // property of the driver, decided once on the left and mirrored to the
    // right. There is deliberately no sum-loss "which polarity fits better"
    // guess — at high frequencies two spatially-separated tops comb-filter, and
    // the guess is noise-driven enough to invert an identical off-axis right
    // tweeter alone.
    private static void InheritBridgePolarity(
        StereoAlignmentPlan plan,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        bool leftInvert = alignment.GetValueOrDefault(plan.BridgeLeft).InvertPolarity;
        AlignmentOverride top = alignment.GetValueOrDefault(plan.BridgeRight);
        alignment[plan.BridgeRight] = top with { InvertPolarity = leftInvert };
        log.AppendLine(
            $"  bridge polarity: {(leftInvert ? "inverted" : "normal")} " +
            $"(inherited from {plan.BridgeLeft.Name}; auto delay keeps L/R polarity symmetric)");
        if (leftInvert != top.InvertPolarity)
        {
            AmendDecision(
                decisions, plan.BridgeRight,
                $"polarity inherited from {plan.BridgeLeft.Name}");
        }
    }

    // Final guarantee for automatic delay: every right driver's polarity flag equals
    // its left counterpart's, so the auto never inverts one side of a pair alone.
    // This is redundant with the per-driver inheritance (the bridge top above, each
    // lower right driver via its forced polarity) but states the invariant in one
    // explicit, testable place. A MANUAL polarity flip in the UI is untouched — this
    // only governs what the auto-delay proposal writes.
    private static void EnforcePolaritySymmetry(
        StereoAlignmentPlan plan,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        void Mirror(IAlignmentChannel left, IAlignmentChannel right)
        {
            if (ReferenceEquals(left, right))
            {
                return; // a shared mono channel carries one polarity by construction
            }

            bool leftInvert = alignment.GetValueOrDefault(left).InvertPolarity;
            AlignmentOverride current = alignment.GetValueOrDefault(right);
            if (current.InvertPolarity != leftInvert)
            {
                alignment[right] = current with { InvertPolarity = leftInvert };
                log.AppendLine(
                    $"  polarity symmetry: {right.Name} -> " +
                    $"{(leftInvert ? "inverted" : "normal")} to match {left.Name}");
                AmendDecision(
                    decisions, right, $"polarity mirrored from {left.Name}");
            }
        }

        Mirror(plan.BridgeLeft, plan.BridgeRight);
        if (plan.PairLinks != null)
        {
            foreach (StereoPairLink link in plan.PairLinks)
            {
                Mirror(link.Left, link.Right);
            }
        }
    }

    // Moves both sides of one linked pair by the same delta — the pair's L-R
    // timing (the scene) is invariant under a co-move — searching for the
    // delta that minimizes the mean loss of the REFERENCE side's junctions
    // adjacent to the pair, bounded by the junctions of both sides. The far
    // side is what the scene mandate pinned off its own junction optimum, and
    // this is the only lever that can recover junction quality without
    // touching the image — but it may not buy that recovery with the near
    // side's alignment (see the evaluator loop). Top pair first, so lower
    // pairs re-balance
    // against the already-settled uppers. The scan is analytic: ONE reprocess
    // fixes the pair's current responses, each junction gets its gated
    // spectra built once, and every probed delta is an e^{-jωΔ} rotation of
    // the moving channel — the probe loop runs no DSP chains at all.
    private static void RebalancePairsKeepingScene(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        IReadOnlyDictionary<AlignmentJunction, OnsetLockState> onsetLocks,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        if (plan.PairLinks == null)
        {
            return;
        }

        // The co-move delta already applied to each channel, so a lower pair's
        // reach is bounded relative to its already-settled neighbor — NOT
        // relative to zero. Each per-pair move is capped at half a junction
        // period, but two adjacent pairs moving that far in opposite directions
        // would open a FULL period across their shared junction (a comb alias:
        // the sum is back in phase, so the search sees no loss, yet the absolute
        // alignment jumped a lobe). Constraining each pair's window around the
        // neighbor's applied delta keeps the RELATIVE shift across every shared
        // junction within half a period, which is what the reach cap must mean.
        var comoveDeltas = new Dictionary<IAlignmentChannel, double>();

        // Every linked pair participates: the scene-locked ones paid their
        // junction sums to the stereo image, the low-frequency ones to the
        // arrival-lobe pin — co-moving both sides by one delta repairs those
        // junctions without touching what the pin bought.
        foreach (StereoPairLink link in plan.PairLinks
            .OrderByDescending(item => item.BandHighHz))
        {
            AlignmentOverride leftOverride = alignment.GetValueOrDefault(link.Left);
            AlignmentOverride rightOverride = alignment.GetValueOrDefault(link.Right);

            // Every junction the pair's channels take part in, on either side:
            // BOTH sides bound how far the shared delta may travel (a move that
            // leaves the far side a lobe off its own neighbour is not a
            // candidate at all), while only the reference side's junctions
            // score it — see the evaluator loop below.
            List<AlignmentJunction> referenceAdjacent = plan.LeftPairs
                .Where(pair => pair.Lower.Channel == link.Left ||
                    pair.Upper.Channel == link.Left)
                .ToList();
            List<AlignmentJunction> adjacent = referenceAdjacent
                .Concat(plan.RightPairs.Where(pair =>
                    pair.Lower.Channel == link.Right ||
                    pair.Upper.Channel == link.Right))
                .ToList();
            if (referenceAdjacent.Count == 0)
            {
                continue;
            }

            // A pair bordering the shared mono channel is not co-moved: the
            // mono is timed by the LEFT pass alone (a pinned invariant — the
            // sub/left-woofer relation must match a left-only run exactly),
            // and a shared shift of the pair would silently re-time the left
            // side against it.
            if (adjacent.Any(junction =>
                plan.MonoChannels.Contains(junction.Lower.Channel) ||
                plan.MonoChannels.Contains(junction.Upper.Channel)))
            {
                continue;
            }

            IReadOnlyList<AlignmentSnapshot> current = reprocess(alignment);
            AlignmentSnapshot SnapshotOf(IAlignmentChannel channel) =>
                current.First(item => item.Channel == channel);
            Complex[] IrOf(IAlignmentChannel channel) =>
                SnapshotOf(channel).ImpulseResponse;
                ValidSampleRange RangeOf(IAlignmentChannel channel) =>
                    current.First(item => item.Channel == channel).ValidRange;

            // One evaluator per junction of the REFERENCE side. The move is
            // shared, so its criterion is a choice, and a mean over both sides
            // buys the far side's junction with the near one's: the delta that
            // wins the average can leave the reference side worse than the
            // cascade left it. The reference side is the one whose drivers sit
            // closest to the listener, where a timing error is easiest to hear,
            // so it is the side the shared move must not spend. The far side
            // still follows the delta (the scene is preserved either way) and
            // is still BOUNDED by its own junctions below — it just does not
            // get a vote on where the pair lands.
            var evaluators = new List<VirtualCrossoverAnalysis.SumLossEvaluator>();
            foreach (AlignmentJunction junction in referenceAdjacent)
            {
                // These are the reference side's junctions, so the moving end
                // is always the link's reference-side member.
                bool lowerMoves = junction.Lower.Channel == link.Left;
                IAlignmentChannel mover = lowerMoves
                    ? junction.Lower.Channel
                    : junction.Upper.Channel;
                IAlignmentChannel neighbor = lowerMoves
                    ? junction.Upper.Channel
                    : junction.Lower.Channel;
                // This junction's own window, held for every delta the pass
                // probes: it re-judges settled pairs, and it moves channels
                // for as little as the co-move threshold, so a window that
                // shifted between two probes would be exactly the size of
                // change this pass can make. Rebuilt from `current` rather
                // than carried from the search that settled the pair — the
                // fronts it reads have since moved with the delays the
                // cascade assigned, and the criterion is a difference over
                // THIS pass's probes, all of which see this one window.
                VirtualCrossoverAnalysis.SumLossEvaluator? evaluator =
                    VirtualCrossoverAnalysis.SumLossEvaluator.Create(
                        IrOf(mover),
                        new List<Complex[]> { IrOf(neighbor) },
                        mover.SampleRate,
                        junction.BandLowHz,
                        junction.BandHighHz,
                        levelMatch: true,
                        requireDelayEvidence: true,
                        gateAnchorSample: null,
                        RangeOf(mover),
                        new[] { RangeOf(neighbor) });
                if (evaluator != null)
                {
                    evaluators.Add(evaluator);
                }
            }
            if (evaluators.Count == 0)
            {
                continue;
            }

            double Score(double deltaMs)
            {
                double total = 0;
                foreach (VirtualCrossoverAnalysis.SumLossEvaluator evaluator
                    in evaluators)
                {
                    (double lossDb, double dipDb) = evaluator.Evaluate(deltaMs);
                    // The same dip-excess penalty the candidate scores carry:
                    // a mean of averages alone would happily buy a hundredth
                    // of a dB with a deep narrow cancellation notch on the
                    // other side's junction.
                    total += lossDb +
                        VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                        (dipDb - lossDb);
                }

                return total / evaluators.Count;
            }

            // Each adjacent junction bounds the pair's reach to within half its
            // period OF THE NEIGHBOR'S ALREADY-APPLIED co-move delta: within
            // half a period the junction sums are single-lobed, so the search
            // can only polish the alignment the arrival-anchored walk chose.
            // Past that lies the next comb lobe, and fractions of a dB of mean
            // junction loss cannot choose a lobe (the same physics as the
            // wide-window promotion reach cap). Centering on the neighbor's
            // delta (0 for a channel that never co-moves — a mono/fixed
            // neighbor) is what keeps the RELATIVE shift across the junction
            // bounded once the neighbor pair has moved: a flat ±half period
            // around zero lets two adjacent pairs drift a full period apart, and
            // a 0.1-0.2 dB "gain" walk the tweeter pair a whole period off its
            // mid at a 2.3 kHz junction.
            double lobeLowMs = -PairComoveSearchRangeMs;
            double lobeHighMs = PairComoveSearchRangeMs;
            foreach (AlignmentJunction junction in adjacent)
            {
                bool lowerIsMover = junction.Lower.Channel == link.Left ||
                    junction.Lower.Channel == link.Right;
                IAlignmentChannel mover = lowerIsMover
                    ? junction.Lower.Channel
                    : junction.Upper.Channel;
                IAlignmentChannel neighbor = lowerIsMover
                    ? junction.Upper.Channel
                    : junction.Lower.Channel;
                double neighborDelta = comoveDeltas.GetValueOrDefault(neighbor);
                double halfPeriodMs = 500.0 / junction.CrossoverHz;
                lobeLowMs = Math.Max(lobeLowMs, neighborDelta - halfPeriodMs);
                lobeHighMs = Math.Min(lobeHighMs, neighborDelta + halfPeriodMs);

                // An onset-locked junction bounds the move by its remaining
                // front slack, not just the lobe: the fine search honored
                // |gap| <= cap and the co-move must keep honoring it. The gap
                // was stored relative to the searched channel, so the sign of
                // this pair's contribution depends on which end is moving:
                // gap_after = gap ± (delta − neighborDelta).
                if (onsetLocks.TryGetValue(junction, out OnsetLockState? locked))
                {
                    bool moverWasSearched =
                        ReferenceEquals(locked.SearchedChannel, mover);
                    double slackLow = moverWasSearched
                        ? neighborDelta - locked.CapMs - locked.GapMs
                        : neighborDelta + locked.GapMs - locked.CapMs;
                    double slackHigh = moverWasSearched
                        ? neighborDelta + locked.CapMs - locked.GapMs
                        : neighborDelta + locked.GapMs + locked.CapMs;
                    lobeLowMs = Math.Max(lobeLowMs, slackLow);
                    lobeHighMs = Math.Min(lobeHighMs, slackHigh);
                }
            }

            // Both bounds are fixed BEFORE the search so the winning delta
            // applies verbatim to both sides (clamping after the fact would
            // move the two sides unequally and silently bend the very scene
            // this pass exists to preserve). The move is RELATIVE — the pair
            // against the rest of the field — so absolute positions are not
            // walls: the same relative placement is reachable via a uniform
            // rebase of everyone, and the bounds only close where the WHOLE
            // field would run out of the DSP's range. Two plans differing by
            // nothing but a global offset must co-move to the same relative
            // answer (the mono co-move already works in this frame).
            double pairMinMs = Math.Min(leftOverride.DelayMs, rightOverride.DelayMs);
            double pairMaxMs = Math.Max(leftOverride.DelayMs, rightOverride.DelayMs);
            List<IAlignmentChannel> fieldOthers = plan.LeftChannelsByBand
                .Concat(plan.RightChannelsByBand)
                .Select(item => item.Channel)
                .Where(channel => channel != link.Left && channel != link.Right)
                .Distinct()
                .ToList();
            double minDelta = lobeLowMs;
            double maxDelta = lobeHighMs;
            if (fieldOthers.Count > 0)
            {
                double maxOtherMs = fieldOthers.Max(
                    channel => alignment.GetValueOrDefault(channel).DelayMs);
                double minOtherMs = fieldOthers.Min(
                    channel => alignment.GetValueOrDefault(channel).DelayMs);
                minDelta = Math.Max(
                    minDelta, -pairMinMs - (MaxDelayMs - maxOtherMs));
                maxDelta = Math.Min(
                    maxDelta, MaxDelayMs - pairMaxMs + minOtherMs);
            }
            // The neighbor lobes can, in principle, exclude zero (a settled
            // neighbor a hair over half a period away); never let the window
            // invert or force a non-zero move — keeping the pair is always legal.
            minDelta = Math.Min(minDelta, 0.0);
            maxDelta = Math.Max(maxDelta, 0.0);
            double baseline = Score(0);
            double bestDelta = 0;
            double bestScore = baseline;
            // The coarse step scales down with the window so a tightly-capped
            // high-junction pair still gets a real grid before refinement.
            double coarseStep = Math.Min(
                0.1, Math.Max(0.02, (maxDelta - minDelta) / 8.0));
            for (double delta = minDelta; delta <= maxDelta + 1e-9; delta += coarseStep)
            {
                double score = Score(delta);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDelta = delta;
                }
            }
            for (double delta = Math.Max(minDelta, bestDelta - coarseStep);
                delta <= Math.Min(maxDelta, bestDelta + coarseStep) + 1e-9;
                delta += 0.02)
            {
                double score = Score(delta);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDelta = delta;
                }
            }

            if (bestDelta != 0 && bestScore > baseline + PairComoveMinimumGainDb)
            {
                // Rounded toward the window so the rounding itself cannot
                // step past a bound the search respected.
                bestDelta = Math.Clamp(Math.Round(bestDelta, 2),
                    Math.Ceiling(minDelta * 100) / 100,
                    Math.Floor(maxDelta * 100) / 100);
                alignment[link.Left] = leftOverride with
                {
                    DelayMs = Math.Round(leftOverride.DelayMs + bestDelta, 2)
                };
                alignment[link.Right] = rightOverride with
                {
                    DelayMs = Math.Round(rightOverride.DelayMs + bestDelta, 2)
                };
                // Record the applied shift so a lower pair's reach is measured
                // from here, keeping the relative shift across the shared
                // junction within half a period.
                comoveDeltas[link.Left] = bestDelta;
                comoveDeltas[link.Right] = bestDelta;
                log.AppendLine(
                    $"Co-move {link.Left.Name}+{link.Right.Name}: " +
                    $"{bestDelta:+0.00;-0.00} ms to both sides " +
                    $"(reference-side dip-penalized junction loss " +
                    $"{baseline:0.00} -> {bestScore:0.00} dB; scene untouched)");
                // The move is a bounded in-lobe polish (the lobe decision the
                // recorded confidence describes stands), but the final delays
                // differ from the walk's — the report must say so.
                string pairAmendment = FormattableString.Invariant(
                    $"pair co-move {bestDelta:+0.00;-0.00} ms (scene kept)");
                AmendDecision(decisions, link.Left, pairAmendment);
                AmendDecision(decisions, link.Right, pairAmendment);
            }
            else
            {
                log.AppendLine(
                    $"Co-move {link.Left.Name}+{link.Right.Name}: kept " +
                    $"(best gain {bestScore - baseline:0.00} dB below the " +
                    $"{PairComoveMinimumGainDb:0.00} dB threshold)");
            }
        }
    }

    // The far-side junction polish: the scene machinery pinned every far
    // channel — the bridge by arithmetic with no search at all, the rest by
    // scene locks around the cross-side target — and their OWN junction sums
    // paid for it. With everything settled, each far channel may now leave its
    // scene position by at most FarSideJunctionPolishMs to buy that summation
    // back, judged by the mean dip-penalized loss of its own adjacent far-side
    // junctions. The budget cannot hop a lobe (a twentieth of a period at the
    // junctions it matters for), cannot smear the image (an order of magnitude
    // under interaural blur), and is spent per channel from its scene position
    // — one pass in band order from the bridge down, each channel judged
    // against neighbors that already carry their own polish. Mono channels are
    // shared with the reference side and never move here.
    // Internal so the tests can pin the budget, the threshold and the mono
    // exclusion directly, the way ComoveMonoChannels is pinned.
    internal static void PolishFarSideJunctions(
        StereoAlignmentPlan plan,
        IReadOnlyList<AlignmentSnapshot> rightByBand,
        IReadOnlyList<AlignmentSnapshot> fullScope,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions)
    {
        foreach (AlignmentSnapshot entry in rightByBand
            .OrderByDescending(item => plan.RightPairs
                .Where(pair => pair.Lower.Channel == item.Channel ||
                    pair.Upper.Channel == item.Channel)
                .Select(pair => pair.CrossoverHz)
                .DefaultIfEmpty(0)
                .Max()))
        {
            IAlignmentChannel channel = entry.Channel;
            if (plan.MonoChannels.Contains(channel))
            {
                continue;
            }

            List<AlignmentJunction> adjacent = plan.RightPairs
                .Where(pair => pair.Lower.Channel == channel ||
                    pair.Upper.Channel == channel)
                .ToList();
            if (adjacent.Count == 0)
            {
                continue;
            }

            AlignmentOverride current = alignment.GetValueOrDefault(channel);
            IReadOnlyList<AlignmentSnapshot> snapshots = reprocess(alignment);
            AlignmentSnapshot SnapshotOf(IAlignmentChannel member) =>
                snapshots.First(item => item.Channel == member);

            var evaluators = new List<VirtualCrossoverAnalysis.SumLossEvaluator>();
            foreach (AlignmentJunction junction in adjacent)
            {
                IAlignmentChannel neighbor = junction.Lower.Channel == channel
                    ? junction.Upper.Channel
                    : junction.Lower.Channel;
                VirtualCrossoverAnalysis.SumLossEvaluator? evaluator =
                    VirtualCrossoverAnalysis.SumLossEvaluator.Create(
                        SnapshotOf(channel).ImpulseResponse,
                        new List<Complex[]>
                        {
                            SnapshotOf(neighbor).ImpulseResponse
                        },
                        channel.SampleRate,
                        junction.BandLowHz,
                        junction.BandHighHz,
                        levelMatch: true,
                        requireDelayEvidence: true,
                        gateAnchorSample: null,
                        SnapshotOf(channel).ValidRange,
                        new[] { SnapshotOf(neighbor).ValidRange });
                if (evaluator != null)
                {
                    evaluators.Add(evaluator);
                }
            }
            if (evaluators.Count == 0)
            {
                continue;
            }

            double Score(double deltaMs)
            {
                double total = 0;
                foreach (VirtualCrossoverAnalysis.SumLossEvaluator evaluator
                    in evaluators)
                {
                    (double lossDb, double dipDb) = evaluator.Evaluate(deltaMs);
                    total += lossDb +
                        VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                        (dipDb - lossDb);
                }

                return total / evaluators.Count;
            }

            // The span NormalizeAndVerifyFeasibility will judge, held by the
            // REST of the field: it rebases on the earliest channel and then
            // measures to the latest, so a trial has to be checked from both
            // ends. Moving this channel later can outrun the earliest one, and
            // moving it earlier can outrun the latest — a polish on the field's
            // own minimum widens the spread just as surely as one on its
            // maximum. Read per channel, since an earlier polish may already
            // have moved either end.
            List<double> othersMs = fullScope
                .Where(item => item.Channel != channel)
                .Select(item => alignment.GetValueOrDefault(item.Channel).DelayMs)
                .ToList();
            double othersMinMs = othersMs.Count > 0
                ? othersMs.Min()
                : double.PositiveInfinity;
            double othersMaxMs = othersMs.Count > 0
                ? othersMs.Max()
                : double.NegativeInfinity;
            double baseline = Score(0);
            double bestDelta = 0;
            double bestScore = baseline;
            // The DSP's own 0.01 ms grid: the applied delay is rounded onto it
            // anyway, so probing between its points would report gains no
            // realizable delay can collect.
            for (double delta = -FarSideJunctionPolishMs;
                delta <= FarSideJunctionPolishMs + 1e-9;
                delta += 0.01)
            {
                double trialMs = current.DelayMs + delta;
                if (trialMs < 0 ||
                    Math.Max(othersMaxMs, trialMs) -
                        Math.Min(othersMinMs, trialMs) > MaxDelayMs)
                {
                    // Delays are non-negative and a polish never earns a
                    // uniform shift of the whole field. Nor may it widen the
                    // spread past what the DSP can realize: this is the only
                    // pass that moves a channel after the cascade has settled,
                    // so on a system already at the range limit a hundredth of
                    // a decibel would otherwise turn a valid proposal into the
                    // feasibility check's refusal.
                    continue;
                }

                double score = Score(delta);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDelta = delta;
                }
            }

            if (bestDelta != 0 && bestScore > baseline + FarSidePolishMinimumGainDb)
            {
                alignment[channel] = current with
                {
                    DelayMs = Math.Round(current.DelayMs + bestDelta, 2)
                };
                log.AppendLine(
                    $"Far-side polish {channel.Name}: " +
                    $"{bestDelta:+0.00;-0.00} ms off the scene position " +
                    $"(own-junction dip-penalized loss " +
                    $"{baseline:0.00} -> {bestScore:0.00} dB)");
                string amendment = FormattableString.Invariant(
                    $"far-side polish {bestDelta:+0.00;-0.00} ms (scene spent, <= ") +
                    FormattableString.Invariant($"{FarSideJunctionPolishMs:0.00} ms)");
                AmendDecision(decisions, channel, amendment);
            }
            else
            {
                log.AppendLine(
                    $"Far-side polish {channel.Name}: kept " +
                    $"(best gain {bestScore - baseline:0.00} dB below the " +
                    $"{FarSidePolishMinimumGainDb:0.00} dB threshold)");
            }
        }
    }

    // The mono-channel co-move: a mono channel (the shared subwoofer) is timed
    // by the LEFT pass and the right descent treats it as fixed, so the lobe
    // the walk chose only ever heard the left junction's vote. Moving or
    // flipping ONE mono channel cannot touch any pair's L-R timing — the
    // scene is invariant by construction — so the final polish sweeps its
    // delay across ± MonoComoveSearchHalfPeriods of its tightest junction
    // period, in both polarities, and keeps the best MEAN dip-penalized loss
    // over its junctions on the two sides: the compromise a user would
    // otherwise dial in by hand. Every probe is an HONEST reprocess of the
    // mono channel (chain re-applied at the probed delay/polarity, gates
    // re-anchored), not a spectrum rotation: at multi-millisecond deltas the
    // rotation probe's fixed gate anchoring misgrades candidates by whole dB,
    // and a lobe decision must not ride on that error.
    // Internal so the tests can pin the pass's coordinate handling directly
    // (the absolute-offset invariance below has no black-box lever).
    internal static void ComoveMonoChannels(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        IReadOnlyList<AlignmentSnapshot> shiftScope,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        foreach (IAlignmentChannel mono in plan.MonoChannels)
        {
            // The mono channel's junctions, one per side. A junction of the
            // left walk and its same-fc twin from the right list differ in the
            // NEIGHBOR channel, which is what matters here.
            List<AlignmentJunction> junctions = plan.LeftPairs
                .Concat(plan.RightPairs)
                .Where(pair => pair.Lower.Channel == mono ||
                    pair.Upper.Channel == mono)
                .Distinct()
                .ToList();
            if (junctions.Count < 2)
            {
                // A single junction had its full say during the walk; there is
                // no second side to compromise with.
                continue;
            }

            // Every junction must hold delay EVIDENCE on its own before the
            // co-move may judge a compromise. The walk certified the LEFT
            // junction individually, but the right sub junction never faced the
            // structure gate alone — the descent searches a COMBINED band whose
            // united fixed sum can hide an evidence-less sub junction behind a
            // healthy upper one. A co-move judged by the measurable side alone
            // would merely re-optimize the left junction the walk already
            // settled, so with any junction unmeasurable the whole co-move
            // abstains and the walk's placement stands.
            //
            // One render, one evaluator per junction (and one per half-band
            // for the veto below): every probe of the grid rotates the mono
            // channel's windowed cut through the SAME spectra
            // (VirtualCrossoverAnalysis.SumLossEvaluator — the same probe the
            // pair co-move and the drawn junction surface read). The windows
            // travel with the channels they hold, so a probe cannot slide the
            // mono out of anyone's window: evidence, level match and window
            // placement are all fixed properties of this one render, exactly
            // the consistency the old per-probe re-rendering bought with ~40
            // reprocesses and still lost whenever a probe carried the mono
            // into the fixed window's fade.
            IReadOnlyList<AlignmentSnapshot> certified = reprocess(alignment);
            Complex[] CertifiedIrOf(IAlignmentChannel channel) =>
                certified.First(item => item.Channel == channel).ImpulseResponse;
            ValidSampleRange CertifiedRangeOf(IAlignmentChannel channel) =>
                certified.First(item => item.Channel == channel).ValidRange;
            VirtualCrossoverAnalysis.SumLossEvaluator? Probe(
                AlignmentJunction junction, double lowHz, double highHz)
            {
                IAlignmentChannel neighbor = junction.Lower.Channel == mono
                    ? junction.Upper.Channel
                    : junction.Lower.Channel;
                return VirtualCrossoverAnalysis.SumLossEvaluator.Create(
                    CertifiedIrOf(mono),
                    new List<Complex[]> { CertifiedIrOf(neighbor) },
                    mono.SampleRate,
                    lowHz,
                    highHz,
                    levelMatch: true,
                    requireDelayEvidence: true,
                    gateAnchorSample: null,
                    CertifiedRangeOf(mono),
                    new[] { CertifiedRangeOf(neighbor) });
            }

            var fullBand =
                new Dictionary<AlignmentJunction,
                    VirtualCrossoverAnalysis.SumLossEvaluator>();
            AlignmentJunction? unmeasurable = null;
            foreach (AlignmentJunction junction in junctions)
            {
                if (Probe(junction, junction.BandLowHz, junction.BandHighHz)
                    is { } evaluator)
                {
                    fullBand[junction] = evaluator;
                }
                else
                {
                    unmeasurable = junction;
                    break;
                }
            }
            if (unmeasurable is { } silent)
            {
                IAlignmentChannel silentNeighbor =
                    silent.Lower.Channel == mono
                        ? silent.Upper.Channel
                        : silent.Lower.Channel;
                log.AppendLine(
                    $"  mono co-move skipped for {mono.Name}: the junction vs " +
                    $"{silentNeighbor.Name} in " +
                    $"{silent.BandLowHz:0}-{silent.BandHighHz:0} Hz holds no " +
                    "delay evidence — a compromise cannot be judged with one " +
                    "side unmeasurable.");
                continue;
            }

            AlignmentOverride over = alignment.GetValueOrDefault(mono);
            double halfPeriodMs = junctions.Min(pair => 500.0 / pair.CrossoverHz);
            double reachMs = MonoComoveSearchHalfPeriods * halfPeriodMs;

            // The move is RELATIVE: the mono channel against the rest of the
            // field. Its own delay hitting zero (or the ceiling) is not a
            // wall, because the same relative placement is reachable by
            // shifting every OTHER channel the opposite way together — a
            // uniform shift of the rest preserves the scene and every
            // non-mono junction. So the bounds only close where the WHOLE
            // field runs out of room: two plans that differ by nothing but a
            // global offset must co-move to the same relative answer.
            List<IAlignmentChannel> others = shiftScope
                .Select(item => item.Channel)
                .Where(channel => channel != mono)
                .Distinct()
                .ToList();
            double maxOtherMs = others.Max(
                channel => alignment.GetValueOrDefault(channel).DelayMs);
            double minOtherMs = others.Min(
                channel => alignment.GetValueOrDefault(channel).DelayMs);
            double minDelta = Math.Max(
                -reachMs, -over.DelayMs - (MaxDelayMs - maxOtherMs));
            double maxDelta = Math.Min(
                reachMs, MaxDelayMs - over.DelayMs + minOtherMs);

            double Score(double deltaMs, bool flip)
            {
                // The mean the co-move optimizes: every junction's rotation
                // probe at this delta, dip-penalized like every other junction
                // score. No junction can drop out mid-sweep — measurability
                // was settled once, above — so the mean is over all of them
                // by construction.
                double total = 0;
                foreach (AlignmentJunction junction in junctions)
                {
                    (double lossDb, double dipDb) =
                        fullBand[junction].Evaluate(deltaMs, flip);
                    total += lossDb +
                        VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                        (dipDb - lossDb);
                }

                return total / junctions.Count;
            }

            double baseline = Score(0, flip: false);
            if (double.IsNegativeInfinity(baseline))
            {
                continue;
            }

            // Every probe re-renders the mono channel, so the grid is kept
            // lean: a half-millisecond coarse pass over both polarities, then
            // two shrinking refinements — ~40 reprocesses for the widest
            // (80 Hz) junction window, each costing one channel's chain (the
            // others are cache hits). The polish candidate (same polarity,
            // within the pair co-move's reach — the current lobe) is tracked
            // separately: it is what a lobe/polarity hop must PLAINLY beat.
            double polishReachMs = Math.Min(PairComoveSearchRangeMs, halfPeriodMs);
            bool IsPolish(double deltaMs, bool flip) =>
                !flip && Math.Abs(deltaMs) <= polishReachMs + 1e-9;
            double bestDelta = 0;
            bool bestFlip = false;
            double bestScore = baseline;
            double bestPolishDelta = 0;
            double bestPolishScore = baseline;
            void Consider(double deltaMs, bool flip, double score)
            {
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDelta = deltaMs;
                    bestFlip = flip;
                }
                if (IsPolish(deltaMs, flip) && score > bestPolishScore)
                {
                    bestPolishScore = score;
                    bestPolishDelta = deltaMs;
                }
            }

            const double CoarseStepMs = 0.5;
            foreach (bool flip in new[] { false, true })
            {
                for (double delta = minDelta;
                    delta <= maxDelta + 1e-9;
                    delta += CoarseStepMs)
                {
                    Consider(delta, flip, Score(delta, flip));
                }
            }
            foreach (double step in new[] { 0.1, 0.02 })
            {
                foreach ((double center, bool flip) in
                    new[] { (bestDelta, bestFlip), (bestPolishDelta, false) }.Distinct())
                {
                    double refineReach = step * 5;
                    for (double delta = Math.Max(minDelta, center - refineReach);
                        delta <= Math.Min(maxDelta, center + refineReach) + 1e-9;
                        delta += step)
                    {
                        Consider(delta, flip, Score(delta, flip));
                    }
                }
            }

            // The hop gate: a winner outside the current lobe (a polarity
            // flip, or farther than the polish reach) must beat the best
            // in-lobe polish by the field-calibrated margin — the pair
            // co-move's 0.05 dB threshold is noise scale for choosing a lobe.
            bool hop = bestFlip || Math.Abs(bestDelta) > polishReachMs + 1e-9;
            if (hop &&
                bestScore <= bestPolishScore + MonoComoveLobeHopMarginDb)
            {
                log.AppendLine(
                    $"  mono lobe hop declined for {mono.Name}: " +
                    $"{bestDelta:+0.00;-0.00} ms{(bestFlip ? " flipped" : "")} " +
                    $"gains only {bestScore - bestPolishScore:0.00} dB over the " +
                    $"in-lobe polish — a lobe hop needs " +
                    $"{MonoComoveLobeHopMarginDb:0.00} dB.");
                bestScore = bestPolishScore;
                bestDelta = bestPolishDelta;
                bestFlip = false;
            }
            else if (hop)
            {
                // The sub-band consistency veto (see
                // MonoComoveSubBandVetoMarginDb): a hop that cleared the margin
                // must also HOLD every (junction, half-band) CELL it can be
                // measured in. Per cell, not per averaged half, or a deficit on
                // one side hides behind a surplus on the other. A cell votes
                // only where the delay is OBSERVABLE — the evidence gate refuses
                // halves where one channel is just a filter tail, whose
                // near-flat loss is noise the level match amplifies toward
                // parity. Losing a measurable cell means the full-band gain came
                // from a mode rewarding a comb impostor, not from a better
                // alignment of the direct sound. Both candidates read through
                // the SAME half-band evaluator, so the comparison cannot be
                // biased by window placement.
                //
                // The reference is the better of the in-lobe polish and the
                // INCUMBENT (no move at all): on a mode-dominated junction the
                // polish itself can slide onto a modal trade — the archived
                // 80 Hz reproduction polishes to -2.45 ms, where the clean
                // lower half reads -6.6 dB — and a hop compared against that
                // spot alone would "win" the very half it is ruining. What a
                // hop must actually beat, cell by cell, is the best placement
                // the channel has WITHOUT hopping.
                bool vetoed = false;
                foreach (AlignmentJunction junction in junctions)
                {
                    foreach (bool upperHalf in new[] { false, true })
                    {
                        (double lowHz, double highHz) = upperHalf
                            ? (junction.CrossoverHz, junction.BandHighHz)
                            : (junction.BandLowHz, junction.CrossoverHz);
                        if (Probe(junction, lowHz, highHz) is not { } cell)
                        {
                            continue;
                        }

                        double CellScore(double deltaMs, bool flip)
                        {
                            (double lossDb, double dipDb) =
                                cell.Evaluate(deltaMs, flip);
                            return lossDb +
                                VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                                (dipDb - lossDb);
                        }

                        double referenceCellScore = Math.Max(
                            CellScore(bestPolishDelta, false),
                            CellScore(0, false));
                        double hopCellScore = CellScore(bestDelta, bestFlip);
                        if (hopCellScore >=
                            referenceCellScore - MonoComoveSubBandVetoMarginDb)
                        {
                            continue;
                        }

                        IAlignmentChannel neighbor =
                            junction.Lower.Channel == mono
                                ? junction.Upper.Channel
                                : junction.Lower.Channel;
                        log.AppendLine(
                            $"  mono lobe hop vetoed for {mono.Name}: " +
                            $"{bestDelta:+0.00;-0.00} ms" +
                            $"{(bestFlip ? " flipped" : "")} wins the full band " +
                            $"by {bestScore - bestPolishScore:0.00} dB but loses " +
                            $"the {lowHz:0}-{highHz:0} Hz half vs " +
                            $"{neighbor.Name} by " +
                            $"{referenceCellScore - hopCellScore:0.00} dB " +
                            "— a true lobe holds every measurable sub-band.");
                        vetoed = true;
                        break;
                    }
                    if (vetoed)
                    {
                        break;
                    }
                }
                if (vetoed)
                {
                    bestScore = bestPolishScore;
                    bestDelta = bestPolishDelta;
                    bestFlip = false;
                }
            }

            if ((bestDelta != 0 || bestFlip) &&
                bestScore > baseline + PairComoveMinimumGainDb)
            {
                // Apply in relative terms: a result below zero (or past the
                // ceiling) rebases the REST of the field the opposite way —
                // the exact equivalence the search bounds assumed.
                double newDelayMs = Math.Round(over.DelayMs + bestDelta, 2);
                if (newDelayMs < 0)
                {
                    ShiftAllExcept(shiftScope, mono, -newDelayMs, alignment, log);
                    newDelayMs = 0;
                }
                else if (newDelayMs > MaxDelayMs)
                {
                    ShiftAllExcept(
                        shiftScope, mono, MaxDelayMs - newDelayMs, alignment, log);
                    newDelayMs = MaxDelayMs;
                }

                alignment[mono] = new AlignmentOverride(
                    newDelayMs,
                    over.InvertPolarity ^ bestFlip);
                log.AppendLine(
                    $"Co-move {mono.Name}: {bestDelta:+0.00;-0.00} ms" +
                    (bestFlip ? ", polarity flipped" : "") +
                    $" (mean dip-penalized junction loss over both sides " +
                    $"{baseline:0.00} -> {bestScore:0.00} dB; a mono move " +
                    "cannot touch the scene)");
                if (decisions != null)
                {
                    // The co-move re-decided this channel from BOTH sides'
                    // junctions, so the walk's decision (typically "reference")
                    // no longer describes it. The confidence maps the applied
                    // gain onto the co-move's own field-calibrated scale —
                    // genuine two-sided recoveries measured 0.20 and 1.36 dB
                    // (see MonoComoveLobeHopMarginDb), an order of magnitude
                    // above the 0.01-0.02 dB noise ties that never apply.
                    double gainDb = bestScore - baseline;
                    AlignmentConfidence comoveConfidence =
                        gainDb >= 10 * MonoComoveLobeHopMarginDb
                            ? AlignmentConfidence.High
                            : gainDb >= MonoComoveLobeHopMarginDb
                                ? AlignmentConfidence.Medium
                                : AlignmentConfidence.Low;
                    string history = decisions.GetValueOrDefault(mono)?.Detail
                        ?? string.Empty;
                    string comoveDetail = FormattableString.Invariant(
                        $"mono co-move {bestDelta:+0.00;-0.00} ms") +
                        (bestFlip ? " + invert" : "") +
                        FormattableString.Invariant(
                            $", both sides' junctions gain {gainDb:0.00} dB");
                    decisions[mono] = new AlignmentDecision(
                        AlignmentDecisionKind.Search,
                        comoveConfidence,
                        history.Length > 0
                            ? $"{history}; {comoveDetail}"
                            : comoveDetail);
                }
            }
            else
            {
                log.AppendLine(
                    $"Co-move {mono.Name}: kept (best gain " +
                    $"{bestScore - baseline:0.00} dB below the " +
                    $"{PairComoveMinimumGainDb:0.00} dB threshold)");
            }
        }
    }

    // A junction whose both sides are already final — the mono subwoofer
    // pinned by the left pass against a settled right channel. Nothing is
    // searched; the resulting loss belongs in the log because it is the price
    // of sharing one mono channel between two differently-timed sides.
    private static void MeasureFixedJunction(
        AlignmentJunction pair,
        IAlignmentChannel monoChannel,
        IAlignmentChannel otherChannel,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
    {
        IReadOnlyList<AlignmentSnapshot> current = reprocess(alignment);
        AlignmentSnapshot mono = current
            .First(item => item.Channel == monoChannel);
        AlignmentSnapshot other = current
            .First(item => item.Channel == otherChannel);
        (double LossDb, double DipDb)? loss = VirtualCrossoverAnalysis.MeasureSumLoss(
            mono.ImpulseResponse,
            new List<Complex[]> { other.ImpulseResponse },
            monoChannel.SampleRate,
            pair.BandLowHz,
            pair.BandHighHz,
            variableValidRange: mono.ValidRange,
            fixedValidRanges: new[] { other.ValidRange });
        if (loss is not { } measured)
        {
            log.AppendLine(
                $"Junction {monoChannel.Name}/{otherChannel.Name} (mono, fixed): " +
                "no bins in the pair band");
            return;
        }

        log.AppendLine(
            $"Junction {monoChannel.Name}/{otherChannel.Name} " +
            $"(mono, timed by the reference side): avg {measured.LossDb:0.00} dB, " +
            $"dip {measured.DipDb:0.0} dB " +
            $"in {pair.BandLowHz:0}-{pair.BandHighHz:0} Hz" +
            (measured.LossDb < -1.0 || measured.DipDb < -6.0
                ? " — WARNING: consider a compromise mono delay by hand"
                : string.Empty));
    }

    private static void AppendCorrelationAlignmentDiagnostics(
        StringBuilder log,
        IReadOnlyList<AlignmentJunction> pairs)
    {
        if (pairs.Count == 0)
        {
            return;
        }

        log.AppendLine();
        log.AppendLine(
            "[corr] band-limited cross-correlation diagnostics " +
            "(full pair band, " +
            $"window ±max({DiagnosticCorrelationRangeMs:0.###} ms, " +
            $"{SeedCorrelationWindowPeriods:0.##} fc periods); " +
            "[corr] raw amplitude, [phat] phase-transform / whitened)");

        foreach (AlignmentJunction pair in pairs)
        {
            // The full pair band, so the correlation reads the same overlap the
            // stage-2 loss search does. The pair band spans fc/2..fc*2 around the
            // crossover, so its width in octaves is log2(high/low).
            double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);

            // Center the lag window on the arrival-based "delay to add to upper"
            // (lower arrival minus upper arrival), the same coarse estimate stage 1
            // computes, so a several-millisecond low-frequency offset stays in the
            // window instead of falling off its zero-centered edge.
            double lowerArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Lower.ImpulseResponse,
                pair.Lower.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz,
                pair.Lower.ValidRange);
            double upperArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Upper.ImpulseResponse,
                pair.Upper.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz,
                pair.Upper.ValidRange);
            double centerLagMs = lowerArrival - upperArrival;

            AppendCorrelationMode(
                log, pair, "corr", passOctaves, centerLagMs, phaseTransform: false);
            AppendCorrelationMode(
                log, pair, "phat", passOctaves, centerLagMs, phaseTransform: true);
        }

        log.AppendLine();
    }

    private static void AppendCorrelationMode(
        StringBuilder log,
        AlignmentJunction pair,
        string tag,
        double passOctaves,
        double centerLagMs,
        bool phaseTransform)
    {
        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                pair.Lower.ImpulseResponse,
                pair.Upper.ImpulseResponse,
                pair.Lower.Channel.SampleRate,
                pair.CrossoverHz,
                passOctaves,
                SeedCorrelationRangeMs(pair.CrossoverHz),
                centerLagMs,
                phaseTransform);
        CorrelationDelayCandidate best = result.BestByMagnitude;

        log.AppendLine(
            $"[{tag}] {pair.Lower.Channel.Name}/" +
            $"{pair.Upper.Channel.Name}: " +
            $"fc {result.CenterFrequencyHz:0} Hz, " +
            $"band {result.BandLowHz:0}-{result.BandHighHz:0} Hz, " +
            $"window ±{result.SearchRangeMs:0.###} ms, " +
            $"delay to add to {pair.Upper.Channel.Name}: " +
            $"{best.DelayMs:+0.000;-0.000} ms, " +
            $"invert {(best.InvertPolarity ? "yes" : "no")}, " +
            $"r {best.Coefficient:+0.000;-0.000}, " +
            $"confidence {result.Confidence:0.000}");
        log.AppendLine(
            $"  [{tag}] peak {result.PositivePeak.DelayMs:+0.000;-0.000} ms " +
            $"(r {result.PositivePeak.Coefficient:+0.000;-0.000}" +
            $"{(result.PositivePeak.EdgePinned ? ", edge" : "")}); " +
            $"trough {result.NegativeTrough.DelayMs:+0.000;-0.000} ms " +
            $"(r {result.NegativeTrough.Coefficient:+0.000;-0.000}, inv" +
            $"{(result.NegativeTrough.EdgePinned ? ", edge" : "")})" +
            (result.PositiveRival is { } rival
                ? $"; rival {rival.DelayMs:+0.000;-0.000} ms " +
                    $"(r {rival.Coefficient:+0.000;-0.000}" +
                    $"{(rival.EdgePinned ? ", edge" : "")})"
                : "") +
            (result.NegativeRival is { } invRival
                ? $"; rival {invRival.DelayMs:+0.000;-0.000} ms " +
                    $"(r {invRival.Coefficient:+0.000;-0.000}, inv" +
                    $"{(invRival.EdgePinned ? ", edge" : "")})"
                : ""));
    }
}
