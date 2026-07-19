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
        Dictionary<IAlignmentChannel, AlignmentDecision>? decisions = null)
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

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
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
}
