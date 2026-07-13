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
    // The fixed span suffices at short-period (mid/high) junctions. At a LOW
    // junction the period is long, and a whitened correlation with too few
    // in-band periods can seed the window a half period off — parking it on a
    // (flip + half-period) impostor whose true, opposite-polarity partner then
    // sits a half period away, beyond this fixed reach. So the effective cap is
    // lifted toward a half period at low junctions (see LowJunctionReachFraction)
    // to admit that partner for AlignmentSelection's invert preference to pick;
    // staying just under a half period keeps the window short of the next
    // SAME-polarity lobe a full period out.
    private const double MaxFineAlignmentRangeMs = 2.5;

    // The fraction of a half period the fine window may reach at a low junction
    // (where a half period exceeds the fixed cap). Just under 1 so the window
    // captures the half-period-away flip partner of an impostor seed without
    // spanning the full-period same-polarity lobe on the far side; the residual
    // ambiguity between those two is resolved by the arrival prior and the
    // AlignmentSelection tie-breaks, exactly as for any other admitted lobe.
    private const double LowJunctionReachFraction = 0.97;

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
    // unless a distinctly better summation exists elsewhere. This flat floor
    // governs sub-lobe moves; per-period hops pay the distance ramp below.
    private const double WideWindowPromotionMarginDb = 0.2;

    // The distance ramp of the promotion margin: how much MORE the wide pick
    // must win per crossover period it moves away from the arrival-anchored
    // result. The envelope is the fundamental observation — on a healthy
    // junction the summation surface is a comb whose lobes differ by mere
    // fractions of a dB to ~1.4 dB (the head_90_grad cabin: a 1.40 dB "gain"
    // 0.92 periods out walked the tweeter pair visibly ahead of its mid while
    // the other side's junction got WORSE), so a hop must be justified by a
    // gain that grows with the hop: one period costs 2 dB, two cost 4 dB. A
    // genuine lobe recovery — the coarse arrival itself off by periods —
    // shows up as a multi-dB gain (a misaligned junction combs across the
    // whole overlap) and still clears the ramp.
    private const double PromotionMarginPerPeriodDb = 2.0;

    // How far (in crossover periods) the promotion may move the pick away from
    // the arrival-anchored fine result. The promotion exists to recover a coarse
    // arrival that landed a lobe or two off at a degenerate junction (a spectral
    // gap between the corners degrades the whitened correlation into near-equal
    // lobes) — real data needs up to ~2 periods of reach for that (the r mid/
    // tweeter cabin junction recovers the user's manual optimum ~1.8 periods off
    // the fine pick). Beyond that the summation surface is a comb of near-equal
    // minima spaced one period apart: which lobe is physically correct is set by
    // the arrival, NOT by the sum (they differ by fractions of a dB). Without a
    // cap the ±3 ms diagnostic window lets a marginally-better comb ALIAS three
    // to four periods away unseat the envelope at a high crossover — the field
    // failure where the tweeter walked ~1.7 ms (~3.9 periods) off its mid for a
    // 0.25 dB "gain". 2.5 periods clears the legitimate ~1.8-period recovery and
    // rejects the ~3.9-period alias. The wide window also scores under a weaker
    // arrival prior, which inflates far aliases; the reach cap bounds that too.
    private const double PromotionReachPeriods = 2.5;

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
            BuildArrivalTimeline(byBand, pairs, log, out HashSet<AlignmentJunction> untrustedSeeds);

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
                timeline, byBand, reprocess, alignment, log,
                untrustedSeedJunctions: untrustedSeeds);
        }
        for (int i = referenceIndex + 1; i < byBand.Count; i++)
        {
            AlignChannelAtJunction(
                byBand[i].Channel, byBand[i - 1].Channel, pairs[i - 1],
                timeline, byBand, reprocess, alignment, log,
                untrustedSeedJunctions: untrustedSeeds);
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
        StringBuilder log,
        out HashSet<AlignmentJunction> untrustedSeedJunctions)
    {
        // Junctions whose coarse seed fell back to the arrival envelope because
        // the PHAT peak was untrusted (a low junction with too few in-band
        // periods): the coarse offset ACROSS such a junction can be a half period
        // off, so aligning its two channels is allowed a half-period window (see
        // LowJunctionReach) to admit the true lobe. Keyed by junction, not channel:
        // the uncertainty is a property of the lower<->upper RELATION, so the wider
        // window fires the same whether the walk reaches the junction from below or
        // above, and never leaks onto a channel's OTHER, phat-trusted junction.
        untrustedSeedJunctions =
            new HashSet<AlignmentJunction>(ReferenceEqualityComparer.Instance);
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
            if (!trustPhat)
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
                $"phat peak {phat.PositivePeak.DelayMs:+0.000;-0.000} ms " +
                $"(r {phat.PositivePeak.Coefficient:+0.000;-0.000}, " +
                $"dom {phat.Confidence:0.000}) -> seed " +
                $"{(trustPhat ? "phat" : "arrival")}");
        }

        return timeline;
    }

    // Fine-aligns one channel against its settled neighbor(s) and writes the
    // result into the alignment map: the stage-2 body shared by the mono walk
    // and the stereo right-side descent. With a SECONDARY settled neighbor
    // (the shared mono subwoofer below a descent channel) the search optimizes
    // BOTH junctions at once: both neighbors join the fixed set, the band
    // spans both junctions, and the window covers both junctions' coarse
    // bases — otherwise the channel buys a perfect upper junction while
    // parking a whole period off its lower one. An external prior (the
    // cross-side Δ-consistent delay) replaces the base as the gentle
    // tie-break when supplied. A physically impossible negative delay is
    // converted into a uniform shift of every OTHER channel in
    // <paramref name="shiftScope"/> (a uniform shift preserves the alignment)
    // — in a stereo run the scope must span BOTH sides, or the shift would
    // silently break the inter-side scene offset.
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
        IReadOnlySet<AlignmentJunction>? untrustedSeedJunctions = null)
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


        // One junction search: candidates of the prior-penalized loss score in
        // a window spanning the coarse base(s) (the PHAT-seeded timeline,
        // arrival envelope where PHAT was untrusted) plus half a period of the
        // slowest involved crossover — wide enough to absorb the coarse error
        // (which grows with the period), narrow enough not to span two
        // same-polarity lobes of one base.
        (IReadOnlyList<AlignmentCandidate> Candidates, double WindowLowMs, double WindowHighMs)
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
            if (secondaryNeighbor != null)
            {
                neighborIrs.Add(current
                    .First(item => item.Channel == secondaryNeighbor).ImpulseResponse);
            }

            // Only where the coarse seed was untrusted (arrival fallback at a
            // low junction) let the cap grow toward a half period so the window
            // can reach a half-period-away flip partner the fixed cap would
            // hide. A trusted seed already sits on the right lobe, and widening
            // there would only invite the impostor the tight window excludes.
            double maxRangeMs = wideSeed
                ? Math.Max(MaxFineAlignmentRangeMs, LowJunctionReachFraction * halfPeriodMs)
                : MaxFineAlignmentRangeMs;
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
                    forcedPolarity: forcedPolarity);
            return (candidates, windowLowMs, windowHighMs);
        }

        {
            (IReadOnlyList<AlignmentCandidate> candidates,
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
                ", candidates " +
                string.Join("; ", candidates.Select(item =>
                    $"{item.DelayMs:0.000} ms" +
                    $"{(item.InvertPolarity ? " inv" : "")} " +
                    $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                    $"dip {item.DipDb:0.0} dB)")));

            AlignmentCandidate chosen = candidates.Count > 0
                ? AlignmentSelection.Select(candidates, anchorMs)
                : new AlignmentCandidate(anchorMs, forcedPolarity ?? false, 0);
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
            (IReadOnlyList<AlignmentCandidate> wide, double wideLow, double wideHigh) =
                SearchJunction(windowOverrideMs: DiagnosticFineRangeMs);
            log.AppendLine(
                $"  [diag] wide {wideLow:0.000}..{wideHigh:0.000} ms: " +
                (wide.Count > 0
                    ? string.Join("; ", wide.Select(item =>
                        $"{item.DelayMs:0.000} ms" +
                        $"{(item.InvertPolarity ? " inv" : "")} " +
                        $"(score {item.ScoreDb:0.00}, avg {item.LossDb:0.00}, " +
                        $"dip {item.DipDb:0.0} dB)"))
                    : "none"));

            // A result pinned to the window edge means the optimum lies beyond
            // the coarse estimate's reach — retry once, widened but still short
            // of a full period so the search cannot land on the next lobe. The
            // edge hit means the base itself is suspect, so the retry relaxes
            // the prior along with the window.
            double retryRangeMs = Math.Min(1.8 * halfPeriodMs, 3.0);
            bool atEdge = chosen.DelayMs <= windowLow + 0.02 ||
                chosen.DelayMs >= windowHigh - 0.02;
            if (sceneLockToleranceMs == null &&
                retryRangeMs > (windowHigh - windowLow) / 2.0 && atEdge)
            {
                (IReadOnlyList<AlignmentCandidate> retried, _, _) =
                    SearchJunction(windowOverrideMs: retryRangeMs);
                if (retried.Count > 0)
                {
                    // Through the same selection rules as the primary pick:
                    // taking retried[0] raw would let the widened window hand
                    // the result to a (flip + half-period) impostor that the
                    // invert margin and the arrival tie-break exist to reject.
                    chosen = AlignmentSelection.Select(retried, anchorMs);
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
            // mere lobe/flip impostor cannot pull the result off the arrival —
            // and with a cross-side prior in the scores, a promotion that walks
            // away from the other side's timing pays for that distance too.
            if (wide.Count > 0 && sceneLockToleranceMs == null)
            {
                AlignmentCandidate wideChosen =
                    AlignmentSelection.Select(wide, anchorMs);
                // Only a lobe's reach from the arrival pick: past that the "better"
                // score is a comb alias the summation cannot distinguish, so the
                // envelope stays authoritative (see PromotionReachPeriods). Inside
                // the reach, the required margin RAMPS with the distance in
                // periods: the arrival is the fundamental observation, and a hop
                // onto another comb lobe must be plainly, not marginally, better.
                double periodMs = 2.0 * halfPeriodMs;
                double promotionReachMs = PromotionReachPeriods * periodMs;
                double promotionStepMs = Math.Abs(wideChosen.DelayMs - chosen.DelayMs);
                double periodsMoved = promotionStepMs / periodMs;
                double requiredMarginDb = Math.Max(
                    WideWindowPromotionMarginDb,
                    PromotionMarginPerPeriodDb * periodsMoved);
                double gainDb = wideChosen.ScoreDb - chosen.ScoreDb;
                if (gainDb > requiredMarginDb && promotionStepMs <= promotionReachMs)
                {
                    log.AppendLine(
                        $"  promoted {wideChosen.DelayMs:0.000} ms" +
                        $"{(wideChosen.InvertPolarity ? " inv" : "")} " +
                        $"over {chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} " +
                        $"(gain {gainDb:0.00} dB, needed {requiredMarginDb:0.00} dB " +
                        $"at {periodsMoved:0.0} periods)");
                    chosen = wideChosen;
                }
                else if (gainDb > WideWindowPromotionMarginDb &&
                    promotionStepMs > promotionReachMs)
                {
                    log.AppendLine(
                        $"  promotion declined: {wideChosen.DelayMs:0.000} ms is " +
                        $"{promotionStepMs:0.000} ms ({periodsMoved:0.0} " +
                        $"periods) from the arrival pick {chosen.DelayMs:0.000} ms — " +
                        "a comb alias beyond the envelope's reach.");
                }
                else if (gainDb > WideWindowPromotionMarginDb)
                {
                    log.AppendLine(
                        $"  promotion declined: {wideChosen.DelayMs:0.000} ms" +
                        $"{(wideChosen.InvertPolarity ? " inv" : "")} gains only " +
                        $"{gainDb:0.00} dB over {chosen.DelayMs:0.000} ms — " +
                        $"a {periodsMoved:0.0}-period hop off the arrival needs " +
                        $"{requiredMarginDb:0.00} dB.");
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
        TimeAlignmentAnalysisResult leftBridge =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                settled.First(item => item.Channel == plan.BridgeLeft).ImpulseResponse,
                plan.BridgeLeft.SampleRate,
                plan.BridgeBandLowHz,
                plan.BridgeBandHighHz);
        TimeAlignmentAnalysisResult rightBridge =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                settled.First(item => item.Channel == plan.BridgeRight).ImpulseResponse,
                plan.BridgeRight.SampleRate,
                plan.BridgeBandLowHz,
                plan.BridgeBandHighHz);

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

        double leftArrival = leftBridge.FirstArrivalDelayMilliseconds;
        double rightArrival = rightBridge.FirstArrivalDelayMilliseconds;
        double bridgeDelay = leftArrival - rightArrival - plan.SceneOffsetMs;
        log.AppendLine(
            $"Bridge {plan.BridgeLeft.Name} -> {plan.BridgeRight.Name}: " +
            $"band {plan.BridgeBandLowHz:0}-{plan.BridgeBandHighHz:0} Hz, " +
            $"arrivals L {leftArrival:0.000} / R {rightArrival:0.000} ms " +
            $"(SNR {leftBridge.SignalToNoiseDecibels:0.0} / " +
            $"{rightBridge.SignalToNoiseDecibels:0.0} dB), " +
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
        alignment[plan.BridgeRight] = new AlignmentOverride(
            Math.Clamp(Math.Round(bridgeDelay, 2), 0, MaxDelayMs), false);

        // Polarity is a property of the DRIVER, not the side, and automatic delay
        // never inverts one side of a pair alone: the right top INHERITS the left
        // top's sign (which the left walk may have flipped for its own cascade),
        // just as every lower right driver inherits its left counterpart's. Set it
        // before the right walk so the right lowers align against a correctly-signed
        // top. A genuinely reverse-wired driver is left for a MANUAL flip in the UI,
        // not an asymmetric automatic one.
        InheritBridgePolarity(plan, alignment, log);

        // Stage R: descent from the bridged top toward the low end (and up
        // from it, if the caller had to bridge a non-top pair) — the same walk
        // as stage 2, referenced to the bridge. Mono channels are final from
        // the left pass: never searched again, their right-side junction is
        // only measured.
        Dictionary<IAlignmentChannel, double> rightTimeline =
            BuildArrivalTimeline(
                rightByBand, plan.RightPairs, log,
                out HashSet<AlignmentJunction> rightUntrustedSeeds);

        // The delay that would land this right channel's arrival exactly the
        // scene offset ahead of its settled left counterpart's, measured by
        // envelope arrivals in the given band. Used as the search prior: a
        // gentle, polarity-blind pull toward the other side's timing that
        // breaks near-ties between lobes the junction sum cannot distinguish
        // — and as the pin of a scene lock, which measures the LOCALIZATION
        // sub-band of the pair's shared band (the part the scene actually
        // follows) rather than the full intersection. Unmeasurable (silent
        // band, low SNR) falls back to null and the search keeps its own-side
        // anchor.
        double? CrossSideTargetMs(
            IAlignmentChannel rightChannel,
            StereoPairLink link,
            double bandLowHz,
            double bandHighHz)
        {
            var searchAlignment =
                new Dictionary<IAlignmentChannel, AlignmentOverride>(alignment);
            searchAlignment.Remove(rightChannel);
            IReadOnlyList<AlignmentSnapshot> current = reprocess(searchAlignment);
            Complex[] leftIr =
                current.First(item => item.Channel == link.Left).ImpulseResponse;
            Complex[] rightIr =
                current.First(item => item.Channel == rightChannel).ImpulseResponse;
            TimeAlignmentAnalysisResult leftArrival =
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    leftIr, link.Left.SampleRate, bandLowHz, bandHighHz);
            TimeAlignmentAnalysisResult rightRaw =
                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                    rightIr, rightChannel.SampleRate, bandLowHz, bandHighHz);
            if (!leftArrival.IsValid || !rightRaw.IsValid ||
                leftArrival.SignalToNoiseDecibels < MinimumArrivalSnrDb ||
                rightRaw.SignalToNoiseDecibels < MinimumArrivalSnrDb)
            {
                return null;
            }

            // Modal-latch guard: the SAME driver measured in the band's upper
            // half must agree with the full-band read to within the dispersion
            // one direct wave packet can show (half a period at the probe's
            // low edge). A full-band read landing far BEHIND its own upper-half
            // read means the detector latched that side onto the in-room modal
            // build-up instead of the direct rise (the under-seat midbass case:
            // 21.2 ms in 80-200 Hz vs 15.2 ms one band up) — the two sides are
            // then timing DIFFERENT features and their difference is garbage.
            // The honest response is to withdraw the prior (the search keeps
            // its own-side junction anchor), NOT to re-measure in the narrow
            // upper half: at a low link band that probe is an octave of mush
            // (~1/BW blur of many ms) that once dragged a woofer 6 ms off.
            double probeLowHz = Math.Sqrt(bandLowHz * bandHighHz);
            if (bandHighHz >=
                probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
            {
                TimeAlignmentAnalysisResult leftProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        leftIr, link.Left.SampleRate, probeLowHz, bandHighHz);
                TimeAlignmentAnalysisResult rightProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        rightIr, rightChannel.SampleRate, probeLowHz, bandHighHz);
                double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
                if (leftProbe.IsValid && rightProbe.IsValid &&
                    leftProbe.SignalToNoiseDecibels >= MinimumArrivalSnrDb &&
                    rightProbe.SignalToNoiseDecibels >= MinimumArrivalSnrDb)
                {
                    bool leftLatched =
                        leftArrival.FirstArrivalDelayMilliseconds
                        - leftProbe.FirstArrivalDelayMilliseconds > toleranceMs;
                    bool rightLatched =
                        rightRaw.FirstArrivalDelayMilliseconds
                        - rightProbe.FirstArrivalDelayMilliseconds > toleranceMs;
                    if (leftLatched || rightLatched)
                    {
                        log.AppendLine(
                            $"  cross-side prior {rightChannel.Name}: withdrawn — " +
                            $"{(leftLatched ? link.Left.Name : rightChannel.Name)}" +
                            $" reads {(leftLatched ? leftArrival : rightRaw).FirstArrivalDelayMilliseconds:0.000} ms" +
                            $" in {bandLowHz:0}-{bandHighHz:0} Hz but " +
                            $"{(leftLatched ? leftProbe : rightProbe).FirstArrivalDelayMilliseconds:0.000} ms" +
                            $" in its {probeLowHz:0}-{bandHighHz:0} Hz half " +
                            "(modal latch: the sides time different features)");
                        return null;
                    }
                }
            }

            double target = leftArrival.FirstArrivalDelayMilliseconds
                - plan.SceneOffsetMs
                - rightRaw.FirstArrivalDelayMilliseconds;
            log.AppendLine(
                $"  cross-side prior {rightChannel.Name}: target {target:0.000} ms " +
                $"(L arrival {leftArrival.FirstArrivalDelayMilliseconds:0.000}, " +
                $"raw R {rightRaw.FirstArrivalDelayMilliseconds:0.000} ms " +
                $"in {bandLowHz:0}-{bandHighHz:0} Hz)");
            return target;
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
            // pinned to the cross-side target, which is then measured in the
            // localization sub-band alone — the low end of a wide shared band
            // (soft envelopes, no localization) must not smear the pin. Pure
            // low-frequency pairs keep the free joint-junction search with
            // the full-band target as a gentle prior only.
            StereoPairLink? channelLink = plan.PairLinks?.FirstOrDefault(
                item => item.Right == channel);
            bool lockable = channelLink != null && IsSceneLockable(channelLink);
            double? crossTarget = channelLink == null
                ? null
                : CrossSideTargetMs(
                    channel,
                    channelLink,
                    lockable
                        ? Math.Max(channelLink.BandLowHz, SceneLockLocalizationLowHz)
                        : channelLink.BandLowHz,
                    channelLink.BandHighHz);
            double? sceneLock = lockable && crossTarget != null
                ? SceneLockToleranceMs
                : null;

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
                rightUntrustedSeeds);
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
        RebalancePairsKeepingScene(plan, reprocess, alignment, log);

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

        // The invariant the user requires of automatic delay: no driver is ever
        // inverted on one side of a pair alone.
        EnforcePolaritySymmetry(plan, alignment, log);
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

    // The scene mandate for the right descent: a channel whose pair band
    // reaches into the localization region is PINNED to the cross-side target
    // (the left counterpart's settled arrival minus the scene offset) and may
    // fine-tune its junction sum only within this tolerance — the stereo
    // image outranks the junction handover. Pairs living entirely below the
    // localization region (the woofers) keep the free joint-junction search:
    // the ear does not localize there, low-band envelope arrivals are soft,
    // and the summation is what remains audible.
    private const double SceneLockToleranceMs = 0.05;

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

    // The right bridge top inherits the left top's sign (set before the right walk,
    // so the right lowers align against a correctly-signed top). Automatic delay
    // never inverts one side of a pair alone — a driver's polarity is a property of
    // the driver, decided once on the left and mirrored to the right; the sum-loss /
    // first-lobe "which polarity fits better" guess is gone, because at high
    // frequencies two spatially-separated tops comb-filter and the guess is
    // noise-driven (it used to invert an identical off-axis right tweeter alone).
    private static void InheritBridgePolarity(
        StereoAlignmentPlan plan,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
    {
        bool leftInvert = alignment.GetValueOrDefault(plan.BridgeLeft).InvertPolarity;
        AlignmentOverride top = alignment.GetValueOrDefault(plan.BridgeRight);
        alignment[plan.BridgeRight] = top with { InvertPolarity = leftInvert };
        log.AppendLine(
            $"  bridge polarity: {(leftInvert ? "inverted" : "normal")} " +
            $"(inherited from {plan.BridgeLeft.Name}; auto delay keeps L/R polarity symmetric)");
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
        StringBuilder log)
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
    // delta that minimizes the MEAN loss of every junction adjacent to the
    // pair on either side. The left side may get slightly worse: by the scene
    // mandate the pinned right channel could not chase its own junction
    // optimum, and this is the only lever that can recover junction quality
    // without touching the image. Top pair first, so lower pairs re-balance
    // against the already-settled uppers. The scan is analytic: ONE reprocess
    // fixes the pair's current responses, each junction gets its gated
    // spectra built once, and every probed delta is an e^{-jωΔ} rotation of
    // the moving channel — the probe loop runs no DSP chains at all.
    private static void RebalancePairsKeepingScene(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
    {
        if (plan.PairLinks == null)
        {
            return;
        }

        foreach (StereoPairLink link in plan.PairLinks
            .Where(IsSceneLockable)
            .OrderByDescending(item => item.BandHighHz))
        {
            AlignmentOverride leftOverride = alignment.GetValueOrDefault(link.Left);
            AlignmentOverride rightOverride = alignment.GetValueOrDefault(link.Right);

            // Every junction the pair's channels take part in, on either side.
            List<AlignmentJunction> adjacent = plan.LeftPairs
                .Where(pair => pair.Lower.Channel == link.Left ||
                    pair.Upper.Channel == link.Left)
                .Concat(plan.RightPairs.Where(pair =>
                    pair.Lower.Channel == link.Right ||
                    pair.Upper.Channel == link.Right))
                .ToList();
            if (adjacent.Count == 0)
            {
                continue;
            }

            IReadOnlyList<AlignmentSnapshot> current = reprocess(alignment);
            Complex[] IrOf(IAlignmentChannel channel) =>
                current.First(item => item.Channel == channel).ImpulseResponse;

            // One evaluator per junction: the pair member is the rotated
            // (moving) channel, its junction neighbor stays fixed. A junction
            // between the pair and its neighbor never has both ends moving —
            // the pair's two channels sit on different sides.
            var evaluators = new List<VirtualCrossoverAnalysis.SumLossEvaluator>();
            foreach (AlignmentJunction junction in adjacent)
            {
                bool lowerMoves = junction.Lower.Channel == link.Left ||
                    junction.Lower.Channel == link.Right;
                IAlignmentChannel mover = lowerMoves
                    ? junction.Lower.Channel
                    : junction.Upper.Channel;
                IAlignmentChannel neighbor = lowerMoves
                    ? junction.Upper.Channel
                    : junction.Lower.Channel;
                VirtualCrossoverAnalysis.SumLossEvaluator? evaluator =
                    VirtualCrossoverAnalysis.SumLossEvaluator.Create(
                        IrOf(mover),
                        new List<Complex[]> { IrOf(neighbor) },
                        mover.SampleRate,
                        junction.BandLowHz,
                        junction.BandHighHz);
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

            // The tightest (highest-frequency) adjacent junction bounds the
            // pair's reach: within half its period the junction sums are
            // single-lobed, so the search can only polish the alignment the
            // arrival-anchored walk chose. Past that lies the next comb lobe —
            // and fractions of a dB of mean junction loss cannot choose a lobe
            // (the same physics as the wide-window promotion reach cap). The
            // flat window alone let a 0.1-0.2 dB "gain" walk the tweeter pair
            // a whole period off its mid at a 2.3 kHz junction.
            double tightestPeriodMs =
                1_000.0 / adjacent.Max(junction => junction.CrossoverHz);
            double reachMs = Math.Min(
                PairComoveSearchRangeMs, 0.5 * tightestPeriodMs);

            // Both bounds are fixed BEFORE the search so the winning delta
            // applies verbatim to both sides: negative deltas may not push
            // either channel below zero, positive ones may not push either
            // past the delay ceiling. Clamping after the fact would move the
            // two sides unequally and silently bend the very scene this pass
            // exists to preserve.
            double minDelta = -Math.Min(
                Math.Min(leftOverride.DelayMs, rightOverride.DelayMs),
                reachMs);
            double maxDelta = Math.Min(
                reachMs,
                MaxDelayMs - Math.Max(leftOverride.DelayMs, rightOverride.DelayMs));
            double baseline = Score(0);
            double bestDelta = 0;
            double bestScore = baseline;
            // The coarse step scales down with the window so a tightly-capped
            // high-junction pair still gets a real grid before refinement.
            double coarseStep = Math.Min(0.1, Math.Max(0.02, reachMs / 4.0));
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
                log.AppendLine(
                    $"Co-move {link.Left.Name}+{link.Right.Name}: " +
                    $"{bestDelta:+0.00;-0.00} ms to both sides " +
                    $"(mean dip-penalized junction loss {baseline:0.00} -> " +
                    $"{bestScore:0.00} dB; scene untouched)");
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
