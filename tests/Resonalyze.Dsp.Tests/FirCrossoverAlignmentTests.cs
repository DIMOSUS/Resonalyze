using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>Auto delay across linear-phase FIR crossovers: a symmetric kernel pre-rings and delays its channel by half its length.</summary>
/// <remarks>Ideal co-located drivers, so the truth is the latency difference plus the fixture's offset. Placement is a
/// parameter: the arrival read of long kernels depended on it (see AutoAlignmentEngine.LinearPhaseKernelOf).</remarks>
public sealed class FirCrossoverAlignmentTests
{
    private const int SampleRate = 48_000;

    private sealed class Channel(
        string name,
        int sampleRate = FirCrossoverAlignmentTests.SampleRate,
        int processorSampleRate = FirCrossoverAlignmentTests.SampleRate) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate { get; } = sampleRate;
        public int ProcessorSampleRate { get; } = processorSampleRate;
    }

    [Theory]
    [InlineData(2_000.0, 255, 255, 0.0, 480)]
    [InlineData(2_000.0, 255, 255, 1.0, 480)]
    [InlineData(2_000.0, 511, 255, 0.0, 480)]
    [InlineData(2_000.0, 16_383, 16_383, 1.0, 480)]
    [InlineData(300.0, 8_191, 8_191, 1.5, 480)]
    // Low corners with long kernels: these once landed 9 to 46 ms off.
    [InlineData(150.0, 4_095, 4_095, 1.5, 480)]
    [InlineData(150.0, 2_047, 2_047, -1.5, 2_880)]
    [InlineData(80.0, 4_095, 4_095, 1.5, 480)]
    [InlineData(80.0, 8_191, 8_191, -1.5, 480)]
    [InlineData(80.0, 4_095, 4_095, 0.0, 2_880)]
    [InlineData(40.0, 4_095, 4_095, 0.0, 2_880)]
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
        // Matched kernels everywhere sum flat, so the proposal must equal the unfiltered one.
        Dictionary<string, double> unfiltered = RunStereo(taps: 0, basePosition);
        Dictionary<string, double> filtered = RunStereo(taps, basePosition);

        foreach ((string name, double delayMs) in filtered)
        {
            // Relative to the sub: the final zero-minimum rebase moves every delay together.
            double relative = delayMs - filtered["sub"];
            double reference = unfiltered[name] - unfiltered["sub"];
            Assert.True(
                Math.Abs(relative - reference) <= 0.3,
                $"{name}: {relative:0.000} ms against the sub with FIR crossovers, {reference:0.000} ms without");
        }
    }

    [Theory]
    [InlineData(2_000.0, 1_023, 1.0, 480)]
    [InlineData(80.0, 4_095, 1.5, 480)]
    [InlineData(80.0, 4_095, -1.5, 2_880)]
    public void AMeasurementAtAnotherRate_StillMeetsTheBranchesWhereTheirLatenciesSay(
        double cornerHz, int taps, double upperEarlyMs, int basePosition)
    {
        // Kernel delay counts at the processor rate; a mix-up would miss by latency × (48/44.1 − 1) (3.9 ms at 4095 taps).
        const int Measurement = 44_100;
        const int Processor = 48_000;
        FirCrossoverDesign lowDesign = Design(CrossoverKind.LowPass, cornerHz, cornerHz, taps) with { SampleRateHz = Processor };
        FirCrossoverDesign highDesign = Design(CrossoverKind.HighPass, cornerHz, cornerHz, taps) with { SampleRateHz = Processor };
        var lowChain = new DspChannelChain(Fir: lowDesign.Build());
        var highChain = new DspChannelChain(Fir: highDesign.Build());
        int length = RecordLength(basePosition, taps);
        int earlySamples = (int)Math.Round(upperEarlyMs * Measurement / 1_000);

        AlignmentSnapshot lower = Snapshot(
            new Channel("W", Measurement, Processor),
            Impulse(length, basePosition + Math.Max(0, earlySamples)),
            lowChain);
        AlignmentSnapshot upper = Snapshot(
            new Channel("T", Measurement, Processor),
            Impulse(length, basePosition + Math.Max(0, -earlySamples)),
            highChain);

        double relativeMs = RelativeDelayMs(lower, upper, cornerHz);

        double expectedMs = earlySamples * 1_000.0 / Measurement;
        Assert.True(
            Math.Abs(relativeMs - expectedMs) <= Tolerance(cornerHz),
            $"expected {expectedMs:0.000} ms, got {relativeMs:0.000} ms");
    }

    [Theory]
    [InlineData(2_000.0, 2_047)]
    [InlineData(150.0, 4_095)]
    public void AnImportedSymmetricCorrection_OverAnIirCrossover_AddsExactlyItsDelay(
        double cornerHz, int taps)
    {
        // Any symmetric kernel (here a zero-phase bump) is only a delay to the arrival read, not a shape to follow.
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, cornerHz, 24);
        var lowChain = new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge));
        var highChain = new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge));
        FirFilter correction = BumpCorrection(cornerHz, taps);
        Assert.True(correction.IsSymmetric);
        Assert.True(correction.Response(cornerHz * 1.4, SampleRate).Magnitude > 1.6);

        double RunWith(DspChannelChain upperChain)
        {
            int length = RecordLength(480, taps);
            AlignmentSnapshot lower = Snapshot(new Channel("W"), Impulse(length, 480), lowChain);
            AlignmentSnapshot upper = Snapshot(new Channel("T"), Impulse(length, 480), upperChain);
            return RelativeDelayMs(lower, upper, cornerHz);
        }

        double without = RunWith(highChain);
        double with = RunWith(highChain with { Fir = correction });

        double latencyMs = correction.LinearPhaseDelaySamples * 1_000.0 / SampleRate;
        Assert.True(
            Math.Abs(with - (without - latencyMs)) <= Tolerance(cornerHz),
            $"without the correction {without:0.000} ms, with it {with:0.000} ms; expected {without - latencyMs:0.000} ms");
    }

    [Theory]
    [InlineData(1_023, 4.0, true)]
    [InlineData(1_023, 4.0, false)]
    [InlineData(2_047, -4.0, false)]
    public void ASymmetricCorrectionOverADispersiveDriver_IsTimedTheSameWhereverTheDriverSitsInTheRecord(
        int taps, double lagMs, bool attenuateLow)
    {
        // Dispersive driver behind a zero-phase tilt: the seed only places the window; the answer comes from the processed sum.
        const double Corner = 120;
        double near = DispersiveJunctionRelativeMs(480, taps, lagMs, attenuateLow);
        double far = DispersiveJunctionRelativeMs(2_880, taps, lagMs, attenuateLow);

        Assert.True(
            Math.Abs(near - far) <= Tolerance(Corner),
            $"{near:0.000} ms with the driver 10 ms into the record, {far:0.000} ms at 60 ms");

        double flipped = DispersiveJunctionRelativeMs(480, taps, lagMs, !attenuateLow);
        Assert.True(
            Math.Abs(near - flipped) <= 1_000.0 / Corner / 24,
            $"{near:0.000} ms with the tilt one way, {flipped:0.000} ms the other");
    }

    private static double DispersiveJunctionRelativeMs(
        int basePosition, int taps, double lagMs, bool attenuateLow)
    {
        const double Corner = 120;
        int length = DspMath.NextPowerOfTwo(basePosition + 2_000 + 3 * taps);
        int start = basePosition + 400;
        int lag = (int)Math.Round(lagMs * SampleRate / 1_000);
        var split = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 115, 24);
        Complex[] lowHalf = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(length, start + Math.Max(0, -lag)),
            new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: split)),
            SampleRate,
            SampleRate);
        Complex[] highHalf = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(length, start + Math.Max(0, lag)),
            new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: split)),
            SampleRate,
            SampleRate);
        var driver = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            driver[i] = lowHalf[i] + highHalf[i];
        }

        // 90 % Butterworth-12 tilt plus 10 % through: one side ~20 dB below the other.
        var tiltEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, Corner, 12);
        FirFilter tilt = new FirCrossoverDesign(
            attenuateLow ? CrossoverKind.HighPass : CrossoverKind.LowPass, tiltEdge, tiltEdge,
            FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, 8, taps, SampleRate).Build();
        var correction = new double[taps];
        for (int i = 0; i < taps; i++)
        {
            correction[i] = 0.9 * tilt.Taps[i];
        }

        correction[(taps - 1) / 2] += 0.1;
        var kernel = new FirFilter(correction, SampleRate);

        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, Corner, 24);
        AlignmentSnapshot lower = Snapshot(
            new Channel("W"),
            Impulse(length, start),
            new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge)));
        AlignmentSnapshot upper = Snapshot(
            new Channel("T"),
            driver,
            new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge), Fir: kernel));
        return RelativeDelayMs(lower, upper, Corner);
    }

    private static FirFilter BumpCorrection(double cornerHz, int taps)
    {
        FirFilter band = Design(CrossoverKind.BandPass, 2 * cornerHz, cornerHz, taps) with
        {
            Method = FirCrossoverMethod.WindowedSinc,
            Window = FirWindow.Blackman
        } is { } design
            ? design.Build()
            : throw new InvalidOperationException();
        var kernel = new double[taps];
        for (int i = 0; i < taps; i++)
        {
            kernel[i] = 0.8 * band.Taps[i];
        }

        kernel[(taps - 1) / 2] += 1.0;
        return new FirFilter(kernel, SampleRate);
    }

    private static double RelativeDelayMs(AlignmentSnapshot lower, AlignmentSnapshot upper, double cornerHz)
    {
        var junction = new AlignmentJunction(lower, upper, cornerHz, cornerHz / 2, cornerHz * 2);

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [Apply(lower, overrides), Apply(upper, overrides)];

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute([lower, upper], [junction], Reprocess, alignment, new StringBuilder());
        return alignment.GetValueOrDefault(upper.Channel).DelayMs -
            alignment.GetValueOrDefault(lower.Channel).DelayMs;
    }

    [Fact]
    public void Symmetry_IsWhatMakesAKernelReadAsItsDelay()
    {
        FirFilter designed = Design(CrossoverKind.LowPass, 80, 80, 1_023).Build();
        Assert.True(designed.IsSymmetric);
        Assert.Equal(511, designed.LinearPhaseDelaySamples);

        var even = new FirFilter([0.25, 0.5, 0.5, 0.25]);
        Assert.True(even.IsSymmetric);
        Assert.Equal(1.5, even.LinearPhaseDelaySamples);

        Assert.False(new FirFilter([1.0, 0.5, 0.25]).IsSymmetric);
        Assert.False(new FirFilter([1.0, 0.0, -1.0]).IsSymmetric);
        Assert.False(new FirFilter([0.0, 0.0, 0.0]).IsSymmetric);

        Assert.True(new FirFilter([0.3, 1.0, 0.3 + 1e-13]).IsSymmetric);
        Assert.False(new FirFilter([0.3, 1.0, 0.3 + 1e-6]).IsSymmetric);
    }

    [Theory]
    [InlineData(80.0, 4_095)]
    [InlineData(80.0, 8_191)]
    [InlineData(150.0, 2_047)]
    public void TheJunctionSurface_ThroughEachChannelsOwnWindow_ReadsTheWholePreRing(double cornerHz, int taps)
    {
        // Field case (v6 FIR session): windows opened a fade before a linear-phase channel's front cut its pre-ring
        // and moved the score optimum 11 ms. With the pre-ring kept, the surface is the one an early shared window reads.
        int length = RecordLength(2_880, taps);
        Complex[] Driver(CrossoverKind kind, double cornerOfDriverHz) =>
            VirtualCrossoverAnalysis.ApplyChain(
                Impulse(length, 2_880),
                new DspChannelChain(Crossover: kind == CrossoverKind.LowPass
                    ? new CrossoverSpec(kind, LowPassEdge: new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, cornerOfDriverHz, 12))
                    : new CrossoverSpec(kind, HighPassEdge: new CrossoverEdge(
                        CrossoverFilterFamily.Butterworth, cornerOfDriverHz, 12))),
                SampleRate,
                SampleRate)[..length];
        Complex[] lower = VirtualCrossoverAnalysis.ApplyChain(
            Driver(CrossoverKind.LowPass, 2.5 * cornerHz),
            new DspChannelChain(Fir: Design(CrossoverKind.LowPass, cornerHz, cornerHz, taps).Build()),
            SampleRate, SampleRate, out ValidSampleRange lowerRange);
        Complex[] upper = VirtualCrossoverAnalysis.ApplyChain(
            Driver(CrossoverKind.HighPass, 0.7 * cornerHz),
            new DspChannelChain(
                DelayMs: 1.0, Fir: Design(CrossoverKind.HighPass, cornerHz, cornerHz, taps).Build()),
            SampleRate, SampleRate, out ValidSampleRange upperRange);
        Assert.True(lowerRange.LeadSamples > taps / 3 && upperRange.LeadSamples > taps / 3);

        double periodMs = 1_000.0 / cornerHz;
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> Sweep(int? anchor) =>
            VirtualCrossoverAnalysis.JunctionLossSweep(
                upper, lower, SampleRate, cornerHz / 2, cornerHz * 2,
                -1.5 * periodMs, 1.5 * periodMs, periodMs / 40, invertVariable: false,
                gateAnchorSample: anchor, levelMatch: true,
                variableValidRange: upperRange, fixedValidRange: lowerRange);
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> own = Sweep(null);
        int earliestPeak = Math.Min(
            VirtualCrossoverAnalysis.FindPeakIndex(lower), VirtualCrossoverAnalysis.FindPeakIndex(upper));
        // Opened a period ahead of the kernels' pre-ring; the band-sized window still reaches well past both peaks here.
        List<VirtualCrossoverAnalysis.JunctionSweepPoint> shared = Sweep(
            earliestPeak - taps / 2 - (int)(SampleRate / cornerHz));

        double worst = own.Zip(shared, (a, b) => Math.Abs(a.LossDb - b.LossDb)).Max();
        double ownBest = own.MaxBy(point => point.LossDb)!.DelayMs;
        double sharedBest = shared.MaxBy(point => point.LossDb)!.DelayMs;
        Assert.True(
            worst <= 0.1 && Math.Abs(ownBest - sharedBest) <= periodMs / 20,
            $"own windows best {ownBest:0.00} ms, shared early window {sharedBest:0.00} ms; " +
            $"surfaces differ by up to {worst:0.00} dB");
    }

    // Stage-2 resolution: a few tenths of a ms at 40 Hz (0.43 ms measured), not pre-ringing.
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
            bypassed, chain, channel.SampleRate, channel.ProcessorSampleRate, out ValidSampleRange range);
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
            snapshot.Channel.SampleRate,
            snapshot.Channel.ProcessorSampleRate,
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
