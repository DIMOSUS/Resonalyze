using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Resonalyze.Dsp.Tests;

public sealed class AutoAlignmentEngineTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 8_192;
    private const int BasePosition = 480; // 10 ms at 48 kHz.

    /// <summary>
    /// A synthetic channel: the initial IR feeds the stage-1 snapshots, the
    /// reprocess IR feeds the stage-2 searches. They are usually the same;
    /// tests that exercise the recovery paths (edge retry, wide-window
    /// promotion, the negative-delay shift) give the searches an IR the
    /// coarse stage did not see — the synthetic equivalent of a coarse
    /// arrival estimate that is off.
    /// </summary>
    private sealed class TestChannel : IAlignmentChannel
    {
        public TestChannel(string name, Complex[] initialIr, Complex[]? reprocessIr = null)
        {
            Name = name;
            InitialIr = initialIr;
            ReprocessIr = reprocessIr ?? initialIr;
        }

        public string Name { get; }
        public int SampleRate => AutoAlignmentEngineTests.SampleRate;
        public int ProcessorSampleRate => SampleRate;
        public Complex[] InitialIr { get; }
        public Complex[] ReprocessIr { get; }
    }

    // A snapshot carrying everything the predicted-arrival probe reads: the
    // PROCESSED response the timeline would time, plus the chain-free
    // response and the chain that turns one into the other.
    private static AlignmentSnapshot PredictableSnapshot(
        string name, Complex[] bypassed, DspChannelChain chain)
    {
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            bypassed, chain, SampleRate, SampleRate, out ValidSampleRange processedRange);
        return new AlignmentSnapshot(
            new TestChannel(name, processed),
            processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            processedRange,
            chain,
            bypassed);
    }

    private static Complex[] UnitImpulse(int position, double amplitude = 1.0)
    {
        var ir = new Complex[IrLength];
        ir[position] = amplitude;
        return ir;
    }

    private static Complex[] DelayedImpulse(double delayMs, bool invert = false) =>
        VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(DelayMs: delayMs, InvertPolarity: invert),
            SampleRate,
            SampleRate);

    // A first arrival plus a competing later copy — the shape that splits the
    // envelope arrival (reads the first copy) from the whitened-correlation
    // peak (follows the stronger copy).
    private static Complex[] ImpulseWithEcho(
        double offsetMs, double amplitude, double echoMs, double echoAmplitude)
    {
        var ir = new Complex[IrLength];
        ir[BasePosition + (int)Math.Round(offsetMs / 1000.0 * SampleRate)] =
            amplitude;
        ir[BasePosition + (int)Math.Round((offsetMs + echoMs) / 1000.0 * SampleRate)] +=
            echoAmplitude;
        return ir;
    }

    // A genuinely PERIODIC front: three near-equal copies one period apart.
    // Every correlation witness ties its own same-sign rivals on such a front —
    // the full-record PHAT and the direct-sound cut alike — so the seed honestly
    // falls back to the arrival envelope, and the onset lock is the one
    // authority left that reads something other than a correlation lobe.
    private static Complex[] PeriodicFront(double periodMs)
    {
        var ir = new Complex[IrLength];
        int period = (int)Math.Round(periodMs / 1000.0 * SampleRate);
        ir[BasePosition] = 0.995;
        ir[BasePosition + period] = 1.0;
        ir[BasePosition + (2 * period)] = 0.995;
        return ir;
    }

    // A clean front with one strong LATE reflection — late enough to stay
    // outside the direct-sound cut. Two channels whose reflections correlate
    // (the cabin's shared geometry) grow a whitened-correlation lobe at the
    // REFLECTION pair's lag: a phantom the full-record trust gates cannot see
    // through, since it is a genuine, dominant, separated extremum.
    private static Complex[] ReflectedFront(
        double frontMs, double reflectionAfterMs, double reflectionAmplitude)
    {
        var ir = new Complex[IrLength];
        ir[BasePosition + (int)Math.Round(frontMs / 1000.0 * SampleRate)] = 1.0;
        ir[BasePosition +
            (int)Math.Round((frontMs + reflectionAfterMs) / 1000.0 * SampleRate)] =
            reflectionAmplitude;
        return ir;
    }

    // A SOFT band-limited front under a strong late resonant build-up BELOW
    // the pair band — the field shape of the modal latch. The front is an
    // impulse smeared by a band-pass (a real driver through its crossover has
    // no sharp click), so in the full pair band its low envelope hides below
    // the 25 dB arrival search depth under the modes' bulk; the band's UPPER
    // half sits past the modes, where the front stands alone. Causal decaying
    // sines keep the record's tail (and with it the SNR grade) clean, unlike
    // a windowed tone burst whose spectral leakage rings across the record.
    private static Complex[] FrontUnderLateMode(
        double frontMs, double modeMs, double modeAmplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                DelayMs: frontMs,
                Crossover: new CrossoverSpec(
                    CrossoverKind.BandPass,
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 800, 24),
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 80, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(modeMs / 1000.0 * SampleRate);
        // A smooth attack keeps the build-up's onset out of the band's upper
        // half — an abruptly switched sine is itself a broadband click there.
        const double AttackSeconds = 0.008;
        const double DecaySeconds = 0.1;
        foreach (double modeHz in new[] { 65.0, 72.0, 80.0 })
        {
            for (int i = start; i < ir.Length; i++)
            {
                double t = (i - start) / (double)SampleRate;
                ir[i] += modeAmplitude *
                    (1 - Math.Exp(-t / AttackSeconds)) *
                    Math.Exp(-t / DecaySeconds) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }
        return ir;
    }

    private static Dictionary<IAlignmentChannel, AlignmentOverride> Run(
        TestChannel[] byBand,
        double[] crossoversHz,
        StringBuilder log,
        (double LowHz, double HighHz)[]? bands = null,
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null,
        Dictionary<IAlignmentChannel, AlignmentOverride>? alignment = null)
    {
        var snapshots = byBand.ToDictionary(
            channel => channel,
            channel => new AlignmentSnapshot(
                channel,
                channel.InitialIr,
                VirtualCrossoverAnalysis.FindPeakIndex(channel.InitialIr)));
        List<AlignmentJunction> junctions = crossoversHz
            .Select((fc, i) => new AlignmentJunction(
                snapshots[byBand[i]],
                snapshots[byBand[i + 1]],
                fc,
                bands?[i].LowHz ?? Math.Max(20, fc / 2),
                bands?[i].HighHz ?? Math.Min(20_000, fc * 2)))
            .ToList();

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            byBand
                .Select(channel =>
                {
                    AlignmentOverride o = overrides.GetValueOrDefault(channel);
                    Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
                        channel.ReprocessIr,
                        new DspChannelChain(
                            DelayMs: o.DelayMs,
                            InvertPolarity: o.InvertPolarity),
                        SampleRate,
                        SampleRate);
                    return new AlignmentSnapshot(
                        channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir));
                })
                .ToList();

        alignment ??= new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            byBand.Select(channel => snapshots[channel]).ToList(),
            junctions,
            Reprocess,
            alignment,
            log,
            decisions);
        return alignment;
    }

    [Fact]
    public void Compute_ReportsPerChannelDecisions()
    {
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var log = new StringBuilder();
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>();

        Run([woofer, tweeter], [1_000], log, decisions: decisions);

        // The reference is not searched — nothing was chosen, so it carries
        // the Reference kind and no confidence.
        Assert.Equal(AlignmentDecisionKind.Reference, decisions[woofer].Kind);
        Assert.Null(decisions[woofer].Confidence);
        Assert.Contains("reference", decisions[woofer].Detail);
        // The clean synthetic junction's whitened extremum is unambiguous, so
        // the seed is trusted and the onset lock stands down (it exists to
        // replace an arrival-envelope anchor, not a measured one): the tweeter
        // reports a free Search, naming its junction and carrying the rival
        // margin as confidence.
        Assert.Equal(AlignmentDecisionKind.Search, decisions[tweeter].Kind);
        Assert.NotNull(decisions[tweeter].Confidence);
        Assert.Contains("vs W", decisions[tweeter].Detail);
        Assert.DoesNotContain("onset-locked", decisions[tweeter].Detail);
    }

    [Fact]
    public void Compute_RecoversAnInsertedDelay_TwoWay()
    {
        // The tweeter arrives 1 ms before the woofer; the woofer is the latest
        // channel, so it anchors and the tweeter is delayed to meet it.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        Assert.False(alignment.ContainsKey(woofer));
        AlignmentOverride result = alignment[tweeter];
        Assert.InRange(result.DelayMs, 0.95, 1.05);
        Assert.False(result.InvertPolarity);
        // Applied delays are rounded to the panel's 0.01 ms display precision.
        Assert.Equal(Math.Round(result.DelayMs, 2), result.DelayMs, 9);
        Assert.Contains("Reference: W", log.ToString());
        Assert.Contains("Pair W/T:", log.ToString());
    }

    [Fact]
    public void Compute_ReferenceIsNotTheBottomChannel_WalksDownward()
    {
        // Mirror image of the two-way test: here the TWEETER arrives latest, so it
        // becomes the reference at band index 1 and the woofer (index 0) is aligned
        // through the downward-walk branch (byBand[i+1] / pairs[i]) — the opposite
        // index arithmetic to the upward walk every other test exercises.
        var woofer = new TestChannel("W", DelayedImpulse(0.0));
        var tweeter = new TestChannel("T", DelayedImpulse(1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        Assert.False(alignment.ContainsKey(tweeter)); // reference stays put
        Assert.InRange(alignment[woofer].DelayMs, 0.95, 1.05);
        Assert.False(alignment[woofer].InvertPolarity);
        Assert.Contains("Reference: T", log.ToString());
    }

    [Fact]
    public void Compute_DetectsAnInvertedChannel()
    {
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0, invert: true));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        AlignmentOverride result = alignment[tweeter];
        Assert.True(result.InvertPolarity);
        Assert.InRange(result.DelayMs, 0.9, 1.1);
    }

    [Fact]
    public void Compute_ChainsDelaysThroughASettledNeighbor_ThreeWay()
    {
        // Mid and tweeter both arrive 2 ms before the sub. The mid aligns to
        // the sub directly; the tweeter never sees the sub — it aligns to the
        // settled mid, so its delay must inherit the mid's 2 ms through the
        // chain.
        var sub = new TestChannel("S", DelayedImpulse(2.0));
        var mid = new TestChannel("M", DelayedImpulse(0.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([sub, mid, tweeter], [200, 2_000], log);

        Assert.False(alignment.ContainsKey(sub));
        Assert.InRange(alignment[mid].DelayMs, 1.9, 2.1);
        Assert.InRange(alignment[tweeter].DelayMs, 1.9, 2.1);
        Assert.False(alignment[mid].InvertPolarity);
        Assert.False(alignment[tweeter].InvertPolarity);
        Assert.Contains("Reference: S", log.ToString());
    }

    [Fact]
    public void Compute_NegativeOptimum_ShiftsTheOtherChannelsInstead()
    {
        // Stage 1 sees the tweeter 0.1 ms early, but the search-time IR
        // arrives 0.3 ms after the woofer: the optimum is a physically
        // impossible -0.3 ms. The engine must zero the tweeter and push the
        // woofer out by the deficit instead — a uniform shift that preserves
        // the alignment.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.9), reprocessIr: DelayedImpulse(1.3));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        Assert.InRange(alignment[tweeter].DelayMs, -0.001, 0.001);
        Assert.InRange(alignment[woofer].DelayMs, 0.25, 0.35);
        Assert.False(alignment[woofer].InvertPolarity);
    }

    [Fact]
    public void Compute_ResultAtTheWindowEdge_RetriesWidened()
    {
        // A 600 Hz junction — below the onset lock's frequency gate, so the
        // edge-retry recovery still owns the seed-error case. Stage 1 seeds
        // the search at 1.0 ms, but the search-time optimum is +0.1 ms — just
        // outside the [0.167, 1.833] fine window, so the first pass pins to
        // the window edge and the widened retry must find the true optimum.
        // (At a locked junction the onset anchor recenters the window on the
        // search-time front directly and no retry is needed — see
        // Compute_SeedErrorAtASharpJunction_OnsetLockRecoversDirectly.)
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.0), reprocessIr: DelayedImpulse(0.9));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [600], log);

        Assert.InRange(alignment[tweeter].DelayMs, 0.05, 0.15);
        Assert.Contains("WARNING: fine result at the search edge", log.ToString());
    }

    [Fact]
    public void Compute_OptimumBeyondTheRetryReach_PromotesTheWideWindowPick()
    {
        // A 600 Hz junction — below the onset lock's frequency gate, where the
        // wide-window promotion still owns the far-lobe recovery. The
        // search-time optimum sits at +2.7 ms, a full period past the fine
        // window around the 1.0 ms seed: the fine pass settles on the comb
        // lobe at ~1.03 ms inside its window, and only the ±3 ms diagnostic
        // sweep reaches the true optimum. Its clearly better summation is
        // within the promotion reach cap (2.5 periods), so it must be
        // promoted. (At a locked junction the promotion is shut and the onset
        // anchor resolves the lobe instead.)
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.0), reprocessIr: DelayedImpulse(-1.7));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [600], log);

        Assert.InRange(alignment[tweeter].DelayMs, 2.6, 2.8);
        Assert.Contains("promoted", log.ToString());
    }

    [Fact]
    public void Compute_SeedErrorAtASharpJunction_OnsetLockRecoversDirectly()
    {
        // A 2 kHz junction whose seed is NOT trusted — the tweeter's front is
        // genuinely PERIODIC (three near-equal copies one period apart), so
        // the same-polarity rivals tie on the full-record PHAT and on the
        // direct-sound cut alike, and the coarse offset falls back to the
        // arrival envelope. That envelope says 1.0 ms while the search-time
        // optimum is +0.4 ms, 1.2 periods off and beyond the fine window
        // around it. Above the lock's frequency gate the broadband onsets of
        // the search-time IRs re-anchor the window on the true front, so the
        // optimum is found directly: no edge retry, no promotion, and the
        // chosen delay lands on the front-aligned lobe. (With a TRUSTED seed
        // the lock stands down — it replaces an arrival anchor, and a measured
        // extremum is the better witness; see Compute_ReportsPerChannelDecisions.
        // A SINGLE competing copy no longer reaches the lock: the direct-cut
        // witness resolves it — see
        // Compute_UntrustedPhatAtAHighJunction_DirectCutWitnessSeeds.)
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", PeriodicFront(0.5),
            reprocessIr: DelayedImpulse(0.6));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        Assert.InRange(alignment[tweeter].DelayMs, 0.35, 0.45);
        Assert.Contains("ONSET-LOCKED", log.ToString());
        Assert.Contains("onset gap after", log.ToString());
        Assert.Contains("direct-cut", log.ToString());
        Assert.Contains("unusable", log.ToString());
        Assert.DoesNotContain(
            "WARNING: fine result at the search edge", log.ToString());
        Assert.DoesNotContain("promoted", log.ToString());
    }

    [Theory]
    // A matched odd-order split nulls in phase at its corner, so the pair is
    // inverted BY CONSTRUCTION and the engine must not leave that to the
    // summation score (which, once each polarity is allowed its own delay, sees
    // only fractions of a dB either way).
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 36, true)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 12, true)]
    [InlineData(CrossoverFilterFamily.Butterworth, 12, true)]
    // ... while the even-order ones sum in phase and must stay that way.
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24, false)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 48, false)]
    public void Compute_MatchedSplit_TakesThePolarityItsFiltersAskFor(
        CrossoverFilterFamily family,
        int slopeDbPerOctave,
        bool expectInverted)
    {
        var log = new StringBuilder();
        (Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            IAlignmentChannel lower,
            IAlignmentChannel upper) = RunFilteredJunction(
                family, slopeDbPerOctave, upperCornerHz: 2_000, log: log);

        // The reference channel of a two-channel walk carries no override at
        // all, which reads as "not inverted" — the same thing the panel applies.
        Assert.Equal(
            expectInverted,
            alignment.GetValueOrDefault(lower).InvertPolarity !=
                alignment.GetValueOrDefault(upper).InvertPolarity);
        // The force fires either way now — what changes is which answer it
        // states — so the log is checked for the direction, not for presence.
        Assert.Contains("by construction", log.ToString());
        Assert.Contains(
            expectInverted ? "sums only inverted" : "sums in phase",
            log.ToString());
    }

    [Fact]
    public void Compute_MatchedEvenOrderSplit_KeepsInPhaseAgainstTheSumsWishes()
    {
        // The in-phase half of the rule, pinned where it can actually be seen.
        // The tweeter's impulse is NEGATIVE, so the summation search has a
        // decisive reason to invert it — several dB, not the fractions the
        // matched-split argument is about. A matched LR24 sums in phase by
        // construction, so the engine takes that and leaves the flip alone.
        //
        // The trade is deliberate and worth stating: a driver genuinely wired
        // backwards behind a matched even-order split is NOT corrected by Auto
        // delay, the same way a matched odd-order split's forced flip is not
        // undone by one. Both are the price of reading a polarity the sum
        // cannot resolve off the crossover that defines it, and both are fenced
        // to junctions above a kilohertz under a trusted seed.
        var log = new StringBuilder();
        (Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            IAlignmentChannel lower,
            IAlignmentChannel upper) = RunFilteredJunction(
                CrossoverFilterFamily.LinkwitzRiley, 24, upperCornerHz: 2_000,
                log: log, upperAmplitude: -1.0);

        Assert.Equal(
            alignment.GetValueOrDefault(lower).InvertPolarity,
            alignment.GetValueOrDefault(upper).InvertPolarity);
        Assert.Contains("sums in phase", log.ToString());
    }
    [Theory]
    [InlineData(37)]
    [InlineData(0)]
    [InlineData(-37)]
    public void PlacePairAt_PutsTheUpperChannelThatMuchLaterEitherWay(
        int slideSamples)
    {
        // The coordinate contract a witness that reads "the applied alignment"
        // depends on: whichever way the candidate points, the pair comes back
        // with the upper channel exactly slideSamples behind the lower. A
        // negative candidate is ordinary — the cascade rebases its delays only
        // at the end — and moving the upper channel earlier would cut its own
        // front off, so the lower one moves later instead.
        Complex[] lower = UnitImpulse(BasePosition);
        Complex[] upper = UnitImpulse(BasePosition);

        (Complex[] placedLower, Complex[] placedUpper) =
            AutoAlignmentEngine.PlacePairAt(lower, upper, slideSamples);

        int lowerPeak = VirtualCrossoverAnalysis.FindPeakIndex(placedLower);
        int upperPeak = VirtualCrossoverAnalysis.FindPeakIndex(placedUpper);
        Assert.Equal(slideSamples, upperPeak - lowerPeak);
        Assert.Equal(lower.Length, placedLower.Length);
        Assert.Equal(upper.Length, placedUpper.Length);
    }
    [Fact]
    public void Compute_StaggeredSplit_LeavesThePolarityToTheSearch()
    {
        // Two corners that merely meet (2000 against 2400 Hz) are not one
        // crossover: they overlap across a region instead of crossing at a
        // point, and their best relative delay is not zero — so the filters have
        // no single phase relation to state and the rule must stay out. The
        // archived cabins' staggered Butterworth 36 and LR24-against-LR48 splits
        // are exactly this shape, and reading them as "inverted" moved four
        // channels of the field battery.
        var log = new StringBuilder();
        RunFilteredJunction(
            CrossoverFilterFamily.LinkwitzRiley, 36, upperCornerHz: 2_400, log: log);

        Assert.DoesNotContain("by construction", log.ToString());
    }

    // A synthetic junction made of FILTERS: one impulse per channel, the lower
    // through a low-pass and the upper through a high-pass, both arriving
    // together. The snapshots carry their chains, which is what lets the engine
    // read the split's designed polarity.
    private static (Dictionary<IAlignmentChannel, AlignmentOverride> Alignment,
        IAlignmentChannel Lower, IAlignmentChannel Upper) RunFilteredJunction(
        CrossoverFilterFamily family,
        int slopeDbPerOctave,
        double upperCornerHz,
        StringBuilder log,
        double upperAmplitude = 1.0)
    {
        const double lowerCornerHz = 2_000;
        var lowPass = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            LowPassEdge: new CrossoverEdge(family, lowerCornerHz, slopeDbPerOctave)));
        var highPass = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(family, upperCornerHz, slopeDbPerOctave)));

        AlignmentSnapshot lower = PredictableSnapshot("W", UnitImpulse(BasePosition), lowPass);
        AlignmentSnapshot upper = PredictableSnapshot(
            "T", UnitImpulse(BasePosition, upperAmplitude), highPass);
        var junction = new AlignmentJunction(
            lower, upper, lowerCornerHz, lowerCornerHz / 2, lowerCornerHz * 2);

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot Apply(AlignmentSnapshot snapshot, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(snapshot.Channel);
                Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
                    snapshot.BypassedImpulseResponse!,
                    chain with
                    {
                        DelayMs = over.DelayMs,
                        InvertPolarity = over.InvertPolarity
                    },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return new AlignmentSnapshot(
                    snapshot.Channel,
                    ir,
                    VirtualCrossoverAnalysis.FindPeakIndex(ir),
                    range,
                    chain,
                    snapshot.BypassedImpulseResponse);
            }

            return [Apply(lower, lowPass), Apply(upper, highPass)];
        }

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [lower, upper], [junction], Reprocess, alignment, log);
        return (alignment, lower.Channel, upper.Channel);
    }

    [Fact]
    public void Compute_UntrustedPhatAtAHighJunction_DirectCutWitnessSeeds()
    {
        // The rescue the direct-cut witness exists for. The tweeter carries a
        // slightly stronger copy one period behind its front, so the
        // full-record PHAT's same-polarity rivals tie and its extremum fails
        // the trust gates. Before the witness this junction fell back to the
        // arrival envelope; measured across the archived cabins that fallback
        // sat 0.6-1.2 periods off the owner's tunes at mid/tweeter junctions.
        // The direct-sound cut tapers the copy against the front and resolves
        // a usable extremum, which seeds the stage-2 window onto the correct
        // lobe directly — no onset lock, no recovery machinery.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", ImpulseWithEcho(0.0, 0.995, 0.5, 1.0),
            reprocessIr: DelayedImpulse(0.6));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        Assert.InRange(alignment[tweeter].DelayMs, 0.35, 0.45);
        Assert.Contains("seed direct-cut (phat:", log.ToString());
        Assert.DoesNotContain("ONSET-LOCKED", log.ToString());
    }

    [Fact]
    public void Compute_ReflectionPhantomFarFromTheArrival_IsRefusedForTheDirectCut()
    {
        // The catastrophic field shape: both channels carry one strong LATE
        // reflection off the cabin's shared geometry, and the reflection pair's
        // whitened-correlation lobe DOMINATES the full record — a separated,
        // high-r extremum that r, dominance and the OLD 3 ms reach all accepted,
        // sitting five periods from the true front alignment. Measured across
        // the archived cabins this passed every gate with the extremum 3.4-4.7
        // periods off the owner's tune in half of the mid/tweeter cells; the
        // honest ones sit within 1.15. With a usable direct-cut witness in hand
        // the reach tightens to a period and a half, which is what refuses this
        // one — and the direct front, which never sees the reflections, seeds
        // instead.
        var woofer = new TestChannel("W", ReflectedFront(1.0, 3.0, 1.4));
        var tweeter = new TestChannel("T", ReflectedFront(0.0, 5.5, 1.4));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        Assert.InRange(alignment[tweeter].DelayMs, 0.9, 1.1);
        Assert.Contains(
            "seed direct-cut (phat: peak beyond the arrival's reach)", log.ToString());
    }

    [Fact]
    public void Compute_TroughDominantLowJunction_SeedsFromTheTroughAndFindsTheInvertedLobe()
    {
        // The field physics this pins (an 85 Hz junction): the upper channel
        // is genuinely inverted and ~15 ms early, so the whitened
        // correlation's strongest extremum is the inverted trough at the true
        // offset while the non-inverted "peak" is only a half-period
        // side-lobe of it. The dominant trough is a measurement like any
        // dominant peak — its POSITION seeds the timeline directly (polarity
        // stays with the loss search), the stage-2 window stays NARROW around
        // the measured lobe, and the far same-polarity alternatives never
        // enter the candidate list. The pre-symmetric gate used to send this
        // junction to the arrival fallback plus a period-wide window, where
        // the true lobe and a non-inverted lobe a third of a period out
        // competed within fractions of a dB.
        var midbass = new TestChannel("B", DelayedImpulse(15.2));
        var mid = new TestChannel("C", DelayedImpulse(0.0, invert: true));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        AlignmentOverride result = alignment[mid];
        Assert.True(result.InvertPolarity);
        Assert.InRange(result.DelayMs, 15.0, 15.4);
        string pairLine = TestLog.Line(text, "Pair B/C");
        Assert.Contains("phat trough", pairLine);
        Assert.Contains("-> seed phat", pairLine);
        Assert.DoesNotContain("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    [Fact]
    public void Compute_TrustedSeedAtALowJunction_KeepsBothPolaritiesInTheWindow()
    {
        // An 85 Hz junction: the polarity partner sits a half period — 5.9 ms —
        // from the seed, well past the fixed 2.5 ms cap a trusted seed's fine
        // window used to carry. A trusted seed fixes WHERE the adjacent lobes
        // sit, not WHICH one is right (the peak-vs-trough margin measures the
        // band's width), so the loss search settles the polarity — and it can
        // only settle what its window contains. The window therefore reaches
        // the MEASURED partner distance, and both polarities must appear as
        // candidates. Without it a hundredth of PHAT coefficient would decide
        // the junction outright: the diagnostic sweep sees the partner, but
        // reaching it there costs a 1.6 dB promotion margin no near-tie pays.
        var midbass = new TestChannel("B", DelayedImpulse(15.2));
        var mid = new TestChannel("C", DelayedImpulse(0.0, invert: true));
        var log = new StringBuilder();

        Run([midbass, mid], [85], log);

        string channelLine = TestLog.Line(log.ToString(), "Channel C:");
        // The premise: a trusted seed, so the wide-seed machinery is not what
        // opened the window.
        Assert.DoesNotContain("WIDE SEED", channelLine);
        Match window = Regex.Match(
            channelLine.Replace(',', '.'),
            @"window (-?\d+\.\d+)\.\.(-?\d+\.\d+) ms");
        Assert.True(window.Success, channelLine);
        double low = double.Parse(
            window.Groups[1].Value, CultureInfo.InvariantCulture);
        double high = double.Parse(
            window.Groups[2].Value, CultureInfo.InvariantCulture);
        // The pick sits at 15.2 ms; its polarity partners are a half period
        // (5.88 ms) to each side, and at least one of them must be reachable —
        // the fixed 2.5 ms cap reached neither.
        Assert.True(
            high - 15.2 >= 5.0 || 15.2 - low >= 5.0,
            $"the polarity partner is out of the search window:\r\n{channelLine}");
        // ...and never as far as the same-polarity rival a full period out.
        Assert.True(
            high - low < 2.0 * 1000.0 / 85.0,
            $"the window must stay inside one period:\r\n{channelLine}");
    }

    [Fact]
    public void Compute_DominantPeakFarFromTheArrival_IsNotTrustedAsTheSeed()
    {
        // A low junction whose upper channel is a soft direct sound under a
        // STRONG late reflection: the arrival detector honestly reads the
        // direct copy (well inside its 25 dB search depth) while the whitened
        // peak aligns the neighbor with the reflection, ~9 ms past it — a
        // cycle-skip candidate. The fixed ±3 ms window used to exclude such a
        // peak by construction; the period-wide window sees it, so the reach
        // rule must refuse it and keep the TIMELINE on the arrival envelope,
        // widened (WIDE SEED). What the loss search and the promotion then
        // make of the deliberately ambiguous summation surface is their
        // pinned-elsewhere business — this test pins the seed contract.
        var midbass = new TestChannel("B", DelayedImpulse(15.0));
        var mid = new TestChannel(
            "C", ImpulseWithEcho(0.0, 0.35, 8.0, 1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        Assert.Contains(
            "seed arrival (peak beyond the arrival's reach)",
            TestLog.Line(text, "Pair B/C"));
        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    // A channel whose LOW band arrives late and HIGH band early: two
    // competing alignment lobes living in different halves of the pair band,
    // the shape that makes the search's gain (in)variance measurable.
    private static Complex[] SplitBandArrivals()
    {
        Complex[] low = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                GainDb: 6,
                DelayMs: 10.0,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 180, 36))),
            SampleRate,
            SampleRate);
        Complex[] high = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                GainDb: -14,
                DelayMs: 2.0,
                Crossover: new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 280, 36))),
            SampleRate,
            SampleRate);
        for (int i = 0; i < low.Length; i++)
        {
            low[i] += high[i];
        }
        return low;
    }

    [Fact]
    public void FindAlignmentCandidates_LevelMatch_PinsTheWinnerAcrossGains()
    {
        // The discrimination weight of each bin rides the LEVEL BALANCE
        // between the channels, so with two lobes living in different halves
        // of the band the winner follows the variable channel's gain: at
        // 0 dB the louder low half rules (align with the late low arrival),
        // at -20 dB the equal-level region migrates into the quiet high half
        // (align with the early high arrival). The precondition below proves
        // this synthetic genuinely discriminates — without it a clean
        // impulse pair stays green even with the level match broken. The
        // level match must then pin ONE winner across 0/-10/-20 dB.
        Complex[] fixedIr = SplitBandArrivals();
        double WinnerMs(double gain, bool levelMatch)
        {
            Complex[] variable = UnitImpulse(BasePosition)
                .Select(value => value * gain)
                .ToArray();
            IReadOnlyList<AlignmentCandidate> candidates =
                VirtualCrossoverAnalysis.FindAlignmentCandidates(
                    variable, [fixedIr], SampleRate, 90, 360, -1, 13,
                    levelMatch: levelMatch);
            Assert.NotEmpty(candidates);
            return candidates[0].DelayMs;
        }

        double unmatchedLoud = WinnerMs(1.0, levelMatch: false);
        double unmatchedQuiet = WinnerMs(0.1, levelMatch: false);
        Assert.True(
            Math.Abs(unmatchedLoud - unmatchedQuiet) > 2.0,
            "the synthetic must discriminate: unmatched winners " +
            $"{unmatchedLoud:0.000} vs {unmatchedQuiet:0.000} ms");

        double matched = WinnerMs(1.0, levelMatch: true);
        foreach (double gain in new[] { 0.316, 0.1 })
        {
            double winner = WinnerMs(gain, levelMatch: true);
            Assert.InRange(winner, matched - 0.35, matched + 0.35);
        }
    }

    [Fact]
    public void Compute_ModalLatchOnTheArrival_ReanchorsOnTheHalfBandReads()
    {
        // The field failure this pins (a 180 Hz midbass/mid junction under a
        // steep LP): the lower channel's soft direct front hides below the
        // 25 dB envelope search depth under a strong late resonant build-up
        // below the band, so the full-band arrival reads the build-up at
        // ~37 ms, over 20 ms late — while the band's upper half still reads
        // the front at ~15 ms. The honesty probe must convict that latch and
        // the pair must re-anchor on the half-band reads: the timeline then
        // seeds near the true ~5.6 ms relation instead of parking a channel
        // tens of ms off (the field run inverted the mids a full period out).
        var midbass = new TestChannel(
            "B", FrontUnderLateMode(5.0, 15.0, 2.0));
        var mid = new TestChannel("C", DelayedImpulse(0.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [180], log);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        Assert.Contains("(modal latch)", text);
        // Re-anchored on the half-band front (~15 ms), not the ~37 ms mode.
        Assert.Contains("arrivals 15", TestLog.Line(text, "Pair B/C"));
        // The mid lands near its true relation to the front — the latched
        // read would have based the search two dozen ms away.
        Assert.InRange(alignment[mid].DelayMs, 3.0, 9.0);
    }

    // A front amplitude either side of half the arrival detector's search
    // depth, against a body that is the band's own energy 8 ms behind it.
    // 0.20 reads -14.8 dB of prominence, 0.30 reads -11.6 dB; nothing else
    // about the pair changes.
    [Theory]
    [InlineData(0.20, true)]
    [InlineData(0.30, false)]
    public void Compute_ArrivalPickedFarUnderItsBandEnergy_CannotVetoTheExtremum(
        double frontAmplitude, bool deepEnoughToStandDown)
    {
        // The field failure this pins (a 110 Hz sub/midbass junction): the
        // subwoofer's own direct front is a real arrival, and the detector
        // finds it well below the band's energy — the cabin's build-up arrives
        // behind it and dwarfs it. Its neighbour's arrival IS its band peak, so
        // the pair anchor subtracts a front from an energy centre and lands a
        // whole lobe away from the whitened extremum, which reads the energy on
        // both sides. The reach veto then refused that extremum (r 0.95, the
        // direct-sound cut concurring at 0.93) for disagreeing with the anchor,
        // and the run parked the midbass half a period early with its polarity
        // flipped to match.
        //
        // Nothing here re-anchors: the pair band's upper half carries the same
        // two copies as the full band, so the honesty probe agrees with the
        // read and the arrival stands. The anchor is honest — it is simply not
        // the read the extremum disagrees with, and only THAT withdraws its
        // veto, and only to the direct-sound cut, which agrees with the
        // extremum here. A shallower pick keeps the veto and the 6.9 ms
        // anchor with it.
        var sub = new TestChannel(
            "W", ImpulseWithEcho(0.0, frontAmplitude, 8.0, 1.0));
        var midbass = new TestChannel("B", DelayedImpulse(8.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([sub, midbass], [110], log);

        double relative = alignment.GetValueOrDefault(midbass).DelayMs -
            alignment.GetValueOrDefault(sub).DelayMs;
        string text = log.ToString();
        Assert.DoesNotContain("(modal latch)", text);
        if (deepEnoughToStandDown)
        {
            // Both channels' energy already coincides, so the proposal is the
            // near-zero relation the extremum reads — not the ~7 ms the two
            // fronts differ by.
            Assert.InRange(relative, -1.5, 1.5);
            Assert.Contains("the veto passes to the cut", text);
            Assert.Contains("-> seed phat", text);
        }
        else
        {
            Assert.Contains("beyond the arrival's reach", text);
            Assert.DoesNotContain("the veto passes to the cut", text);
            Assert.True(
                Math.Abs(relative) > 2.5,
                $"the vetoed run should keep the arrival's answer, got {relative:0.00} ms");
        }
    }

    [Fact]
    public void Compute_DeepArrivalPickWithAContradictingDirectCut_KeepsTheReachVeto()
    {
        // The blanket version of the exception would have been a cycle skip
        // waiting to happen: withdrawing the reach leaves the extremum with
        // quality gates only (|r|, a rival margin, an edge pin), and not one of
        // them can tell a lobe from the next one over. This is that shape.
        //
        // Both channels carry one strong LATE reflection off shared geometry,
        // five periods behind the fronts, and it is loud enough (x5) that the
        // arrival is picked 13.9 dB under the band's energy — deep enough to
        // open the exception. The reflection pair's whitened lobe dominates the
        // full record at r 0.980, separated and past the reach. The direct-sound
        // cut never sees those reflections and puts the pair 2.5 ms away, so it
        // refuses to corroborate and the veto stands — which is also what keeps
        // the tightened direct-seed reach at a mid/tweeter junction from being
        // bypassed by a deep pick.
        var woofer = new TestChannel("W", ReflectedFront(1.0, 3.0, 5.0));
        var tweeter = new TestChannel("T", ReflectedFront(0.0, 5.5, 5.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log, bands: [(700, 5_600)]);

        string text = log.ToString();
        Assert.DoesNotContain("the veto passes to the cut", text);
        Assert.Contains(
            "seed direct-cut (phat: peak beyond the arrival's reach)", text);
        // The fronts' own relation, not the reflection pair's phantom at
        // -1.5 ms.
        Assert.InRange(alignment[tweeter].DelayMs, 0.9, 1.1);
    }

    // The upper channel of the incomparable-probe case: its full band reads
    // the (low-passed) direct front, but its upper half is owned by a strong
    // high-passed late reflection — the half-band probe times a feature far
    // LATER than the channel's own full-band front, so its certificate is
    // UNVERIFIED: the probe is valid and clean, yet not the wavefront a
    // latched partner's probe found.
    private static Complex[] FrontWithLateHighReflection(
        double frontMs, double reflectionMs, double reflectionGainDb)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                DelayMs: frontMs,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 150, 36))),
            SampleRate,
            SampleRate);
        Complex[] late = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(
                GainDb: reflectionGainDb,
                DelayMs: reflectionMs,
                Crossover: new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, 220, 36))),
            SampleRate,
            SampleRate);
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] += late[i];
        }
        return ir;
    }

    [Fact]
    public void Compute_ModalLatchWithIncomparableProbe_KeepsTheFullBandAnchor()
    {
        // One side convicted (its half-band probe found the true early
        // front), the other UNVERIFIED: its probe timed a late high-passed
        // reflection far behind its own full-band front. The two probes then
        // time DIFFERENT physical events, so the pair must not re-anchor on
        // them — the Pair line keeps the full-band arrivals and the reach
        // veto stays armed: re-anchoring on incomparable probes is exactly
        // what must not happen.
        var midbass = new TestChannel("B", FrontUnderLateMode(5.0, 15.0, 2.0));
        var mid = new TestChannel("C", FrontWithLateHighReflection(0.0, 8.0, 8));
        var log = new StringBuilder();

        try
        {
            Run([midbass, mid], [180], log);
        }
        catch (InvalidOperationException)
        {
            // A refused run (infeasible spread) is an acceptable outcome for
            // this deliberately poisoned pair; the seed contract under test
            // was logged before the refusal.
        }

        string text = log.ToString();
        Assert.Contains("(modal latch)", text);
        string pairLine = TestLog.Line(text, "Pair B/C");
        // The latched side's full-band anchor (~37 ms) stands — no re-anchor
        // onto the probes' mismatched wavefronts.
        Assert.Contains("arrivals 37", pairLine);
        // And the veto that anchor cannot be talked out of. This pair also
        // trips the low-prominence exception (the unverified side's front is
        // picked 17 dB under its own band's energy), and that exception is
        // exactly what must NOT reach here: a conviction with no comparable
        // replacement keeps its corrupted diff deliberately, so lifting the
        // veto would seed from the modal extremum measured around it.
        Assert.Contains("beyond the arrival's reach", pairLine);
        Assert.DoesNotContain("cannot veto it", text);
    }

    [Fact]
    public void Compute_SamePolarityRivalNearTie_IsNotTrustedAsTheSeed()
    {
        // Two same-polarity correlation lobes a full period apart, the FAR
        // one marginally stronger — the configuration peak-vs-trough
        // Confidence cannot see (the second positive lobe is simply absent
        // from it), so the far lobe used to seed as a confidently
        // "unambiguous" peak: a silent whole-period cycle skip that stage 2
        // could no longer recover (its window and the wide sweep both reach
        // well under a period). The junction band is WIDE so the whitened
        // kernel's own trough stays shallow — the trough rules must not be
        // the ones refusing this seed; the rival rule must.
        var midbass = new TestChannel("B", DelayedImpulse(15.0));
        var mid = new TestChannel(
            "C", ImpulseWithEcho(0.0, 0.995, 11.76, 1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log, bands: [(30, 340)]);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        Assert.Contains(
            "seed arrival (same-polarity rival near-tie)",
            TestLog.Line(text, "Pair B/C"));
        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    [Fact]
    public void Compute_SameSignTroughRivalNearTie_IsNotTrustedAsTheSeed()
    {
        // The mirror of the peak-rival case for the now seed-capable trough:
        // two INVERTED copies a full period apart give two near-equal trough
        // lobes, and which one the whitened correlation crowns is decided by
        // which reflection ran slightly hotter — a whole-period cycle skip if
        // seeded. The trough may dominate its window, but the NegativeRival
        // near-tie must send the seed back to the arrival envelope.
        var midbass = new TestChannel("B", DelayedImpulse(15.0));
        var mid = new TestChannel(
            "C", ImpulseWithEcho(0.0, -0.995, 11.76, -1.0));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([midbass, mid], [85], log, bands: [(30, 340)]);

        string text = log.ToString();
        Assert.False(alignment.ContainsKey(midbass));
        string pairLine = TestLog.Line(text, "Pair B/C");
        Assert.Contains("phat trough", pairLine);
        Assert.Contains(
            "seed arrival (same-polarity rival near-tie)", pairLine);
        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel C:"));
    }

    [Fact]
    public void Compute_RejectsInvalidInput()
    {
        var only = new TestChannel("A", DelayedImpulse(0.0));
        var other = new TestChannel("B", DelayedImpulse(0.5));
        var snapshot = new AlignmentSnapshot(
            only, only.InitialIr, BasePosition);
        var otherSnapshot = new AlignmentSnapshot(
            other, other.InitialIr, BasePosition);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [snapshot, otherSnapshot];

        Assert.Throws<ArgumentException>(() => AutoAlignmentEngine.Compute(
            [snapshot],
            [],
            Reprocess,
            new Dictionary<IAlignmentChannel, AlignmentOverride>(),
            new StringBuilder()));
        Assert.Throws<ArgumentException>(() => AutoAlignmentEngine.Compute(
            [snapshot, otherSnapshot],
            [],
            Reprocess,
            new Dictionary<IAlignmentChannel, AlignmentOverride>(),
            new StringBuilder()));
    }

    // A channel whose sample rate differs from the harness default, for the
    // mixed-rate rejection below.
    private sealed class OddRateChannel(string name, Complex[] ir) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => 44_100;
        public int ProcessorSampleRate => SampleRate;
        public Complex[] Ir { get; } = ir;
    }

    [Fact]
    public void Compute_RejectsMixedSampleRates()
    {
        // Every cross-channel figure assumes ONE rate; mixed rates would
        // silently misscale frequencies and delays rather than fail.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var odd = new OddRateChannel("T", DelayedImpulse(0.0));
        var wooferSnapshot = new AlignmentSnapshot(
            woofer, woofer.InitialIr, BasePosition);
        var oddSnapshot = new AlignmentSnapshot(odd, odd.Ir, BasePosition);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [wooferSnapshot, oddSnapshot];

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => AutoAlignmentEngine.Compute(
                [wooferSnapshot, oddSnapshot],
                [new AlignmentJunction(wooferSnapshot, oddSnapshot, 1_000, 500, 2_000)],
                Reprocess,
                new Dictionary<IAlignmentChannel, AlignmentOverride>(),
                new StringBuilder()));
        Assert.Contains("sample rate", error.Message);
    }

    [Fact]
    public void Compute_ClearsAStaleAlignmentMap()
    {
        // The contract promises an ABSOLUTE proposal: stale entries (a repeat
        // call with the same dictionary) must not leak into the neighbor bases.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel("T", DelayedImpulse(0.0));
        var stale = new TestChannel("stale", DelayedImpulse(0.0));
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [stale] = new AlignmentOverride(42.0, true)
        };
        var log = new StringBuilder();

        Run([woofer, tweeter], [1_000], log, alignment: alignment);

        Assert.False(alignment.ContainsKey(stale));
    }

    [Fact]
    public void Compute_SilentJunction_RefusesTheRunInsteadOfFabricatingADelay()
    {
        // The B/C junction has NO evidence at all (both IRs empty). The engine
        // used to fabricate a candidate at the coarse anchor and apply it as a
        // result; a partial skip would be no better (earlier uniform shifts
        // could leave the channel a foreign delay). The whole run must refuse
        // with the reason.
        var woofer = new TestChannel("A", DelayedImpulse(1.0));
        var silentB = new TestChannel("B", new Complex[IrLength]);
        var silentC = new TestChannel("C", new Complex[IrLength]);
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([woofer, silentB, silentC], [200, 1_000], log));

        Assert.Contains("No junction evidence", error.Message);
        Assert.Contains("refusing the run", log.ToString());
    }

    // Deterministic seeded noise: a dead channel in the field is noise, not
    // digital zeros. Its band-limited envelope SNR reads ~8 dB (a flat record
    // has no quiet quarter), comfortably under the 12 dB floor — the arrival
    // detector's own noise reference is what tells noise from signal, which
    // per-bin spectral levels alone cannot.
    private static Complex[] NoiseIr(int seed, double amplitude)
    {
        var random = new Random(seed);
        var ir = new Complex[IrLength];
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] = amplitude * (random.NextDouble() * 2.0 - 1.0);
        }
        return ir;
    }

    [Fact]
    public void Compute_IndependentEqualLevelNoise_RefusesTheRun()
    {
        // Two comparable noise channels pass any per-bin level balance by
        // construction; the loss surface is noise phases and the prior would
        // pick a delay. The arrival-SNR evidence gate must refuse the run.
        var noiseA = new TestChannel("A", NoiseIr(1, 1.0));
        var noiseB = new TestChannel("B", NoiseIr(2, 1.0));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([noiseA, noiseB], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    [Fact]
    public void Compute_ActiveAndLowLevelNoise_RefusesTheRun()
    {
        // A live neighbor plus a channel that is only -40 dB measurement
        // noise: bins exist and the noise even "balances" some of them, but
        // the noise channel's own arrival SNR exposes it.
        var woofer = new TestChannel("A", DelayedImpulse(1.0));
        var noise = new TestChannel("B", NoiseIr(3, 0.01));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([woofer, noise], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    // A continuous shared spectral line: identical in both channels, so no
    // per-bin level test can tell it from a real junction — but a line has no
    // timeable front (its band-limited envelope is FLAT, reading ~-7 dB SNR
    // against the 12 dB floor), which is exactly what the arrival-SNR
    // evidence refusal measures. A single tone cannot resolve a broadband
    // delay (it is ambiguous modulo its own period), so refusing is honest.
    private static Complex[] SharedLineIr(double toneHz, double startMs)
    {
        var ir = new Complex[IrLength];
        int start = BasePosition + (int)Math.Round(startMs / 1000.0 * SampleRate);
        for (int i = 0; start + i < ir.Length; i++)
        {
            ir[start + i] = Math.Sin(Math.Tau * toneHz * i / SampleRate);
        }
        return ir;
    }

    [Fact]
    public void Compute_SharedNarrowLineOnALowJunction_RefusesTheRun()
    {
        var lower = new TestChannel("A", SharedLineIr(120, 1.0));
        var upper = new TestChannel("B", SharedLineIr(120, 1.2));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([lower, upper], [120], log, bands: [(80, 175)]));

        Assert.Contains("No junction evidence", error.Message);
    }

    [Fact]
    public void Compute_ActiveFixedAndSilentVariable_RefusesTheRun()
    {
        // The reviewer's exact scenario: the FIXED neighbor radiates normally,
        // the searched channel is silent. Bins then exist (the fixed side's
        // energy), the loss is flat 0 dB for every delay, and the arrival
        // prior alone used to manufacture a confident candidate at the anchor.
        // The evidence gate must return no candidates and the run must refuse.
        var woofer = new TestChannel("A", DelayedImpulse(1.0));
        var silent = new TestChannel("B", new Complex[IrLength]);
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([woofer, silent], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    [Fact]
    public void Compute_SilentFixedAndActiveVariable_RefusesTheRun()
    {
        // The mirror direction: the reference channel is the silent one.
        var silent = new TestChannel("A", new Complex[IrLength]);
        var tweeter = new TestChannel("B", DelayedImpulse(0.0));
        var log = new StringBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Run([silent, tweeter], [1_000], log));

        Assert.Contains("No junction evidence", error.Message);
    }

    private static TimeAlignmentAnalysisResult Read(
        double arrivalMs, double snrDb = 40, bool valid = true) =>
        default(TimeAlignmentAnalysisResult) with
        {
            FirstArrivalDelayMilliseconds = arrivalMs,
            SignalToNoiseDecibels = snrDb,
            IsValid = valid
        };

    // The single classification behind the cross-side links, the donor
    // certificates and the stereo bridge — table-tested so the three
    // consumers cannot drift apart. (An inline table rather than a Theory:
    // the certificate enum is internal and must not appear in a public test
    // signature.)
    [Fact]
    public void ClassifyArrival_GradesTheHonestyProbe()
    {
        var table = new (double FullMs, double ProbeMs, double ProbeSnrDb,
            bool ProbeValid, AutoAlignmentEngine.ArrivalCertificate Expected)[]
        {
            // agreeing reads certify
            (10.0, 10.4, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Verified),
            // full far LATER than its upper half: the proven modal latch
            (21.2, 13.9, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Latched),
            // full far EARLIER: the probe is blind to the front — usable, uncertified
            (8.0, 20.0, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Unverified),
            // probe below the SNR floor cannot certify
            (10.0, 10.1, 5.0, true, AutoAlignmentEngine.ArrivalCertificate.Unverified),
            // invalid probe cannot certify
            (10.0, 0.0, 40.0, false, AutoAlignmentEngine.ArrivalCertificate.Unverified),
            // exactly at the tolerance edge still certifies
            (12.0, 10.0, 40.0, true, AutoAlignmentEngine.ArrivalCertificate.Verified),
        };

        foreach (var row in table)
        {
            AutoAlignmentEngine.ArrivalCertificate actual =
                AutoAlignmentEngine.ClassifyArrival(
                    Read(row.FullMs),
                    Read(row.ProbeMs, row.ProbeSnrDb, row.ProbeValid),
                    toleranceMs: 2.0);
            Assert.True(row.Expected == actual,
                $"full {row.FullMs}, probe {row.ProbeMs} " +
                $"(SNR {row.ProbeSnrDb}, valid {row.ProbeValid}): " +
                $"expected {row.Expected}, got {actual}");
        }

        // The classifier is self-sufficient: an unmeasurable or near-noise
        // FULL read cannot be certified (or latched) either — no hidden
        // caller-side precondition.
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Unverified,
            AutoAlignmentEngine.ClassifyArrival(
                Read(10.0, valid: false), Read(10.2), toleranceMs: 2.0));
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Unverified,
            AutoAlignmentEngine.ClassifyArrival(
                Read(10.0, snrDb: 5.0), Read(10.2), toleranceMs: 2.0));
    }

    // The physical claim the predictor rests on, across the filter families a
    // real system uses: refiltering the chain-free front through the channel's
    // own chain reproduces where its PROCESSED arrival actually lands. A
    // response holding only a direct front must therefore verify against
    // itself — this is what an analytic band-averaged group delay could not do
    // (it overshoots by 3.8 ms on the steep high-pass below, and by 2.3 ms on
    // the all-pass).
    [Theory]
    [InlineData("LR48 HP 80", 40, 160)]
    [InlineData("BW36 BP 70-200", 100, 400)]
    [InlineData("BW12 LP 200", 100, 400)]
    [InlineData("LR24 LP 2000", 750, 3000)]
    [InlineData("BW48 HP 1700", 750, 3000)]
    [InlineData("AllPass 330", 100, 400)]
    [InlineData("PEQ 120 Q8", 100, 400)]
    public void PredictedArrival_ReproducesTheProcessedFront(
        string chainName, double lowHz, double highHz)
    {
        AlignmentSnapshot snapshot = PredictableSnapshot(
            chainName, UnitImpulse(BasePosition), NamedChain(chainName));

        double measuredMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            snapshot.ImpulseResponse, SampleRate, lowHz, highHz,
            snapshot.ValidRange).FirstArrivalDelayMilliseconds;
        AutoAlignmentEngine.PredictionState state =
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs, lowHz, highHz, out double predictedMs);

        Assert.Equal(AutoAlignmentEngine.PredictionState.Verified, state);
        // Well inside the 2.5 ms allowance, not merely within it: the whole
        // point is that the prediction reproduces the front rather than
        // approximating it (the analytic group delay it replaced missed by
        // 3.8 ms on the steep high-pass here, and by 2.3 ms on the all-pass).
        Assert.True(Math.Abs(measuredMs - predictedMs) < 0.5,
            $"{chainName}: predicted {predictedMs:0.000} ms against a " +
            $"measured {measuredMs:0.000} ms");
    }

    private static DspChannelChain NamedChain(string name) => name switch
    {
        "LR48 HP 80" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, 80, 48))),
        "BW36 BP 70-200" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 70, 36))),
        "BW12 LP 200" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 12))),
        "LR24 LP 2000" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24))),
        "BW48 HP 1700" => new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.Butterworth, 1_700, 48))),
        "AllPass 330" => new DspChannelChain(
            Peq: new EqualizationCurve(
                [new PeqBand(330, 3.5, 0, PeqBandType.AllPassSecondOrder)])),
        "PEQ 120 Q8" => new DspChannelChain(
            Peq: new EqualizationCurve([new PeqBand(120, 8.0, 6.0)])),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // The failure the probe exists for: a steep low-pass leaves the junction
    // band's energy in the room's modal region, the PROCESSED arrival times
    // the mode instead of the front, and the prediction — built from the
    // chain-free front — exposes it. The same channel without the mode must
    // pass, so the conviction is the mode's doing and not the filter's.
    [Fact]
    public void PredictedArrival_ConvictsALateModeAndClearsThePlainFront()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 70, 36)));
        // The BYPASSED response is the driver in the room: a front, and the
        // room's later build-up riding on it. The processed response is that
        // same measurement through the steep chain, which is what pushes the
        // detector onto the mode. Both reads below come from the real
        // detector — nothing is nudged by hand.
        Complex[] front = FrontUnderLateMode(0.0, 12.0, 0.0);
        Complex[] withMode = FrontUnderLateMode(0.0, 12.0, 0.6);

        AlignmentSnapshot clean = PredictableSnapshot("clean", front, chain);
        AlignmentSnapshot latched = PredictableSnapshot("latched", withMode, chain);

        double Read(AlignmentSnapshot side) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, 100, 400, side.ValidRange)
                .FirstArrivalDelayMilliseconds;

        double cleanMs = Read(clean);
        double latchedMs = Read(latched);
        // The fixture only means anything if the mode actually moved the
        // detector: assert that before asserting what the probe makes of it.
        Assert.True(latchedMs - cleanMs > 5.0,
            $"the fixture did not latch: clean {cleanMs:0.000}, " +
            $"with mode {latchedMs:0.000} ms");

        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Verified,
            AutoAlignmentEngine.GradeAgainstPrediction(
                clean, cleanMs, 100, 400, out _));
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Latched,
            AutoAlignmentEngine.GradeAgainstPrediction(
                latched, latchedMs, 100, 400, out _));
    }

    // The sub/woofer field shape at a low junction: the sub's own front plus a
    // late in-cabin build-up BELOW the corner. Its steep low-pass concentrates
    // the junction band's energy exactly there, so the PROCESSED envelope
    // fronts on the build-up while the driver's own (un-crossovered) front is
    // still where it was — the read then sits several ms past what the chain
    // can explain, without reaching the modal-latch conviction bar.
    private static Complex[] LowFrontUnderCabinBuildUp(
        int length, double buildUpMs, double amplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            SingleImpulse(length, BasePosition),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 25, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(buildUpMs / 1000.0 * SampleRate);
        const double AttackSeconds = 0.002;
        const double DecaySeconds = 0.06;
        foreach (double modeHz in new[] { 33.0, 38.0, 44.0 })
        {
            for (int i = start; i < ir.Length; i++)
            {
                double t = (i - start) / (double)SampleRate;
                ir[i] += amplitude *
                    (1 - Math.Exp(-t / AttackSeconds)) *
                    Math.Exp(-t / DecaySeconds) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }

        return ir;
    }

    private static Complex[] SingleImpulse(int length, int position)
    {
        var ir = new Complex[length];
        ir[position] = Complex.One;
        return ir;
    }

    // A midbass whose own front is followed by a strong late in-cabin build-up
    // INSIDE a 150 Hz junction's band: the channel's steep low-pass leaves the
    // band's energy sitting on the build-up, so the PROCESSED envelope fronts
    // on it while the driver's un-crossovered front stays where it was. That
    // is the predicted-arrival probe's conviction shape one junction up from
    // LowFrontUnderCabinBuildUp, whose sub-corner modes fall outside this band.
    private static Complex[] FrontUnderInBandBuildUp(
        int length, double buildUpMs, double amplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            SingleImpulse(length, BasePosition),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 400, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 40, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(buildUpMs / 1000.0 * SampleRate);
        const double AttackSeconds = 0.004;
        const double DecaySeconds = 0.05;
        foreach (double modeHz in new[] { 85.0, 95.0, 108.0 })
        {
            for (int i = start; i < ir.Length; i++)
            {
                double t = (i - start) / (double)SampleRate;
                ir[i] += amplitude *
                    (1 - Math.Exp(-t / AttackSeconds)) *
                    Math.Exp(-t / DecaySeconds) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }

        return ir;
    }

    [Fact]
    public void Compute_NearTiedPeakAndTrough_StillSeedFromTheExtremum()
    {
        // The v5 cabin's 150 Hz junction, where the peak-vs-trough gate used to
        // refuse the seed. Steep corners leave a narrow effective overlap, so
        // the whitened correlation's envelope barely decays over a half period
        // and its peak and trough come within a few hundredths — which says how
        // wide the band is, not whether the extremum can be believed (a PERFECT
        // synthetic junction only reaches 0.167 there). The extremum must seed
        // the search anyway: the half period it leaves ambiguous is the one the
        // fine window spans and the loss search settles by polarity. The field
        // cost of the old refusal: the mid parked a lobe off, at -0.22 dB
        // average junction loss where the extremum's lobe read -0.14 dB and
        // matched the owner's hand tune to 0.02 ms.
        const int Length = 32_768;
        var midbassChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 130, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 60, 36)));
        var midChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1700, 48),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 170, 36)));
        Complex[] midbassBypassed = FrontUnderInBandBuildUp(Length, 9.0, 0.25);
        Complex[] midBypassed = SingleImpulse(
            Length, BasePosition + 4 * SampleRate / 1000);

        AlignmentSnapshot Snapshot(
            string name, Complex[] bypassed, DspChannelChain chain)
        {
            Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                bypassed, chain, SampleRate, SampleRate, out ValidSampleRange range);
            return new AlignmentSnapshot(
                new TestChannel(name, processed), processed,
                VirtualCrossoverAnalysis.FindPeakIndex(processed), range,
                chain, bypassed);
        }

        AlignmentSnapshot midbass = Snapshot("B", midbassBypassed, midbassChain);
        AlignmentSnapshot mid = Snapshot("C", midBypassed, midChain);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot One(
                AlignmentSnapshot side, Complex[] bypassed, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(side.Channel);
                Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                    bypassed,
                    chain with
                    {
                        DelayMs = over.DelayMs,
                        InvertPolarity = over.InvertPolarity
                    },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return side with
                {
                    ImpulseResponse = processed,
                    PeakIndex = VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    ValidRange = range
                };
            }

            return
            [
                One(midbass, midbassBypassed, midbassChain),
                One(mid, midBypassed, midChain)
            ];
        }

        var log = new StringBuilder();
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [midbass, mid],
            [new AlignmentJunction(midbass, mid, 150, 75, 300)],
            Reprocess,
            alignment,
            log);

        string text = log.ToString();
        string pairLine = TestLog.Line(text, "Pair B/C");
        // The fixture must actually reach the state under test: a near-tied
        // extremum (and, as in the field, a pair anchor the prediction had to
        // replace — the case where believing the arrival instead cost most).
        Assert.Contains("modal latch behind the crossover", text);
        Assert.Matches(@"dom 0,0\d\d", pairLine.Replace('.', ','));
        Assert.Contains("seed phat", pairLine);

        // The half period the near-tie leaves open reaches the loss search:
        // both polarities are candidates, which is the whole reason a near-tied
        // extremum is allowed to seed. (Here the partner sits inside the fixed
        // cap; Compute_TrustedSeedAtALowJunction_KeepsBothPolaritiesInTheWindow
        // covers the low junctions where it does not.)
        string channelLine = TestLog.Line(text, "Channel C:");
        Assert.Contains(" inv (", channelLine);
        Assert.Contains("; ", channelLine);
    }

    [Fact]
    public void Compute_KeepsTheConservativePathWhenTheLobeGeometryIsUnmeasured()
    {
        // 55 Hz junction, 36 dB/oct both sides: the lobes sit ~9 ms apart, and
        // the arrival allowance (half a period at the band centre) is exactly
        // that — so a read dragged a lobe late by an in-cabin build-up passes
        // every conviction bar the predictor has. The lobe-boundary conviction
        // catches that class, but only where the boundary is MEASURED; this
        // fixture is the other case, and the assertions below say which.
        const int Length = 32_768;
        var subChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        var wooferChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 180, 36),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 36)));
        Complex[] subBypassed = LowFrontUnderCabinBuildUp(Length, 2.0, 0.02);
        // The woofer fires 23 ms after the sub's front, which only re-centres
        // the correlation window (a pure delay moves a channel's read and its
        // prediction together, so the pair's disagreement is untouched) — far
        // enough from the window edge that the seed's neighbouring lobe is
        // measured rather than edge-pinned.
        Complex[] wooferBypassed = SingleImpulse(
            Length, BasePosition + 23 * SampleRate / 1000);

        AlignmentSnapshot Snapshot(string name, Complex[] bypassed, DspChannelChain chain)
        {
            Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                bypassed, chain, SampleRate, SampleRate, out ValidSampleRange range);
            return new AlignmentSnapshot(
                new TestChannel(name, processed), processed,
                VirtualCrossoverAnalysis.FindPeakIndex(processed), range,
                chain, bypassed);
        }

        AlignmentSnapshot sub = Snapshot("SUB", subBypassed, subChain);
        AlignmentSnapshot woofer = Snapshot("W", wooferBypassed, wooferChain);
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot One(AlignmentSnapshot side, Complex[] bypassed, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(side.Channel);
                Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                    bypassed,
                    chain with { DelayMs = over.DelayMs, InvertPolarity = over.InvertPolarity },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return side with
                {
                    ImpulseResponse = processed,
                    PeakIndex = VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    ValidRange = range
                };
            }

            return [One(sub, subBypassed, subChain), One(woofer, wooferBypassed, wooferChain)];
        }

        var log = new StringBuilder();
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [sub, woofer],
            [new AlignmentJunction(sub, woofer, 55, 27.5, 110)],
            Reprocess,
            alignment,
            log);

        // This pair's whitened correlation shows no INTERIOR lobe beside its
        // seed — the nearest opposite extremum is pinned to the window edge,
        // so its position is an artifact and the lobe spacing the conviction
        // reasons from was never measured. Absence of that evidence may not
        // license re-anchoring (and with it the lifting of the seed-reach
        // veto): the pair keeps the conservative path instead, which is the
        // reach rule refusing the extremum on a zero-floored reach.
        //
        // The conviction's other half — an anchor convicted where the geometry
        // IS measured — is validated on the field cabin the fix came from
        // (55 Hz junction, a 6.82 ms disagreement against a measured 4.13 ms
        // lobe boundary), where the corrected anchor lands the junction on the
        // hand-tuned delay, 5.29 ms against 5.23 by hand, at 0.0 dB average
        // summation loss. A synthetic pair rich enough to show interior lobes
        // AND carry a lobe-sized arrival error has no arithmetically known
        // answer to assert against, so it is not faked here.
        string trace = log.ToString();
        Assert.DoesNotContain("cannot place the junction inside a lobe", trace);
        Assert.True(
            trace.Contains("beyond the arrival's reach"),
            $"the seed should have been refused conservatively:\r\n{trace}");
    }

    // The dead-zone shape (see LatchArbitrationMinR in the engine): one SHORT-
    // tailed mode close behind the front. The long-tailed build-ups above
    // carry the band read whole periods late — past the conviction factor,
    // where the predictor convicts alone. A short tail near the front drags
    // the read late by BETWEEN one and two allowances instead: Inconsistent,
    // which used to withdraw the pair silently.
    private static Complex[] FrontUnderShortMode(
        int length, double modeHz, double modeMs, double amplitude)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            SingleImpulse(length, BasePosition),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 25, 24))),
            SampleRate,
            SampleRate);
        int start = BasePosition + (int)Math.Round(modeMs / 1000.0 * SampleRate);
        const double AttackSeconds = 0.002;
        const double DecaySeconds = 0.012;
        for (int i = start; i < ir.Length; i++)
        {
            double t = (i - start) / (double)SampleRate;
            ir[i] += amplitude *
                (1 - Math.Exp(-t / AttackSeconds)) *
                Math.Exp(-t / DecaySeconds) *
                Math.Sin(2 * Math.PI * modeHz * t);
        }
        return ir;
    }

    private static (string Trace, AlignmentOverride Woofer) RunDeadZonePair(
        AlignmentSnapshot sub, Complex[] subBypassed, DspChannelChain subChain,
        AlignmentSnapshot woofer, Complex[] wooferBypassed,
        DspChannelChain wooferChain)
    {
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            AlignmentSnapshot One(
                AlignmentSnapshot side, Complex[] bypassed, DspChannelChain chain)
            {
                AlignmentOverride over = overrides.GetValueOrDefault(side.Channel);
                Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                    bypassed,
                    chain with
                    {
                        DelayMs = over.DelayMs,
                        InvertPolarity = over.InvertPolarity
                    },
                    SampleRate,
                    SampleRate,
                    out ValidSampleRange range);
                return side with
                {
                    ImpulseResponse = processed,
                    PeakIndex = VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    ValidRange = range
                };
            }

            return
            [
                One(sub, subBypassed, subChain),
                One(woofer, wooferBypassed, wooferChain)
            ];
        }

        var log = new StringBuilder();
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            [sub, woofer],
            [new AlignmentJunction(sub, woofer, 55, 27.5, 110)],
            Reprocess,
            alignment,
            log);
        return (log.ToString(), alignment.GetValueOrDefault(woofer.Channel));
    }

    // The archived Passat v2 defect: the sub's band read latched onto a mode
    // 1.7 allowances past its prediction — inside the conviction dead zone,
    // where the predictor may not convict alone (its own shaping error can
    // reach 1.2 allowances) — and the silently withdrawn pair anchored the
    // junction a period late. The whitened comb is the second witness: the
    // pair's shared content sits with the prediction, so the read is
    // convicted and the junction lands on the true front family.
    [Fact]
    public void Compute_DeadZoneLatch_IsConvictedByTheWhitenedCombArbitration()
    {
        const int Length = 32_768;
        var subChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 48)));
        var wooferChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 48),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 48)));
        Complex[] subBypassed = FrontUnderShortMode(Length, 40.0, 4.0, 0.10);
        Complex[] wooferBypassed = SingleImpulse(Length, BasePosition);
        AlignmentSnapshot sub = PredictableSnapshot("SUB", subBypassed, subChain);
        AlignmentSnapshot woofer = PredictableSnapshot(
            "W", wooferBypassed, wooferChain);

        // The fixture must actually sit in the dead zone: the sub's read
        // LATER than its prediction by one-to-two allowances (Inconsistent —
        // the predictor alone would withdraw the pair), the woofer verified.
        double Read(AlignmentSnapshot side) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, 27.5, 110, side.ValidRange)
                .FirstArrivalDelayMilliseconds;
        double subRead = Read(sub);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                sub, subRead, 27.5, 110, out double subPrediction));
        double allowance = AutoAlignmentEngine.PredictedArrivalAllowanceMs(
            27.5, 110);
        Assert.InRange((subRead - subPrediction) / allowance, 1.0, 2.0);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Verified,
            AutoAlignmentEngine.GradeAgainstPrediction(
                woofer, Read(woofer), 27.5, 110, out _));

        (string trace, AlignmentOverride over) = RunDeadZonePair(
            sub, subBypassed, subChain, woofer, wooferBypassed, wooferChain);

        Assert.Contains("SUB: read sits in the conviction dead zone", trace);
        Assert.Contains("convicted by arbitration", trace);
        Assert.Contains("modal latch behind the crossover", trace);
        Assert.Contains("seed phat", trace);
        // The junction lands on the true front family (the clean-sum optimum
        // sits at ~8 ms, the latched family a lobe later at ~14+ ms inv).
        Assert.False(over.InvertPolarity);
        Assert.InRange(over.DelayMs, 6.0, 10.0);
    }

    // The arbitration's other verdict, and the fleet's common one: when the
    // woofer carries its own late build-up, the comb reads as strongly at the
    // measured family as at the predicted one — the two are indistinguishable,
    // so there is no second witness and the conviction-strength discrepancy
    // may not be acted on. The pair withdraws from the predictor exactly as
    // it did before the arbitration existed.
    [Fact]
    public void Compute_DeadZoneLatch_ArbitrationStandsDownWithoutASecondWitness()
    {
        const int Length = 32_768;
        var subChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 48)));
        var wooferChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 300, 24),
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 55, 24)));
        Complex[] subBypassed = FrontUnderShortMode(Length, 40.0, 4.0, 0.06);
        Complex[] wooferBypassed = FrontUnderShortMode(Length, 70.0, 4.0, 0.35);
        AlignmentSnapshot sub = PredictableSnapshot("SUB", subBypassed, subChain);
        AlignmentSnapshot woofer = PredictableSnapshot(
            "W", wooferBypassed, wooferChain);

        // Same dead zone as the conviction case; the woofer's build-up stays
        // inside its own allowance, so the woofer still verifies.
        double Read(AlignmentSnapshot side) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, 27.5, 110, side.ValidRange)
                .FirstArrivalDelayMilliseconds;
        double subRead = Read(sub);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                sub, subRead, 27.5, 110, out double subPrediction));
        double allowance = AutoAlignmentEngine.PredictedArrivalAllowanceMs(
            27.5, 110);
        Assert.InRange((subRead - subPrediction) / allowance, 1.0, 2.0);
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Verified,
            AutoAlignmentEngine.GradeAgainstPrediction(
                woofer, Read(woofer), 27.5, 110, out _));

        (string trace, _) = RunDeadZonePair(
            sub, subBypassed, subChain, woofer, wooferBypassed, wooferChain);

        Assert.Contains("latch arbitration stood down for SUB/W", trace);
        Assert.DoesNotContain("convicted by arbitration", trace);
        Assert.DoesNotContain("modal latch behind the crossover", trace);
    }

    [Fact]
    public void PredictedArrival_GradesEveryState()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 36)));
        AlignmentSnapshot snapshot = PredictableSnapshot(
            "graded", UnitImpulse(BasePosition), chain);
        double measuredMs = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            snapshot.ImpulseResponse, SampleRate, 100, 400, snapshot.ValidRange)
            .FirstArrivalDelayMilliseconds;

        // Far LATER than the prediction: a latch. The allowance here is
        // 2.5 ms and a conviction needs twice that.
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Latched,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs + 9.0, 100, 400, out _));
        // Only MARGINALLY later: not a conviction. A driver worked below its
        // own passband costs its chain several ms more than the reference
        // impulse the shift is measured on, and the field's false convictions
        // all sat within 1.2 allowances while every true latch cleared 2.5 —
        // so a marginal exceedance is INCONSISTENT, which neither convicts
        // the read nor lets it certify the anchor.
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs + 3.0, 100, 400, out _));
        // Far EARLIER: not a latch, but nothing the prediction can explain —
        // and specifically NOT a confirmation.
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Inconsistent,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot, measuredMs - 9.0, 100, 400, out _));
        // No bypassed response, and no chain: nothing to grade against.
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Unavailable,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot with { BypassedImpulseResponse = null },
                measuredMs, 100, 400, out _));
        Assert.Equal(
            AutoAlignmentEngine.PredictionState.Unavailable,
            AutoAlignmentEngine.GradeAgainstPrediction(
                snapshot with { ProcessingChain = null },
                measuredMs, 100, 400, out _));
    }

    // The upper-half probe's allowance must still credit a chain's own smear
    // — the original defect — but now measured the same way the prediction is.
    [Fact]
    public void ArrivalProbeTolerance_CreditsTheChannelsOwnCrossoverSmear()
    {
        AlignmentSnapshot filtered = PredictableSnapshot(
            "midbass", UnitImpulse(BasePosition), NamedChain("BW36 BP 70-200"));
        var chainless = new AlignmentSnapshot(
            new TestChannel("bare", UnitImpulse(BasePosition)),
            UnitImpulse(BasePosition),
            BasePosition);

        // The credit is only offered to a read the predictor VERIFIES, so
        // both sides are graded against their own measured arrival.
        double MeasuredMs(AlignmentSnapshot side, double lowHz) =>
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ImpulseResponse, SampleRate, lowHz, 400, side.ValidRange)
                .FirstArrivalDelayMilliseconds;
        double bare = AutoAlignmentEngine.ArrivalProbeToleranceMs(
            chainless, MeasuredMs(chainless, 100), MeasuredMs(chainless, 200),
            100, 200, 400);
        double credited = AutoAlignmentEngine.ArrivalProbeToleranceMs(
            filtered, MeasuredMs(filtered, 100), MeasuredMs(filtered, 200),
            100, 200, 400);

        // Without a chain to credit, the generic half period at the probe's
        // lower edge: 200 Hz -> 2.5 ms.
        Assert.Equal(2.5, bare, 6);
        // The field skew (2.88 ms) must sit INSIDE the credited allowance,
        // while a real latch (the v3 cabin's 10.97 ms) stays outside it.
        Assert.True(credited > 2.88,
            $"expected the filter smear to be credited past 2.88 ms; got {credited:0.000}");
        Assert.True(credited < 10.97,
            $"expected a real modal latch to stay convicted; got {credited:0.000}");
    }

    [Fact]
    public void ArrivalProbeTolerance_NeverTightensBelowTheGenericFloor()
    {
        AlignmentSnapshot highPassed = PredictableSnapshot(
            "tweeter", UnitImpulse(BasePosition), NamedChain("BW48 HP 1700"));

        double tolerance = AutoAlignmentEngine.ArrivalProbeToleranceMs(
            highPassed,
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                highPassed.ImpulseResponse, SampleRate, 750, 3_000,
                highPassed.ValidRange).FirstArrivalDelayMilliseconds,
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                highPassed.ImpulseResponse, SampleRate, 1_500, 3_000,
                highPassed.ValidRange).FirstArrivalDelayMilliseconds,
            750, 1_500, 3_000);

        Assert.True(tolerance >= Math.Max(1.0, 500.0 / 1_500),
            $"the generic floor must hold; got {tolerance:0.000}");
    }

    [Fact]
    public void NormalizeAndVerifyFeasibility_LiftsTheFieldAndRefusesAWideSpan()
    {
        var early = new TestChannel("E", DelayedImpulse(0.0));
        var late = new TestChannel("L", DelayedImpulse(1.0));
        var earlySnapshot = new AlignmentSnapshot(early, early.InitialIr, BasePosition);
        var lateSnapshot = new AlignmentSnapshot(late, late.InitialIr, BasePosition);

        // A field of 8..28 normalizes to 0..20 (a uniform trim, relations kept).
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [early] = new AlignmentOverride(8.0, false),
            [late] = new AlignmentOverride(28.0, false)
        };
        AutoAlignmentEngine.NormalizeAndVerifyFeasibility(
            [earlySnapshot, lateSnapshot], alignment, new StringBuilder());
        Assert.Equal(0.0, alignment[early].DelayMs, 2);
        Assert.Equal(20.0, alignment[late].DelayMs, 2);

        // A span wider than the DSP's 50 ms delay range (no car processor
        // reaches that far) cannot be realized by any uniform shift: the
        // proposal must refuse loudly, not clamp silently.
        alignment[early] = new AlignmentOverride(0.0, false);
        alignment[late] = new AlignmentOverride(65.0, false);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => AutoAlignmentEngine.NormalizeAndVerifyFeasibility(
                [earlySnapshot, lateSnapshot], alignment, new StringBuilder()));
        Assert.Contains("does not fit", error.Message);
    }
}
