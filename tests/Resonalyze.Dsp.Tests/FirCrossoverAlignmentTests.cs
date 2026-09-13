using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Auto delay across junctions built from linear-phase FIR crossovers. A symmetric
/// kernel rings BEFORE its peak as much as after it, and it delays its channel by half
/// its length, which Auto delay has to absorb like any other delay in the chain.
/// </summary>
/// <remarks>
/// <para>
/// The drivers are ideal and co-located, so the truth is known: the delay a channel
/// needs is the difference between the kernels' latencies plus whatever physical
/// offset the fixture inserts. Two matched linear-phase branches add nothing else — no
/// phase turn at the corner — so nothing can honestly pull the answer off it.
/// </para>
/// <para>
/// The impulse's PLACEMENT in the record is a parameter on purpose. The band-limited
/// arrival detector read a long kernel's pre-ringing differently depending only on
/// where the content sat (see AutoAlignmentEngine.LinearPhaseKernelOf), so the defect
/// these tests pin showed at 480 samples for some cases and at 2880 for others. Every
/// low-corner long-kernel case below landed 9 to 46 ms off before the engine learned
/// to read such a kernel as its exact delay.
/// </para>
/// </remarks>
public sealed class FirCrossoverAlignmentTests
{
    private const int SampleRate = 48_000;

