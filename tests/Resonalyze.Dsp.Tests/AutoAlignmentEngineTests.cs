using System.Numerics;
using System.Text;

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
        public Complex[] InitialIr { get; }
        public Complex[] ReprocessIr { get; }
    }

    private static Complex[] UnitImpulse(int position)
    {
        var ir = new Complex[IrLength];
        ir[position] = Complex.One;
        return ir;
    }

    private static Complex[] DelayedImpulse(double delayMs, bool invert = false) =>
        VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(BasePosition),
            new DspChannelChain(DelayMs: delayMs, InvertPolarity: invert),
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
        // The clean synthetic junction has razor-sharp fronts, so the onset
        // lock pins the tweeter: the decision reports the Locked kind (the
        // constraint chose, not the acoustics) with no confidence, naming the
        // junction and the constraint in the detail.
        Assert.Equal(AlignmentDecisionKind.Locked, decisions[tweeter].Kind);
        Assert.Null(decisions[tweeter].Confidence);
        Assert.Contains("vs W", decisions[tweeter].Detail);
        Assert.Contains("onset-locked", decisions[tweeter].Detail);
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
        // A 2 kHz junction: the stage-1 seed says 1.0 ms but the search-time
        // optimum is +0.4 ms — 1.2 periods off, beyond the fine window around
        // the seed. Above the lock's frequency gate the broadband onsets of
        // the search-time IRs re-anchor the window on the true front, so the
        // optimum is found directly: no edge retry, no promotion, and the
        // chosen delay lands on the front-aligned lobe.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.0), reprocessIr: DelayedImpulse(0.6));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log);

        Assert.InRange(alignment[tweeter].DelayMs, 0.35, 0.45);
        Assert.Contains("ONSET-LOCKED", log.ToString());
        Assert.Contains("onset gap after", log.ToString());
        Assert.DoesNotContain(
            "WARNING: fine result at the search edge", log.ToString());
        Assert.DoesNotContain("promoted", log.ToString());
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
            "C", ImpulseWithEcho(0.0, 0.97, 11.76, 1.0));
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
            "C", ImpulseWithEcho(0.0, -0.97, 11.76, -1.0));
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

        // A span wider than the DSP's 30 ms delay range (real car processors
        // cap there) cannot be realized by any uniform shift: the proposal
        // must refuse loudly, not clamp silently.
        alignment[early] = new AlignmentOverride(0.0, false);
        alignment[late] = new AlignmentOverride(45.0, false);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => AutoAlignmentEngine.NormalizeAndVerifyFeasibility(
                [earlySnapshot, lateSnapshot], alignment, new StringBuilder()));
        Assert.Contains("does not fit", error.Message);
    }
}
