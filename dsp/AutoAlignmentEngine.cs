using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp;

/// <summary>Delay/polarity proposal for one channel; the delay is absolute.</summary>
public readonly record struct AlignmentOverride(
    double DelayMs,
    bool InvertPolarity);

/// <summary>Ordered so Math.Min/Max express "cap at" / "floor at".</summary>
public enum AlignmentConfidence
{
    Low,
    Medium,
    High
}

/// <summary>Locked picks carry no confidence (a constraint, not an acoustic vote); Bridge confidence is the weaker side's SNR.</summary>
public enum AlignmentDecisionKind
{
    Search,
    Locked,
    Reference,
    Bridge
}

/// <summary>User-report view of a decision; Confidence is null for locked and reference channels.</summary>
public sealed record AlignmentDecision(
    AlignmentDecisionKind Kind,
    AlignmentConfidence? Confidence,
    string Detail);

/// <summary>Reference equality keys the override maps.</summary>
public interface IAlignmentChannel
{
    string Name { get; }

    /// <summary>The MEASUREMENT's rate — the grid every impulse response here lives on.</summary>
    int SampleRate { get; }

    /// <summary>Rate the simulated processor runs its filters at: used only for chain math (ApplyChain, filter responses, FIR kernel delay); measured content uses <see cref="SampleRate"/>.</summary>
    int ProcessorSampleRate { get; }
}

/// <summary>
/// ValidRange: where measured content sits in the delay-shifted, padded record (empty = unknown, padding heuristic).
/// BypassedImpulseResponse + ProcessingChain let the engine re-derive the processed front; null degrades to the upper-half probe.
/// See docs/tech/auto-alignment.md#predicted-front-arrival.
/// </summary>
public sealed record AlignmentSnapshot(
    IAlignmentChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex,
    ValidSampleRange ValidRange = default,
    DspChannelChain? ProcessingChain = null,
    Complex[]? BypassedImpulseResponse = null,
    ValidSampleRange BypassedValidRange = default);

/// <summary>Adjacent pair: crossover and the overlap band (an octave each side) where arrivals are compared and the fine search correlates.</summary>
public sealed record AlignmentJunction(
    AlignmentSnapshot Lower,
    AlignmentSnapshot Upper,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz);

/// <summary>
/// Mono channels appear in both lists as the same instance and are tuned once, by the left pass.
/// Left/Right are roles: Left is the reference; SceneOffsetMs &gt; 0 makes Right lead. Right-hand-drive cabins pass the plan mirrored.
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

/// <summary>L/R pair below the bridge with its shared band; aims the right descent's prior at the delay giving the scene-offset Δ.</summary>
public sealed record StereoPairLink(
    IAlignmentChannel Left,
    IAlignmentChannel Right,
    double BandLowHz,
    double BandHighHz);

/// <summary>Reprocesses with overrides (absent = zero delay, normal polarity). Called off the UI thread: no shared mutable state.</summary>
public delegate IReadOnlyList<AlignmentSnapshot> AlignmentReprocessor(
    IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides);

/// <summary>
/// Two-stage auto time alignment: band-limited arrivals give coarse delays, then a pairwise summation-loss search walks out from the
/// top channel of the chain, the fixed reference; a later-arriving lower channel shifts the settled stack instead.
/// See docs/tech/auto-alignment.md#walk-order.
/// </summary>
public static class AutoAlignmentEngine
{
    // Stage-2 span = half the crossover period, clamped; the floor covers arrival error that does not shrink with the period.
    // See docs/tech/auto-alignment.md#fine-search-window.
    private const double MinFineAlignmentRangeMs = 0.5;
    private const double MaxFineAlignmentRangeMs = 2.5;

    // Just under 1 half period: reaches the flip partner, not the full-period same-polarity lobe.
    private const double LowJunctionReachFraction = 0.97;

    /// <summary>Reach past the measured partner distance so the partner's optimum is interior, not an edge pin.</summary>
    private const double SeedPartnerReachFactor = 1.2;

    /// <summary>Between the half-period partner (must contain) and the full-period rival (must not).</summary>
    private const double SeedPartnerMaxReachPeriods = 0.75;

    /// <summary>Auto delay ceiling when the device's MaxDelayMs is unknown: a transferability gate (car DSPs cap in tens of ms), not an operating region.</summary>
    public const double DefaultMaxDelayMs = 50;

    // Wide window whose candidates are always logged; may feed promotion where the onset lock does not govern.
    private const double DiagnosticFineRangeMs = 3.0;
    private const double DiagnosticCorrelationRangeMs = 3.0;

    // Reaches past the flip partner at low junctions, where the fixed span is sub-period.
    private const double DiagnosticFineReachHalfPeriods = 1.25;

    // Polarity partners and the same-sign rival one period over must be whole lobes in the window for the edge and rival gates. See docs/tech/auto-alignment.md#seed-trust-gates.
    private const double SeedCorrelationWindowPeriods = 1.25;

    private static double SeedCorrelationRangeMs(double crossoverHz) =>
        Math.Max(
            DiagnosticCorrelationRangeMs,
            SeedCorrelationWindowPeriods * 1000.0 / crossoverHz);

    // Half a period (no cycle skip), floored at the fixed span; farther lobes need a stand-down door (chain skew or a disqualified anchor).
    private static double SeedReachMs(double crossoverHz) =>
        Math.Max(DiagnosticCorrelationRangeMs, 500.0 / crossoverHz);

    // Min |r| of the dominant PHAT extremum to seed stage 2 (position only). Deliberately low: an off seed is recovered downstream.
    private const double PhatSeedMinCoefficient = 0.15;

    /// <summary>Arrival pick depth below band energy past which the reach veto MAY stand down (only with every other clause of MayWithdrawSeedReachVeto). See docs/tech/auto-alignment.md#seed-reach-veto.</summary>
    private const double SeedVetoMinProminenceDb =
        -TimeAlignmentAnalysisOptions.DefaultFirstPeakThresholdBelowMaxDb / 2.0;

