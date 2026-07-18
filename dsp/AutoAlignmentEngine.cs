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
/// <see cref="ValidRange"/> is where the MEASURED content sits inside the
/// (delay-shifted, FFT-length-padded) record — the range the
/// <see cref="VirtualCrossoverAnalysis.ApplyChain(System.Numerics.Complex[], DspChannelChain, int, out ValidSampleRange)"/>
/// overload reports — so envelope/SNR analyses skip both the delay prefix
/// and the manufactured tail. The default (empty) range means unknown: the
/// analyses then fall back to the padding-signature heuristic.
/// </summary>
public sealed record AlignmentSnapshot(
    IAlignmentChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex,
    ValidSampleRange ValidRange = default);

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

    // A deliberately wide fine-search window (many periods at a high crossover,
    // ~one at a low one). Its candidates are always logged, surfacing summation
    // optima several lobes outside the working window; at a junction the onset
    // lock does not govern, the promotion path below may also CHOOSE from them.
    private const double DiagnosticFineRangeMs = 3.0;
    private const double DiagnosticCorrelationRangeMs = 3.0;

    // The wide diagnostic sweep must reach past the flip partner half a period
    // out even at a LOW junction, where the fixed millisecond span above is
    // sub-period — otherwise the [diag] line (and the promotion pool at
    // un-locked junctions) simply cannot contain the true optimum it exists to
    // surface. 1.25 half periods clears that partner with margin; mid/high
    // junctions keep the fixed span, already many periods there.
    private const double DiagnosticFineReachHalfPeriods = 1.25;

    // The stage-1 correlation window in periods of the pair crossover. The
    // peak-vs-trough dominance gate below is only meaningful when BOTH
    // polarity partners are complete lobes inside the window, and the arrival
    // estimate the window centers on can itself sit up to a half period off at
    // a low junction — so the window must hold at least a full period to each
    // side. Under the fixed ±3 ms window a field 85 Hz junction had its
    // non-inverted rival lobe cut by the window edge: the truncated value
    // understated the rival, the dominance gate passed on the corrupted
    // number, and the timeline was seeded from the cut — a 3.6 ms miss the
    // fine search could no longer reach. Mid/high junctions stay on the fixed
    // floor (±3 ms already spans several periods there).
    private const double SeedCorrelationWindowPeriods = 1.25;

    // The stage-1 / diagnostic correlation half-window for a junction: the
    // fixed floor, grown with the crossover period at low junctions so both
    // polarity partners fit as whole lobes.
    private static double SeedCorrelationRangeMs(double crossoverHz) =>
        Math.Max(
            DiagnosticCorrelationRangeMs,
            SeedCorrelationWindowPeriods * 1000.0 / crossoverHz);

    // How far from the arrival estimate a trusted seed extremum may sit: half
    // a period — the next same-polarity lobe is a full period out, so half a
    // period is the no-cycle-skip bound — floored at the FIXED window span.
    // The floor is deliberately the fixed ±3 ms, not the grown window: at a
    // mid/high junction it keeps exactly the reach the fixed window used to
    // enforce by construction, and at a low junction the grown window may SEE
    // farther lobes but must never hand one to the timeline.
    private static double SeedReachMs(double crossoverHz) =>
        Math.Max(DiagnosticCorrelationRangeMs, 500.0 / crossoverHz);

    // The minimum |r| of the dominant PHAT extremum (peak or trough — the seed
    // uses only its POSITION, polarity stays with the loss search) for it to
    // seed the stage-2 window instead of the arrival envelope. Below it the
    // extremum is noise (a low-frequency junction with too few in-band
    // periods), and the arrival estimate stands. Deliberately low: even a
    // modest genuine extremum beats the arrival envelope, and a seed that
    // still lands a little off is recovered downstream — by the onset lock at
    // sharp junctions, by the loss search and the wide-window promotion below
    // it.
    private const double PhatSeedMinCoefficient = 0.15;

    // The minimum dominance (Confidence: |best extremum| minus |its rival|) the
    // PHAT correlation must show before its extremum position is trusted as the
    // seed.
    // A junction whose corners leave a spectral gap (e.g. LP 1300 / HP 1800)
    // narrows the effective overlap, and the whitened correlation degenerates
    // into a comb of near-equal lobes: the peak coefficient still looks healthy,
    // but which lobe it sits on is decided by noise — trusting it can move the
    // seed whole periods off the arrival estimate (a cycle skip the prior then
    // cements). Peak-vs-trough closeness is exactly that lobe ambiguity, so a
    // near-tie sends the seed back to the polarity-blind arrival envelope.
    private const double PhatSeedMinDominance = 0.1;

    // How much better (in score dB) a wide-window optimum must be before it
    // unseats the arrival-anchored fine pick, at a junction the onset lock
    // does not govern (below its frequency gate, or a smeared front): there
    // the window is still centered on the coarse arrival, which can sit a
    // whole lobe off, and the promotion recovers that lobe while the margin
    // keeps the physically-minimal arrival pick unless a distinctly better
    // summation exists elsewhere. Calibrated on two pre-lock field runs of
    // the same cabin (2.3 kHz mid/tweeter junctions, since taken over by the
    // lock — the physics transfers): a FALSE hop offered 1.40 dB, a GENUINE
    // lobe recovery offered 1.91 dB — comb noise between real lobes runs up
    // to ~1.4 dB, a real envelope error shows as ~2 dB across the whole
    // basin. A distance-scaled ramp cannot separate those two points at any
    // slope; this flat threshold does.
    private const double WideWindowPromotionMarginDb = 1.6;

    // The gain above which a declined promotion is worth a log line: below it
    // the wide window merely confirmed the arrival pick.
    private const double PromotionNoteworthyGainDb = 0.2;

    // How far (in crossover periods) the promotion may move the pick away from
    // the arrival-anchored fine result. The promotion exists to recover a coarse
    // arrival that landed a lobe or two off at a degenerate junction (a spectral
    // gap between the corners degrades the whitened correlation into near-equal
    // lobes) — real data needs up to ~2 periods of reach for that. Beyond it
    // the summation surface is a comb of near-equal minima spaced one period
    // apart: which lobe is physically correct is set by the arrival, NOT by the
    // sum (they differ by fractions of a dB). Without a cap the ±3 ms
    // diagnostic window let a marginally-better comb ALIAS unseat the envelope
    // — the pre-lock field failure where the tweeter walked ~1.7 ms
    // (~3.9 periods) off its mid for a 0.25 dB "gain"; that junction class is
    // now onset-locked, and this cap guards the remaining, un-locked domain.
    // 2.5 periods clears the legitimate ~1.8-period recovery and rejects the
    // ~3.9-period alias. The wide window also scores under a weaker arrival
    // prior, which inflates far aliases; the reach cap bounds that too.
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
        StringBuilder log) =>
        Compute(channelsByBand, pairs, reprocess, alignment, log, onsetLocks: null);

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
        Dictionary<AlignmentJunction, OnsetLockState>? onsetLocks)
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
                untrustedSeedJunctions: untrustedSeeds,
                onsetLocks: onsetLocks);
        }
        for (int i = referenceIndex + 1; i < byBand.Count; i++)
        {
            AlignChannelAtJunction(
                byBand[i].Channel, byBand[i - 1].Channel, pairs[i - 1],
                timeline, byBand, reprocess, alignment, log,
                untrustedSeedJunctions: untrustedSeeds,
                onsetLocks: onsetLocks);
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
                pair.BandHighHz,
                pair.Lower.ValidRange);
            double upperArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                pair.Upper.ImpulseResponse,
                pair.Upper.Channel.SampleRate,
                pair.BandLowHz,
                pair.BandHighHz,
                pair.Upper.ValidRange);

            // Refine the coarse offset with the DOMINANT GCC-PHAT extremum of
            // either sign: at a mid/high junction it lands the stage-2 window
            // on the correct lobe directly, sparing the wide-window recovery.
            // Only the extremum's POSITION is used (polarity and the final
            // lobe stay with the loss search), and only when it is the honest
            // winner of its window — otherwise the arrival envelope stands.
            // Seeding from a dominant TROUGH matters as much as from a peak:
            // a junction whose true relation is inverted (the cabin sub/woofer
            // junction, whitened trough r −0.97) used to be sent to the
            // arrival fallback plus a period-wide window, where the true lobe
            // and a non-inverted lobe a third of a period out competed within
            // fractions of a dB — a coin flip that parked the sub 3.5-5 ms off
            // the woofer's attack in two of three field crossover configs. The
            // trough position pins that window to the measured physics
            // instead. Each distrust rule guards a distinct failure, checked
            // in the order the earlier ones corrupt the later ones' inputs:
            //  - an EDGE-PINNED extremum is a lobe cut by the window boundary,
            //    so its position and magnitude (and hence every comparison
            //    below) are artifacts of where the window ended;
            //  - a WEAK or BARELY-DOMINANT extremum is lobe ambiguity, decided
            //    by noise (see the two constants);
            //  - a near-tie against the SAME-SIGN rival one period over (a
            //    lobe Confidence, peak-vs-trough, cannot see) means the choice
            //    of lobe — and with it a whole-period cycle skip — would be
            //    decided by which reflection ran slightly hotter;
            //  - an extremum FARTHER FROM THE ARRIVAL than half a period (or
            //    the fixed window floor, whichever is larger — the reach the
            //    fixed window used to enforce by construction) is a cycle-skip
            //    candidate the now period-wide window must not hand to the
            //    timeline.
            // The timeline stores arrivals as (upper - lower); the extremum is
            // the delay to add to the upper channel, i.e. the same quantity
            // negated.
            double passOctaves = Math.Log2(pair.BandHighHz / pair.BandLowHz);
            double centerLagMs = lowerArrival - upperArrival;
            CorrelationAlignmentResult phat =
                VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                    pair.Lower.ImpulseResponse,
                    pair.Upper.ImpulseResponse,
                    pair.Lower.Channel.SampleRate,
                    pair.CrossoverHz,
                    passOctaves,
                    SeedCorrelationRangeMs(pair.CrossoverHz),
                    centerLagMs,
                    phaseTransform: true);
            CorrelationDelayCandidate seed = phat.BestByMagnitude;
            CorrelationDelayCandidate? sameSignRival =
                seed.InvertPolarity ? phat.NegativeRival : phat.PositiveRival;
            string seedLabel = seed.InvertPolarity ? "trough" : "peak";
            double seedOffsetMs = seed.DelayMs - centerLagMs;
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
                if (phat.Confidence < PhatSeedMinDominance)
                {
                    return "peak-trough near-tie";
                }
                if (sameSignRival is { } rival &&
                    Math.Abs(seed.Coefficient) - Math.Abs(rival.Coefficient) <
                        PhatSeedMinDominance)
                {
                    return "same-polarity rival near-tie";
                }
                if (Math.Abs(seedOffsetMs) > SeedReachMs(pair.CrossoverHz))
                {
                    return $"{seedLabel} beyond the arrival's reach";
                }
                return null;
            }
            string? distrust = Distrust();
            bool trustPhat = distrust == null;
            double increment =
                trustPhat ? -seed.DelayMs : upperArrival - lowerArrival;
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
                $"phat {seedLabel} {seed.DelayMs:+0.000;-0.000} ms " +
                $"(r {seed.Coefficient:+0.000;-0.000}, " +
                $"dom {phat.Confidence:0.000}) -> seed " +
                $"{(trustPhat ? "phat" : $"arrival ({distrust})")}");
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
        Dictionary<AlignmentJunction, OnsetLockState>? onsetLocks = null)
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

        // Reprocess so the settled neighbors participate with their new
        // delays and polarities. The searched channel is dropped from the
        // override map so its response is the raw, undelayed IR — the search
        // provides the delay, and chosen.DelayMs is then the absolute delay
        // to assign. Without this reset, a uniform shift applied earlier to
        // a not-yet-searched channel (the negative-delay branch below) would
        // bake a stray offset into variableIr that the reported delay does
        // not account for, mis-aligning that channel by the shift. Hoisted out
        // of SearchJunction: the fine, wide and retry searches all run on the
        // same settled state, and the onset lock reads the same IRs.
        var searchAlignment =
            new Dictionary<IAlignmentChannel, AlignmentOverride>(alignment);
        searchAlignment.Remove(channel);
        IReadOnlyList<AlignmentSnapshot> current = reprocess(searchAlignment);
        AlignmentSnapshot variableSnapshot =
            current.First(item => item.Channel == channel);
        AlignmentSnapshot primaryNeighborSnapshot =
            current.First(item => item.Channel == neighborChannel);
        Complex[] variableIr = variableSnapshot.ImpulseResponse;
        var neighborIrs = new List<Complex[]>
        {
            primaryNeighborSnapshot.ImpulseResponse
        };
        if (secondaryNeighbor != null)
        {
            neighborIrs.Add(current
                .First(item => item.Channel == secondaryNeighbor).ImpulseResponse);
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
        if (secondaryNeighbor == null &&
            sceneLockToleranceMs == null &&
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
        (IReadOnlyList<AlignmentCandidate> Candidates, double WindowLowMs, double WindowHighMs)
            SearchJunction(double? windowOverrideMs = null)
        {
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
                (onsetAnchorMs is { } onsetForLog
                    ? $", ONSET-LOCKED {onsetForLog:0.000} \u00b1{onsetCapMs:0.000} ms"
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
            else if (candidates.Count > 0 && chosen.InvertPolarity &&
                AlignmentSelection.DeclinedInvertRescue(candidates, anchorMs)
                    is { } rescue)
            {
                log.AppendLine(
                    $"  kept {chosen.DelayMs:0.000} ms inv: rescue " +
                    $"{rescue.DelayMs:0.000} ms " +
                    $"(margin {chosen.ScoreDb - rescue.ScoreDb:0.00} dB) is " +
                    $"{Math.Abs(rescue.DelayMs - anchorMs) - Math.Abs(chosen.DelayMs - anchorMs):0.000} ms " +
                    "farther from the arrival (reach " +
                    $"{AlignmentSelection.DefaultInvertPreferenceReachMs:0.00} ms)");
            }

            // Wide sweep: the same junction searched across a much wider
            // window so lobes beyond the working range appear in the log.
            // At an un-locked junction the promotion below may adopt its
            // winner; under a scene or onset lock it is log-only. At a low
            // junction the fixed span is sub-period, so it grows to reach the
            // flip partner half a period out (see DiagnosticFineReachHalfPeriods).
            (IReadOnlyList<AlignmentCandidate> wide, double wideLow, double wideHigh) =
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
            if (sceneLockToleranceMs == null && onsetAnchorMs == null &&
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
                    trustedReachMs, WideWindowPromotionMarginDb);
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
            if (wide.Count > 0 && sceneLockToleranceMs == null &&
                onsetAnchorMs == null)
            {
                AlignmentCandidate wideChosen =
                    AlignmentSelection.Select(wide, anchorMs);
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
                    // The pre-lock field failure that pinned this: at a 1500 Hz
                    // mid/tweeter split TWO adjacent same-polarity lobes both
                    // cleared the gate and the 0.14 dB-deeper one sat a full
                    // period past the user's correct alignment. That junction
                    // class is onset-locked now, but the same comb physics
                    // holds wherever the promotion still runs. Snap to the
                    // arrival-nearest lobe that still clears the gate;
                    // wideChosen itself qualifies, so this only ever pulls the
                    // pick closer to the arrival, never onto a declined junction.
                    AlignmentCandidate promoted = AlignmentSelection.SelectPromotionLobe(
                        wide,
                        wideChosen,
                        AcousticScore,
                        fineScore,
                        WideWindowPromotionMarginDb,
                        arrivalPick.DelayMs,
                        anchorMs,
                        promotionReachMs);
                    promotionStepMs = Math.Abs(promoted.DelayMs - arrivalPick.DelayMs);
                    periodsMoved = promotionStepMs / periodMs;
                    gainDb = AcousticScore(promoted) - fineScore;
                    log.AppendLine(
                        $"  promoted {promoted.DelayMs:0.000} ms" +
                        $"{(promoted.InvertPolarity ? " inv" : "")} " +
                        $"over {chosen.DelayMs:0.000} ms" +
                        $"{(chosen.InvertPolarity ? " inv" : "")} " +
                        $"(gain {gainDb:0.00} dB at {periodsMoved:0.0} periods)");
                    chosen = promoted;
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

        // Onset-locked junctions accumulated across both sides: the co-move
        // must respect the front pins the fine searches honored.
        var onsetLocks = new Dictionary<AlignmentJunction, OnsetLockState>(
            ReferenceEqualityComparer.Instance);

        // Stage L: the left side, exactly like a mono run.
        Compute(
            plan.LeftChannelsByBand, plan.LeftPairs, reprocess, alignment, log,
            onsetLocks);

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
        // Returns the delay that Δ-aligns the right channel to its left
        // counterpart, and whether the target is COARSE (the energy-peak rung
        // of the ladder, good to a fraction of a millisecond) — a coarse
        // target may pin a lobe but never the tight scene tolerance.
        (double TargetMs, bool Coarse)? CrossSideTargetMs(
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

            // Both sides measured in one band, with a modal-latch guard: the
            // Latched flag separates "one side timed the wrong feature" (worth
            // retrying one band up) from "the band cannot be measured at all"
            // (silence or a too-narrow intersection — the link stays without a
            // target, exactly as before the ladder existed).
            // SAME driver measured in the band's upper half must agree with
            // the full-band read to within the dispersion one direct wave
            // packet can show (half a period at the probe's low edge). A
            // full-band read landing far BEHIND its own upper-half read means
            // the detector latched that side onto the in-room modal build-up
            // instead of the direct rise (the under-seat midbass case:
            // 21.2 ms in 80-200 Hz vs 13.9 ms one band up) — the two sides
            // are then timing DIFFERENT features and their difference is
            // garbage. The narrow upper half itself is NOT a substitute (at a
            // low band it is an octave of mush that once dragged a woofer
            // 6 ms off) — it only votes on the full band's honesty.
            ((TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                Reads, bool Latched) MeasureConsistent(double lowHz, double highHz)
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
                    return (null, false);
                }

                double probeLowHz = Math.Sqrt(lowHz * highHz);
                if (highHz <
                    probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
                {
                    return ((left, right), false);
                }

                TimeAlignmentAnalysisResult leftProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        leftIr, link.Left.SampleRate, probeLowHz, highHz,
                        leftSnapshot.ValidRange);
                TimeAlignmentAnalysisResult rightProbe =
                    VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                        rightIr, rightChannel.SampleRate, probeLowHz, highHz,
                        rightSnapshot.ValidRange);
                if (!leftProbe.IsValid || !rightProbe.IsValid ||
                    leftProbe.SignalToNoiseDecibels < MinimumArrivalSnrDb ||
                    rightProbe.SignalToNoiseDecibels < MinimumArrivalSnrDb)
                {
                    return ((left, right), false);
                }

                double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
                bool leftLatched = left.FirstArrivalDelayMilliseconds
                    - leftProbe.FirstArrivalDelayMilliseconds > toleranceMs;
                bool rightLatched = right.FirstArrivalDelayMilliseconds
                    - rightProbe.FirstArrivalDelayMilliseconds > toleranceMs;
                if (leftLatched || rightLatched)
                {
                    log.AppendLine(
                        $"  cross-side link {rightChannel.Name}: " +
                        $"{(leftLatched ? link.Left.Name : rightChannel.Name)}" +
                        $" reads {(leftLatched ? left : right).FirstArrivalDelayMilliseconds:0.000} ms" +
                        $" in {lowHz:0}-{highHz:0} Hz but " +
                        $"{(leftLatched ? leftProbe : rightProbe).FirstArrivalDelayMilliseconds:0.000} ms" +
                        $" in its {probeLowHz:0}-{highHz:0} Hz half " +
                        "(modal latch: the sides time different features)");
                    return (null, true);
                }

                return ((left, right), false);
            }

            // The consistency ladder: the pair's own shared band first; when a
            // side LATCHED there, the channel's junction band — the engine
            // already trusts it for junction work, and the direct rise that
            // hid under a mode in the low link band is usually plain one
            // octave up. An unmeasurable link band (silence, too narrow) does
            // NOT ladder: the link was inadmissible, not mis-read. Only when
            // both bands are poisoned is the prior withdrawn and the search
            // keeps its own-side junction anchor.
            double usedLowHz = bandLowHz;
            double usedHighHz = bandHighHz;
            bool anyLatch;
            ((TimeAlignmentAnalysisResult Left, TimeAlignmentAnalysisResult Right)?
                Reads, bool Latched) measured =
                MeasureConsistent(bandLowHz, bandHighHz);
            anyLatch = measured.Latched;
            if (measured.Reads == null && measured.Latched &&
                (fallbackLowHz != bandLowHz || fallbackHighHz != bandHighHz))
            {
                measured = MeasureConsistent(fallbackLowHz, fallbackHighHz);
                anyLatch |= measured.Latched;
                if (measured.Reads != null)
                {
                    usedLowHz = fallbackLowHz;
                    usedHighHz = fallbackHighHz;
                }
            }
            if (measured.Reads is not { } arrivals)
            {
                if (!anyLatch)
                {
                    // The link band itself was inadmissible (silent or too
                    // narrow): no target, exactly as before the ladder.
                    return null;
                }

                // The ladder's last rung: when no band reads both DIRECT rises
                // consistently, the pair is timed by its processed IRs' energy
                // peaks. For a band-passed channel that peak IS its in-band
                // dominant packet (typically the modal build-up) — the same
                // physical feature on both sides of one cabin, defined without
                // any detector threshold. Its L/R difference tracks the true
                // path split to a fraction of a millisecond — coarse against
                // the tight scene pin, but the lobe lock only needs the target
                // within half a junction period.
                double leftPeakMs = leftSnapshot.PeakIndex
                    * 1_000.0 / link.Left.SampleRate;
                double rightPeakMs = rightSnapshot.PeakIndex
                    * 1_000.0 / rightChannel.SampleRate;
                // Guard the asymmetric-cabin case: if the two peaks sit farther
                // apart than any real inter-side path, they are different room
                // modes, not one shared feature, so their difference is not a
                // usable L/R split. Withdraw the prior (free own-side search)
                // rather than pin the pair to a fabricated target.
                if (Math.Abs(leftPeakMs - rightPeakMs) > MaxInterSideDirectPathMs)
                {
                    log.AppendLine(
                        $"  cross-side prior {rightChannel.Name}: withdrawn — " +
                        $"energy peaks {leftPeakMs:0.000} / {rightPeakMs:0.000} ms " +
                        "are too far apart to be one shared feature " +
                        "(asymmetric modal dominance)");
                    return null;
                }

                double peakTarget = leftPeakMs
                    - plan.SceneOffsetMs
                    - rightPeakMs;
                log.AppendLine(
                    $"  cross-side prior {rightChannel.Name}: target " +
                    $"{peakTarget:0.000} ms from the processed IR energy peaks " +
                    $"(L {leftPeakMs:0.000}, raw R {rightPeakMs:0.000} ms — " +
                    "both sides' direct reads modal-latched)");
                return (peakTarget, true);
            }

            double target = arrivals.Left.FirstArrivalDelayMilliseconds
                - plan.SceneOffsetMs
                - arrivals.Right.FirstArrivalDelayMilliseconds;
            log.AppendLine(
                $"  cross-side prior {rightChannel.Name}: target {target:0.000} ms " +
                $"(L arrival {arrivals.Left.FirstArrivalDelayMilliseconds:0.000}, " +
                $"raw R {arrivals.Right.FirstArrivalDelayMilliseconds:0.000} ms " +
                $"in {usedLowHz:0}-{usedHighHz:0} Hz)");
            return (target, false);
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
            // (soft envelopes, no localization) must not smear the pin. A
            // pure low-frequency pair is pinned too, but only to the LOBE: an
            // identical L/R driver pair's delay split is physical (path
            // difference), and a junction comb whose lobes differ by a dB
            // must not choose it — the field failure put one under-seat
            // midbass at 0 and the other at 10.85 ms for exactly that. The
            // lock tolerance is half the period of the tightest junction the
            // channel searches against, so the sum keeps full authority
            // inside the arrival's lobe and none across lobes.
            StereoPairLink? channelLink = plan.PairLinks?.FirstOrDefault(
                item => item.Right == channel);
            bool lockable = channelLink != null && IsSceneLockable(channelLink);
            (double TargetMs, bool Coarse)? cross = channelLink == null
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
            double? sceneLock = cross is not { } resolved
                ? null
                : lockable && !resolved.Coarse
                    ? SceneLockToleranceMs
                    : 500.0 / Math.Max(
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
                rightUntrustedSeeds, onsetLocks);
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
        RebalancePairsKeepingScene(plan, reprocess, alignment, log, onsetLocks);

        // A mono channel's own final polish: its delay (and polarity) is
        // scene-invariant by construction — one shared channel moves both
        // sides' handovers identically — so with everything else settled, the
        // best compromise across its left AND right junctions is searched
        // directly. This is the only pass where the right junction gets a
        // vote on the mono channel at all.
        ComoveMonoChannels(plan, reprocess, alignment, log);

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
    // localization region (the woofers) are pinned more loosely, to the
    // arrival's LOBE (half the tightest adjacent junction period): the ear
    // does not localize there, but an identical driver pair's delay split is
    // still physical, and the junction comb must polish within that lobe,
    // not choose one. With no reliable cross-side arrival at all the free
    // joint-junction search remains.
    private const double SceneLockToleranceMs = 0.05;

    // The lower edge of the localization region. Only the part of a pair's
    // shared band ABOVE this edge carries scene information, so the lock's
    // cross-side target is measured in that sub-band — and a pair whose band
    // merely pokes past the edge (e.g. 80-310 Hz) has too little localizable
    // content to pin: the lock requires at least a third of an octave above
    // the edge, the same admission rule the arrival analysis itself applies.
    private const double SceneLockLocalizationLowHz = 300;

    // The widest the two sides of one stereo pair can plausibly differ in
    // direct-path time, measured from one listening position: a car cabin is
    // at most a couple of metres across, so ~8 ms bounds any real path split
    // with generous margin. The energy-peak fallback (below) trusts that both
    // sides' dominant IR packets are the SAME physical feature; peaks farther
    // apart than this are almost certainly DIFFERENT room modes dominating L
    // vs R (an acoustically asymmetric install), so the peak difference is not
    // a real L/R split and the pair falls back to its own-side junction search
    // rather than pinning to a fabricated target.
    private const double MaxInterSideDirectPathMs = 8.0;

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

    // The mono-channel co-move (see ComoveMonoChannels): the search spans a
    // full half period of the mono channel's tightest junction to each side,
    // in BOTH polarities. Unlike the pair co-move this deliberately reaches
    // other comb lobes: a mono channel is timed by the left pass alone, so
    // the walk's lobe choice never heard the right junction's vote — the
    // field failure that pinned this had the sub/midbass junctions near-tied
    // on the left while the right junction clearly preferred the flip partner
    // a third of a period away, and only a hand-tuned compromise served both
    // sides. With the polarity dimension the half-period window covers every
    // lobe family exactly once, judged by the mean of the two junctions.
    private const double MonoComoveSearchHalfPeriods = 1.0;

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
        StringBuilder log,
        IReadOnlyDictionary<AlignmentJunction, OnsetLockState> onsetLocks)
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

            // Each adjacent junction bounds the pair's reach to within half its
            // period OF THE NEIGHBOR'S ALREADY-APPLIED co-move delta: within
            // half a period the junction sums are single-lobed, so the search
            // can only polish the alignment the arrival-anchored walk chose.
            // Past that lies the next comb lobe — and fractions of a dB of mean
            // junction loss cannot choose a lobe (the same physics as the
            // wide-window promotion reach cap). Centering on the neighbor's
            // delta (0 for a channel that never co-moves — a mono/fixed
            // neighbor) is what keeps the RELATIVE shift across the junction
            // bounded even when the neighbor pair already moved; a flat ±half
            // period around zero let two adjacent pairs drift a full period
            // apart, and the flat window alone let a 0.1-0.2 dB "gain" walk the
            // tweeter pair a whole period off its mid at a 2.3 kHz junction.
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
            // applies verbatim to both sides: negative deltas may not push
            // either channel below zero, positive ones may not push either
            // past the delay ceiling. Clamping after the fact would move the
            // two sides unequally and silently bend the very scene this pass
            // exists to preserve.
            double minDelta = Math.Max(
                lobeLowMs,
                -Math.Min(leftOverride.DelayMs, rightOverride.DelayMs));
            double maxDelta = Math.Min(
                lobeHighMs,
                MaxDelayMs - Math.Max(leftOverride.DelayMs, rightOverride.DelayMs));
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
    private static void ComoveMonoChannels(
        StereoAlignmentPlan plan,
        AlignmentReprocessor reprocess,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log)
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

            AlignmentOverride over = alignment.GetValueOrDefault(mono);
            double halfPeriodMs = junctions.Min(pair => 500.0 / pair.CrossoverHz);
            double reachMs = MonoComoveSearchHalfPeriods * halfPeriodMs;
            double minDelta = Math.Max(-reachMs, -over.DelayMs);
            double maxDelta = Math.Min(reachMs, MaxDelayMs - over.DelayMs);

            double Score(double deltaMs, bool flip)
            {
                var trial =
                    new Dictionary<IAlignmentChannel, AlignmentOverride>(alignment)
                    {
                        [mono] = new AlignmentOverride(
                            Math.Round(over.DelayMs + deltaMs, 2),
                            over.InvertPolarity ^ flip)
                    };
                IReadOnlyList<AlignmentSnapshot> current = reprocess(trial);
                Complex[] IrOf(IAlignmentChannel channel) =>
                    current.First(item => item.Channel == channel).ImpulseResponse;
                double total = 0;
                int measured = 0;
                foreach (AlignmentJunction junction in junctions)
                {
                    IAlignmentChannel neighbor = junction.Lower.Channel == mono
                        ? junction.Upper.Channel
                        : junction.Lower.Channel;
                    (double LossDb, double DipDb)? loss =
                        VirtualCrossoverAnalysis.MeasureSumLoss(
                            IrOf(mono),
                            new List<Complex[]> { IrOf(neighbor) },
                            mono.SampleRate,
                            junction.BandLowHz,
                            junction.BandHighHz);
                    if (loss is { } value)
                    {
                        total += value.LossDb +
                            VirtualCrossoverAnalysis.DipExcessPenaltyWeight *
                            (value.DipDb - value.LossDb);
                        measured++;
                    }
                }

                return measured > 0 ? total / measured : double.NegativeInfinity;
            }

            double baseline = Score(0, flip: false);
            if (double.IsNegativeInfinity(baseline))
            {
                continue;
            }

            // Every probe re-renders the mono channel, so the grid is kept
            // lean: a half-millisecond coarse pass over both polarities, then
            // two shrinking refinements around the winner — ~40 reprocesses
            // for the widest (80 Hz) junction window, each costing one
            // channel's chain (the others are cache hits).
            double bestDelta = 0;
            bool bestFlip = false;
            double bestScore = baseline;
            const double CoarseStepMs = 0.5;
            foreach (bool flip in new[] { false, true })
            {
                for (double delta = minDelta;
                    delta <= maxDelta + 1e-9;
                    delta += CoarseStepMs)
                {
                    double score = Score(delta, flip);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDelta = delta;
                        bestFlip = flip;
                    }
                }
            }
            foreach (double step in new[] { 0.1, 0.02 })
            {
                double center = bestDelta;
                double reach = step * 5;
                for (double delta = Math.Max(minDelta, center - reach);
                    delta <= Math.Min(maxDelta, center + reach) + 1e-9;
                    delta += step)
                {
                    double score = Score(delta, bestFlip);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDelta = delta;
                    }
                }
            }

            if ((bestDelta != 0 || bestFlip) &&
                bestScore > baseline + PairComoveMinimumGainDb)
            {
                alignment[mono] = new AlignmentOverride(
                    Math.Round(over.DelayMs + bestDelta, 2),
                    over.InvertPolarity ^ bestFlip);
                log.AppendLine(
                    $"Co-move {mono.Name}: {bestDelta:+0.00;-0.00} ms" +
                    (bestFlip ? ", polarity flipped" : "") +
                    $" (mean dip-penalized junction loss over both sides " +
                    $"{baseline:0.00} -> {bestScore:0.00} dB; a mono move " +
                    "cannot touch the scene)");
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