    private sealed class Channel(string name) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => FirCrossoverAlignmentTests.SampleRate;
        public int ProcessorSampleRate => FirCrossoverAlignmentTests.SampleRate;
    }

    [Theory]
    // Mid/tweeter corners: matched, a physical lead, a longer woofer kernel (the
    // tweeter waits the 128-sample latency difference), and the constructor's longest.
    [InlineData(2_000.0, 255, 255, 0.0, 480)]
    [InlineData(2_000.0, 255, 255, 1.0, 480)]
    [InlineData(2_000.0, 511, 255, 0.0, 480)]
    [InlineData(2_000.0, 16_383, 16_383, 1.0, 480)]
    [InlineData(300.0, 8_191, 8_191, 1.5, 480)]
    // Low corners with long kernels — every one of these landed 9 to 46 ms off.
    [InlineData(150.0, 4_095, 4_095, 1.5, 480)]
    [InlineData(150.0, 2_047, 2_047, -1.5, 2_880)]
    [InlineData(80.0, 4_095, 4_095, 1.5, 480)]
    [InlineData(80.0, 8_191, 8_191, -1.5, 480)]
    [InlineData(80.0, 4_095, 4_095, 0.0, 2_880)]
    [InlineData(40.0, 4_095, 4_095, 0.0, 2_880)]
    // And the short kernel that always held.
    [InlineData(80.0, 1_023, 1_023, -1.5, 480)]
    public void AutoDelay_MeetsLinearPhaseBranches_WhereTheirLatenciesSayItShould(
        double cornerHz, int lowerTaps, int upperTaps, double upperEarlyMs, int basePosition)
    {
        FirCrossoverDesign lowerDesign = Design(CrossoverKind.LowPass, cornerHz, cornerHz, lowerTaps);
        FirCrossoverDesign upperDesign = Design(CrossoverKind.HighPass, cornerHz, cornerHz, upperTaps);
        var lowerChain = new DspChannelChain(Fir: lowerDesign.Build());
        var upperChain = new DspChannelChain(Fir: upperDesign.Build());
        int length = RecordLength(basePosition, Math.Max(lowerTaps, upperTaps));
        int earlySamples = (int)Math.Round(upperEarlyMs * SampleRate / 1_000);

        AlignmentSnapshot lower = Snapshot(
            new Channel("W"), Impulse(length, basePosition + Math.Max(0, earlySamples)), lowerChain);
        AlignmentSnapshot upper = Snapshot(
            new Channel("T"), Impulse(length, basePosition + Math.Max(0, -earlySamples)), upperChain);
        var junction = new AlignmentJunction(lower, upper, cornerHz, cornerHz / 2, cornerHz * 2);

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [Apply(lower, overrides), Apply(upper, overrides)];

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var log = new StringBuilder();
        AutoAlignmentEngine.Compute([lower, upper], [junction], Reprocess, alignment, log);

        // What the upper channel must wait, relative to the lower one: its physical
        // lead plus the latency the lower kernel has over it.
        double expectedMs =
            upperEarlyMs + (lowerDesign.LatencySamples - upperDesign.LatencySamples) * 1_000.0 / SampleRate;
        double relativeMs =
            alignment.GetValueOrDefault(upper.Channel).DelayMs -
            alignment.GetValueOrDefault(lower.Channel).DelayMs;

        Assert.True(
            Math.Abs(relativeMs - expectedMs) <= Tolerance(cornerHz),
            $"expected the upper channel {expectedMs:0.000} ms behind the lower, got {relativeMs:0.000} ms" +
            Environment.NewLine + log);
        Assert.Equal(
            alignment.GetValueOrDefault(lower.Channel).InvertPolarity,
            alignment.GetValueOrDefault(upper.Channel).InvertPolarity);
    }

    [Theory]
    [InlineData(4_095, 480)]
    [InlineData(4_095, 2_880)]
    [InlineData(8_191, 480)]
    public void TheStereoCascade_TimesAFullLinearPhaseSystem_AsItTimesTheSameSystemUnfiltered(
        int taps, int basePosition)
    {
        // A shared sub and woofer, mid and tweeter per side, every channel cut by a
        // matched linear-phase crossover of one length: the kernels add the same
        // latency everywhere and sum flat at every corner, so the proposal has to be
        // the one the same impulses get with no filter at all. Before the fix the sub
        // was proposed 11 to 48 ms off it.
        Dictionary<string, double> unfiltered = RunStereo(taps: 0, basePosition);
        Dictionary<string, double> filtered = RunStereo(taps, basePosition);

        foreach ((string name, double delayMs) in filtered)
        {
            // Relative to the sub: the cascade's final rebase to a zero minimum may
            // pick another channel, which moves every delay together.
            double relative = delayMs - filtered["sub"];
            double reference = unfiltered[name] - unfiltered["sub"];
            Assert.True(
                Math.Abs(relative - reference) <= 0.3,
                $"{name}: {relative:0.000} ms against the sub with FIR crossovers, {reference:0.000} ms without");
        }
    }

    [Fact]
    public void Symmetry_IsWhatMakesAKernelReadAsItsDelay()
    {
        FirFilter designed = Design(CrossoverKind.LowPass, 80, 80, 1_023).Build();
        Assert.True(designed.IsSymmetric);
        Assert.Equal(511, designed.LinearPhaseDelaySamples);

        // Even lengths are symmetric too, half a sample off the grid.
        var even = new FirFilter([0.25, 0.5, 0.5, 0.25]);
        Assert.True(even.IsSymmetric);
        Assert.Equal(1.5, even.LinearPhaseDelaySamples);

        // A minimum-phase-like kernel, an antisymmetric one and silence are not.
        Assert.False(new FirFilter([1.0, 0.5, 0.25]).IsSymmetric);
        Assert.False(new FirFilter([1.0, 0.0, -1.0]).IsSymmetric);
        Assert.False(new FirFilter([0.0, 0.0, 0.0]).IsSymmetric);

        // Rounding is not asymmetry, a real difference is.
        Assert.True(new FirFilter([0.3, 1.0, 0.3 + 1e-13]).IsSymmetric);
        Assert.False(new FirFilter([0.3, 1.0, 0.3 + 1e-6]).IsSymmetric);
    }

    // A tenth and a half of a millisecond at the mid corners, a 48th of a period
    // below: the stage-2 search resolves a 40 Hz junction to a few tenths of a
    // millisecond with or without a FIR stage (0.43 ms measured on these branches),
    // and that is its resolution, not the pre-ringing this file is about.
    private static double Tolerance(double cornerHz) => Math.Max(0.15, 1_000.0 / cornerHz / 48);

    private static int RecordLength(int basePosition, int taps) =>
        DspMath.NextPowerOfTwo(basePosition + 400 + 3 * taps);

    private static Dictionary<string, double> RunStereo(int taps, int basePosition)
    {
        int length = RecordLength(basePosition + 150, Math.Max(taps, 1));
        Complex[] At(double milliseconds) =>
            Impulse(length, basePosition + (int)Math.Round(milliseconds * SampleRate / 1_000));
        DspChannelChain Chain(CrossoverKind kind, double lowPassHz, double highPassHz) =>
            taps == 0
                ? DspChannelChain.Identity
                : new DspChannelChain(Fir: Design(kind, lowPassHz, highPassHz, taps).Build());

        (Channel Channel, Complex[] Ir, DspChannelChain Chain)[] channels =
        [
            (new Channel("sub"), At(2.0), Chain(CrossoverKind.LowPass, 80, 80)),
            (new Channel("L woof"), At(1.0), Chain(CrossoverKind.BandPass, 400, 80)),
            (new Channel("L mid"), At(0.4), Chain(CrossoverKind.BandPass, 2_500, 400)),
            (new Channel("L twr"), At(0.0), Chain(CrossoverKind.HighPass, 2_500, 2_500)),
            (new Channel("R woof"), At(2.5), Chain(CrossoverKind.BandPass, 400, 80)),
            (new Channel("R mid"), At(1.9), Chain(CrossoverKind.BandPass, 2_500, 400)),
            (new Channel("R twr"), At(1.5), Chain(CrossoverKind.HighPass, 2_500, 2_500))
        ];
        List<AlignmentSnapshot> initial = channels
            .Select(item => Snapshot(item.Channel, item.Ir, item.Chain))
            .ToList();

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            initial.Select(snapshot => Apply(snapshot, overrides)).ToList();

        List<AlignmentSnapshot> left = [initial[0], initial[1], initial[2], initial[3]];
        List<AlignmentSnapshot> right = [initial[0], initial[4], initial[5], initial[6]];
        double[] corners = [80, 400, 2_500];
        List<AlignmentJunction> Pairs(List<AlignmentSnapshot> side) =>
            corners
                .Select((cornerHz, index) => new AlignmentJunction(
                    side[index], side[index + 1], cornerHz, cornerHz / 2, cornerHz * 2))
                .ToList();
        var links = new List<StereoPairLink>
        {
            new(initial[1].Channel, initial[4].Channel, 80, 400),
            new(initial[2].Channel, initial[5].Channel, 400, 2_500),
            new(initial[3].Channel, initial[6].Channel, 2_500, 20_000)
        };

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.ComputeStereo(
            new StereoAlignmentPlan(
                left,
                Pairs(left),
                right,
                Pairs(right),
                new HashSet<IAlignmentChannel> { initial[0].Channel },
                initial[3].Channel,
                initial[6].Channel,
                BridgeBandLowHz: 2_500,
                BridgeBandHighHz: 12_000,
                SceneOffsetMs: 0,
                links),
            Reprocess,
            alignment,
            new StringBuilder());
        return channels.ToDictionary(
            item => item.Channel.Name,
            item => alignment.GetValueOrDefault(item.Channel).DelayMs);
    }

    private static FirCrossoverDesign Design(CrossoverKind kind, double lowPassHz, double highPassHz, int taps) =>
        new(
            kind,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, lowPassHz, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, highPassHz, 24),
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            taps,
            SampleRate);

    private static Complex[] Impulse(int length, int position)
    {
        var ir = new Complex[length];
        ir[position] = 1.0;
        return ir;
    }

    private static AlignmentSnapshot Snapshot(Channel channel, Complex[] bypassed, DspChannelChain chain)
    {
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            bypassed, chain, SampleRate, SampleRate, out ValidSampleRange range);
        return new AlignmentSnapshot(
            channel,
            processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            range,
            chain,
            bypassed);
    }

    private static AlignmentSnapshot Apply(
        AlignmentSnapshot snapshot,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
    {
        AlignmentOverride applied = overrides.GetValueOrDefault(snapshot.Channel);
        DspChannelChain chain = snapshot.ProcessingChain!;
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