    /// <summary>Seed reach veto stand-down policy; false unless every clause passes. The delegate defers the direct cut until the cheap clauses pass.</summary>
    internal static bool MayWithdrawSeedReachVeto(
        double anchorProminenceDb,
        bool anchorIsRawReads,
        CorrelationDelayCandidate seed,
        double crossoverHz,
        Func<CorrelationDelayCandidate?> directCorroboration)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(directCorroboration);
        return anchorProminenceDb < SeedVetoMinProminenceDb &&
            ExtremumMayStandOnItsOwn(
                anchorIsRawReads, seed, crossoverHz, directCorroboration);
    }

    // Shared tail of the stand-down doors: raw-read anchor, the direct-seed bar, and the direct cut's last word above its frequency.
    private static bool ExtremumMayStandOnItsOwn(
        bool anchorIsRawReads,
        CorrelationDelayCandidate seed,
        double crossoverHz,
        Func<CorrelationDelayCandidate?> directCorroboration)
    {
        if (!anchorIsRawReads ||
            Math.Abs(seed.Coefficient) < DirectSeedMinCoefficient)
        {
            return false;
        }

        if (crossoverHz < DirectSeedMinCrossoverHz)
        {
            return true;
        }

        return directCorroboration() is { } corroboration &&
            corroboration.InvertPolarity == seed.InvertPolarity &&
            Math.Abs(corroboration.DelayMs - seed.DelayMs) <=
                DirectCorroborationPeriods * 1000.0 / crossoverHz;
    }

    // Chain skew at or past the reach disqualifies the anchor like a deep pick. See docs/tech/auto-alignment.md#seed-reach-veto.
    internal static bool ChainSkewDisqualifiesTheAnchor(
        double reachMs,
        double? chainSkewMs) =>
        chainSkewMs is { } skew && Math.Abs(skew) >= reachMs;

    /// <summary>Quarter period: half is the flip partner, a full period the cycle skip. See docs/tech/auto-alignment.md#seed-reach-veto.</summary>
    private const double DirectCorroborationPeriods = 0.25;

    /// <summary>Margin over the same-polarity rival one period over (the cycle skip the window cannot undo). See docs/tech/auto-alignment.md#seed-trust-gates.</summary>
    private const double PhatSeedMinRivalDominance = 0.05;

    /// <summary>Min crossover for the direct-cut seed witness; below ~1 kHz the cut does not isolate a wavefront. See docs/tech/auto-alignment.md#direct-sound-seed.</summary>
    private const double DirectSeedMinCrossoverHz = 1000;

    /// <summary>Min |r| for the direct-cut witness to speak; far above the seed floor since honest cuts correlate strongly (field min 0.58).</summary>
    private const double DirectSeedMinCoefficient = 0.5;

    /// <summary>Joint-support margin (min |r| of both surfaces within a quarter period) needed to move a contested seed off the full-record extremum. See docs/tech/auto-alignment.md#direct-sound-seed.</summary>
    private const double DirectSeedJointTieMarginR = 0.05;

    /// <summary>Max full-record extremum distance from the arrival (periods) while the direct cut offers a seed. See docs/tech/auto-alignment.md#direct-sound-seed.</summary>
    private const double DirectSeedTrustReachPeriods = 1.5;

    /// <summary>Corners are typed, not measured: they either match or were deliberately staggered.</summary>
    private const double MatchedSplitToleranceHz = 0.5;

    /// <summary>Inverted filter-sum advantage needed to expect relative inversion; keeps in-phase when filters say nothing (BW18, no crossover, staggered corners). See docs/tech/auto-alignment.md#expected-polarity.</summary>
    private const double ExpectedInversionMarginDb = 1.0;

    // Near-tie between sub-leading and sub-trailing lobes is decided for the leading sub (precedence); kept just under the ~1.4 dB comb noise.
    // See docs/tech/auto-alignment.md#sub-precedence.
    private const double SubPrecedenceMarginDb = 1.0;

    // Within this of the envelope anchor a candidate is neither leading nor trailing.
    private const double SubPrecedenceSlackMs = 0.5;

    // Score gain a wide-window optimum needs to unseat the arrival-anchored pick where the onset lock does not govern.
    // See docs/tech/auto-alignment.md#wide-window-promotion.
    private const double WideWindowPromotionMarginDb = 1.6;

    private const double PromotionNoteworthyGainDb = 0.2;

    // Caps promotion distance: beyond ~2 periods the sum is a comb of near-equal aliases. See docs/tech/auto-alignment.md#wide-window-promotion.
    private const double PromotionReachPeriods = 2.5;

    // Onset lock: at sharp high junctions the window is the broadband onset ± reach and escape hatches stay shut.
    // See docs/tech/auto-alignment.md#onset-lock.

    private const double OnsetLockMinCrossoverHz = 700;

    // Admits onset error plus per-driver GD split and the half-period flip partner; excludes the full-period lobe.
    private const double OnsetLockReachPeriods = 0.75;

    // Lock engages only when the 10/25/50 % onset differences agree within this many periods.
    private const double OnsetLockMaxSpreadPeriods = 0.5;

    /// <summary>Direct-coherence witness: arbitrates score ties between polarity partners by direct-sound r. See docs/tech/auto-alignment.md#direct-coherence-witness.</summary>
    private const double DirectCoherenceMinCrossoverHz = 120;
    private const double DirectCoherenceTieMarginDb = 0.3;
    private const double DirectCoherenceMinR = 0.6;
    private const double DirectCoherenceMinAdvantage = 0.05;

    /// <summary>Coherence ladder second opinion: vetoes a slim direct-correlation swap. See docs/tech/auto-alignment.md#coherence-ladder-veto.</summary>
    private const double LadderVetoMaxAdvantage = 0.10;
    private const double LadderVetoMinCrossoverHz = 1000;
    private const int LadderVetoMinBandMargin = 2;

    /// <summary>Min envelope peak-to-noise (dB) for onset lock: pure noise grades ~13-14 dB, loopback 40+ dB. Public for tests.</summary>
    public const double OnsetLockMinimumSnrDb = 20;

    /// <summary>
    /// <paramref name="pairs"/>[i] joins channels i and i+1 of <paramref name="channelsByBand"/>. Snapshots are override-free;
    /// the proposal is absolute. <paramref name="maxDelayMs"/> bounds every pass (device ceiling).
    /// </summary>
    public static void Compute(
        IReadOnlyList<AlignmentSnapshot> channelsByBand,
        IReadOnlyList<AlignmentJunction> pairs,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null,
        double maxDelayMs = DefaultMaxDelayMs)
    {
        // Arrival reads repeat on identical input across the run; see AlignmentRunMemo.
        using AlignmentRunMemo.Scope runMemo = AlignmentRunMemo.Begin();
        ArgumentNullException.ThrowIfNull(channelsByBand);
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDelayMs);
        RequireOneSampleRate(channelsByBand);
        // Absolute proposal: stale entries would leak into neighbor-base and reprocess reads.
        alignment.Clear();
        decisions?.Clear();
        Compute(channelsByBand, pairs, reprocess, alignment, log,
            onsetLocks: null, decisions);
        NormalizeAndVerifyFeasibility(
            channelsByBand.ToList(), alignment, log, maxDelayMs);
        NormalizePolarityPresentation(channelsByBand, alignment, log);
    }

    // A global flip changes no relation, so a proposal inverting more channels than it keeps is presented flipped (one inverted sub, not three stack channels).
    /// <param name="positions">What is counted, bottom first: one channel per driver position, since a stereo pair
    /// always shares its polarity (the reference side of a stereo run). Null counts <paramref name="scope"/>.</param>
    internal static void NormalizePolarityPresentation(
        IReadOnlyList<AlignmentSnapshot> scope,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        IReadOnlyList<AlignmentSnapshot>? positions = null)
    {
        List<IAlignmentChannel> channels = scope
            .Select(item => item.Channel)
            .Distinct()
            .ToList();
        List<IAlignmentChannel> counted = (positions ?? scope)
            .Select(item => item.Channel)
            .Distinct()
            .ToList();
        int inverted = counted.Count(
            channel => alignment.GetValueOrDefault(channel).InvertPolarity);
        // Exactly half is a tie, and the bottom position breaks it: a sub is what a tuner leaves alone.
        bool flip = inverted * 2 > counted.Count ||
            (inverted * 2 == counted.Count &&
                alignment.GetValueOrDefault(counted[0]).InvertPolarity);
        if (!flip)
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
            $"{counted.Count} positions were inverted) — a global flip changes no relation.");
    }

    /// <summary>Full-band read judged against the same record's upper-half read. See docs/tech/auto-alignment.md#arrival-certificate.</summary>
    public enum ArrivalCertificate
    {
        Unverified,
        Latched,
        Verified
    }

    /// <summary>Estimated processed front: the bypassed-response arrival plus the chain's measured shift; null when unavailable.
    /// An estimate, never a conviction on its own. See docs/tech/auto-alignment.md#predicted-front-arrival.</summary>
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

        // Impulse length = measured content, not the padded bypassed array (already an ApplyChain output, twice the crop).
        int contentLength = side.BypassedValidRange.IsKnown
            ? side.BypassedValidRange.EndSample - side.BypassedValidRange.StartSample
            : bypassed.Length;
        return bare.FirstArrivalDelayMilliseconds +
            ChainArrivalShiftMs(chain, sampleRate, processorRate, bandLowHz,
                bandHighHz, contentLength, side.PeakIndex);
    }

    // Measured through the real ApplyChain and detector; bulk delay and polarity excluded (neutral in the reads). Symmetric FIR adds its exact delay instead.
    private static double ChainArrivalShiftMs(
        DspChannelChain chain,
        int sampleRate,
        int processorSampleRate,
        double bandLowHz,
        double bandHighHz,
        int length,
        int peakIndex)
    {
        if (LinearPhaseKernelOf(chain) is { } kernel)
        {
            return ChainArrivalShiftMs(
                    chain with { Fir = null },
                    sampleRate,
                    processorSampleRate,
                    bandLowHz,
                    bandHighHz,
                    length,
                    peakIndex) +
                LinearPhaseDelayMs(kernel, processorSampleRate);
        }

        var impulse = new Complex[length];
        impulse[Math.Clamp(peakIndex, 0, length - 1)] = Complex.One;
        // Both reads go through ApplyChain so the detector crops them alike.
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

    /// <summary>Symmetric (linear-phase) FIR kernel, or null: its delay is known exactly, and its pre-ring cannot be read by the detector.
    /// See docs/tech/auto-alignment.md#linear-phase-fir.</summary>
    private static FirFilter? LinearPhaseKernelOf(DspChannelChain chain) =>
        chain.Fir is { IsSymmetric: true } kernel ? kernel : null;

    // Kernel taps are at the processor's rate.
    private static double LinearPhaseDelayMs(FirFilter kernel, int processorSampleRate) =>
        kernel.LinearPhaseDelaySamples * 1_000.0 / processorSampleRate;

    /// <summary>Band-limited processed arrival; a symmetric FIR is removed from the read and its exact delay added back.</summary>
    private static TimeAlignmentAnalysisResult ReadProcessedArrival(
        AlignmentSnapshot side,
        double bandLowHz,
        double bandHighHz)
    {
        int sampleRate = side.Channel.SampleRate;
        if (side.ProcessingChain is not { } chain ||
            LinearPhaseKernelOf(chain) is not { } kernel ||
            side.BypassedImpulseResponse is not { } bypassed)
        {
            return VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, sampleRate, bandLowHz, bandHighHz, side.ValidRange);
        }

        // Cut at the end only, so the re-render keeps the processed response's time origin.
        Complex[] content = side.BypassedValidRange.IsKnown
            ? bypassed[..side.BypassedValidRange.EndSample]
            : bypassed;
        int processorSampleRate = side.Channel.ProcessorSampleRate;
        Complex[] withoutKernel = VirtualCrossoverAnalysis.ApplyChain(
            content,
            chain with { DelayMs = 0, InvertPolarity = false, Fir = null },
            sampleRate,
            processorSampleRate,
            out ValidSampleRange range);
        TimeAlignmentAnalysisResult read = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            withoutKernel, sampleRate, bandLowHz, bandHighHz, range);
        if (!read.IsValid)
        {
            return read;
        }

        double delayMs = LinearPhaseDelayMs(kernel, processorSampleRate);
        int delaySamples = (int)Math.Round(delayMs * sampleRate / 1_000.0);
        return read with
        {
            FirstArrivalPeakSample = read.FirstArrivalPeakSample + delaySamples,
            FirstArrivalDelayMilliseconds = read.FirstArrivalDelayMilliseconds + delayMs,
            StrongestPeakSample = read.StrongestPeakSample + delaySamples,
            StrongestDelayMilliseconds = read.StrongestDelayMilliseconds + delayMs,
            EnergyOnsetSample = read.EnergyOnsetSample + delaySamples,
            EnergyOnsetDelayMilliseconds = read.EnergyOnsetDelayMilliseconds + delayMs
        };
    }

    // Non-common chain shift of the two sides in the pair band, signed like the anchor (lowerArrival − upperArrival).
    // Null when either side is unmeasurable: the partner's shift is unknown there, not zero.
    internal static double? PairChainArrivalSkewMs(AlignmentJunction pair) =>
        SideChainArrivalShiftMs(pair.Lower, pair.BandLowHz, pair.BandHighHz)
            is { } lower &&
        SideChainArrivalShiftMs(pair.Upper, pair.BandLowHz, pair.BandHighHz)
            is { } upper
            ? lower - upper
            : null;

    // The OFFSET is corrected by the skew and must fit the unwidened reach, so cycle skips stay refused.
    internal static bool ChainSkewExplainsSeedOffset(
        double seedOffsetMs,
        double reachMs,
        double? chainSkewMs) =>
        chainSkewMs is { } skew &&
        Math.Abs(seedOffsetMs + skew) < reachMs;

    private static double? SideChainArrivalShiftMs(
        AlignmentSnapshot side,
        double bandLowHz,
        double bandHighHz)
    {
        if (side.BypassedImpulseResponse is not { } bypassed ||
            side.ProcessingChain is not { } chain)
        {
            return null;
        }

        int contentLength = side.BypassedValidRange.IsKnown
            ? side.BypassedValidRange.EndSample - side.BypassedValidRange.StartSample
            : bypassed.Length;
        return ChainArrivalShiftMs(
            chain,
            side.Channel.SampleRate,
            side.Channel.ProcessorSampleRate,
            bandLowHz,
            bandHighHz,
            contentLength,
            side.PeakIndex);
    }

    /// <summary>Upper-half probe allowance for one side: generic dispersion plus the predicted full-vs-probe skew.
    /// See docs/tech/auto-alignment.md#arrival-honesty-probe.</summary>
    /// <param name="appliedDelayMs">Override delay the reads were taken through; the prediction excludes bulk delay.</param>
    internal static double ArrivalProbeToleranceMs(
        AlignmentSnapshot side,
        double measuredMs,
        double probeMeasuredMs,
        double bandLowHz,
        double probeLowHz,
        double bandHighHz,
        double appliedDelayMs = 0)
    {
        ArgumentNullException.ThrowIfNull(side);
        double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
        // Credit is a difference of two predictions, so both must verify; Inconsistent or Latched sides earn nothing.
        if (GradeAgainstPrediction(
                side, measuredMs - appliedDelayMs, bandLowHz, bandHighHz,
                out double full) != PredictionState.Verified ||
            GradeAgainstPrediction(
                side, probeMeasuredMs - appliedDelayMs, probeLowHz, bandHighHz,
                out double probe) != PredictionState.Verified)
        {
            return toleranceMs;
        }

        // Capped at the generic allowance: an uncapped credit grows with the very mode the probe convicts.
        return toleranceMs + Math.Clamp(full - probe, 0, toleranceMs);
    }

    /// <summary>Latched = later than prediction by more than ConvictionFactor allowances; Inconsistent = earlier beyond one allowance, or later by 1 to ConvictionFactor (not convicted, not certified). See docs/tech/auto-alignment.md#predicted-front-arrival.</summary>
    internal enum PredictionState { Unavailable, Verified, Latched, Inconsistent }

    /// <summary>Prediction accuracy floor: honest reads land well inside, field latches run 6.7 ms and up.</summary>
    private const double PredictedArrivalAccuracyMs = 2.5;

    /// <summary>Allowances past which a prediction may convict; false convictions 1.01-1.17, true latches 2.49-3.89. See docs/tech/auto-alignment.md#predicted-front-arrival.</summary>
    private const double PredictedArrivalConvictionFactor = 2.0;

    /// <summary>Whitened-correlation arbitration for the conviction dead zone (1-2 allowances). See docs/tech/auto-alignment.md#latch-arbitration.</summary>
    private const double LatchArbitrationMinR = 0.6;
    private const double LatchArbitrationMinAdvantage = 0.05;

    /// <summary>Half a period at the band's geometric centre, floored at the prediction's accuracy.</summary>
    internal static double PredictedArrivalAllowanceMs(
        double bandLowHz, double bandHighHz) =>
        Math.Max(
            PredictedArrivalAccuracyMs,
            500.0 / Math.Sqrt(bandLowHz * bandHighHz));

    /// <summary>Two-sided: an earlier-than-predicted read is not a latch, but not a confirmation either.</summary>
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

    // Searches read a neighbor's IR at the searched channel's rate: mixed rates would silently misscale.
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

    // Consumed by the co-move so its shared delta cannot push a locked junction's front gap past the lock's cap.
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
                CrossoverSettlesJunctionPolarity,
                out HashSet<AlignmentJunction> untrustedSeeds,
                out Dictionary<AlignmentJunction, double> seedPartnerReach);

        // The top of the chain anchors the walk, so the channel the image follows — and the one a stereo run bridges
        // the sides on — is never the last link of an inherited chain. See docs/tech/auto-alignment.md#walk-order.
        AlignmentSnapshot top = byBand[^1];
        IAlignmentChannel reference = top.Channel;
        log.AppendLine($"Reference: {reference.Name}");
        if (decisions != null)
        {
            decisions[reference] = new AlignmentDecision(
                AlignmentDecisionKind.Reference, Confidence: null,
                "reference (others align to it)");
        }

        // Stage 2 descends from it band by band; each window is sized by its own junction, and a low-junction error
        // moves the group below it together. A channel that arrives later than the top needs a negative delay, which
        // ShiftAllExcept turns into a uniform shift of everything settled so far.
        for (int i = byBand.Count - 2; i >= 0; i--)
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
    }

    /// <summary>Polarity settled before the search: by the crossover on polarity-searching walks, inherited on the far-side descent. Stage 1 must use the same.</summary>
    internal readonly record struct SettledJunctionPolarity(
        bool Inverted,
        string Because);

    /// <summary>Matched split at or above <see cref="DirectSeedMinCrossoverHz"/> with filters that decide; null otherwise.</summary>
    internal static SettledJunctionPolarity? CrossoverSettlesJunctionPolarity(
        AlignmentJunction pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        if (SettledRelativeInversion(pair) is not bool inverted)
        {
            return null;
        }

        string sums = inverted ? "only inverted" : "in phase";
        return new SettledJunctionPolarity(
            inverted,
            FormattableString.Invariant(
                $"the matched {pair.CrossoverHz:0} Hz split sums {sums}"));
    }

    /// <summary>Far-side junction polarity inherited from the reference side, so stage 1 never centres on a family stage 2 forbids.
    /// Null unless both channels resolve to a settled counterpart.</summary>
    internal static SettledJunctionPolarity? InheritedJunctionPolarity(
        AlignmentJunction pair,
        IReadOnlyCollection<IAlignmentChannel> monoChannels,
        IReadOnlyList<StereoPairLink>? pairLinks,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> alignment)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(monoChannels);
        ArgumentNullException.ThrowIfNull(alignment);

        IAlignmentChannel? Counterpart(IAlignmentChannel channel) =>
            monoChannels.Contains(channel)
                ? channel
                : pairLinks?.FirstOrDefault(link => link.Right == channel)?.Left;

        if (Counterpart(pair.Lower.Channel) is not { } lower ||
            Counterpart(pair.Upper.Channel) is not { } upper)
        {
            return null;
        }

        // Own reference channel has no override: reads as not inverted.
        bool inverted =
            alignment.GetValueOrDefault(lower).InvertPolarity ^
            alignment.GetValueOrDefault(upper).InvertPolarity;
        string settled = inverted ? "inverted" : "in phase";
        return new SettledJunctionPolarity(
            inverted,
            $"the reference side settled this pair {settled} and the far side " +
            "inherits its polarity driver by driver");
    }

    /// <summary>Seed goes to the direct cut when the record's dominant extremum is in the forbidden polarity family and the cut agrees with the settled answer.
    /// See docs/tech/auto-alignment.md#settled-polarity-seed.</summary>
    internal static bool SeedFamilyFollowsTheSettledPolarity(
        SettledJunctionPolarity? settled,
        CorrelationDelayCandidate dominant,
        CorrelationDelayCandidate? directSeed)
    {
        ArgumentNullException.ThrowIfNull(dominant);
        return settled is { } answer &&
            dominant.InvertPolarity != answer.Inverted &&
            directSeed?.InvertPolarity == answer.Inverted;
    }

    /// <summary>The relation Auto delay forces on a junction, as stage 2 does: at a matched split at or above 1 kHz, true where
    /// the pair's own filters sum inverted (LR12/LR36, BW12/BW36), false where they sum in phase (LR24/LR48); null where the
    /// search decides. See docs/tech/auto-alignment.md#expected-polarity.</summary>
    internal static bool? SettledRelativeInversion(
        CrossoverEdge? lowPass,
        CrossoverEdge? highPass,
        int processorSampleRate) =>
        lowPass is { } low && highPass is { } high &&
        0.5 * (low.FrequencyHz + high.FrequencyHz) >= DirectSeedMinCrossoverHz
            ? ExpectsRelativeInversion(low, high, processorSampleRate)
            : null;

    private static bool? SettledRelativeInversion(AlignmentJunction pair) =>
        SettledRelativeInversion(
            pair.Lower.ProcessingChain?.LowPassEdge,
            pair.Upper.ProcessingChain?.HighPassEdge,
            pair.Lower.Channel.ProcessorSampleRate);

    private static bool? ExpectsRelativeInversion(AlignmentJunction pair) =>
        pair.Lower.ProcessingChain?.LowPassEdge is { } lowPass &&
        pair.Upper.ProcessingChain?.HighPassEdge is { } highPass
            ? ExpectsRelativeInversion(lowPass, highPass, pair.Lower.Channel.ProcessorSampleRate)
            : null;

    private static bool? ExpectsRelativeInversion(
        CrossoverEdge lowPass,
        CrossoverEdge highPass,
        int processorSampleRate)
    {
        double preferenceDb = FilterPolarityPreferenceDb(lowPass, highPass, processorSampleRate);
        if (double.IsNaN(preferenceDb) ||
            Math.Abs(preferenceDb) <= ExpectedInversionMarginDb)
        {
            // Staggered split, no crossover, or BW18's 90°: the search decides; not the same as a matched split asking for in-phase.
            return null;
        }

        return preferenceDb > 0;
    }

    /// <summary>Inverted-sum advantage (dB) of the junction's own filters at the corner; NaN unless the split is matched.
    /// See docs/tech/auto-alignment.md#expected-polarity.</summary>
    private static double FilterPolarityPreferenceDb(
        CrossoverEdge lowPass,
        CrossoverEdge highPass,
        int rate)
    {
        if (lowPass.Family != highPass.Family ||
            lowPass.SlopeDbPerOctave != highPass.SlopeDbPerOctave ||
            Math.Abs(lowPass.FrequencyHz - highPass.FrequencyHz) >
                MatchedSplitToleranceHz)
        {
            return double.NaN;
        }

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

    /// <summary>What stage 1 anchors a junction on: the measured band-limited fronts, replaced by the predicted fronts where
    /// a modal latch is convicted (#predicted-front-arrival) or by the upper-half reads where the upper-half probe catches one
    /// (#arrival-honesty-probe). See docs/tech/auto-alignment.md#arrival-honesty-probe.</summary>
    internal sealed record JunctionArrivalRead(
        double LowerMs,
        double UpperMs,
        double LowerPredictionMs,
        double UpperPredictionMs,
        bool PairPredictionGradeable,
        bool LowerLatchedByPrediction,
        bool UpperLatchedByPrediction,
        double PredictionDisagreementMs,
        bool ArrivalReanchored,
        bool UnreplacedLatch);

    internal static JunctionArrivalRead ReadJunctionArrivals(
        AlignmentJunction pair,
        TimeAlignmentAnalysisResult lowerRead,
        TimeAlignmentAnalysisResult upperRead,
        StringBuilder log)
    {
        double lowerArrival = lowerRead.FirstArrivalDelayMilliseconds;
        double upperArrival = upperRead.FirstArrivalDelayMilliseconds;

        // Arrival honesty: the predicted-arrival probe first, then the upper-half probe. See docs/tech/auto-alignment.md#arrival-honesty-probe.
        // Both sides must be gradeable: prediction and measurement residuals cancel only against their own kind.
        PredictionState lowerState = GradeAgainstPrediction(
            pair.Lower, lowerArrival, pair.BandLowHz, pair.BandHighHz,
            out double lowerPrediction);
        PredictionState upperState = GradeAgainstPrediction(
            pair.Upper, upperArrival, pair.BandLowHz, pair.BandHighHz,
            out double upperPrediction);
        // Inconsistent is ungradeable: a prediction that cannot explain the read may not replace it.
        static bool Gradeable(PredictionState state) =>
            state is PredictionState.Verified or PredictionState.Latched;
        bool pairGradeable = Gradeable(lowerState) && Gradeable(upperState);
        bool lowerLatchedByPrediction =
            pairGradeable && lowerState == PredictionState.Latched;
        bool upperLatchedByPrediction =
            pairGradeable && upperState == PredictionState.Latched;

        // Conviction dead zone (see LatchArbitrationMinR): the whitened comb decides between predictions and measured arrivals.
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

            // Both sides move to the prediction (estimators must not mix).
            lowerArrival = lowerPrediction;
            upperArrival = upperPrediction;
        }

        // Disagreement with the prediction, NOT an error bound (shared detector, room and bypassed response hide common bias); only adds restrictions.
        // A replaced side's residual is unknowable, so the stand-in is 2 × allowance (two sides within A differ by up to 2A).
        double predictionDisagreementMs =
            lowerLatchedByPrediction || upperLatchedByPrediction
                ? 2.0 * PredictedArrivalAllowanceMs(
                    pair.BandLowHz, pair.BandHighHz)
                : Math.Abs(
                    (lowerArrival - lowerPrediction) -
                    (upperArrival - upperPrediction));

        // Gradeable, not confirmed: every side verified or was replaced.
        bool pairPredictionGradeable = pairGradeable;

        double probeLowHz = Math.Sqrt(pair.BandLowHz * pair.BandHighHz);
        bool arrivalReanchored = false;
        // A convicted latch the upper-half probe could not replace keeps the corrupted diff; the prominence exception must not undo that.
        bool unreplacedLatch = false;
        if (!lowerLatchedByPrediction && !upperLatchedByPrediction &&
            pair.BandHighHz >=
            probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
        {
            TimeAlignmentAnalysisResult lowerProbe =
                ReadProcessedArrival(pair.Lower, probeLowHz, pair.BandHighHz);
            TimeAlignmentAnalysisResult upperProbe =
                ReadProcessedArrival(pair.Upper, probeLowHz, pair.BandHighHz);
            // Per channel: each side's chain explains a different smear.
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
                // Re-anchor only when both certificates are not Unverified; otherwise the corrupted diff stays and the reach veto stays armed.
                if (lowerCertificate != ArrivalCertificate.Unverified &&
                    upperCertificate != ArrivalCertificate.Unverified)
                {
                    lowerArrival = lowerProbe.FirstArrivalDelayMilliseconds;
                    upperArrival = upperProbe.FirstArrivalDelayMilliseconds;
                    arrivalReanchored = true;
                }
                else
                {
                    unreplacedLatch = true;
                }
            }
        }

        return new JunctionArrivalRead(
            lowerArrival, upperArrival, lowerPrediction, upperPrediction,
            pairPredictionGradeable, lowerLatchedByPrediction, upperLatchedByPrediction,
            predictionDisagreementMs, arrivalReanchored, unreplacedLatch);
    }

    // No default on purpose: a forgotten polarity authority is a compile error, not a walk seeding against its own stage 2.
    private static Dictionary<IAlignmentChannel, double> BuildArrivalTimeline(
        IReadOnlyList<AlignmentSnapshot> byBand,
        IReadOnlyList<AlignmentJunction> pairs,
        StringBuilder log,
        Func<AlignmentJunction, SettledJunctionPolarity?> settledPolarity,
        out HashSet<AlignmentJunction> untrustedSeedJunctions,
        out Dictionary<AlignmentJunction, double> seedPartnerDistanceMs)
    {
        ArgumentNullException.ThrowIfNull(settledPolarity);
        // Untrusted-seed junctions may be a half period off, so they get the half-period window. Keyed by junction: it is a property of the relation.
        untrustedSeedJunctions =
            new HashSet<AlignmentJunction>(ReferenceEqualityComparer.Instance);
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
                ReadProcessedArrival(pair.Lower, pair.BandLowHz, pair.BandHighHz);
            TimeAlignmentAnalysisResult upperRead =
                ReadProcessedArrival(pair.Upper, pair.BandLowHz, pair.BandHighHz);

            // A near-noise band has no signal to rescue: refuse the run and name the channel (dead driver, wrong source, mis-set crossover).
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

            JunctionArrivalRead arrivals =
                ReadJunctionArrivals(pair, lowerRead, upperRead, log);
            double lowerArrival = arrivals.LowerMs;
            double upperArrival = arrivals.UpperMs;
            double lowerPrediction = arrivals.LowerPredictionMs;
            double upperPrediction = arrivals.UpperPredictionMs;
            bool pairPredictionGradeable = arrivals.PairPredictionGradeable;
            bool lowerLatchedByPrediction = arrivals.LowerLatchedByPrediction;
            bool upperLatchedByPrediction = arrivals.UpperLatchedByPrediction;
            double predictionDisagreementMs = arrivals.PredictionDisagreementMs;
            bool arrivalReanchored = arrivals.ArrivalReanchored;
            bool unreplacedLatch = arrivals.UnreplacedLatch;

            // Seed from the dominant GCC-PHAT extremum of either sign (position only). See docs/tech/auto-alignment.md#seed-selection.
            // Timeline stores (upper − lower); the extremum is the delay to add to the upper channel, i.e. that negated.
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

            // Lobe-boundary conviction: an anchor disagreeing with its predictions by more than half the lobe spacing moves to the predictions.
            // See docs/tech/auto-alignment.md#lobe-boundary-conviction.
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

            // Only on measured lobe geometry, a raw anchor not already replaced, and a disagreement above the predictor's accuracy floor.
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
                    // Re-centred once: iterating would be a loop with no fixed point.
                    phat = Correlate(centerLagMs);
                }
            }

            CorrelationDelayCandidate seed = phat.BestByMagnitude;
            CorrelationDelayCandidate? sameSignRival =
                seed.InvertPolarity ? phat.NegativeRival : phat.PositiveRival;
            string seedLabel = seed.InvertPolarity ? "trough" : "peak";
            double seedOffsetMs = seed.DelayMs - centerLagMs;
            SettledJunctionPolarity? settled = settledPolarity(pair);
            // Declared ahead of the trust gate below, which reads the witness
            // (a local function cannot capture a variable declared after it).
            CorrelationAlignmentResult? directPhat = null;
            CorrelationDelayCandidate? directSeed = null;
            // The weaker of the two sides' picks: the anchor is their
            // DIFFERENCE, so one side reading a feature the other does not is
            // enough to make it one. It describes the anchor only while the
            // anchor is still those raw reads — a predicted front has replaced
            // both of them, and a latch convicted without a comparable
            // replacement is the one case where the corrupted diff is kept
            // DELIBERATELY, with the reach veto as what stands between it and
            // the modal extremum measured around it. Both keep the veto.
            double anchorProminenceDb = Math.Min(
                lowerRead.FirstArrivalProminenceDecibels,
                upperRead.FirstArrivalProminenceDecibels);
            bool anchorIsRawReads =
                !lowerLatchedByPrediction && !upperLatchedByPrediction &&
                !unreplacedLatch;

            // Direct-cut corroboration (only at or above DirectSeedMinCrossoverHz): answers only "is the pair on this lobe".
            CorrelationDelayCandidate? DirectCorroboration()
            {
                CorrelationAlignmentResult? witness = directPhat;
                if (witness == null)
                {
                    (Complex[] lowerCut, Complex[] upperCut) =
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
                    witness = VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                        lowerCut,
                        upperCut,
                        pair.Lower.Channel.SampleRate,
                        pair.CrossoverHz,
                        passOctaves,
                        SeedCorrelationRangeMs(pair.CrossoverHz),
                        centerLagMs,
                        phaseTransform: true);
                }

                CorrelationDelayCandidate best = witness.BestByMagnitude;
                CorrelationDelayCandidate? rival = best.InvertPolarity
                    ? witness.NegativeRival
                    : witness.PositiveRival;
                return !witness.PositivePeak.EdgePinned &&
                    !witness.NegativeTrough.EdgePinned &&
                    Math.Abs(best.Coefficient) >= DirectSeedMinCoefficient &&
                    (rival == null ||
                        Math.Abs(best.Coefficient) - Math.Abs(rival.Coefficient) >=
                            PhatSeedMinRivalDominance)
                    ? best
                    : null;
            }
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
                // No peak-vs-trough gate: that margin measures band width (see PhatSeedMinRivalDominance).
                if (sameSignRival is { } rival &&
                    Math.Abs(seed.Coefficient) - Math.Abs(rival.Coefficient) <
                        PhatSeedMinRivalDominance)
                {
                    return "same-polarity rival near-tie";
                }
                // Reach veto doors: a re-anchored pair, a chain skew that explains the offset, or a disqualified anchor (skew ≥ reach, deep pick). See docs/tech/auto-alignment.md#seed-reach-veto.
                double reachMs = SeedReachMs(pair.CrossoverHz);
                // With a usable direct-cut seed, tighten the reach to DirectSeedTrustReachPeriods (the 3 ms floor spans 4-5 periods up there).
                if (directSeed != null)
                {
                    reachMs = Math.Min(
                        reachMs, DirectSeedTrustReachPeriods * 1000.0 / pair.CrossoverHz);
                }

                // At the boundary the lobes are equidistant: refuse. Gate reads the RAW offset; skew correction only opens admittance doors.
                // See docs/tech/auto-alignment.md#chain-skew.
                if (!arrivalReanchored && Math.Abs(seedOffsetMs) >= reachMs)
                {
                    // Subtract the measured chain skew from the disagreement; never widen the half-period reach. See docs/tech/auto-alignment.md#chain-skew.
                    double? chainSkewMs = PairChainArrivalSkewMs(pair);
                    if (ChainSkewExplainsSeedOffset(
                        seedOffsetMs, reachMs, chainSkewMs))
                    {
                        log.AppendLine(
                            $"  {pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}: " +
                            $"the {seedLabel} sits {Math.Abs(seedOffsetMs):0.000} ms " +
                            $"from the arrival anchor, past its {reachMs:0.000} ms " +
                            $"reach — but the pair's own chains displace that " +
                            $"anchor by {chainSkewMs!.Value:+0.000;-0.000} ms of " +
                            "non-common shift, and against the corrected anchor " +
                            $"the extremum (r {Math.Abs(seed.Coefficient):0.000}) " +
                            $"sits {Math.Abs(seedOffsetMs + chainSkewMs.Value):0.000} ms " +
                            "off, inside the reach; the veto is corrected away " +
                            "and the extremum stands");
                        return null;
                    }

                    // Skew as large as the reach disqualifies the anchor; the veto then stands down only under ExtremumMayStandOnItsOwn.
                    if (ChainSkewDisqualifiesTheAnchor(reachMs, chainSkewMs) &&
                        ExtremumMayStandOnItsOwn(
                            anchorIsRawReads, seed, pair.CrossoverHz,
                            DirectCorroboration))
                    {
                        log.AppendLine(
                            $"  {pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}: " +
                            $"the {seedLabel} sits {Math.Abs(seedOffsetMs):0.000} ms " +
                            $"from the arrival anchor — but the pair's own " +
                            $"chains displace that anchor by " +
                            $"{chainSkewMs!.Value:+0.000;-0.000} ms, the full " +
                            $"{reachMs:0.000} ms reach and more, so the anchor " +
                            "cannot grade lobes and its veto stands down; the " +
                            "extremum stands on its own strength " +
                            $"(r {Math.Abs(seed.Coefficient):0.000})");
                        return null;
                    }

                    // Deep-pick anchor: at or above DirectSeedMinCrossoverHz the veto passes to the direct cut; below, the extremum's own strength decides.
                    if (!MayWithdrawSeedReachVeto(
                        anchorProminenceDb,
                        anchorIsRawReads,
                        seed,
                        pair.CrossoverHz,
                        DirectCorroboration))
                    {
                        return $"{seedLabel} beyond the arrival's reach";
                    }

                    log.AppendLine(
                        $"  {pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}: " +
                        $"the {seedLabel} sits {Math.Abs(seedOffsetMs):0.000} ms " +
                        $"from the arrival anchor, past its {reachMs:0.000} ms " +
                        $"reach — but that anchor was picked " +
                        $"{-anchorProminenceDb:0.0} dB under its own band's " +
                        $"energy, and the extremum stands at " +
                        $"r {Math.Abs(seed.Coefficient):0.000} — " +
                        (pair.CrossoverHz >= DirectSeedMinCrossoverHz
                            ? "the veto passes to the direct-sound cut, which puts " +
                              "the pair on this same lobe"
                            : "below the cut's own frequency there is no wavefront " +
                              "witness to pass it to, and the dominant extremum " +
                              "stands on its own strength"));
                }
                return null;
            }
            // Direct-cut witness: silenced by edge pin, weak r or position past the reach, leaving the full-record path unchanged.
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
                // Same-sign rival gate: a periodic front ties its own lobes. Honest direct-seed margins run 0.10-0.47.
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

            // Record the measured partner distance so the fine window can reach it (fixed ±2.5 ms misses it below ~200 Hz).
            // Local alias: an out parameter cannot be captured by a local function.
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
            if (SeedFamilyFollowsTheSettledPolarity(settled, seed, directSeed))
            {
                // Record's extremum is in the forbidden family: the record cannot rank lobes in the permitted one, so the cut seeds.
                increment = -directSeed!.DelayMs;
                string because = settled!.Value.Because;
                seedSource = FormattableString.Invariant(
                    $"direct-cut ({because}; the record's dominant {seedLabel} {seed.DelayMs:+0.000;-0.000} ms is the polarity this junction will not be searched in)");
                RecordPartnerReach(directPhat!);
            }
            else if (directSeed is { } adjudicated && trustPhat &&
                Math.Abs(adjudicated.DelayMs - seed.DelayMs) > halfPeriodAtFcMs)
            {
                // Two trusted extrema on different lobes: adjudicate by joint support (see DirectSeedJointTieMarginR).
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
                // Full-record extremum failed its gates; the direct front beats the envelope fallback here.
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

            // Band arrival many ms later than the channel's own energy peak is a detector artifact.
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

    // Stage-2 body shared by the mono walk and the stereo descent. See docs/tech/auto-alignment.md#fine-alignment-at-a-junction.
    // shiftScope must span both sides in stereo, or the uniform shift breaks the scene offset.
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
        // Untrusted seed across this junction (or its secondary): the base may be a half period off.
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
        double anchorMs = priorOverrideMs ?? (primaryBase + secondaryBase) / 2.0;

        // Matched split at or above DirectSeedMinCrossoverHz, no wide seed, no joint search: the filters force polarity (inverted or in phase); a caller polarity outranks it.
        // See docs/tech/auto-alignment.md#expected-polarity.
        bool? filterPolarity = SettledRelativeInversion(pair);
        bool expectsInversion = filterPolarity == true;
        if (forcedPolarity == null && filterPolarity is bool expectedInversion &&
            !wideSeed && secondaryNeighbor == null)
        {
            // Both answers are forced: a matched LR24 sums in phase as decisively as LR36 nulls. Only null is the search's.
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

        // Searched channel dropped from the overrides: its IR is undelayed, so chosen.DelayMs is absolute and no earlier uniform shift leaks in.
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
        // Each response is windowed at its own front per bins build (see BuildAlignmentBins); a shared anchor could not span settled neighbors.
        var neighborIrs = new List<Complex[]>
        {
            primaryNeighborSnapshot.ImpulseResponse
        };
        // Valid ranges travel along so front detection never reads a delay's silent prefix as SNR.
        var neighborRanges = new List<ValidSampleRange>
        {
            primaryNeighborSnapshot.ValidRange
        };
        if (secondaryNeighborSnapshot != null)
        {
            neighborIrs.Add(secondaryNeighborSnapshot.ImpulseResponse);
            neighborRanges.Add(secondaryNeighborSnapshot.ValidRange);
        }

        // Level match saturates at its cap; tell the user to level gains rather than degrade silently.
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

        // Onset lock (see OnsetLockMinCrossoverHz): not for joint two-neighbour searches; a scene lock outranks it.
        double? onsetAnchorMs = null;
        double onsetCapMs = 0;
        // Only where the anchor is the arrival envelope: a trusted whitened extremum beats a threshold onset. See docs/tech/auto-alignment.md#onset-lock.
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
                // Spread of the onset DIFFERENCE: per-channel spreads partially cancel, and the difference is what the anchor uses.
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

        // Window: coarse base(s) ± half the slowest crossover period, clamped (see fine-search-window) — absorbs coarse error without spanning two same-polarity lobes.
        (IReadOnlyList<AlignmentCandidate> Candidates,
            IReadOnlyList<AlignmentCandidate> AllOptima,
            double WindowLowMs, double WindowHighMs)
            SearchJunction(double? windowOverrideMs = null, double? centerOverrideMs = null)
        {
            // Untrusted seed: cap grows toward a half period. Trusted seed: reach the MEASURED partner distance so the loss search can settle polarity.
            // See docs/tech/auto-alignment.md#fine-search-window.
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
            double windowLowMs = (centerOverrideMs ?? Math.Min(primaryBase, secondaryBase)) - rangeMs;
            double windowHighMs = (centerOverrideMs ?? Math.Max(primaryBase, secondaryBase)) + rangeMs;
            if (sceneLockToleranceMs is { } lockTolerance && windowOverrideMs == null)
            {
                // Scene lock: the window IS the tolerance around the cross-side target.
                windowLowMs = anchorMs - lockTolerance;
                windowHighMs = anchorMs + lockTolerance;
            }
            else if (onsetAnchorMs is { } onsetAnchor && windowOverrideMs == null)
            {
                // Onset lock: the window IS the constraint; the diagnostic sweep still sees past it for the log.
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
                    // A probe centred elsewhere carries its own prior: the arrival anchor is what sent it there.
                    priorDelayMs: centerOverrideMs ?? anchorMs,
                    priorSigmaMs: (windowHighMs - windowLowMs) / 4.0,
                    forcedPolarity: forcedPolarity,
                    // Lobe choice must not depend on playback gains.
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
                // Log the window: a trusted seed's reach is not derivable from base and crossover alone.
                $", window {windowLow:0.000}..{windowHigh:0.000} ms" +
                ", candidates " +
                string.Join("; ", candidates.Select(item =>
                    $"{item.DelayMs:0.000} ms" +
                    $"{(item.InvertPolarity ? " inv" : "")} " +
                    $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                    $"dip {item.DipDb:0.0} dB)")));

            // Purity is relative to the settled neighbor: an absolute-flag preference would slide a tweeter a quarter period off its inverted twin's onset line.
            bool neighborInverted =
                alignment.GetValueOrDefault(neighborChannel).InvertPolarity;
            // "Pure" is what the filters ask for: matched LR12/LR36 and BW12/BW36 sum inverted (see ExpectsRelativeInversion).

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

            // Wide sweep: logs lobes beyond the working range; promotion may adopt its winner only at un-locked junctions.
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

            // An empty fine window is not "no evidence": adopt the wide sweep's selection.
            if (selected == null && wide.Count > 0)
            {
                selected = AlignmentSelection.Select(wide, anchorMs,
                    neighborInverted: neighborInverted,
                    expectedRelativeInversion: expectsInversion);
                log.AppendLine(
                    $"  fine window empty — adopted {selected.DelayMs:0.000} ms" +
                    $"{(selected.InvertPolarity ? " inv" : "")} from the wide sweep");
            }

            // No usable evidence in either window: refuse the run (a fabricated or skipped channel corrupts later shifts and walks).
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

            // Captured before the edge retry so a widened retry cannot stack with the promotion reach.
            AlignmentCandidate arrivalPick = chosen;

            // Edge-pinned result: retry once, wider but short of a period, with a relaxed prior (the base is suspect).
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
                    // Same selection rules: retried[0] raw could be a (flip + half-period) impostor.
                    chosen = AlignmentSelection.Select(retried, anchorMs,
                        neighborInverted: neighborInverted,
                        expectedRelativeInversion: expectsInversion);
                }

                log.AppendLine(
                    $"  WARNING: fine result at the search edge; widened to " +
                    $"±{retryRangeMs:0.000} ms -> {chosen.DelayMs:0.000} ms, " +
                    $"invert {(chosen.InvertPolarity ? "yes" : "no")}");
            }

            // Wide-seed picks beyond the trusted reach must clear the promotion standard on the prior-free score (see AlignmentSelection.GateWideSeedLobe).
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

            // Promote the wide-window optimum only at un-locked junctions and only when clearly better. See docs/tech/auto-alignment.md#wide-window-promotion.
            bool promoted = false;
            if (wide.Count > 0 && sceneLockToleranceMs == null &&
                onsetAnchorMs == null)
            {
                AlignmentCandidate wideChosen =
                    AlignmentSelection.Select(wide, anchorMs,
                        neighborInverted: neighborInverted,
                        expectedRelativeInversion: expectsInversion);
                // Reach and gain are measured on window-independent quantities: the wide window's weaker prior would otherwise credit the promotion.
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
                    // Snap to the arrival-nearest lobe that still clears the gate: the deepest sum may be a period past (0.14 dB at a 1500 Hz split).
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
            // Set where sub precedence rules and the pick stands by it: a later vote may only choose among picks it would
            // let stand. A trailing pick it found no lead for leaves the vote free.
            Func<AlignmentCandidate, bool>? subPrecedenceAdmits = null;
            // Sub precedence (see SubPrecedenceMarginDb): pool spans fine and wide sets on the prior-free score; bounded to one period past the anchor.
            if (monoChannels != null &&
                sceneLockToleranceMs == null && onsetAnchorMs == null)
            {
                bool subSearched = monoChannels.Contains(channel);
                bool subNeighbor = secondaryNeighbor == null &&
                    monoChannels.Contains(neighborChannel);
                double leadSign = subNeighbor ? 1.0 : -1.0;
                if (subSearched ^ subNeighbor)
                {
                    Func<AlignmentCandidate, bool> leadsTheStack = item =>
                        leadSign * (item.DelayMs - anchorMs) >= -SubPrecedenceSlackMs;
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

                    if (leadsTheStack(chosen))
                    {
                        subPrecedenceAdmits = leadsTheStack;
                    }
                }
            }

            // Direct-coherence witness (see DirectCoherenceMinCrossoverHz); off wherever a lock, forced polarity or joint search already pins the lobe.
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

                    // Ladder veto (see LadderVetoMaxAdvantage). See docs/tech/auto-alignment.md#coherence-ladder-veto.
                    string? ladderVetoDetail = null;
                    if (rivalR - chosenR < LadderVetoMaxAdvantage &&
                        pair.CrossoverHz >= LadderVetoMinCrossoverHz)
                    {
                        // The ladder reads the pair at its applied alignment: place it at the standing candidate first (a negative one slides the neighbor later).
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

                    // A veto is a decision too: report it.
                    directCoherenceDetail ??= ladderVetoDetail;
                }
            }

            // Direct lobe check (see docs/tech/auto-alignment.md#direct-lobe-check): the tie arbitration above only
            // weighs the flip partner within a tie. This asks the same wavefronts about the FINAL pick over a full
            // period either way, which is where a displaced search base leaves the answer.
            string? directLobeDetail = null;
            bool directLobeUnsettled = false;
            string? directLobeSkip = secondaryNeighbor != null ? "a secondary neighbour"
                : sceneLockToleranceMs != null ? "the scene lock"
                : onsetAnchorMs != null ? "the onset anchor"
                : forcedPolarity != null ? "a settled polarity"
                : null;
            if (directLobeSkip != null && pair.CrossoverHz >= DirectCoherenceMinCrossoverHz)
            {
                log.AppendLine($"  [diag] direct lobe: not asked under {directLobeSkip}.");
            }
            if (directLobeSkip == null &&
                pair.CrossoverHz >= DirectCoherenceMinCrossoverHz)
            {
                List<AlignmentCandidate> lobes = fineOptima
                    .Concat(wideOptima)
                    .Concat(retriedOptima)
                    .Where(item => Math.Abs(item.DelayMs - chosen.DelayMs) <= 2.0 * halfPeriodMs)
                    .ToList();
                List<SignalPoint> curve =
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
                        // A lobe either way, plus its own half period so each lobe's crest is interior.
                        2.5 * halfPeriodMs,
                        chosen.DelayMs,
                        phaseTransform: true);
                DirectLobeReading? reading =
                    DirectLobeWitness.Read(curve, lobes, chosen, halfPeriodMs);
                log.AppendLine(reading == null
                    ? "  [diag] direct lobe: no curve."
                    : FormattableString.Invariant(
                        $"  [diag] direct lobe: chosen {chosen.DelayMs:0.000} ms r {reading.ChosenR:0.00}; best of {lobes.Count} lobes r {reading.CandidateR:0.00}; curve best r {reading.BestR:0.00} @ {reading.BestLagMs:0.000} ms."));
                if (reading != null && DirectLobeWitness.Overturns(reading, chosen))
                {
                    log.AppendLine(
                        $"  direct lobe: preferred {reading.Candidate!.DelayMs:0.000} ms" +
                        $"{(reading.Candidate.InvertPolarity ? " inv" : "")} (direct r " +
                        $"{reading.CandidateR:0.00}) over {chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} (r {reading.ChosenR:0.00}) — " +
                        $"a lobe the wavefronts want by more than " +
                        $"{DirectLobeWitness.LobeAdvantage:0.00}.");
                    directLobeDetail = FormattableString.Invariant(
                        $"direct lobe r {reading.CandidateR:0.00} vs {reading.ChosenR:0.00} moved the pick");
                    chosen = reading.Candidate;
                    reading = DirectLobeWitness.Read(curve, lobes, chosen, halfPeriodMs);
                }

                // A lag the wavefronts want that no candidate occupies means the window never reached it. One probe
                // is searched there, and it is adopted only where the summation agrees: two lines of evidence, not one.
                if (reading != null &&
                    DirectLobeWitness.NamesAnUnreachedLobe(reading, chosen, halfPeriodMs))
                {
                    (IReadOnlyList<AlignmentCandidate> probed, _, double probeLow, double probeHigh) =
                        SearchJunction(
                            windowOverrideMs: halfPeriodMs,
                            centerOverrideMs: reading.BestLagMs);
                    AlignmentCandidate? probePick = probed.Count > 0
                        ? AlignmentSelection.Select(probed, reading.BestLagMs,
                            neighborInverted: neighborInverted,
                            expectedRelativeInversion: expectsInversion)
                        : null;
                    double probeGainDb = probePick == null
                        ? 0
                        : AcousticScore(probePick) - AcousticScore(chosen);
                    // Not the score-only lobe-hop bar: the wavefronts named this lag independently, so the summation
                    // only has to AGREE past comb noise rather than carry the move by itself.
                    if (probePick != null && probeGainDb > DecisionMediumMarginDb)
                    {
                        log.AppendLine(
                            $"  direct lobe probe: the wavefronts named {reading.BestLagMs:0.000} ms " +
                            $"(r {reading.BestR:0.00}) outside the window; searching " +
                            $"{probeLow:0.000}..{probeHigh:0.000} ms found {probePick.DelayMs:0.000} ms" +
                            $"{(probePick.InvertPolarity ? " inv" : "")}, {probeGainDb:0.00} dB better " +
                            $"than {chosen.DelayMs:0.000} ms.");
                        directLobeDetail = FormattableString.Invariant(
                            $"the direct sound reached a lobe the window missed ({probeGainDb:0.00} dB better)");
                        chosen = probePick;
                    }
                    else
                    {
                        log.AppendLine(
                            $"  direct lobe UNREACHED: the wavefronts want {reading.BestLagMs:0.000} ms " +
                            $"(r {reading.BestR:0.00}), {reading.BestLagMs - chosen.DelayMs:+0.000;-0.000} ms " +
                            $"from {chosen.DelayMs:0.000} ms (r {reading.ChosenR:0.00}), and a probe there " +
                            (probePick == null
                                ? "found no candidate."
                                : FormattableString.Invariant(
                                    $"gains only {probeGainDb:0.00} dB — the summation does not agree.")));
                        directLobeUnsettled = true;
                        directLobeDetail = FormattableString.Invariant(
                            $"the direct sound wants a lobe {reading.BestLagMs - chosen.DelayMs:+0.000;-0.000} ms away that the summation refuses");
                    }
                }
            }

            // Low-junction polarity (see docs/tech/auto-alignment.md#low-junction-polarity): where the
            // direct-coherence witness stands down, the summation cannot tell a lobe from its
            // half-period-plus-inversion twin, and the channels' own crests are the witness that remains.
            string? lowPolarityDetail = null;
            bool lowPolarityUnsettled = false;
            if (secondaryNeighbor == null &&
                sceneLockToleranceMs == null &&
                onsetAnchorMs == null &&
                forcedPolarity == null &&
                pair.CrossoverHz < DirectCoherenceMinCrossoverHz &&
                LowJunctionPolarity.Read(
                    neighborIrs[0], variableIr, channel.SampleRate,
                    primaryNeighborSnapshot.ValidRange,
                    variableSnapshot.ValidRange,
                    anchorMs) is { } crests &&
                crests.IsDecisive(pair.CrossoverHz))
            {
                // The neighbour is read as rendered, its own inversion applied, so the crests name the searched
                // channel's absolute polarity; relative to the neighbour it is that XOR the neighbour's flag.
                bool expected = crests.ExpectsRelativeInversion ^ neighborInverted;
                string phase = expected ? "inverted" : "in phase";
                // The filters do not decide down here (see #expected-polarity), but they do withhold the crests'
                // authority: a matched split that says the opposite makes this a coin flip, not a reading.
                bool contradicted = ExpectsRelativeInversion(pair) is bool filtersSay &&
                    filtersSay != expected;

                // Polarity only: the pool stays inside the neighbouring lobes so the vote cannot walk a period, and
                // out of the trailing picks sub precedence turned down, which a tie would otherwise hand straight back.
                AlignmentCandidate votedPick = contradicted
                    ? chosen
                    : LowJunctionPolarity.Decide(
                        fineOptima
                            .Concat(wideOptima)
                            .Concat(retriedOptima)
                            .Where(item => Math.Abs(item.DelayMs - chosen.DelayMs)
                                <= 1.5 * halfPeriodMs &&
                                (subPrecedenceAdmits?.Invoke(item) ?? true))
                            .ToList(),
                        chosen, AcousticScore, expected, anchorMs, neighborInverted);
                if (votedPick != chosen)
                {
                    log.AppendLine(
                        $"  low-junction polarity: preferred {votedPick.DelayMs:0.000} ms" +
                        $"{(votedPick.InvertPolarity ? " inv" : "")} over " +
                        $"{chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} — the crests meet " +
                        $"{phase} {crests.ShiftMs:0.000} ms away, {crests.SeparationMs:0.000} ms " +
                        "nearer than the other sign, and the summation ties within " +
                        $"{LowJunctionPolarity.TieMarginDb:0.00} dB.");
                    lowPolarityDetail = FormattableString.Invariant(
                        $"the crests decided the low-junction polarity tie ({phase})");
                    chosen = votedPick;
                }
                else if (contradicted)
                {
                    log.AppendLine(
                        $"  low-junction polarity unsettled: the crests meet {phase}, the matched " +
                        $"{pair.CrossoverHz:0} Hz split sums the other way — neither vetoes the " +
                        $"summation's {chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")}.");
                    lowPolarityUnsettled = true;
                    lowPolarityDetail = FormattableString.Invariant(
                        $"low-junction polarity unsettled: the crests meet {phase}, the matched split the other way");
                }
                else if (chosen.InvertPolarity != (neighborInverted ^ expected))
                {
                    log.AppendLine(
                        $"  low-junction polarity unsettled: the crests meet {phase}, but the " +
                        $"summation keeps {chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} by more than " +
                        $"{LowJunctionPolarity.TieMarginDb:0.00} dB.");
                    lowPolarityUnsettled = true;
                    lowPolarityDetail = FormattableString.Invariant(
                        $"low-junction polarity unsettled: the crests meet {phase}, the summation the opposite");
                }
            }

            double newDelay = chosen.DelayMs;
            if (newDelay < 0)
            {
                ShiftAllExcept(shiftScope, channel, -newDelay, alignment, log);
                newDelay = 0;
            }

            // Unclamped above zero: relations matter mid-run; the final feasibility check refuses a span that does not fit.
            alignment[channel] = new AlignmentOverride(
                Math.Max(0, Math.Round(newDelay, 2)),
                chosen.InvertPolarity);

            if (decisions != null)
            {
                string versus = neighborChannel.Name +
                    (secondaryNeighbor != null ? $" + {secondaryNeighbor.Name}" : "");
                // Rivals come from the UNCAPPED optimum sets (selection lists are truncated). Overlap = the weaker per-neighbor fraction,
                // each in its own junction band, so a low-junction partner is not judged over octaves it never overlaps.
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
                if (directLobeDetail != null)
                {
                    AmendDecision(
                        decisions, channel, directLobeDetail,
                        unsettled: directLobeUnsettled);
                }
                if (lowPolarityDetail != null)
                {
                    AmendDecision(
                        decisions, channel, lowPolarityDetail,
                        unsettled: lowPolarityUnsettled);
                }
                if (subPrecedenceBehindDb is { } precedenceBehindDb)
                {
                    // Report the policy override, else a negative rival margin reads as an algorithm error.
                    AmendDecision(
                        decisions, channel,
                        "sub-precedence policy: the sub-leading lobe stands " +
                        $"{precedenceBehindDb:0.00} dB behind the pre-policy " +
                        "pick by design");
                }
            }

            if (onsetAnchorMs is { } settledAnchor)
            {
                // Relative gap: later uniform shifts leave it intact.
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

    // Prior-free score: ScoreDb's prior scales with the window (sigma = window / 4), so cross-window comparisons must use this.
    private static double AcousticScore(AlignmentCandidate candidate) =>
        candidate.LossDb +
        VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
        (candidate.DipDb - candidate.LossDb);

    // Comb noise between real lobes runs a few tenths of a dB: below this the prior and tie-breaks decided, not the acoustics.
    private const double DecisionMediumMarginDb = 0.4;

    // Overlap fraction of the pair band below which a delay is precise but barely observed; healthy junctions share ~19-25 % (v3 cabin).
    private const double MinTrustedOverlapFraction = 0.12;

    // Rival = best other lobe (≥ quarter period) or opposite polarity over pooled candidates. Locks report Locked with no confidence;
    // a wide seed or an edge retry tempers a free search's confidence.
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
                level = AlignmentConfidence.Low;
            }

            confidence = level;
        }

        // User report must read the same regardless of OS locale.
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
        // The low-overlap note is about the data, so it shows on locked decisions too.
        if (overlapFraction < MinTrustedOverlapFraction)
        {
            detail += FormattableString.Invariant(
                $", low overlap ({overlapFraction * 100:0}% of band)");
        }

        return new AlignmentDecision(kind, confidence, detail);
    }

    /// <summary>
    /// Places the upper channel <paramref name="slideSamples"/> later than the lower; a negative placement slides the lower one later.
    /// A witness's lags are relative to this placement, so a wrong sign offsets every lag by the candidate delay.
    /// </summary>
    internal static (Complex[] Lower, Complex[] Upper) PlacePairAt(
        Complex[] lower, Complex[] upper, int slideSamples) =>
        slideSamples >= 0
            ? (lower, SlideBySamples(upper, slideSamples))
            : (SlideBySamples(lower, -slideSamples), upper);

    private static (ValidSampleRange Lower, ValidSampleRange Upper) PlacePairAt(
        ValidSampleRange lower, ValidSampleRange upper, int slideSamples) =>
        slideSamples >= 0
            ? (lower, SlideBySamples(upper, slideSamples))
            : (SlideBySamples(lower, -slideSamples), upper);
    /// <summary>Whole-sample slide later, keeping length; callers ask questions coarser than one sample.</summary>
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

    private static ValidSampleRange SlideBySamples(
        ValidSampleRange range, int samples) =>
        range.IsKnown && samples > 0
            ? range with
            {
                StartSample = range.StartSample + samples,
                EndSample = range.EndSample + samples
            }
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
                // No clamping: a channel pinned at the ceiling would silently break the uniform shift (and the stereo scene); the final feasibility check refuses.
                alignment[item.Channel] = currentAlignment with
                {
                    DelayMs = currentAlignment.DelayMs + shiftMs
                };
            }
        }
    }

    /// <summary>
    /// Stereo cascade: left side like <see cref="Compute"/> (mono channels final), bridge right top to left top by envelope arrivals with the scene offset,
    /// right side descends skipping mono channels, then the union is rebased to zero. Every uniform shift spans both sides.
    /// See docs/tech/auto-alignment.md#stereo-cascade.
    /// </summary>
    public static void ComputeStereo(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null,
        double maxDelayMs = DefaultMaxDelayMs)
    {
        // Arrival reads repeat on identical input across the run; see AlignmentRunMemo.
        using AlignmentRunMemo.Scope runMemo = AlignmentRunMemo.Begin();
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reprocess);
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDelayMs);
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
        // Absolute proposal: stage L uses the private overload, so clear here.
        alignment.Clear();
        decisions?.Clear();

        var onsetLocks = new Dictionary<AlignmentJunction, OnsetLockState>(
            ReferenceEqualityComparer.Instance);

        Compute(
            plan.LeftChannelsByBand, plan.LeftPairs, reprocess, alignment, log,
            onsetLocks, decisions, plan.MonoChannels);

        // Scope of every uniform shift from here on: one side alone would break the bridge's offset.
        var allChannels = new List<AlignmentSnapshot>(plan.LeftChannelsByBand);
        foreach (AlignmentSnapshot item in rightByBand)
        {
            if (allChannels.All(existing => existing.Channel != item.Channel))
            {
                allChannels.Add(item);
            }
        }

        // Bridge by envelope arrivals, not cross-correlation: L/R same-band drivers correlate as lobe-ambiguous noise (r ~0.3). See docs/tech/auto-alignment.md#stereo-cascade.
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

        // The bridge is the single link between sides: gate its arrivals and refuse rather than time a side by garbage.
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

        // Honesty certificate on the read that times a whole side: disagreement refuses; unmeasurable upper half caps confidence at Low.
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
                    plan.BridgeBandHighHz,
                    // The settled side is read through its delay.
                    alignment.GetValueOrDefault(channel).DelayMs)))
                {
                    case ArrivalCertificate.Latched:
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
            // The right top cannot be advanced: delay everything settled by the deficit and start it at zero.
            double shift = -bridgeDelay;
            ShiftAllExcept(allChannels, plan.BridgeRight, shift, alignment, log);
            bridgeDelay = 0;
            log.AppendLine(
                $"  advanced via a uniform +{shift:0.000} ms shift " +
                "of every settled channel");
        }
        alignment[plan.BridgeRight] = new AlignmentOverride(
            Math.Max(0, Math.Round(bridgeDelay, 2)), false);
        if (decisions != null)
        {
            // Bridge confidence = the weaker side's arrival SNR (clean measurements run 40-70 dB).
            double bridgeSnrDb = Math.Min(
                leftBridge.SignalToNoiseDecibels,
                rightBridge.SignalToNoiseDecibels);
            AlignmentConfidence bridgeConfidence =
                bridgeSnrDb >= BridgeHighSnrDb ? AlignmentConfidence.High
                : bridgeSnrDb >= BridgeMediumSnrDb ? AlignmentConfidence.Medium
                : AlignmentConfidence.Low;
            if (!bridgeVerified)
            {
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

        // Polarity belongs to the driver: the right top inherits the left top's sign before the right walk; reverse wiring is a manual flip.
        InheritBridgePolarity(plan, alignment, log, decisions);

        // Stage R: the stage-2 walk referenced to the bridge; mono channels are final and only measured.
        Dictionary<IAlignmentChannel, double> rightTimeline =
            BuildArrivalTimeline(
                rightByBand, plan.RightPairs, log,
                // Inherited polarity, not the crossover: stage 1 must not centre on a family stage 2 forbids.
                pair => InheritedJunctionPolarity(
                    pair, plan.MonoChannels, plan.PairLinks, alignment),
                out HashSet<AlignmentJunction> rightUntrustedSeeds,
                out Dictionary<AlignmentJunction, double> rightSeedPartnerReach);

        // Delay landing the right arrival the scene offset ahead of its left twin: the search prior and the scene-lock pin.
        // Coarse pins only the lobe; TightLock (quarter period) only from corroborated donor geometry; null = no trusted target.
        // See docs/tech/auto-alignment.md#cross-side-target.
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

            // Both sides in one band with the upper-half honesty probe: Latched = retry one band up, no reads = inadmissible link.
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

                // One instrument for the whole link so a split never subtracts a peak from an onset.
                bool energyOnset = LinkReadsEnergyOnset(
                    lowHz, highHz,
                    left.SignalToNoiseDecibels, right.SignalToNoiseDecibels);
                if (LinkBandReadsEnergyOnset(lowHz, highHz) && !energyOnset)
                {
                    log.AppendLine(
                        $"  cross-side link {rightChannel.Name}: {lowHz:0}-{highHz:0} Hz " +
                        $"read by first peaks — the energy onset needs " +
                        $"{EnergyOnsetMinimumSnrDb:0} dB on both sides " +
                        $"(L {left.SignalToNoiseDecibels:0.0}, R {right.SignalToNoiseDecibels:0.0} dB)");
                }
                if (energyOnset)
                {
                    left = AsEnergyOnset(left);
                    right = AsEnergyOnset(right);
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
                if (energyOnset)
                {
                    leftProbe = AsEnergyOnset(leftProbe);
                    rightProbe = AsEnergyOnset(rightProbe);
                }

                ArrivalCertificate leftCertificate = ClassifyLinkArrival(
                    left, leftProbe,
                    LinkProbeToleranceMs(
                        leftSnapshot, left, leftProbe, energyOnset,
                        lowHz, probeLowHz, highHz,
                        searchAlignment.GetValueOrDefault(link.Left).DelayMs),
                    energyOnset);
                ArrivalCertificate rightCertificate = ClassifyLinkArrival(
                    right, rightProbe,
                    LinkProbeToleranceMs(
                        rightSnapshot, right, rightProbe, energyOnset,
                        lowHz, probeLowHz, highHz, 0),
                    energyOnset);
                if (energyOnset)
                {
                    foreach ((string name, TimeAlignmentAnalysisResult probeRead) in
                        new[] { (link.Left.Name, leftProbe), (rightChannel.Name, rightProbe) })
                    {
                        if (probeRead.IsValid &&
                            probeRead.SignalToNoiseDecibels >= MinimumArrivalSnrDb &&
                            probeRead.SignalToNoiseDecibels < EnergyOnsetMinimumSnrDb)
                        {
                            log.AppendLine(
                                $"  cross-side link {rightChannel.Name}: {name} " +
                                $"{probeLowHz:0}-{highHz:0} Hz half at " +
                                $"{probeRead.SignalToNoiseDecibels:0.0} dB cannot " +
                                "witness an energy onset — read left uncertified");
                        }
                    }
                }
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

            // Consistency ladder: link band, then the junction band on a latch; an unmeasurable link band does not ladder.
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
            // The junction band also witnesses the link certificate, which can self-verify on a mode; it convicts only the same feature.
            // See docs/tech/auto-alignment.md#cross-side-target.
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
                    return null;
                }

                // Last rung: donor L/R geometry from other cleanly measured pairs. See docs/tech/auto-alignment.md#donor-geometry.
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

                        TimeAlignmentAnalysisResult fullLeft = Read(otherLeft, lowHz2, highHz2);
                        TimeAlignmentAnalysisResult fullRight = Read(otherRight, lowHz2, highHz2);
                        bool energyOnset2 = LinkReadsEnergyOnset(
                            lowHz2, highHz2,
                            fullLeft.SignalToNoiseDecibels, fullRight.SignalToNoiseDecibels);

                        // Donors must POSITIVELY read clean direct arrivals on both sides; absence of a latch is not proof.
                        bool CleanDirect(
                            AlignmentSnapshot side,
                            TimeAlignmentAnalysisResult full,
                            out double rawMs)
                        {
                            TimeAlignmentAnalysisResult probe = Read(side, probeLow2, highHz2);
                            if (energyOnset2)
                            {
                                full = AsEnergyOnset(full);
                                probe = AsEnergyOnset(probe);
                            }

                            double appliedMs =
                                searchAlignment.GetValueOrDefault(side.Channel).DelayMs;
                            double tolerance2 = LinkProbeToleranceMs(
                                side, full, probe, energyOnset2,
                                lowHz2, probeLow2, highHz2, appliedMs);
                            rawMs = full.FirstArrivalDelayMilliseconds - appliedMs;
                            return full.IsValid &&
                                full.SignalToNoiseDecibels >= MinimumArrivalSnrDb &&
                                ClassifyLinkArrival(full, probe, tolerance2, energyOnset2) ==
                                    ArrivalCertificate.Verified;
                        }

                        if (CleanDirect(otherLeft, fullLeft, out double rawLeft) &&
                            CleanDirect(otherRight, fullRight, out double rawRight))
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
                    // No corroborated geometry: withdraw the prior rather than hard-lock to a fabricated one.
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
                // Name only donors inside the resolver's winning span (a distance-to-median test can catch outliers).
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

            // Unverified read: pins only the lobe (Coarse), never the tight tolerance.
            double target = arrivals.Left.FirstArrivalDelayMilliseconds
                - plan.SceneOffsetMs
                - arrivals.Right.FirstArrivalDelayMilliseconds;
            log.AppendLine(
                $"  cross-side prior {rightChannel.Name}: target {target:0.000} ms " +
                $"(L arrival {arrivals.Left.FirstArrivalDelayMilliseconds:0.000}, " +
                $"raw R {arrivals.Right.FirstArrivalDelayMilliseconds:0.000} ms " +
                $"in {usedLowHz:0}-{usedHighHz:0} Hz" +
                (LinkReadsEnergyOnset(
                    usedLowHz, usedHighHz,
                    arrivals.Left.SignalToNoiseDecibels,
                    arrivals.Right.SignalToNoiseDecibels)
                    ? ", energy onsets"
                    : "") +
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

            // An already-final far-side neighbor (the mono sub) joins as a second reference, else the upper sum ruins the sub handover.
            IAlignmentChannel? secondary = null;
            AlignmentJunction? secondaryPair = null;
            int otherIndex = index + (index - neighborIndex);
            if (otherIndex >= 0 && otherIndex < rightByBand.Count &&
                plan.MonoChannels.Contains(rightByBand[otherIndex].Channel))
            {
                secondary = rightByBand[otherIndex].Channel;
                secondaryPair = plan.RightPairs[Math.Min(index, otherIndex)];
            }

            // Scene lock: localization-band pairs pin to the cross-side target; low pairs pin only the lobe (tolerance = half the tightest junction period).
            // See docs/tech/auto-alignment.md#scene-lock.
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
            // TightLock gets ±T/4: with direct arrivals unmeasurable, modes shape the sum too. A lone donor keeps ±T/2.
            double? sceneLock = cross is not { } resolved
                ? null
                : lockable && !resolved.Coarse
                    ? SceneLockToleranceMs
                    : (resolved.TightLock ? 250.0 : 500.0) / Math.Max(
                        pair.CrossoverHz,
                        secondaryPair?.CrossoverHz ?? pair.CrossoverHz);

            // Right channels inherit their left twin's sign (per-driver asymmetric inversion is impossible); the right top's sign comes from the bridge.
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

        // Shared per-pair delta keeps the scene while trading junction loss between sides.
        RebalancePairsKeepingScene(
            plan, reprocess, alignment, log, onsetLocks, maxDelayMs, decisions);

        // Both sides read the same junction before one side’s near-tie stands for both.
        RebalanceJunctionBranches(
            plan, plan.LeftChannelsByBand, rightByBand, allChannels,
            reprocess, alignment, log, maxDelayMs, decisions);

        // Mono channels are scene-invariant: this is the first pass where their right junction votes.
        ComoveMonoChannels(
            plan, reprocess, alignment, log, allChannels, maxDelayMs, decisions);

        // The polish moves the far side under the mono channels' right junctions; a mono channel that followed may release
        // a trim the polish refused for its sake, so the two alternate while both keep moving.
        // See docs/tech/auto-alignment.md#post-descent-passes.
        var polishSpentMs = new Dictionary<IAlignmentChannel, double>();
        for (int round = 1; round <= PolishMonoRounds; round++)
        {
            if (round > 1)
            {
                log.AppendLine($"Far-side polish and mono co-move, round {round}:");
            }
            bool followed =
                PolishFarSideJunctions(
                    plan, rightByBand, allChannels, reprocess, alignment, log,
                    maxDelayMs, decisions, polishSpentMs) &&
                ComoveMonoChannels(
                    plan, reprocess, alignment, log, allChannels, maxDelayMs, decisions, afterPolish: true);
            if (!followed)
            {
                break;
            }
            if (round == PolishMonoRounds)
            {
                log.AppendLine(
                    $"Far-side polish and mono co-move: still moving after {PolishMonoRounds} rounds, stopped.");
            }
        }

        NormalizeAndVerifyFeasibility(allChannels, alignment, log, maxDelayMs);

        // Invariant: no driver inverted on one side of a pair alone.
        EnforcePolaritySymmetry(plan, alignment, log, decisions);

        // Counted per driver position: the sides share polarity, so the reference side stands for both.
        NormalizePolarityPresentation(
            allChannels, alignment, log, positions: plan.LeftChannelsByBand);
    }

    // Rebase the minimum delay to zero (uniform, so negatives are legal), then refuse the run if the span exceeds the ceiling; clamping would break relations.
    internal static void NormalizeAndVerifyFeasibility(
        IReadOnlyList<AlignmentSnapshot> scope,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        double maxDelayMs = DefaultMaxDelayMs)
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
        if (widestDelayMs > maxDelayMs + 0.005)
        {
            throw new InvalidOperationException(
                "The proposed alignment does not fit the DSP delay range: " +
                $"{widest.Channel.Name} needs {widestDelayMs:0.00} ms with the " +
                $"earliest channel at 0, but the limit is {maxDelayMs:0.##} ms. " +
                "The measured spread between the earliest and latest channels " +
                "is wider than the DSP can realize.");
        }
    }

    // Post-passes append to the recorded decision so the report describes the FINAL delay and polarity.
    /// <param name="unsettled">Two witnesses answered and disagreed: the pick stands, but it is not a confident read.</param>
    private static void AmendDecision(
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions,
        IAlignmentChannel channel,
        string amendment,
        bool unsettled = false)
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
            Confidence = unsettled && existing.Confidence != null
                ? AlignmentConfidence.Low
                : existing.Confidence,
            Detail = existing.Detail.Length > 0
                ? $"{existing.Detail}; {amendment}"
                : amendment
        };
    }

    /// <summary>Min band-limited arrival SNR (dB) for any inter-side decision (bridge, cross-side targets, the panel's Δ L−R).</summary>
    public const double MinimumArrivalSnrDb = 12;

    /// <summary>Below this band centre a cross-side LINK reads the energy onset instead of the first envelope peak (links only, never the junction timeline).
    /// See docs/tech/auto-alignment.md#energy-onset-links.</summary>
    public const double EnergyOnsetBandCenterHz = 300;

    /// <summary>SNR both link sides need for the energy onset (its gate is fixed 30 dB under the peak); below, both fall back to peaks.
    /// See docs/tech/auto-alignment.md#energy-onset-links.</summary>
    public const double EnergyOnsetMinimumSnrDb = 30;

    public static bool LinkBandReadsEnergyOnset(double bandLowHz, double bandHighHz) =>
        Math.Sqrt(bandLowHz * bandHighHz) < EnergyOnsetBandCenterHz;

    /// <summary>Decided once per link from both full-band reads and applied to every read of it, so a split never subtracts a peak from an onset.</summary>
    public static bool LinkReadsEnergyOnset(
        double bandLowHz,
        double bandHighHz,
        double leftSignalToNoiseDb,
        double rightSignalToNoiseDb) =>
        LinkBandReadsEnergyOnset(bandLowHz, bandHighHz) &&
        leftSignalToNoiseDb >= EnergyOnsetMinimumSnrDb &&
        rightSignalToNoiseDb >= EnergyOnsetMinimumSnrDb;

    /// <summary><see cref="ClassifyArrival"/>, except an energy-onset read whose upper-half probe is under <see cref="EnergyOnsetMinimumSnrDb"/> is Unverified.</summary>
    public static ArrivalCertificate ClassifyLinkArrival(
        TimeAlignmentAnalysisResult full,
        TimeAlignmentAnalysisResult probe,
        double toleranceMs,
        bool energyOnset) =>
        energyOnset && probe.SignalToNoiseDecibels < EnergyOnsetMinimumSnrDb
            ? ArrivalCertificate.Unverified
            : ClassifyArrival(full, probe, toleranceMs);

    /// <summary>Arrival fields swapped for the energy onset; peak-describing figures keep their meaning.</summary>
    public static TimeAlignmentAnalysisResult AsEnergyOnset(
        TimeAlignmentAnalysisResult read) =>
        !read.IsValid
            ? read
            : read with
            {
                FirstArrivalPeakSample = read.EnergyOnsetSample,
                FirstArrivalDelayMilliseconds = read.EnergyOnsetDelayMilliseconds
            };

    /// <summary>Energy-onset reads get only the generic allowance: the chain-smear credit is graded in peaks.</summary>
    private static double LinkProbeToleranceMs(
        AlignmentSnapshot side,
        TimeAlignmentAnalysisResult full,
        TimeAlignmentAnalysisResult probe,
        bool energyOnset,
        double bandLowHz,
        double probeLowHz,
        double bandHighHz,
        double appliedDelayMs) =>
        energyOnset
            ? Math.Max(1.0, 500.0 / probeLowHz)
            : ArrivalProbeToleranceMs(
                side, full.FirstArrivalDelayMilliseconds,
                probe.FirstArrivalDelayMilliseconds,
                bandLowHz, probeLowHz, bandHighHz, appliedDelayMs);

    // Clean bridges run 40-70 dB; within ~6 dB of the refusal floor is Low.
    private const double BridgeHighSnrDb = 30;
    private const double BridgeMediumSnrDb = 18;

    // Scene lock tolerance for localization-band pairs; low pairs lock only to the lobe. See docs/tech/auto-alignment.md#scene-lock.
    private const double SceneLockToleranceMs = 0.05;

    // Generous: rejects a lone donor dominated by its own DSP asymmetry, accepts shared offsets (v3: mids +1.37, tweeters +1.41).
    private const double CrossSideDonorAgreementMs = 0.6;

    // Two-plus agreeing donors = Tight, a lone donor = Loose, none or disagreeing = no pin. See docs/tech/auto-alignment.md#donor-geometry.
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

        // Largest window MUTUALLY within tolerance (0.45 / 1.00 / 1.55 is not a cluster); two-pointer sweep.
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

        if (bestCount < 2 || windowsAtBest > 1)
        {
            return (0.0, CrossSideLockTier.None, 0, 0.0, 0.0);
        }

        double[] cluster = sorted[bestStart..(bestStart + bestCount)];
        double median = cluster.Length % 2 == 1
            ? cluster[cluster.Length / 2]
            : 0.5 * (cluster[cluster.Length / 2 - 1] + cluster[cluster.Length / 2]);
        // Contiguous in sorted order, so [min, max] names the members exactly.
        return (median, CrossSideLockTier.Tight, bestCount, cluster[0], cluster[^1]);
    }

    // Only shared band above this carries scene information; at least a third of an octave above it is required to lock.
    private const double SceneLockLocalizationLowHz = 300;

    private static bool IsSceneLockable(StereoPairLink link) =>
        link.BandHighHz >=
        Math.Max(link.BandLowHz, SceneLockLocalizationLowHz) *
        VirtualCrossoverAnalysis.MinimumArrivalBandRatio;

    // Pair co-move range; also capped per pair to half its tightest junction period (a flat window walked tweeters a lobe off).
    private const double PairComoveSearchRangeMs = 1.2;
    private const double PairComoveMinimumGainDb = 0.05;

    // Far-side polish reach: an eighth of the period of the channel's highest junction (45° there), so the leash
    // tightens up the chain; the bridge has none. See docs/tech/auto-alignment.md#post-descent-passes.
    private const double FarSidePolishReachPeriods = 0.125;
    private const double FarSidePolishMinimumGainDb = 0.01;

    // Far-side polish and mono co-move alternate this many times at most.
    private const int PolishMonoRounds = 3;

    // Mono co-move spans a half period each side in both polarities: the walk's lobe choice never heard the right junction.
    private const double MonoComoveSearchHalfPeriods = 1.0;

    // A mono lobe/polarity hop must beat in-lobe polish by this. See docs/tech/auto-alignment.md#post-descent-passes.
    private const double MonoComoveLobeHopMarginDb = 0.1;

    // A mono hop must hold every observable half-band within this: at a sub junction an impostor lobe flattered by a
    // mode wins the full band and loses the clean half. Every other move may cost a half what it gains (the stereo
    // branch: what the far side gains). See docs/tech/auto-alignment.md#one-sum-one-veto.
    private const double MonoHopHalfBandMarginDb = 0.1;

    // The dip-penalized junction loss every post-descent pass scores: a plain mean buys a hundredth of a dB with a deep notch.
    private static double PenalizedLoss(
        VirtualCrossoverAnalysis.SumLossEvaluator evaluator, double deltaMs, bool flip = false)
    {
        (double lossDb, double dipDb) = evaluator.Evaluate(deltaMs, flip);
        return lossDb +
            VirtualCrossoverAnalysis.DipExcessPenaltyWeight * (dipDb - lossDb);
    }

    // A junction's sum over a band from one render, rotated per probe; null where the band holds no delay evidence.
    private static VirtualCrossoverAnalysis.SumLossEvaluator? JunctionSum(
        IReadOnlyList<AlignmentSnapshot> render,
        IAlignmentChannel mover,
        IAlignmentChannel neighbor,
        double lowHz,
        double highHz)
    {
        AlignmentSnapshot moving = render.First(item => item.Channel == mover);
        AlignmentSnapshot held = render.First(item => item.Channel == neighbor);
        return VirtualCrossoverAnalysis.SumLossEvaluator.Create(
            moving.ImpulseResponse,
            [held.ImpulseResponse],
            mover.SampleRate,
            lowHz,
            highHz,
            // The physical sum, as the panel judges it. A level match lifts a member that is tens of dB down in a
            // half-band it barely reaches and turns its phase into a cancellation that never plays.
            levelMatch: false,
            requireDelayEvidence: true,
            gateAnchorSample: null,
            moving.ValidRange,
            [held.ValidRange]);
    }

    private static IAlignmentChannel OtherMember(AlignmentJunction junction, IAlignmentChannel member) =>
        junction.Lower.Channel == member ? junction.Upper.Channel : junction.Lower.Channel;

    // One half of a junction's band where the delay is observable: the cell no post-descent move may wreck.
    private sealed record HalfBandCell(
        AlignmentJunction Junction,
        IAlignmentChannel Neighbor,
        double LowHz,
        double HighHz,
        VirtualCrossoverAnalysis.SumLossEvaluator Sum);

    // Both halves of every junction the mover sits on; halves where one member is a filter tail are left out.
    private static List<HalfBandCell> HalfBandCells(
        IEnumerable<AlignmentJunction> junctions,
        IAlignmentChannel mover,
        IReadOnlyList<AlignmentSnapshot> render)
    {
        var cells = new List<HalfBandCell>();
        foreach (AlignmentJunction junction in junctions)
        {
            IAlignmentChannel neighbor = OtherMember(junction, mover);
            foreach (bool upperHalf in new[] { false, true })
            {
                (double lowHz, double highHz) = upperHalf
                    ? (junction.CrossoverHz, junction.BandHighHz)
                    : (junction.BandLowHz, junction.CrossoverHz);
                if (JunctionSum(render, mover, neighbor, lowHz, highHz) is { } sum)
                {
                    cells.Add(new HalfBandCell(junction, neighbor, lowHz, highHz, sum));
                }
            }
        }

        return cells;
    }

    // The first cell a move costs more than it may, named for the log; null when every half holds. The allowance is
    // the move's own gain, or MonoHopHalfBandMarginDb for a mono hop. See docs/tech/auto-alignment.md#one-sum-one-veto.
    private static string? HalfBandRefusal(
        IEnumerable<HalfBandCell> cells,
        Func<HalfBandCell, double> lossDb,
        double allowedLossDb)
    {
        foreach (HalfBandCell cell in cells)
        {
            double loss = lossDb(cell);
            if (loss > allowedLossDb + 1e-9)
            {
                return FormattableString.Invariant(
                    $"the {cell.LowHz:0}-{cell.HighHz:0} Hz half vs {cell.Neighbor.Name} by {loss:0.00} dB");
            }
        }

        return null;
    }

    // No sum-loss polarity guess for the bridge top: two separated tops comb-filter and would invert one tweeter alone.
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

    // Explicit, testable statement of the polarity-symmetry invariant; manual UI flips are untouched.
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
                return;
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

    // Co-move each linked pair by one delta (scene invariant), scored on the reference side's junctions and bounded by both sides.
    // Analytic scan: one reprocess, then e^{-jωΔ} rotations. See docs/tech/auto-alignment.md#post-descent-passes.
    private static void RebalancePairsKeepingScene(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        IReadOnlyDictionary<AlignmentJunction, OnsetLockState> onsetLocks,
        double maxDelayMs,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        if (plan.PairLinks == null)
        {
            return;
        }

        // Reach is relative to the neighbor's applied delta, else two pairs could open a full period across their shared junction.
        var comoveDeltas = new Dictionary<IAlignmentChannel, double>();

        foreach (StereoPairLink link in plan.PairLinks
            .OrderByDescending(item => item.BandHighHz))
        {
            AlignmentOverride leftOverride = alignment.GetValueOrDefault(link.Left);
            AlignmentOverride rightOverride = alignment.GetValueOrDefault(link.Right);

            // Both sides bound the delta; only the reference side scores it.
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

            // Pairs bordering the mono channel stay put: the mono is timed by the left pass alone.
            if (adjacent.Any(junction =>
                plan.MonoChannels.Contains(junction.Lower.Channel) ||
                plan.MonoChannels.Contains(junction.Upper.Channel)))
            {
                continue;
            }

            IReadOnlyList<AlignmentSnapshot> current = reprocess(alignment);

            // Only the reference (near-listener) side votes: a two-side mean buys the far junction with the near one.
            var evaluators = new List<VirtualCrossoverAnalysis.SumLossEvaluator>();
            foreach (AlignmentJunction junction in referenceAdjacent)
            {
                bool lowerMoves = junction.Lower.Channel == link.Left;
                IAlignmentChannel mover = lowerMoves
                    ? junction.Lower.Channel
                    : junction.Upper.Channel;
                IAlignmentChannel neighbor = lowerMoves
                    ? junction.Upper.Channel
                    : junction.Lower.Channel;
                // The window is held fixed across all probed deltas and rebuilt from `current`, whose fronts moved with the cascade.
                VirtualCrossoverAnalysis.SumLossEvaluator? evaluator = JunctionSum(
                    current, mover, neighbor, junction.BandLowHz, junction.BandHighHz);
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
                    total += PenalizedLoss(evaluator, deltaMs);
                }

                return total / evaluators.Count;
            }

            // Half a period around the neighbor's applied delta: single-lobed polish only.
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

                // Onset-locked junction: keep |gap| <= cap; gap_after = gap ± (delta − neighborDelta).
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

            // Bounds fixed before the search so the delta applies verbatim to both sides; relative move, walls only where the whole field leaves the DSP range.
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
                    minDelta, -pairMinMs - (maxDelayMs - maxOtherMs));
                maxDelta = Math.Min(
                    maxDelta, maxDelayMs - pairMaxMs + minOtherMs);
            }
            // Keeping the pair is always legal, even if neighbor lobes would exclude zero.
            minDelta = Math.Min(minDelta, 0.0);
            maxDelta = Math.Max(maxDelta, 0.0);
            List<HalfBandCell>? cells = null;
            double baseline = Score(0);
            double bestDelta = 0;
            double bestScore = baseline;
            double refusedDelta = 0;
            double refusedScore = baseline;
            string? refusedWhy = null;
            void Consider(double delta)
            {
                double score = Score(delta);
                if (score <= bestScore)
                {
                    return;
                }

                string? why = HalfBandRefusal(
                    cells ??= HalfBandCells(referenceAdjacent, link.Left, current),
                    cell => PenalizedLoss(cell.Sum, 0) - PenalizedLoss(cell.Sum, delta),
                    score - baseline);
                if (why != null)
                {
                    if (score > refusedScore)
                    {
                        refusedScore = score;
                        refusedDelta = delta;
                        refusedWhy = why;
                    }

                    return;
                }

                bestScore = score;
                bestDelta = delta;
            }

            double coarseStep = Math.Min(
                0.1, Math.Max(0.02, (maxDelta - minDelta) / 8.0));
            for (double delta = minDelta; delta <= maxDelta + 1e-9; delta += coarseStep)
            {
                Consider(delta);
            }
            for (double delta = Math.Max(minDelta, bestDelta - coarseStep);
                delta <= Math.Min(maxDelta, bestDelta + coarseStep) + 1e-9;
                delta += 0.02)
            {
                Consider(delta);
            }

            if (refusedWhy != null && refusedScore > bestScore)
            {
                log.AppendLine(
                    $"Co-move {link.Left.Name}+{link.Right.Name}: {refusedDelta:+0.00;-0.00} ms refused — " +
                    $"it would gain {refusedScore - baseline:0.00} dB over the reference-side junctions but loses {refusedWhy}");
            }

            if (bestDelta != 0 && bestScore > baseline + PairComoveMinimumGainDb)
            {
                // Round toward the window so rounding cannot step past a bound.
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
                comoveDeltas[link.Left] = bestDelta;
                comoveDeltas[link.Right] = bestDelta;
                log.AppendLine(
                    $"Co-move {link.Left.Name}+{link.Right.Name}: " +
                    $"{bestDelta:+0.00;-0.00} ms to both sides " +
                    $"(reference-side dip-penalized junction loss " +
                    $"{baseline:0.00} -> {bestScore:0.00} dB; scene untouched)");
                // In-lobe polish, but the final delays differ from the walk's: report it.
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

    // Far-side polish: each far channel may leave its scene position by an eighth of its highest junction's period to
    // recover its own junctions; spentMs carries each channel's trim across rounds. True when a channel moved.
    // See docs/tech/auto-alignment.md#post-descent-passes.
    internal static bool PolishFarSideJunctions(
        StereoAlignmentPlan plan,
        IReadOnlyList<AlignmentSnapshot> rightByBand,
        IReadOnlyList<AlignmentSnapshot> fullScope,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        double maxDelayMs,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions,
        Dictionary<IAlignmentChannel, double> spentMs)
    {
        bool moved = false;
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

            // The bridge IS the scene: it stands at the user's delta to its twin and polishes nothing.
            if (channel == plan.BridgeRight)
            {
                log.AppendLine(
                    $"Far-side polish {channel.Name}: none, the bridge holds the scene delta");
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

            double reachMs = FarSidePolishReachPeriods * 1000.0 /
                adjacent.Max(junction => junction.CrossoverHz);

            AlignmentOverride current = alignment.GetValueOrDefault(channel);
            // The reach is spent from the scene position: a second round otherwise walks a channel twice its leash.
            double spent = spentMs.GetValueOrDefault(channel);
            double sceneMs = current.DelayMs - spent;
            IReadOnlyList<AlignmentSnapshot> snapshots = reprocess(alignment);
            List<VirtualCrossoverAnalysis.SumLossEvaluator> evaluators = adjacent
                .Select(junction => JunctionSum(
                    snapshots, channel, OtherMember(junction, channel), junction.BandLowHz, junction.BandHighHz))
                .OfType<VirtualCrossoverAnalysis.SumLossEvaluator>()
                .ToList();
            if (evaluators.Count == 0)
            {
                continue;
            }

            // Half-band cells, built when a trim first needs them: a period-long leash can sell a junction's upper half
            // for its lower one, and the upper half is the one the front is made of.
            List<HalfBandCell>? cells = null;

            double Score(double deltaMs) =>
                evaluators.Sum(evaluator => PenalizedLoss(evaluator, deltaMs)) / evaluators.Count;

            // Feasibility span is rebased on the earliest channel: check a trial against both ends of the rest of the field.
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
            double bestTrialMs = current.DelayMs;
            double bestScore = baseline;
            double refusedDelta = 0;
            double refusedScore = baseline;
            string? refusedWhy = null;
            // Absolute ticks of the DSP's 0.01 ms grid: the exact move to a tick is scored and that tick is written. The
            // channel may stand off the grid here (the descent rebases the field by unrounded amounts).
            // One tick more than fits: from an off-grid scene the far edge holds a tick a floor would drop, and the exact
            // reach check below discards the surplus.
            int reachTicks = (int)Math.Ceiling(reachMs / 0.01);
            int sceneTick = (int)Math.Round(sceneMs / 0.01);
            for (int tick = sceneTick - reachTicks; tick <= sceneTick + reachTicks; tick++)
            {
                double trialMs = Math.Round(tick * 0.01, 2);
                double delta = trialMs - current.DelayMs;
                if (Math.Abs(trialMs - sceneMs) > reachMs + 1e-9 ||
                    trialMs < 0 ||
                    Math.Max(othersMaxMs, trialMs) -
                        Math.Min(othersMinMs, trialMs) > maxDelayMs)
                {
                    // No negatives, no uniform shift, no span widening past the DSP range (this pass runs after the cascade settled).
                    continue;
                }

                double score = Score(delta);
                if (score <= bestScore)
                {
                    continue;
                }

                if (HalfBandRefusal(
                        cells ??= HalfBandCells(adjacent, channel, snapshots),
                        cell => PenalizedLoss(cell.Sum, 0) - PenalizedLoss(cell.Sum, delta),
                        score - baseline) is { } why)
                {
                    if (score > refusedScore)
                    {
                        refusedScore = score;
                        refusedDelta = delta;
                        refusedWhy = why;
                    }
                    continue;
                }

                bestScore = score;
                bestTrialMs = trialMs;
            }

            if (refusedWhy != null && refusedScore > bestScore)
            {
                log.AppendLine(
                    $"Far-side polish {channel.Name}: {refusedDelta:+0.00;-0.00} ms refused — " +
                    $"it would gain {refusedScore - baseline:0.00} dB over its junctions but loses {refusedWhy}");
            }

            if (bestTrialMs != current.DelayMs && bestScore > baseline + FarSidePolishMinimumGainDb)
            {
                alignment[channel] = current with { DelayMs = bestTrialMs };
                spent = bestTrialMs - sceneMs;
                spentMs[channel] = spent;
                moved = true;
                log.AppendLine(
                    $"Far-side polish {channel.Name}: " +
                    $"{spent:+0.00;-0.00} ms off the scene position " +
                    $"(own-junction dip-penalized loss " +
                    $"{baseline:0.00} -> {bestScore:0.00} dB)");
                string amendment = FormattableString.Invariant(
                    $"far-side polish, now {spent:+0.00;-0.00} ms off the scene position (<= ") +
                    FormattableString.Invariant($"{reachMs:0.00} ms)");
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

        return moved;
    }

    /// <summary>Moves the stack above a junction by <paramref name="deltaMs"/> and flips its polarity, on both sides.
    /// A negative move would push the stack below zero, so the rest of the field rises by the same amount instead —
    /// the same relation, and no clamping.</summary>
    private static void ApplyBranchMove(
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        IReadOnlyCollection<IAlignmentChannel> above,
        IReadOnlyList<AlignmentSnapshot> shiftScope,
        double deltaMs)
    {
        if (deltaMs >= 0)
        {
            foreach (IAlignmentChannel channel in above)
            {
                AlignmentOverride over = alignment.GetValueOrDefault(channel);
                alignment[channel] = over with { DelayMs = over.DelayMs + deltaMs };
            }
        }
        else
        {
            foreach (AlignmentSnapshot item in shiftScope)
            {
                if (above.Contains(item.Channel))
                {
                    continue;
                }

                AlignmentOverride over = alignment.GetValueOrDefault(item.Channel);
                alignment[item.Channel] = over with { DelayMs = over.DelayMs - deltaMs };
            }
        }

        foreach (IAlignmentChannel channel in above)
        {
            AlignmentOverride over = alignment.GetValueOrDefault(channel);
            alignment[channel] = over with
            {
                InvertPolarity = !over.InvertPolarity
            };
        }
    }

    // A junction the reference side could not tell apart commits the far side too. Moving the whole stack ABOVE the
    // junction, on both sides, by half a period with a polarity flip changes that junction and nothing else.
    // See docs/tech/auto-alignment.md#stereo-branch-check.
    internal static void RebalanceJunctionBranches(
        StereoAlignmentPlan plan,
        IReadOnlyList<AlignmentSnapshot> referenceByBand,
        IReadOnlyList<AlignmentSnapshot> farByBand,
        IReadOnlyList<AlignmentSnapshot> shiftScope,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        double maxDelayMs = DefaultMaxDelayMs,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
    {
        if (plan.LeftPairs.Count != plan.RightPairs.Count)
        {
            return;
        }

        IAlignmentChannel? Counterpart(IAlignmentChannel channel) =>
            plan.MonoChannels.Contains(channel)
                ? channel
                : plan.PairLinks?.FirstOrDefault(link => link.Left == channel)?.Right;

        for (int index = 0; index < plan.LeftPairs.Count; index++)
        {
            AlignmentJunction reference = plan.LeftPairs[index];
            AlignmentJunction far = plan.RightPairs[index];
            // Twins only: a staged side with a different channel order would score two different junctions.
            if (Counterpart(reference.Lower.Channel) != far.Lower.Channel ||
                Counterpart(reference.Upper.Channel) != far.Upper.Channel ||
                far.Upper.Channel == reference.Upper.Channel)
            {
                continue;
            }

            // The stack above the junction moves rigidly, so every junction above it is untouched.
            List<IAlignmentChannel> above =
            [
                .. referenceByBand.Skip(index + 1).Select(item => item.Channel),
                .. farByBand.Skip(index + 1).Select(item => item.Channel)
            ];
            above = [.. above.Distinct()];
            if (above.Count == 0 || above.Any(plan.MonoChannels.Contains))
            {
                continue;
            }

            IReadOnlyList<AlignmentSnapshot> current = reprocess(alignment);
            VirtualCrossoverAnalysis.SumLossEvaluator? FullBand(AlignmentJunction junction) =>
                JunctionSum(
                    current, junction.Upper.Channel, junction.Lower.Channel,
                    junction.BandLowHz, junction.BandHighHz);

            if (FullBand(reference) is not { } referenceBand ||
                FullBand(far) is not { } farBand)
            {
                continue;
            }

            double halfPeriodMs = 500.0 / reference.CrossoverHz;
            double BranchScore(bool farSide, double deltaMs, bool flip) =>
                PenalizedLoss(farSide ? farBand : referenceBand, deltaMs, flip);
            StereoBranchReading? reading = StereoJunctionBranch.Read(
                BranchScore, halfPeriodMs);
            if (reading == null ||
                reading.FarGainDb <= StereoJunctionBranch.NoteworthyFarGainDb)
            {
                continue;
            }

            // The scan's optimum is moved onto the DSP's 0.01 ms grid here, so the re-render judges, the log names
            // and the alignment carries the delay the processor will actually play.
            reading = StereoJunctionBranch.Quantize(reading, BranchScore);
            string junctionName =
                $"{reference.Lower.Channel.Name}/{reference.Upper.Channel.Name}";
            string move = FormattableString.Invariant(
                $"{reading.DeltaMs:+0.00;-0.00} ms flipped");
            if (!StereoJunctionBranch.Adopt(reading))
            {
                log.AppendLine(
                    $"  stereo branch declined at {reference.Lower.Channel.Name}/" +
                    $"{reference.Upper.Channel.Name}: {move} would gain " +
                    $"{reading.FarGainDb:0.00} dB on the far side but " +
                    $"{reading.ReferenceGainDb:+0.00;-0.00} dB on the reference side.");
                continue;
            }

            // The scan rotates inside a fixed window; before a branch is adopted the candidate is RE-RENDERED and
            // measured, because a half period is where that approximation is weakest.
            var trial = new Dictionary<IAlignmentChannel, AlignmentOverride>(alignment);
            ApplyBranchMove(trial, above, shiftScope, reading.DeltaMs);
            IReadOnlyList<AlignmentSnapshot> rendered = reprocess(trial);
            double? Rendered(AlignmentJunction junction, double lowHz, double highHz) =>
                JunctionSum(
                    rendered, junction.Upper.Channel, junction.Lower.Channel, lowHz, highHz) is { } sum
                    ? PenalizedLoss(sum, 0)
                    : null;

            double GainOf(AlignmentJunction junction, VirtualCrossoverAnalysis.SumLossEvaluator before) =>
                Rendered(junction, junction.BandLowHz, junction.BandHighHz) is { } after
                    ? after - PenalizedLoss(before, 0)
                    : 0;

            var verified = new StereoBranchReading(
                reading.DeltaMs,
                true,
                GainOf(reference, referenceBand),
                GainOf(far, farBand));

            // The far gain is the whole justification for disturbing a settled junction, so it is what a half may cost.
            string? refusal = HalfBandRefusal(
                HalfBandCells([reference], reference.Upper.Channel, current)
                    .Concat(HalfBandCells([far], far.Upper.Channel, current)),
                cell => PenalizedLoss(cell.Sum, 0) -
                    (Rendered(cell.Junction, cell.LowHz, cell.HighHz) ?? PenalizedLoss(cell.Sum, 0)),
                Math.Max(0.0, verified.FarGainDb));
            if (refusal != null)
            {
                refusal = "it loses " + refusal;
            }

            // The move is optional, and the field must stay realizable: a span past the ceiling would make the final
            // feasibility check refuse the whole run for a branch it could simply have kept.
            if (refusal == null)
            {
                List<double> trialDelays = shiftScope
                    .Select(item => trial.GetValueOrDefault(item.Channel).DelayMs)
                    .ToList();
                double spanMs = trialDelays.Max() - trialDelays.Min();
                if (spanMs > maxDelayMs)
                {
                    refusal = FormattableString.Invariant(
                        $"the field would span {spanMs:0.00} ms, past the {maxDelayMs:0} ms ceiling");
                }
            }

            if (refusal == null && !StereoJunctionBranch.Adopt(verified))
            {
                refusal = FormattableString.Invariant(
                    $"re-rendered it gains {verified.FarGainDb:0.00} dB on the far side and {verified.ReferenceGainDb:+0.00;-0.00} dB on the reference one");
            }
            if (refusal != null)
            {
                log.AppendLine(
                    $"  stereo branch declined at {junctionName}: {move} gains " +
                    $"{verified.FarGainDb:+0.00;-0.00} dB on the far junction and " +
                    $"{verified.ReferenceGainDb:+0.00;-0.00} dB on the reference one — {refusal}.");
                continue;
            }

            reading = verified;
            ApplyBranchMove(alignment, above, shiftScope, reading.DeltaMs);

            log.AppendLine(
                $"  stereo branch moved at {reference.Lower.Channel.Name}/" +
                $"{reference.Upper.Channel.Name}: the stack above it went {move} on " +
                $"both sides — the far junction gains {reading.FarGainDb:0.00} dB and " +
                $"the reference one {reading.ReferenceGainDb:+0.00;-0.00} dB.");
            if (decisions != null)
            {
                foreach (IAlignmentChannel channel in above)
                {
                    AmendDecision(
                        decisions,
                        channel,
                        FormattableString.Invariant(
                            $"moved {move} with the stack above {junctionName}: the far side wanted the other branch by {reading.FarGainDb:0.00} dB"));
                }
            }
        }
    }

    // Mono co-move: sweep the mono channel over both polarities for the best mean over its left and right junctions.
    // True when a mono channel moved. See docs/tech/auto-alignment.md#post-descent-passes.
    internal static bool ComoveMonoChannels(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        IReadOnlyList<AlignmentSnapshot> shiftScope,
        double maxDelayMs = DefaultMaxDelayMs,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null,
        bool afterPolish = false)
    {
        bool moved = false;
        foreach (IAlignmentChannel mono in plan.MonoChannels)
        {
            // One junction per side; the same-fc twins differ in the neighbor channel.
            List<AlignmentJunction> junctions = plan.LeftPairs
                .Concat(plan.RightPairs)
                .Where(pair => pair.Lower.Channel == mono ||
                    pair.Upper.Channel == mono)
                .Distinct()
                .ToList();
            if (junctions.Count < 2)
            {
                continue;
            }

            // Every junction must hold delay evidence on its own (the descent's combined band can hide an evidence-less sub junction); else abstain.
            // One render, rotation evaluators per junction and half-band: windows travel with their channels.
            IReadOnlyList<AlignmentSnapshot> certified = reprocess(alignment);
            var fullBand =
                new Dictionary<AlignmentJunction,
                    VirtualCrossoverAnalysis.SumLossEvaluator>();
            AlignmentJunction? unmeasurable = null;
            foreach (AlignmentJunction junction in junctions)
            {
                if (JunctionSum(
                        certified, mono, OtherMember(junction, mono),
                        junction.BandLowHz, junction.BandHighHz) is { } evaluator)
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
                IAlignmentChannel silentNeighbor = OtherMember(silent, mono);
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

            // Relative move: bounds close only where the whole field leaves the DSP range.
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
                -reachMs, -over.DelayMs - (maxDelayMs - maxOtherMs));
            double maxDelta = Math.Min(
                reachMs, maxDelayMs - over.DelayMs + minOtherMs);

            double Score(double deltaMs, bool flip)
            {
                double total = 0;
                foreach (AlignmentJunction junction in junctions)
                {
                    total += PenalizedLoss(fullBand[junction], deltaMs, flip);
                }

                return total / junctions.Count;
            }

            double baseline = Score(0, flip: false);
            if (double.IsNegativeInfinity(baseline))
            {
                continue;
            }

            // The in-lobe polish candidate is tracked separately: a lobe/polarity hop must plainly beat it.
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
                // Per (junction, half-band) cell against the better of in-lobe polish and the incumbent: a hop holds
                // every observable half. See docs/tech/auto-alignment.md#post-descent-passes.
                string? veto = HalfBandRefusal(
                    HalfBandCells(junctions, mono, certified),
                    cell => Math.Max(
                        PenalizedLoss(cell.Sum, bestPolishDelta),
                        PenalizedLoss(cell.Sum, 0)) - PenalizedLoss(cell.Sum, bestDelta, bestFlip),
                    MonoHopHalfBandMarginDb);
                if (veto != null)
                {
                    log.AppendLine(
                        $"  mono lobe hop vetoed for {mono.Name}: " +
                        $"{bestDelta:+0.00;-0.00} ms" +
                        $"{(bestFlip ? " flipped" : "")} wins the full band " +
                        $"by {bestScore - bestPolishScore:0.00} dB but loses " +
                        $"{veto} — a true lobe holds every measurable sub-band.");
                    bestScore = bestPolishScore;
                    bestDelta = bestPolishDelta;
                    bestFlip = false;
                }
            }

            if ((bestDelta != 0 || bestFlip) &&
                bestScore > baseline + PairComoveMinimumGainDb)
            {
                // Out-of-range results rebase the rest of the field, the equivalence the bounds assumed.
                moved = true;
                double newDelayMs = Math.Round(over.DelayMs + bestDelta, 2);
                if (newDelayMs < 0)
                {
                    ShiftAllExcept(shiftScope, mono, -newDelayMs, alignment, log);
                    newDelayMs = 0;
                }
                else if (newDelayMs > maxDelayMs)
                {
                    ShiftAllExcept(
                        shiftScope, mono, maxDelayMs - newDelayMs, alignment, log);
                    newDelayMs = maxDelayMs;
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
                if (afterPolish && !bestFlip && Math.Abs(bestDelta) <= polishReachMs + 1e-9)
                {
                    // A trim that follows the polished far side amends the decision; only a hop re-decides it.
                    AmendDecision(
                        decisions, mono,
                        FormattableString.Invariant(
                            $"mono co-move {bestDelta:+0.00;-0.00} ms after the far-side polish"));
                }
                else if (decisions != null)
                {
                    // Re-decided from both sides; confidence maps the gain onto the co-move's calibrated scale (see MonoComoveLobeHopMarginDb).
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

        return moved;
    }

    // Both sides already final (mono sub vs settled right channel): log the price of sharing one mono channel.
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
            double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);

            // Centre the lag window on the arrival-based coarse delay so a multi-ms LF offset stays inside.
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
