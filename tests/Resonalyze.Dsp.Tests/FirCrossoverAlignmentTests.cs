using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Auto delay across a junction built from linear-phase FIR crossovers — the check the
/// owner asked for before trusting the constructor's kernels in a tune. A symmetric
/// kernel rings BEFORE its peak as much as after it, which is exactly what an arrival
/// reader could mistake for an earlier front, and it delays its channel by half its
/// length, which Auto delay has to absorb like any other delay in the chain.
/// </summary>
/// <remarks>
/// The drivers are ideal and co-located, so the truth is known: the delay a channel
/// needs is the difference between the two kernels' latencies plus whatever physical
/// offset the fixture inserts, and with two matched linear-phase branches nothing else
/// — no phase turn at the corner — can pull the answer off it.
/// </remarks>
public sealed class FirCrossoverAlignmentTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int BasePosition = 480;

    private sealed class Channel(string name) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => FirCrossoverAlignmentTests.SampleRate;
        public int ProcessorSampleRate => FirCrossoverAlignmentTests.SampleRate;
    }

    [Theory]
    // Matched kernels at a mid/tweeter corner: nothing to add.
    [InlineData(2_000.0, 255, 255, 0.0)]
    // The tweeter physically 1 ms early: it waits the 1 ms.
    [InlineData(2_000.0, 255, 255, 1.0)]
    // A longer woofer kernel: the tweeter waits the latency difference, 128 samples.
    [InlineData(2_000.0, 511, 255, 0.0)]
    // The constructor's longest kernel, 167 ms of ringing either side of its peak.
    [InlineData(2_000.0, 15_999, 15_999, 1.0)]
    // At and above 300 Hz the pre-ringing never misleads it, whatever the length.
    [InlineData(300.0, 8_191, 8_191, 1.5)]
    [InlineData(300.0, 4_095, 4_095, -1.5)]
    // Below, only short kernels are safe: 1023 taps, 10.6 ms of latency at 48 kHz.
    // Longer ones were measured landing 9 to 46 ms off at 40, 80 and 150 Hz for some
    // placements of the arrival in the record — see
    // FirCrossoverDesign.MayMisleadAutoDelay, which the constructor warns with.
    [InlineData(150.0, 1_023, 1_023, 1.5)]
    [InlineData(80.0, 1_023, 1_023, -1.5)]
    [InlineData(40.0, 1_023, 1_023, 1.5)]
    public void AutoDelay_MeetsLinearPhaseBranches_WhereTheirLatenciesSayItShould(
        double cornerHz, int lowerTaps, int upperTaps, double upperEarlyMs)
    {
        FirCrossoverDesign lowerDesign = Design(CrossoverKind.LowPass, cornerHz, lowerTaps);
        FirCrossoverDesign upperDesign = Design(CrossoverKind.HighPass, cornerHz, upperTaps);
        var lowerChain = new DspChannelChain(Fir: lowerDesign.Build());
        var upperChain = new DspChannelChain(Fir: upperDesign.Build());
        int earlySamples = (int)Math.Round(upperEarlyMs * SampleRate / 1_000);

        AlignmentSnapshot lower = Snapshot("W", Impulse(BasePosition + earlySamples), lowerChain);
        AlignmentSnapshot upper = Snapshot("T", Impulse(BasePosition), upperChain);
        var junction = new AlignmentJunction(lower, upper, cornerHz, cornerHz / 2, cornerHz * 2);

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [Apply(lower, lowerChain, overrides), Apply(upper, upperChain, overrides)];

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var log = new StringBuilder();
        AutoAlignmentEngine.Compute([lower, upper], [junction], Reprocess, alignment, log);

        // What the upper channel must wait, relative to the lower one: its physical
        // lead plus the latency the lower kernel has over it. A negative figure means
        // the LOWER channel waits instead.
        double expectedMs =
            upperEarlyMs + (lowerDesign.LatencySamples - upperDesign.LatencySamples) * 1_000.0 / SampleRate;
        double upperMs = alignment.GetValueOrDefault(upper.Channel).DelayMs;
        double lowerMs = alignment.GetValueOrDefault(lower.Channel).DelayMs;
        double relativeMs = upperMs - lowerMs;

        Assert.True(
            Math.Abs(relativeMs - expectedMs) <= 0.15,
            $"expected the upper channel {expectedMs:0.000} ms behind the lower, got {relativeMs:0.000} ms" +
            Environment.NewLine + log);
        Assert.Equal(
            alignment.GetValueOrDefault(lower.Channel).InvertPolarity,
            alignment.GetValueOrDefault(upper.Channel).InvertPolarity);
        // Every case here is one the constructor does not warn about.
        Assert.False(lowerDesign.MayMisleadAutoDelay);
        Assert.False(upperDesign.MayMisleadAutoDelay);
    }

    [Theory]
    [InlineData(80.0, 1_023, false)]
    [InlineData(80.0, 2_047, true)]
    [InlineData(299.0, 2_047, true)]
    [InlineData(300.0, 15_999, false)]
    public void TheWarning_CoversTheLowCornersAndLengthsThatWereMeasuredToMislead(
        double cornerHz, int taps, bool warns)
    {
        Assert.Equal(warns, Design(CrossoverKind.HighPass, cornerHz, taps).MayMisleadAutoDelay);
        // A band-pass is judged by its lower corner.
        var bandPass = new FirCrossoverDesign(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 5_000, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, cornerHz, 24),
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            taps,
            SampleRate);
        Assert.Equal(warns, bandPass.MayMisleadAutoDelay);
    }

    private static FirCrossoverDesign Design(CrossoverKind kind, double cornerHz, int taps)
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, cornerHz, 24);
        return new FirCrossoverDesign(
            kind, edge, edge, FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, 8, taps, SampleRate);
    }

    private static Complex[] Impulse(int position)
    {
        var ir = new Complex[IrLength];
        ir[position] = 1.0;
        return ir;
    }

    private static AlignmentSnapshot Snapshot(string name, Complex[] bypassed, DspChannelChain chain)
    {
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            bypassed, chain, SampleRate, SampleRate, out ValidSampleRange range);
        return new AlignmentSnapshot(
            new Channel(name),
            processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            range,
            chain,
            bypassed);
    }

    private static AlignmentSnapshot Apply(
        AlignmentSnapshot snapshot,
        DspChannelChain chain,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
    {
        AlignmentOverride applied = overrides.GetValueOrDefault(snapshot.Channel);
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            snapshot.BypassedImpulseResponse!,
            chain with { DelayMs = applied.DelayMs, InvertPolarity = applied.InvertPolarity },
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
}
