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
/// A channel as the alignment engine sees it: an identity (reference
/// equality keys the override maps), a display name for the diagnostic log
/// and a sample rate. The caller's channel model implements this.
/// </summary>
public interface IAlignmentChannel
{
    string Name { get; }
    int SampleRate { get; }
}

/// <summary>
/// One channel's processed impulse response for an alignment round: the full
/// DSP chain applied, ready for arrival detection and correlation.
/// </summary>
public sealed record AlignmentSnapshot(
    IAlignmentChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex);

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
/// <see cref="SceneOffsetMs"/> is positive when the right side should LEAD
/// (arrive earlier at the microphone) by that much — the "image toward the
/// dash center" convention for a left-seated listener; negative for a
/// right-seated one.
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
    double SceneOffsetMs);

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
/// Time-Alignment-style band-limited first arrivals give a coarse delay per
/// channel (robust, no phase ambiguity). Stage 2: walking pair by pair
/// outward from the reference, a phase correlation fine-tunes each channel
/// against its settled neighbor inside their shared pair band, also deciding
/// whether its polarity should flip. The latest-arriving channel is the
/// fixed reference, so the proposed delays stay non-negative by
/// construction.
/// </summary>
public static class AutoAlignmentEngine
{
    // Bounds of the stage-2 fine-search span. The span scales with the
    // crossover frequency (half its period) because the coarse arrival error
    // grows with the period — but it never drops below half a millisecond:
    // arrival estimates carry a floor of error (filter group-delay asymmetry,
    // driver rise time) that does not shrink with the junction period, so at a
    // high split half a period would regularly miss the true optimum. The
    // extra lobes a wide window admits are handled by the candidate list, the
    // arrival prior, and the physical tie-break in AlignmentSelection.
    private const double MinFineAlignmentRangeMs = 0.5;
    private const double MaxFineAlignmentRangeMs = 2.5;

    // The delay ceiling a proposal may reach, mirroring the UI's per-channel
    // delay limit. Kept here so the uniform negative-delay shift can detect —
    // and log — the rare case where clamping a pinned channel breaks the
    // shift's alignment-preserving property.
    private const double MaxDelayMs = 100;

    // Diagnostics only: a deliberately wide fine-search window (many periods at a
    // high crossover, ~one at a low one) whose candidates are logged but never
    // chosen. It surfaces summation optima that sit several lobes outside the
    // working window, so a log can show whether a better lobe exists there.
    private const double DiagnosticFineRangeMs = 3.0;
    private const double DiagnosticCorrelationRangeMs = 3.0;

    // The minimum non-inverted PHAT peak correlation for its position to seed the
    // stage-2 window instead of the arrival envelope. Below it the peak is noise
    // (a low-frequency junction with too few in-band periods), and the arrival
    // estimate stands. Deliberately low: even a modest genuine peak beats the
    // arrival envelope, and the loss search plus the wide-window promotion recover
    // from a seed that still lands a little off.
    private const double PhatSeedMinCoefficient = 0.15;

    // The minimum dominance (Confidence: |best extremum| minus |its rival|) the
    // PHAT correlation must show before its peak position is trusted as the seed.
    // A junction whose corners leave a spectral gap (e.g. LP 1300 / HP 1800)
    // narrows the effective overlap, and the whitened correlation degenerates
    // into a comb of near-equal lobes: the peak coefficient still looks healthy,
    // but which lobe it sits on is decided by noise — trusting it can move the
    // seed whole periods off the arrival estimate (a cycle skip the prior then
    // cements). Peak-vs-trough closeness is exactly that lobe ambiguity, so a
    // near-tie sends the seed back to the polarity-blind arrival envelope.
    private const double PhatSeedMinDominance = 0.1;

