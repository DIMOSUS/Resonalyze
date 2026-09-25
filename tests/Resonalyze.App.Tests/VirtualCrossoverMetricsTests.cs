using System.Collections.Concurrent;
using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverMetricsTests
{
    private static readonly AnalysisCurve EmptyCurve = new("x", []);
    private static readonly GatedMagnitude EmptyMagnitude = new(EmptyCurve, EmptyCurve);

    private static Complex[] Impulse(int peak = 10)
    {
        var ir = new Complex[64];
        ir[peak] = Complex.One;
        return ir;
    }

    private static ProcessedChannel Processed(string name, Complex[] ir, int peak, int rate)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = rate };
        return new ProcessedChannel(channel, ir, peak, rate, OxyColors.White);
    }

    private static VirtualCrossoverChannel ResolvedChannel(string name, int rate)
    {
        var channel = new VirtualCrossoverChannel(name);
        VirtualCrossoverChannelState left = channel.PhysicalSideState(false);
        left.TransferImpulseResponse = Impulse();
        left.SampleRate = rate;
        return channel;
    }

    private static Complex[] FlatSpectrum(double gain, double phaseRadians, int length = 4_096)
    {
        var spectrum = new Complex[length];
        Complex value = Complex.FromPolarCoordinates(gain, phaseRadians);
        for (int bin = 1; bin < length / 2; bin++)
        {
            spectrum[bin] = value;
            spectrum[length - bin] = Complex.Conjugate(value);
        }

        return spectrum;
    }

    private static IEnumerable<SignalPoint> InBand(IReadOnlyList<SignalPoint> curve) =>
        curve.Where(point => point.X is >= 100 and <= 10_000);

    [Fact]
    public void BuildDirectLossCurve_ReadsTheLossOutOfThePrebuiltSpectra()
    {
        // Half level inverted: |0.5| vs magnitude sum 1.5 = -9.54 dB everywhere.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        ProcessedChannel a = Processed("A", Impulse(), 10, 48_000);
        ProcessedChannel b = Processed("B", Impulse(), 10, 48_000);

        List<SignalPoint>? coherent = metrics.BuildDirectLossCurve(
            [a, b], [FlatSpectrum(1.0, 0.3), FlatSpectrum(1.0, 0.3)], smoothingInverseOctaves: 0);
        List<SignalPoint>? cancelling = metrics.BuildDirectLossCurve(
            [a, b], [FlatSpectrum(1.0, 0.0), FlatSpectrum(0.5, Math.PI)], smoothingInverseOctaves: 0);

        Assert.NotNull(coherent);
        Assert.NotNull(cancelling);
        Assert.NotEmpty(InBand(coherent!));
        Assert.All(InBand(coherent!), point => Assert.Equal(0.0, point.Y, 1e-6));
        Assert.All(InBand(cancelling!), point =>
            Assert.Equal(20 * Math.Log10(0.5 / 1.5), point.Y, 1e-6));
    }

    [Fact]
    public void BuildDirectLossCurve_HasNoMetric_ForOneChannelOrMixedRates()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        ProcessedChannel a = Processed("A", Impulse(), 10, 48_000);
        ProcessedChannel b = Processed("B", Impulse(), 10, 96_000);

        Assert.Null(metrics.BuildDirectLossCurve([a], [FlatSpectrum(1.0, 0.0)], 0));
        Assert.Null(metrics.BuildDirectLossCurve(
            [a, b], [FlatSpectrum(1.0, 0.0), FlatSpectrum(1.0, 0.0)], 0));
    }

    [Fact]
    public void BuildCurves_ReadsTheSnapshotRate_NotTheLiveChannel()
    {
        // Session import can zero the live channel's rate mid-rebuild (a real crash); the render must read a snapshot.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var seenRates = new ConcurrentBag<int>();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (_, _, sampleRate, _, _) =>
            {
                seenRates.Add(sampleRate);
                return EmptyMagnitude;
            });
        ProcessedChannel first = Processed("A", Impulse(), 10, 48_000);
        ProcessedChannel second = Processed("B", Impulse(), 10, 48_000);
        first.Channel.SampleRate = 0;
        second.Channel.SampleRate = 0;

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum, _) =
            metrics.BuildCurves([first, second], 0);

        Assert.NotNull(magnitudes);
        Assert.NotNull(sum);
        Assert.NotEmpty(seenRates);
        Assert.All(seenRates, rate => Assert.Equal(48_000, rate));
    }

    [Fact]
    public void BuildCurves_StillDrawsTheChannel_WithNoMetricToGoWithIt()
    {
        // One channel has no loss but still has a curve: withholding it silently disabled the hybrid view downstream.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum, List<SignalPoint>? loss) =
            metrics.BuildCurves([Processed("A", Impulse(), 5, 48_000)], 0);

        Assert.NotNull(magnitudes);
        Assert.Single(magnitudes!);
        Assert.Null(sum);
        Assert.Null(loss);
    }

    [Fact]
    public void BuildCurves_HasNothingToDrawForAnEmptySet()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum, List<SignalPoint>? loss) =
            metrics.BuildCurves([], 0);

        Assert.Null(magnitudes);
        Assert.Null(sum);
        Assert.Null(loss);
    }

    [Fact]
    public void BuildCurves_AnchorsEveryCurveToTheEarliestFront()
    {
        // Peak later and louder than the front: the shared window must open at the front.
        var early = new Complex[1_024];
        early[300] = 1.0;
        early[400] = 2.0;
        var late = new Complex[1_024];
        late[600] = 1.0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var captured = new ConcurrentBag<(Complex[] Ir, int Peak, int Rate)>();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (ir, peak, rate, _, _) =>
            {
                captured.Add((ir, peak, rate));
                return EmptyMagnitude;
            });

        metrics.BuildCurves(
        [
            Processed("A", early, peak: 400, rate: 48_000),
            Processed("B", late, peak: 600, rate: 48_000)
        ],
        0);

        Assert.All(captured, entry => Assert.InRange(entry.Peak, 250, 320));
    }

    [Fact]
    public void BuildCurves_AnchorsEveryCurveToOneSampleAndSumsTheResponses()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var captured = new ConcurrentBag<(Complex[] Ir, int Peak, int Rate)>();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (ir, peak, rate, _, _) =>
            {
                captured.Add((ir, peak, rate));
                return EmptyMagnitude;
            });
        Complex[] a = Impulse(12);
        Complex[] b = Impulse(20);

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum, _) = metrics.BuildCurves(
        [
            Processed("A", a, peak: 5, rate: 48_000),
            Processed("B", b, peak: 2, rate: 48_000)
        ],
        0);

        Assert.NotNull(magnitudes);
        Assert.Equal(2, magnitudes.Count);
        Assert.NotNull(sum);
        // 64-sample records are too short for the front estimator, so the anchor falls back to the earliest peak.
        Assert.Equal(3, captured.Count);
        Assert.All(captured, entry => Assert.Equal(2, entry.Peak));
        Complex[] expectedSum = VirtualCrossoverAnalysis.SumImpulseResponses([a, b]);
        Assert.Contains(captured, entry => entry.Ir.SequenceEqual(expectedSum));
    }

    [Fact]
    public void BuildEntries_IsEmptyWhenThereIsNoMetric()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

        Assert.Empty(metrics.BuildEntries(
            [Processed("A", Impulse(), 5, 48_000)], lossCurve: null));
    }

    [Fact]
    public void JunctionSpectra_KeepEachCurveOnItsOwnWindowAndStillCarryTheDelay()
    {
        // Per-curve placement is a different time reference per channel; spectra must be rotated back to the record origin or placement reads as delay.
        List<ProcessedChannel> ordered =
        [
            ProcessedThroughChain("A", CrossoverKind.LowPass, 200),
            ProcessedThroughChain("B", CrossoverKind.HighPass, 200, delayMs: 2.0)
        ];

        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            PlacementChannel.From(ordered),
            PhaseGatePlacement.EarliestStartMs(
                PlacementChannel.From(ordered), 48_000),
            48_000,
            pinnedOffsetMs: null,
            leftMs: 0.5,
            plateauMs: 4.0,
            rightMs: 1.5);
        Assert.True(
            offsets[1] - offsets[0] > 1.0,
            $"the placements have to differ: {offsets[0]} and {offsets[1]} ms");

        JunctionPhaseResult? result = JunctionPhaseAlignment.AnalyzeWindowedSpectra(
            JunctionSpectra(ordered)[0],
            JunctionSpectra(ordered)[1],
            48_000,
            crossoverHz: 200,
            bandLowHz: 100,
            bandHighHz: 400);

        Assert.NotNull(result);
        Assert.InRange(result!.BestExtraDelayMs, 1.9, 2.1);
    }

    private static ProcessedChannel ProcessedThroughChain(
        string name,
        CrossoverKind kind,
        double crossoverHz,
        double delayMs = 0)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = 48_000 };
        channel.Settings.CrossoverKind = kind;
        var edge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley, crossoverHz, 24);
        if (kind == CrossoverKind.LowPass)
        {
            channel.Settings.LowPassEdge = edge;
        }
        else
        {
            channel.Settings.HighPassEdge = edge;
        }
        channel.Settings.DelayMs = delayMs;

        var impulse = new Complex[8_192];
        impulse[480] = Complex.One;
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            impulse, channel.Settings.ToChain(channel.Pair.Zone), 48_000, 48_000);
        return new ProcessedChannel(
            channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir), 48_000,
            OxyColors.White);
    }

    private static IReadOnlyList<Complex[]> JunctionSpectra(
        IReadOnlyList<ProcessedChannel> ordered) =>
        JunctionPhaseSpectra.Build(
            ordered,
            ordered[0].SampleRate,
            pinnedOffsetMs: null,
            leftMs: 0.5,
            plateauMs: 4.0,
            rightMs: 1.5);

    [Fact]
    public void BuildPhaseEntries_IsEmptyForFewerThanTwoChannels()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

        Assert.Empty(metrics.BuildPhaseEntries(
            [ProcessedThroughChain("A", CrossoverKind.LowPass, 200)],
            JunctionSpectra));
    }

    [Fact]
    public void BuildPhaseEntries_ReadsTheJunctionAndRecoversAMisalignment()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

        // Passed upper-first on purpose: entries must order by band, not argument order.
        List<VirtualCrossoverMetric.PhaseEntry> entries = metrics.BuildPhaseEntries(
        [
            ProcessedThroughChain("B", CrossoverKind.HighPass, 200, delayMs: 2.0),
            ProcessedThroughChain("A", CrossoverKind.LowPass, 200)
        ],
            JunctionSpectra);

        VirtualCrossoverMetric.PhaseEntry entry = Assert.Single(entries);
        Assert.Equal("A/B", entry.Junction);
        Assert.Equal("A", entry.LowerChannel);
        Assert.Equal(200, entry.CrossoverHz);
        Assert.Equal(100, entry.LowHz);
        Assert.Equal(400, entry.HighHz);
        Assert.InRange(entry.Result.BestExtraDelayMs, 1.9, 2.1);
        // The default 0.5/4/1.5 ms gate holds barely one period of 200 Hz, so skirts cost a few hundredths.
        Assert.InRange(entry.Result.BestScore, 0.90, 1.0);
    }

    [Fact]
    public async Task ComputeSideSumAsync_SumsTheParticipatingSides()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();

        VirtualCrossoverSideSum? side = await metrics.ComputeSideSumAsync(
            [ResolvedChannel("A", 48_000), ResolvedChannel("B", 48_000)],
            rightSide: false,
            revision,
            minimumChannels: 2);

        Assert.NotNull(side);
        Assert.Equal(2, side.ChannelCount);
        Assert.NotEmpty(side.ImpulseResponse);
    }

    [Fact]
    public async Task ComputeSideSumAsync_HandsBackThePartsThatWentIntoTheSum()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        VirtualCrossoverChannel a = ResolvedChannel("A", 48_000);
        VirtualCrossoverChannel b = ResolvedChannel("B", 48_000);
        VirtualCrossoverChannel silent = new("C");
        long revision = coordinator.Invalidate();

        VirtualCrossoverSideSum? side = await metrics.ComputeSideSumAsync(
            [a, silent, b], rightSide: false, revision, minimumChannels: 2);

        Assert.NotNull(side);
        Assert.Equal([a, b], side.Channels.Select(item => item.Channel));
        Assert.Equal(side.Channels.Count, side.ChannelCount);
        foreach (ProcessedChannel item in side.Channels)
        {
            Assert.Equal(48_000, item.SampleRate);
            Assert.Equal(side.ImpulseResponse.Length, item.ImpulseResponse.Length);
        }

        for (int i = 0; i < side.ImpulseResponse.Length; i++)
        {
            Complex total = side.Channels
                .Aggregate(Complex.Zero, (sum, item) => sum + item.ImpulseResponse[i]);
            Assert.Equal(side.ImpulseResponse[i].Real, total.Real, 12);
            Assert.Equal(side.ImpulseResponse[i].Imaginary, total.Imaginary, 12);
        }
    }

    [Fact]
    public async Task ComputeSideSumAsync_HonorsTheMinimumChannelCount()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();

        VirtualCrossoverSideSum? forAudition = await metrics.ComputeSideSumAsync(
            [ResolvedChannel("A", 48_000)], rightSide: false, revision,
            minimumChannels: 1);
        VirtualCrossoverSideSum? forOverlay = await metrics.ComputeSideSumAsync(
            [ResolvedChannel("A", 48_000)], rightSide: false, revision,
            minimumChannels: 2);

        Assert.NotNull(forAudition);
        Assert.Equal(1, forAudition.ChannelCount);
        Assert.Equal(48_000, forAudition.SampleRate);
        Assert.Null(forOverlay);
    }

    [Fact]
    public async Task ComputeSideSumAsync_MonoChannelContributesToBothSidesAtFullLevel()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        // A mono sub plays into both ears at full level, so both side sums carry it unattenuated.
        VirtualCrossoverChannel mono = ResolvedChannel("Sub", 48_000);
        mono.Pair.Mono = true;
        long revision = coordinator.Invalidate();

        VirtualCrossoverSideSum? left = await metrics.ComputeSideSumAsync(
            [mono], rightSide: false, revision, minimumChannels: 1);
        VirtualCrossoverSideSum? right = await metrics.ComputeSideSumAsync(
            [mono], rightSide: true, revision, minimumChannels: 1);

        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.Equal(left.ImpulseResponse.Length, right.ImpulseResponse.Length);
        for (int i = 0; i < left.ImpulseResponse.Length; i++)
        {
            Assert.Equal(left.ImpulseResponse[i], right.ImpulseResponse[i]);
        }
    }

    [Fact]
    public async Task ComputeSideSumAsync_ReturnsNullForAStaleRevision()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        coordinator.Invalidate();

        VirtualCrossoverSideSum? result = await metrics.ComputeSideSumAsync(
            [ResolvedChannel("A", 48_000)], rightSide: false, revision,
            minimumChannels: 1);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_SkipsAStereoPairWithOnlyOneSideResolved()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([ResolvedChannel("A", 48_000)], revision);

        Assert.Empty(deltas);
    }

    private static Complex[] LongImpulse()
    {
        var ir = new Complex[4_096];
        ir[512] = Complex.One;
        return ir;
    }

    private static void Resolve(VirtualCrossoverChannel channel, bool rightSide)
    {
        VirtualCrossoverChannelState state = channel.PhysicalSideState(rightSide);
        state.TransferImpulseResponse = LongImpulse();
        state.SampleRate = 48_000;
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_ReportsOneDeltaForAResolvedStereoPair()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("A");
        Resolve(channel, rightSide: false);
        Resolve(channel, rightSide: true);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.Equal("A", delta.Channel);
        Assert.Equal(20, delta.LowHz);
        Assert.Equal(20_000, delta.HighHz);
        Assert.NotNull(channel.PhysicalSideState(false).ArrivalCache);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_TakesTheLevelFromTheHybridReaderWhenSupplied()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("A");
        Resolve(channel, rightSide: false);
        Resolve(channel, rightSide: true);
        var asked = new List<(double LowHz, double HighHz)>();

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync(
                [channel],
                revision,
                hybridLevelDeltaDb: (_, lowHz, highHz) =>
                {
                    asked.Add((lowHz, highHz));
                    return -2.5;
                });

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.Equal(-2.5, delta.LevelDeltaDb);
        Assert.True(delta.LevelFromSpatialAverage);
        Assert.Equal((20.0, 20_000.0), Assert.Single(asked));
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_AReaderWithNothingToSayLeavesThePointMeasuredLevel()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("A");
        Resolve(channel, rightSide: false);
        Resolve(channel, rightSide: true);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync(
                [channel], revision, hybridLevelDeltaDb: (_, _, _) => null);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.Equal(0.0, delta.LevelDeltaDb!.Value, 6);
        Assert.False(delta.LevelFromSpatialAverage);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_NeverAsksTheHybridReaderForAMonoChannel()
    {
        // A shared driver has no L-R; consulting the reader would invent an imbalance.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("Sub") { Pair = { Mono = true } };
        Resolve(channel, rightSide: false);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync(
                [channel],
                revision,
                hybridLevelDeltaDb: (_, _, _) =>
                    throw new InvalidOperationException(
                        "A mono channel must not reach the hybrid level reader."));

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.Null(delta.LevelDeltaDb);
        Assert.False(delta.LevelFromSpatialAverage);
    }

    private static void AddBurst(
        Complex[] ir, double toneHz, int cycles, double amplitude, double startMs)
    {
        int start = (int)(startMs * 48_000 / 1000.0);
        int length = (int)(cycles * 48_000 / toneHz);
        for (int i = 0; i < length && start + i < ir.Length; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(Math.Tau * i / length));
            ir[start + i] += new Complex(
                amplitude * window * Math.Sin(Math.Tau * toneHz * i / 48_000), 0);
        }
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_FlagsAModalLatchedSide()
    {
        // Field latch: weak direct wavelet 34 dB under late modal ringing; full band times the build-up, upper half the wavelet.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();

        var latched = new Complex[8_192];
        AddBurst(latched, toneHz: 300, cycles: 3, amplitude: 0.02, startMs: 10);
        AddBurst(latched, toneHz: 130, cycles: 8, amplitude: 1.0, startMs: 25);
        var clean = new Complex[8_192];
        AddBurst(clean, toneHz: 300, cycles: 3, amplitude: 1.0, startMs: 10);

        var channel = new VirtualCrossoverChannel("B");
        foreach (bool rightSide in new[] { false, true })
        {
            VirtualCrossoverChannelState state = channel.PhysicalSideState(rightSide);
            state.TransferImpulseResponse = rightSide ? clean : latched;
            state.SampleRate = 48_000;
            VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
            settings.CrossoverKind = CrossoverKind.BandPass;
            settings.HighPassEdge = new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, 100, 24);
            settings.LowPassEdge = new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, 400, 24);
        }

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.True(delta.EnergyOnset);
        Assert.True(delta.LeftLatched);
        Assert.False(delta.RightLatched);
        Assert.NotNull(channel.PhysicalSideState(false).ArrivalCache!.Value.Probe);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_TheCoinTheFieldPairTossed_ReadsTheSameSplitByOnsets()
    {
        // The engine's coin: first peaks read the sides 5 ms apart for a true skew of zero; onsets agree within 0.2 ms.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("B");
        foreach (bool rightSide in new[] { false, true })
        {
            var ir = new Complex[16_384];
            ir[2_400] = Complex.One;
            ir[2_400 + (rightSide ? 7 : 8) * 48] += 1.4;
            VirtualCrossoverChannelState state = channel.PhysicalSideState(rightSide);
            state.TransferImpulseResponse = ir;
            state.SampleRate = 48_000;
            VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
            settings.CrossoverKind = CrossoverKind.BandPass;
            settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 65, 36);
            settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 48);
        }

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        TimeAlignmentAnalysisResult left = channel.PhysicalSideState(false).ArrivalCache!.Value.Result;
        TimeAlignmentAnalysisResult right = channel.PhysicalSideState(true).ArrivalCache!.Value.Result;
        Assert.True(
            Math.Abs(left.FirstArrivalDelayMilliseconds - right.FirstArrivalDelayMilliseconds) > 3.0,
            $"peaks split {left.FirstArrivalDelayMilliseconds - right.FirstArrivalDelayMilliseconds:0.00} ms");
        Assert.True(delta.EnergyOnset);
        Assert.InRange(delta.DeltaMs!.Value, -0.3, 0.3);
        Assert.False(delta.LeftLatched);
        Assert.False(delta.RightLatched);
    }

    private static VirtualCrossoverChannel BandPassPair(
        string name, double lowHz, double highHz, Complex[] left, Complex[] right)
    {
        var channel = new VirtualCrossoverChannel(name);
        foreach (bool rightSide in new[] { false, true })
        {
            VirtualCrossoverChannelState state = channel.PhysicalSideState(rightSide);
            state.TransferImpulseResponse = rightSide ? right : left;
            state.SampleRate = 48_000;
            VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
            settings.CrossoverKind = CrossoverKind.BandPass;
            settings.HighPassEdge = new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, lowHz, 24);
            settings.LowPassEdge = new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, highHz, 24);
        }

        return channel;
    }

    private static Complex[] MidbassPacket(double amplitude = 1.0)
    {
        var ir = new Complex[8_192];
        AddBurst(ir, toneHz: 130, cycles: 4, amplitude: amplitude, startMs: 25);
        return ir;
    }

    private static void AddNoise(Complex[] ir, double rms, int seed)
    {
        var random = new Random(seed);
        for (int i = 0; i < ir.Length; i++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            ir[i] += rms * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(Math.Tau * u2);
        }
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_ReadsALowPairByItsEnergyOnsets()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        VirtualCrossoverChannel channel = BandPassPair(
            "B", 65, 200, MidbassPacket(), MidbassPacket());

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.True(delta.EnergyOnset);
        TimeAlignmentAnalysisResult left = channel.PhysicalSideState(false).ArrivalCache!.Value.Result;
        TimeAlignmentAnalysisResult right = channel.PhysicalSideState(true).ArrivalCache!.Value.Result;
        Assert.True(left.SignalToNoiseDecibels >= AutoAlignmentEngine.EnergyOnsetMinimumSnrDb);
        Assert.Equal(left.EnergyOnsetDelayMilliseconds, delta.LeftMs!.Value, 9);
        Assert.Equal(right.EnergyOnsetDelayMilliseconds, delta.RightMs!.Value, 9);
        Assert.True(delta.LeftMs.Value < left.FirstArrivalDelayMilliseconds - 0.5);
        Assert.False(delta.LeftLatched);
        Assert.False(delta.RightLatched);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_ALowPairReadsFirstPeaksOnBothSidesWhenOneCannotWitnessAnOnset()
    {
        // Right side above the 12 dB arrival floor but under the 30 dB onset SNR: both sides fall back to first peaks.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        Complex[] noisy = MidbassPacket();
        AddNoise(noisy, rms: 0.4, seed: 7);
        VirtualCrossoverChannel channel = BandPassPair(
            "B", 65, 200, MidbassPacket(), noisy);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        TimeAlignmentAnalysisResult left = channel.PhysicalSideState(false).ArrivalCache!.Value.Result;
        TimeAlignmentAnalysisResult right = channel.PhysicalSideState(true).ArrivalCache!.Value.Result;
        Assert.InRange(
            right.SignalToNoiseDecibels,
            AutoAlignmentEngine.MinimumArrivalSnrDb,
            AutoAlignmentEngine.EnergyOnsetMinimumSnrDb - 0.01);
        Assert.False(delta.EnergyOnset);
        Assert.True(delta.EnergyOnsetWithheld);
        Assert.Equal(left.FirstArrivalDelayMilliseconds, delta.LeftMs!.Value, 9);
        Assert.Equal(right.FirstArrivalDelayMilliseconds, delta.RightMs!.Value, 9);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_APairCentredAboveTheOnsetRegionReadsFirstPeaks()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        var packet = new Complex[8_192];
        AddBurst(packet, toneHz: 600, cycles: 4, amplitude: 1.0, startMs: 25);
        VirtualCrossoverChannel channel = BandPassPair("C", 300, 1_200, packet, packet);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.False(delta.EnergyOnset);
        Assert.False(delta.EnergyOnsetWithheld);
        TimeAlignmentAnalysisResult left = channel.PhysicalSideState(false).ArrivalCache!.Value.Result;
        Assert.Equal(left.FirstArrivalDelayMilliseconds, delta.LeftMs!.Value, 9);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_AMonoChannelReadsItsFirstPeakEvenInAnOnsetBand()
    {
        // A mono channel has no twin to cancel the onset bias, so it keeps the first peak.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        VirtualCrossoverChannel channel = BandPassPair(
            "A", 65, 200, MidbassPacket(), MidbassPacket());
        channel.Pair.Mono = true;

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.False(delta.EnergyOnset);
        Assert.Null(delta.RightMs);
        TimeAlignmentAnalysisResult left = channel.PhysicalSideState(false).ArrivalCache!.Value.Result;
        Assert.Equal(left.FirstArrivalDelayMilliseconds, delta.LeftMs!.Value, 9);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_MonoChannelReportsNoRightSide()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("Sub") { Pair = { Mono = true } };
        Resolve(channel, rightSide: false);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.Equal("Sub", delta.Channel);
        Assert.Null(delta.RightMs);
    }

    [Fact]
    public void BuildEntries_KeepsTheRealJunctionAndWithholdsTheTotalAcrossAHole()
    {
        // Two crossing subs then a rear fill above a hole: the sub row survives, the total must not.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        List<SignalPoint> loss =
            [.. Enumerable.Range(0, 400).Select(i =>
                new SignalPoint(20.0 * Math.Pow(1_000.0, i / 399.0), -1.0))];
        List<ProcessedChannel> brokenChain =
        [
            ProcessedThroughChain("A", CrossoverKind.LowPass, 50),
            ProcessedThroughChain("B", CrossoverKind.HighPass, 50),
            ProcessedThroughChain("C", CrossoverKind.HighPass, 290)
        ];
        brokenChain[1].Channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        brokenChain[1].Channel.Settings.LowPassEdge =
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 110, 24);

        List<VirtualCrossoverMetric.Entry> entries =
            metrics.BuildEntries(brokenChain, loss);

        Assert.Equal("A/B", Assert.Single(entries).Junction);
        Assert.DoesNotContain(entries, entry => entry.IsTotal);

        List<VirtualCrossoverMetric.Entry> whole =
            metrics.BuildEntries([brokenChain[0], brokenChain[1]], loss);

        Assert.Contains(whole, entry => entry.IsTotal);
    }

    [Fact]
    public void BuildEntries_ReadsJunctionsOffTheSummingSet_ACentreBetweenTwoFrontDriversInventsNone()
    {
        // A high-passed centre ordered with the front wedges between mid and tweeter; callers pass the SUMMING set.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        List<SignalPoint> loss =
            [.. Enumerable.Range(0, 400).Select(i =>
                new SignalPoint(20.0 * Math.Pow(1_000.0, i / 399.0), -1.0))];
        ProcessedChannel mid = ProcessedThroughChain("B", CrossoverKind.HighPass, 290);
        mid.Channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        mid.Channel.Settings.LowPassEdge =
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_500, 24);
        ProcessedChannel tweeter = ProcessedThroughChain("C", CrossoverKind.HighPass, 3_500);
        ProcessedChannel centre = ProcessedThroughChain("X", CrossoverKind.HighPass, 290);

        List<VirtualCrossoverMetric.Entry> summed = metrics.BuildEntries([mid, tweeter], loss);
        List<VirtualCrossoverMetric.Entry> drawn = metrics.BuildEntries([mid, centre, tweeter], loss);

        Assert.Contains(summed, entry => entry.Junction == "B/C");
        Assert.DoesNotContain(drawn, entry => entry.Junction == "B/C");
        Assert.Contains(drawn, entry => entry.Junction == "B/X" || entry.Junction == "X/C");
    }

    [Fact]
    public void AFramesSum_IsWhatTheViewSums_NotEveryChannelOfTheSideAsOneChain()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var flat = new AnalysisCurve(
            "x", [.. Enumerable.Range(0, 400).Select(i => new SignalPoint(20.0 * Math.Pow(1_000.0, i / 399.0), 0.0))]);
        var summedSets = new List<string>();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (_, _, _, _, _) => new GatedMagnitude(flat, flat),
            null,
            (channels, _) =>
            {
                summedSets.Add(string.Join("+", channels.Select(item => item.Channel.Name)));
                return new GatedMagnitude(flat, flat);
            });
        ProcessedChannel mid = ProcessedThroughChain("B", CrossoverKind.HighPass, 290);
        mid.Channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        mid.Channel.Settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_500, 24);
        ProcessedChannel tweeter = ProcessedThroughChain("C", CrossoverKind.HighPass, 3_500);
        ProcessedChannel rear = ProcessedThroughChain("R", CrossoverKind.HighPass, 290);
        rear.Channel.Pair.Zone = VirtualCrossoverZone.Rear;
        ProcessedChannel centre = ProcessedThroughChain("X", CrossoverKind.HighPass, 290);
        centre.Channel.Pair.Zone = VirtualCrossoverZone.Center;

        (AnalysisCurve? sum, List<VirtualCrossoverMetric.Entry> entries) =
            VirtualCrossoverFrame.Of([mid, rear, centre, tweeter], VirtualCrossoverGroupView.FrontAndSub)
                .ReadSum(metrics, 12);

        Assert.NotNull(sum);
        Assert.Equal(["B+C"], summedSets);
        Assert.Contains(entries, entry => entry.Junction == "B/C");
        Assert.All(entries.Where(entry => !entry.IsTotal), entry => Assert.Equal("B/C", entry.Junction));
    }

    [Fact]
    public void BuildCurves_SumsOnlyTheSubsetItIsGiven_ButStillDrawsEveryChannel()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var summedSets = new List<int>();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (_, _, _, _, _) => EmptyMagnitude,
            null,
            (channels, _) =>
            {
                summedSets.Add(channels.Count);
                return EmptyMagnitude;
            });
        List<ProcessedChannel> processed =
        [
            Processed("A", Impulse(10), 10, 48_000),
            Processed("B", Impulse(12), 12, 48_000),
            Processed("C", Impulse(14), 14, 48_000)
        ];

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum, _) = metrics.BuildCurves(
            processed, 12, [processed[0], processed[1]]);

        Assert.Equal(3, magnitudes?.Count);
        Assert.NotNull(sum);
        Assert.Equal(2, Assert.Single(summedSets));
    }

    [Fact]
    public void BuildCurves_WithFewerThanTwoSummingChannels_StillDrawsThem()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (_, _, _, _, _) => EmptyMagnitude,
            null,
            (_, _) => EmptyMagnitude);
        List<ProcessedChannel> processed =
        [
            Processed("A", Impulse(10), 10, 48_000),
            Processed("B", Impulse(12), 12, 48_000)
        ];

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum, List<SignalPoint>? loss) =
            metrics.BuildCurves(processed, 12, [processed[0]]);

        Assert.Equal(2, magnitudes?.Count);
        Assert.Null(sum);
        Assert.Null(loss);
    }

    [Fact]
    public async Task ComputeSideSumAsync_HonoursTheZoneFilterItIsGiven()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (_, _, _, _, _) => EmptyMagnitude,
            null,
            (_, _) => EmptyMagnitude);
        VirtualCrossoverChannel front = ResolvedChannel("A", 48_000);
        front.Pair.Zone = VirtualCrossoverZone.Front;
        VirtualCrossoverChannel rear = ResolvedChannel("B", 48_000);
        rear.Pair.Zone = VirtualCrossoverZone.Rear;
        VirtualCrossoverChannel sub = ResolvedChannel("C", 48_000);
        sub.Pair.Zone = VirtualCrossoverZone.Sub;

        VirtualCrossoverSideSum? frontAndSub = await metrics.ComputeSideSumAsync(
            [front, rear, sub],
            rightSide: false,
            coordinator.Invalidate(),
            minimumChannels: 2,
            includePair: pair => pair.Zone != VirtualCrossoverZone.Rear);

        Assert.NotNull(frontAndSub);
        Assert.Equal(2, frontAndSub.ChannelCount);

        VirtualCrossoverSideSum? rearOnly = await metrics.ComputeSideSumAsync(
            [front, rear, sub],
            rightSide: false,
            coordinator.Invalidate(),
            minimumChannels: 2,
            includePair: pair => pair.Zone == VirtualCrossoverZone.Rear);

        Assert.Null(rearOnly);
    }

    private static ProcessedChannel Zoned(
        string name,
        VirtualCrossoverZone zone,
        int peak,
        Complex[]? ir = null)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = 48_000 };
        channel.Pair.Zone = zone;
        ir ??= LongImpulse(peak);
        return new ProcessedChannel(channel, ir, peak, 48_000, OxyColors.White);
    }

    private static Complex[] LongImpulse(int peak)
    {
        var ir = new Complex[4096];
        ir[peak] = Complex.One;
        return ir;
    }

    [Fact]
    public async Task ComputeGroupDeltas_ReusesTheReadOutWhileTheResponsesStand()
    {
        // A view switch touches no response, so group deltas must come from memory rather than re-running arrival FFTs.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);
        List<ProcessedChannel> shown =
        [
            Zoned("Front", VirtualCrossoverZone.Front, 100),
            Zoned("Centre", VirtualCrossoverZone.Center, 150)
        ];

        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> first =
            await metrics.ComputeGroupDeltasAsync(
                shown, VirtualCrossoverGroupView.FrontAndCenter, coordinator.Invalidate());
        // RequestRedraw invalidates first, so the repeat frame carries a NEW revision like a real toggle.
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> second =
            await metrics.ComputeGroupDeltasAsync(
                shown, VirtualCrossoverGroupView.FrontAndCenter, coordinator.Invalidate());

        Assert.Single(first);
        Assert.Equal(VirtualCrossoverZone.Center, first[0].Zone);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task ComputeGroupDeltas_TakesTheLevelFromTheHybridReaderAndKeysTheCacheOnIt()
    {
        // The hybrid answer joins the cache key: point-measured memory must not answer for capture-based deltas.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);
        List<ProcessedChannel> shown =
        [
            Zoned("Front", VirtualCrossoverZone.Front, 100),
            Zoned("Centre", VirtualCrossoverZone.Center, 150)
        ];
        var asked = new List<(int Members, int Front, double LowHz, double HighHz)>();

        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> hybrid =
            await metrics.ComputeGroupDeltasAsync(
                shown, VirtualCrossoverGroupView.FrontAndCenter, coordinator.Invalidate(),
                hybridGroupLevelDeltaDb: (members, front, lowHz, highHz) =>
                {
                    asked.Add((members.Count, front.Count, lowHz, highHz));
                    return -5.5;
                });

        VirtualCrossoverMetric.GroupDelta delta = Assert.Single(hybrid);
        Assert.Equal(-5.5, delta.LevelDb);
        Assert.True(delta.LevelFromSpatialAverage);
        Assert.Equal((1, 1, 20.0, 20_000.0), Assert.Single(asked));

        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> point =
            await metrics.ComputeGroupDeltasAsync(
                shown, VirtualCrossoverGroupView.FrontAndCenter, coordinator.Invalidate());

        Assert.NotSame(hybrid, point);
        Assert.False(Assert.Single(point).LevelFromSpatialAverage);
    }

    [Fact]
    public async Task ComputeGroupDeltas_AnswerNothingForASupersededFrame()
    {
        // An overtaken frame still gets nothing; a cache hit must not bypass the staleness check.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);
        List<ProcessedChannel> shown =
        [
            Zoned("Front", VirtualCrossoverZone.Front, 100),
            Zoned("Centre", VirtualCrossoverZone.Center, 150)
        ];

        long superseded = coordinator.Invalidate();
        Assert.Single(await metrics.ComputeGroupDeltasAsync(
            shown, VirtualCrossoverGroupView.FrontAndCenter, superseded));
        coordinator.Invalidate();

        Assert.Empty(await metrics.ComputeGroupDeltasAsync(
            shown, VirtualCrossoverGroupView.FrontAndCenter, superseded));
    }

    [Fact]
    public async Task ComputeGroupDeltas_RecomputesWhenAResponseIsReplaced()
    {
        // The coordinator makes new arrays when inputs move, so the cache is keyed on array identity.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);
        ProcessedChannel front = Zoned("Front", VirtualCrossoverZone.Front, 100);
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> first =
            await metrics.ComputeGroupDeltasAsync(
                [front, Zoned("Centre", VirtualCrossoverZone.Center, 150)],
                VirtualCrossoverGroupView.FrontAndCenter,
                coordinator.CurrentRevision);

        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> second =
            await metrics.ComputeGroupDeltasAsync(
                [front, Zoned("Centre", VirtualCrossoverZone.Center, 260)],
                VirtualCrossoverGroupView.FrontAndCenter,
                coordinator.CurrentRevision);

        Assert.NotSame(first, second);
        Assert.Single(second);
        Assert.NotNull(first[0].DelayMs);
        Assert.NotNull(second[0].DelayMs);
        Assert.True(second[0].DelayMs > first[0].DelayMs);
    }

    [Fact]
    public async Task ComputeGroupDeltas_StaySilentInASingleGroupView()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);

        Assert.Empty(await metrics.ComputeGroupDeltasAsync(
            [
                Zoned("Front", VirtualCrossoverZone.Front, 100),
                Zoned("Sub", VirtualCrossoverZone.Sub, 150)
            ],
            VirtualCrossoverGroupView.FrontAndSub,
            coordinator.CurrentRevision));
    }

    [Fact]
    public async Task ComputeGroupDeltas_ReportOneRowPerComparedGroup()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);

        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> deltas =
            await metrics.ComputeGroupDeltasAsync(
                [
                    Zoned("Front", VirtualCrossoverZone.Front, 100),
                    Zoned("Rear", VirtualCrossoverZone.Rear, 200),
                    Zoned("Centre", VirtualCrossoverZone.Center, 150)
                ],
                VirtualCrossoverGroupView.Everything,
                coordinator.CurrentRevision);

        Assert.Equal(2, deltas.Count);
        Assert.Equal(VirtualCrossoverZone.Rear, deltas[0].Zone);
        Assert.Equal(VirtualCrossoverZone.Center, deltas[1].Zone);
        Assert.Equal(100.0 / 48.0, deltas[0].DelayMs!.Value, 2);
        Assert.Equal(50.0 / 48.0, deltas[1].DelayMs!.Value, 2);
    }

    [Fact]
    public async Task ComputeStereoDeltas_DoesNotEvictTheFramesProcessedResponses()
    {
        // List position is the coordinator cache slot: a filtered list renumbers blocks and the read-out and frame thrash each other.
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _, _) =>
            {
                Interlocked.Increment(ref processCount);
                return source.Apply(chain, sampleRate, sampleRate);
            });
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);
        List<VirtualCrossoverChannel> channels =
            [ResolvedMono("A"), ResolvedMono("B"), ResolvedMono("C")];
        long revision = coordinator.Invalidate();
        VirtualCrossoverProcessingSnapshot frame = new(
            revision,
            channels.Select((channel, index) => new VirtualCrossoverChannelSnapshot(
                index,
                new ProcessingSlotId(index, false),
                channel.SideState(false).ProcessingSource!,
                48_000,
                48_000,
                channel.Settings.ToChain(channel.Pair.Zone))));

        Assert.NotNull(await coordinator.ProcessAsync(frame));
        int afterFirstFrame = processCount;

        await metrics.ComputeStereoDeltasAsync(
            channels, revision, includePair: pair => ReferenceEquals(pair, channels[2].Pair));
        Assert.NotNull(await coordinator.ProcessAsync(frame));

        Assert.Equal(3, afterFirstFrame);
        Assert.Equal(3, processCount);
    }

    private static VirtualCrossoverChannel ResolvedMono(string name)
    {
        VirtualCrossoverChannel channel = ResolvedChannel(name, 48_000);
        channel.Pair.Mono = true;
        return channel;
    }
}
