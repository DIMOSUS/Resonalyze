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

    private static Dictionary<IAlignmentChannel, AlignmentOverride> Run(
        TestChannel[] byBand,
        double[] crossoversHz,
        StringBuilder log)
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
                Math.Max(20, fc / 2),
                Math.Min(20_000, fc * 2)))
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
            log);
        return alignment;
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
        // Stage 1 seeds the search at 1.0 ms, but the search-time optimum is
        // +0.4 ms — just outside the [0.5, 1.5] fine window, so the first pass
        // pins to the window edge (0.1 ms off the optimum, clearly the best
        // in-window score) and the widened retry must find the true optimum.
        // A farther-out optimum recovers through the wide-window promotion
        // instead (next test), because the arrival prior pushes the selection
        // off the edge and the retry never triggers.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.0), reprocessIr: DelayedImpulse(0.6));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [1_000], log);

        Assert.InRange(alignment[tweeter].DelayMs, 0.35, 0.45);
        Assert.Contains("WARNING: fine result at the search edge", log.ToString());
    }

    [Fact]
    public void Compute_OptimumBeyondTheRetryReach_PromotesTheWideWindowPick()
    {
        // A 2 kHz junction: the fine window is ±0.5 ms and the retry reach is
        // ±0.45 ms (1.8 half-periods), while the search-time optimum sits at
        // +2.5 ms — 1.5 ms past the stage-1 seed. Only the ±3 ms diagnostic
        // sweep reaches it, and its clearly better summation must be promoted.
        var woofer = new TestChannel("W", DelayedImpulse(1.0));
        var tweeter = new TestChannel(
            "T", DelayedImpulse(0.0), reprocessIr: DelayedImpulse(-1.5));
        var log = new StringBuilder();

        Dictionary<IAlignmentChannel, AlignmentOverride> alignment =
            Run([woofer, tweeter], [2_000], log);

        Assert.InRange(alignment[tweeter].DelayMs, 2.4, 2.6);
        Assert.Contains("promoted", log.ToString());
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