    // How much better (in score dB) a wide-window optimum must be before it
    // unseats the arrival-anchored fine pick. The narrow window is centered on
    // the coarse arrival, which at a high crossover can be a whole lobe off (its
    // period is a fraction of the arrival uncertainty); the promotion recovers
    // that lobe, while the margin keeps the physically-minimal arrival pick
    // unless a distinctly better summation exists elsewhere.
    private const double WideWindowPromotionMarginDb = 0.2;

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
        StringBuilder log)
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
            BuildArrivalTimeline(byBand, pairs, log);

        // The relatively latest channel is the fixed reference; everyone else is
        // delayed toward it, so the coarse deltas are non-negative.
        double latest = timeline.Values.Max();
        IAlignmentChannel reference =
            timeline.First(pair => pair.Value == latest).Key;
        log.AppendLine($"Reference: {reference.Name}");

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
                timeline, byBand, reprocess, alignment, log);
        }
        for (int i = referenceIndex + 1; i < byBand.Count; i++)
        {
            AlignChannelAtJunction(
                byBand[i].Channel, byBand[i - 1].Channel, pairs[i - 1],
                timeline, byBand, reprocess, alignment, log);
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
    private static Dictionary<IAlignmentChannel, double> BuildArrivalTimeline(
        IReadOnlyList<AlignmentSnapshot> byBand,
        IReadOnlyList<AlignmentJunction> pairs,
        StringBuilder log)
    {
        var timeline = new Dictionary<IAlignmentChannel, double>
        {
            [byBand[0].Channel] = 0
        };
        foreach (AlignmentJunction pair in pairs)
        {
            double lowerArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Lower.ImpulseResponse,
                pair.Lower.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz);
            double upperArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Upper.ImpulseResponse,
                pair.Upper.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz);

            // Refine the coarse offset with the non-inverted GCC-PHAT peak: at a
            // mid/high junction it lands the stage-2 window on the correct lobe
            // directly, sparing the wide-window recovery. Only the peak POSITION
            // is used (polarity and the final lobe stay with the loss search), and
            // only when the peak carries a real correlation — otherwise the arrival
            // envelope stands. The PHAT window is centered on the arrival estimate,
            // so a trusted peak is by construction within reach of it. The timeline
            // stores arrivals as (upper - lower); the PHAT peak is the delay to add
            // to the upper channel, i.e. the same quantity negated.
            double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);
            CorrelationAlignmentResult phat =
                VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                    pair.Lower.ImpulseResponse,
                    pair.Upper.ImpulseResponse,
                    pair.Lower.Channel.SampleRate,
                    pair.CrossoverHz,
                    passOctaves,
                    DiagnosticCorrelationRangeMs,
                    centerLagMs: lowerArrival - upperArrival,
                    phaseTransform: true);
            bool trustPhat =
                phat.PositivePeak.Coefficient >= PhatSeedMinCoefficient &&
                phat.Confidence >= PhatSeedMinDominance;
            double increment =
                trustPhat ? -phat.PositivePeak.DelayMs : upperArrival - lowerArrival;
            timeline[pair.Upper.Channel] = timeline[pair.Lower.Channel] + increment;

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
                $"phat peak {phat.PositivePeak.DelayMs:+0.000;-0.000} ms " +
                $"(r {phat.PositivePeak.Coefficient:+0.000;-0.000}, " +
                $"dom {phat.Confidence:0.000}) -> seed " +
                $"{(trustPhat ? "phat" : "arrival")}");
        }

        return timeline;
    }

    // Fine-aligns one channel against its settled neighbor at their shared
    // junction and writes the result into the alignment map: the stage-2 body
    // shared by the mono walk and the stereo right-side descent. A physically
    // impossible negative delay is converted into a uniform shift of every
    // OTHER channel in <paramref name="shiftScope"/> (a uniform shift preserves
    // the alignment) — in a stereo run the scope must span BOTH sides, or the
    // shift would silently break the inter-side scene offset.
    private static void AlignChannelAtJunction(
        IAlignmentChannel channel,
        IAlignmentChannel neighborChannel,
        AlignmentJunction pair,
        IReadOnlyDictionary<IAlignmentChannel, double> timeline,
        IReadOnlyList<AlignmentSnapshot> shiftScope,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
    {
        // One junction search: candidates of the prior-penalized loss score in
        // a window around the coarse delta (the PHAT-seeded timeline, arrival
        // envelope where PHAT was untrusted). The window is half the period of
        // THIS junction's crossover — wide enough to absorb the coarse error
        // (which grows with the period), narrow enough not to span two
        // same-polarity lobes. The base doubles as a soft prior: a quadratic dB
        // penalty that deters far lobes.
        (IReadOnlyList<AlignmentCandidate> Candidates, double BaseDelta, double HalfPeriodMs)
            SearchJunction(double? windowOverrideMs = null)
        {
            // Reprocess so the settled neighbors participate with their new
            // delays and polarities. The searched channel is dropped from the
            // override map so its response is the raw, undelayed IR — the search
            // provides the delay, and chosen.DelayMs is then the absolute delay
            // to assign. Without this reset, a uniform shift applied earlier to
            // a not-yet-searched channel (the negative-delay branch below) would
            // bake a stray offset into variableIr that the reported delay does
            // not account for, mis-aligning that channel by the shift.
            var searchAlignment =
                new Dictionary<IAlignmentChannel, AlignmentOverride>(alignment);
            searchAlignment.Remove(channel);
            IReadOnlyList<AlignmentSnapshot> current = reprocess(searchAlignment);
            Complex[] variableIr = current
                .First(item => item.Channel == channel).ImpulseResponse;
            var neighborIrs = new List<Complex[]>
            {
                current.First(item => item.Channel == neighborChannel).ImpulseResponse
            };

            double halfPeriodMs = 500.0 / pair.CrossoverHz;
            double rangeMs = windowOverrideMs ?? Math.Clamp(
                halfPeriodMs, MinFineAlignmentRangeMs, MaxFineAlignmentRangeMs);
            double baseDelta = alignment.GetValueOrDefault(neighborChannel).DelayMs
                + timeline[neighborChannel] - timeline[channel];
            IReadOnlyList<AlignmentCandidate> candidates =
                VirtualCrossoverAnalysis.FindAlignmentCandidates(
                    variableIr,
                    neighborIrs,
                    channel.SampleRate,
                    pair.BandLowHz,
                    pair.BandHighHz,
                    baseDelta - rangeMs,
                    baseDelta + rangeMs,
                    priorDelayMs: baseDelta,
                    priorSigmaMs: rangeMs / 2);
            return (candidates, baseDelta, halfPeriodMs);
        }

        {
            (IReadOnlyList<AlignmentCandidate> candidates, double baseDelta,
                double halfPeriodMs) = SearchJunction();
            log.AppendLine(
                $"Channel {channel.Name}: " +
                $"vs {neighborChannel.Name} " +
                $"in {pair.BandLowHz:0}-{pair.BandHighHz:0} Hz, " +
                $"base {baseDelta:0.000} ms, candidates " +
                string.Join("; ", candidates.Select(item =>
                    $"{item.DelayMs:0.000} ms" +
                    $"{(item.InvertPolarity ? " inv" : "")} " +
                    $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                    $"dip {item.DipDb:0.0} dB)")));

            AlignmentCandidate chosen = candidates.Count > 0
                ? AlignmentSelection.Select(candidates, baseDelta)
                : new AlignmentCandidate(baseDelta, false, 0);
            if (candidates.Count > 0 && chosen != candidates[0])
            {
                log.AppendLine(
                    $"  preferred {chosen.DelayMs:0.000} ms" +
                    $"{(chosen.InvertPolarity ? " inv" : "")} over " +
                    $"{candidates[0].DelayMs:0.000} ms" +
                    $"{(candidates[0].InvertPolarity ? " inv" : "")} " +
                    $"(margin {candidates[0].ScoreDb - chosen.ScoreDb:0.00} dB)");
            }

            // Diagnostic wide sweep: the same junction searched across a much
            // wider window so lobes beyond the working range appear in the log.
            // Purely informational — the chosen result above is untouched.
            (IReadOnlyList<AlignmentCandidate> wide, double wideBase, _) =
                SearchJunction(windowOverrideMs: DiagnosticFineRangeMs);
            log.AppendLine(
                $"  [diag] wide +-{DiagnosticFineRangeMs:0.0} ms " +
                $"(base {wideBase:0.000} ms): " +
                (wide.Count > 0
                    ? string.Join("; ", wide.Select(item =>
                        $"{item.DelayMs:0.000} ms" +
                        $"{(item.InvertPolarity ? " inv" : "")} " +
                        $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                        $"dip {item.DipDb:0.0} dB)"))
                    : "none"));

            double fineRangeMs = Math.Clamp(
                halfPeriodMs, MinFineAlignmentRangeMs, MaxFineAlignmentRangeMs);

            // A result pinned to the window edge means the optimum lies beyond
            // the coarse estimate's reach — retry once, widened but still short
            // of a full period so the search cannot land on the next lobe. The
            // edge hit means the base itself is suspect, so the retry relaxes
            // the prior along with the window.
            double retryRangeMs = Math.Min(1.8 * halfPeriodMs, 3.0);
            if (retryRangeMs > fineRangeMs &&
                Math.Abs(chosen.DelayMs - baseDelta) >= fineRangeMs - 0.02)
            {
                (IReadOnlyList<AlignmentCandidate> retried, _, _) =
                    SearchJunction(windowOverrideMs: retryRangeMs);
                if (retried.Count > 0)
                {
                    // Through the same selection rules as the primary pick:
                    // taking retried[0] raw would let the widened window hand
                    // the result to a (flip + half-period) impostor that the
                    // invert margin and the arrival tie-break exist to reject.
                    chosen = AlignmentSelection.Select(retried, baseDelta);
                }

                log.AppendLine(
                    $"  WARNING: fine result at the search edge; widened to " +
                    $"±{retryRangeMs:0.000} ms -> {chosen.DelayMs:0.000} ms, " +
                    $"invert {(chosen.InvertPolarity ? "yes" : "no")}");
            }

            // Promote the wide-window optimum when it clearly beats the
            // arrival-anchored pick: the coarse arrival can sit a whole lobe off
            // at a high crossover, and the narrow window cannot reach the true
            // summation optimum a few periods away. AlignmentSelection applies
            // the same flip/tie rules to the wide set, and the margin ensures a
            // mere lobe/flip impostor cannot pull the result off the arrival.
            if (wide.Count > 0)
            {
                AlignmentCandidate wideChosen = AlignmentSelection.Select(wide, wideBase);
                if (wideChosen.ScoreDb > chosen.ScoreDb + WideWindowPromotionMarginDb)
                {
                    log.AppendLine(
                        $"  promoted {wideChosen.DelayMs:0.000} ms" +
                        $"{(wideChosen.InvertPolarity ? " inv" : "")} " +
                        $"over {chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} " +
                        $"(gain {wideChosen.ScoreDb - chosen.ScoreDb:0.00} dB)");
                    chosen = wideChosen;
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

            alignment[channel] = new AlignmentOverride(
                Math.Clamp(Math.Round(newDelay, 2), 0, MaxDelayMs),
                chosen.InvertPolarity);
        }
    }

    // A uniform delay shift of every channel in the scope but one: the standard
    // way to "advance" a channel that would otherwise need a negative delay.
    // Uniformity is what preserves the alignment, so the scope must cover every
    // channel whose relative timing has already been settled.
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
                double shifted = currentAlignment.DelayMs + shiftMs;
                if (shifted > MaxDelayMs)
                {
                    // The shift is only alignment-preserving while it is
                    // uniform; a channel pinned at the ceiling breaks the
                    // relative delays silently, so say so in the log.
                    log.AppendLine(
                        $"  WARNING: uniform shift +{shiftMs:0.000} ms pushes " +
                        $"{item.Channel.Name} past the {MaxDelayMs:0} ms " +
                        $"delay limit ({shifted:0.000} ms, clamped) — " +
                        "the relative alignment is no longer preserved.");
                }
                alignment[item.Channel] = currentAlignment with
                {
                    DelayMs = Math.Min(MaxDelayMs, shifted)
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
        StringBuilder log)
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

        // Stage L: the left side, exactly like a mono run.
        Compute(plan.LeftChannelsByBand, plan.LeftPairs, reprocess, alignment, log);

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
        // offset makes the right side LEAD (arrive earlier), pulling the image
        // toward the right — the dash-center convention for a left-seated
        // driver; a right-seated driver enters a negative offset.
        IReadOnlyList<AlignmentSnapshot> settled = reprocess(alignment);
        double leftArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            settled.First(item => item.Channel == plan.BridgeLeft).ImpulseResponse,
            plan.BridgeLeft.SampleRate,
            plan.BridgeBandLowHz,
            plan.BridgeBandHighHz);
        double rightArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            settled.First(item => item.Channel == plan.BridgeRight).ImpulseResponse,
            plan.BridgeRight.SampleRate,
            plan.BridgeBandLowHz,
            plan.BridgeBandHighHz);
        double bridgeDelay = leftArrival - rightArrival - plan.SceneOffsetMs;
        log.AppendLine(
            $"Bridge {plan.BridgeLeft.Name} -> {plan.BridgeRight.Name}: " +
            $"band {plan.BridgeBandLowHz:0}-{plan.BridgeBandHighHz:0} Hz, " +
            $"arrivals L {leftArrival:0.000} / R {rightArrival:0.000} ms, " +
            $"scene offset {plan.SceneOffsetMs:+0.000;-0.000} ms " +
            $"(positive: right leads) -> right delay {bridgeDelay:0.000} ms");
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
        // The bridge sets only the timing; the polarity of the right top stays
        // with the channel's own switch (arrivals are polarity-blind, and a
        // symmetric install wires both tweeters alike).
        alignment[plan.BridgeRight] = new AlignmentOverride(
            Math.Clamp(Math.Round(bridgeDelay, 2), 0, MaxDelayMs), false);

        // Stage R: descent from the bridged top toward the low end (and up
        // from it, if the caller had to bridge a non-top pair) — the same walk
        // as stage 2, referenced to the bridge. Mono channels are final from
        // the left pass: never searched again, their right-side junction is
        // only measured.
        Dictionary<IAlignmentChannel, double> rightTimeline =
            BuildArrivalTimeline(rightByBand, plan.RightPairs, log);
        void AlignRight(int index, int neighborIndex, AlignmentJunction pair)
        {
            IAlignmentChannel channel = rightByBand[index].Channel;
            IAlignmentChannel neighbor = rightByBand[neighborIndex].Channel;
            if (plan.MonoChannels.Contains(channel))
            {
                MeasureFixedJunction(pair, channel, neighbor, reprocess, alignment, log);
                return;
            }

            AlignChannelAtJunction(
                channel, neighbor, pair,
                rightTimeline, allChannels, reprocess, alignment, log);
        }
        for (int i = bridgeIndex - 1; i >= 0; i--)
        {
            AlignRight(i, i + 1, plan.RightPairs[i]);
        }
        for (int i = bridgeIndex + 1; i < rightByBand.Count; i++)
        {
            AlignRight(i, i - 1, plan.RightPairs[i - 1]);
        }

        // Final normalization: the smallest total latency that preserves every
        // relation — the minimum proposed delay lands exactly at zero.
        // (Channels without an entry sit at zero, so this only acts when a
        // uniform shift raised the whole field.)
        double minimum = allChannels.Min(
            item => alignment.GetValueOrDefault(item.Channel).DelayMs);
        if (minimum > 0.005)
        {
            foreach (AlignmentSnapshot item in allChannels)
            {
                AlignmentOverride current = alignment.GetValueOrDefault(item.Channel);
                alignment[item.Channel] = current with
                {
                    DelayMs = Math.Round(current.DelayMs - minimum, 2)
                };
            }
            log.AppendLine(
                $"Normalized: -{minimum:0.000} ms off every channel " +
                "(minimum delay back to zero)");
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
        Complex[] mono = current
            .First(item => item.Channel == monoChannel).ImpulseResponse;
        Complex[] other = current
            .First(item => item.Channel == otherChannel).ImpulseResponse;
        (double LossDb, double DipDb)? loss = VirtualCrossoverAnalysis.MeasureSumLoss(
            mono,
            new List<Complex[]> { other },
            monoChannel.SampleRate,
            pair.BandLowHz,
            pair.BandHighHz);
        if (loss is not { } measured)
        {
            log.AppendLine(
                $"Junction {monoChannel.Name}/{otherChannel.Name} (mono, fixed): " +
                "no bins in the pair band");
            return;
        }

        log.AppendLine(
            $"Junction {monoChannel.Name}/{otherChannel.Name} " +
            $"(mono, timed by the left side): avg {measured.LossDb:0.00} dB, " +
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
            $"window ±{DiagnosticCorrelationRangeMs:0.###} ms; " +
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
                pair.BandHighHz);
            double upperArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Upper.ImpulseResponse,
                pair.Upper.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz);
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
                DiagnosticCorrelationRangeMs,
                centerLagMs,
                phaseTransform);
        CorrelationDelayCandidate best = result.BestByMagnitude;

        log.AppendLine(
            $"[{tag}] {pair.Lower.Channel.Name}/" +
            $"{pair.Upper.Channel.Name}: " +
            $"fc {result.CenterFrequencyHz:0} Hz, " +
            $"band {result.BandLowHz:0}-{result.BandHighHz:0} Hz, " +
            $"delay to add to {pair.Upper.Channel.Name}: " +
            $"{best.DelayMs:+0.000;-0.000} ms, " +
            $"invert {(best.InvertPolarity ? "yes" : "no")}, " +
            $"r {best.Coefficient:+0.000;-0.000}, " +
            $"confidence {result.Confidence:0.000}");
        log.AppendLine(
            $"  [{tag}] peak {result.PositivePeak.DelayMs:+0.000;-0.000} ms " +
            $"(r {result.PositivePeak.Coefficient:+0.000;-0.000}); " +
            $"trough {result.NegativeTrough.DelayMs:+0.000;-0.000} ms " +
            $"(r {result.NegativeTrough.Coefficient:+0.000;-0.000}, inv)");
    }
}
