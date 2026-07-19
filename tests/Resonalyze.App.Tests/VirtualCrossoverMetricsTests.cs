using System.Collections.Concurrent;
using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Characterization tests for <see cref="VirtualCrossoverMetrics"/>: the metric
/// curve building (shared anchor, complex sum), the participating-channel gating
/// of the opposite-side sum and the eligibility gating of the stereo Δ read-out —
/// exercised through a real processing coordinator, with the magnitude-curve
/// builder faked so no calibration/options are needed.
/// </summary>
public sealed class VirtualCrossoverMetricsTests
{
    private static readonly AnalysisCurve EmptyCurve = new("x", []);

    private static Complex[] Impulse(int peak = 10)
    {
        var ir = new Complex[64];
        ir[peak] = Complex.One;
        return ir;
    }

    private static ProcessedChannel Processed(string name, Complex[] ir, int peak, int rate)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = rate };
        return new ProcessedChannel(channel, ir, peak, OxyColors.White);
    }

    // A channel with a resolved source on its LEFT side (the default active side).
    private static VirtualCrossoverChannel ResolvedChannel(string name, int rate)
    {
        var channel = new VirtualCrossoverChannel(name);
        VirtualCrossoverChannelState left = channel.PhysicalSideState(false);
        left.TransferImpulseResponse = Impulse();
        left.SampleRate = rate;
        return channel;
    }

    [Fact]
    public void BuildCurves_ReturnsNoMetric_ForFewerThanTwoChannels()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum) =
            metrics.BuildCurves([Processed("A", Impulse(), 5, 48_000)]);

        Assert.Null(magnitudes);
        Assert.Null(sum);
    }

    [Fact]
    public void BuildCurves_AnchorsEveryCurveToTheEarliestPeakAndSumsTheResponses()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var captured = new ConcurrentBag<(Complex[] Ir, int Peak, int Rate)>();
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (ir, peak, rate) =>
            {
                captured.Add((ir, peak, rate));
                return EmptyCurve;
            });
        Complex[] a = Impulse(12);
        Complex[] b = Impulse(20);

        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum) = metrics.BuildCurves(
        [
            Processed("A", a, peak: 5, rate: 48_000),
            Processed("B", b, peak: 2, rate: 48_000)
        ]);

        Assert.NotNull(magnitudes);
        Assert.Equal(2, magnitudes.Count);
        Assert.NotNull(sum);
        // Two channel spectra + one sum spectrum, all anchored to the earliest peak.
        Assert.Equal(3, captured.Count);
        Assert.All(captured, entry => Assert.Equal(2, entry.Peak));
        // One of the calls built the complex sum of the two responses.
        Complex[] expectedSum = VirtualCrossoverAnalysis.SumImpulseResponses([a, b]);
        Assert.Contains(captured, entry => entry.Ir.SequenceEqual(expectedSum));
    }

    [Fact]
    public void BuildEntries_IsEmptyWhenThereIsNoMetric()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);

        Assert.Empty(metrics.BuildEntries(
            [Processed("A", Impulse(), 5, 48_000)], magnitudes: null, sumCurve: null));
    }

    // A channel processed through a real crossover chain, for the junction
    // phase read-out: the settings carry the crossover (so the junction and its
    // overlap band resolve) and the IR is the chain-applied impulse.
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
            impulse, channel.Settings.ToChain(), 48_000);
        return new ProcessedChannel(
            channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir), OxyColors.White);
    }

    [Fact]
    public void BuildPhaseEntries_IsEmptyForFewerThanTwoChannels()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);

        Assert.Empty(metrics.BuildPhaseEntries(
            [ProcessedThroughChain("A", CrossoverKind.LowPass, 200)]));
    }

    [Fact]
    public void BuildPhaseEntries_ReadsTheJunctionAndRecoversAMisalignment()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);

        // Passed upper-first on purpose: the entries must order by band, not by
        // argument order. The upper channel runs 2 ms late, so the read-out
        // recommends the same extra delay on the lower one.
        List<VirtualCrossoverMetric.PhaseEntry> entries = metrics.BuildPhaseEntries(
        [
            ProcessedThroughChain("B", CrossoverKind.HighPass, 200, delayMs: 2.0),
            ProcessedThroughChain("A", CrossoverKind.LowPass, 200)
        ]);

        VirtualCrossoverMetric.PhaseEntry entry = Assert.Single(entries);
        Assert.Equal("A/B", entry.Junction);
        Assert.Equal("A", entry.LowerChannel);
        Assert.Equal(200, entry.CrossoverHz);
        Assert.Equal(100, entry.LowHz);
        Assert.Equal(400, entry.HighHz);
        Assert.InRange(entry.Result.BestExtraDelayMs, 1.9, 2.1);
        Assert.InRange(entry.Result.BestScore, 0.95, 1.0);
    }

    [Fact]
    public async Task ComputeOppositeSumCurveAsync_ReturnsNull_WithFewerThanTwoParticipatingChannels()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);
        long revision = coordinator.Invalidate();

        AnalysisCurve? result = await metrics.ComputeOppositeSumCurveAsync(
            [ResolvedChannel("A", 48_000)], oppositeRight: false, revision);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComputeOppositeSumCurveAsync_SumsTheParticipatingSides()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        Complex[]? summed = null;
        var metrics = new VirtualCrossoverMetrics(
            coordinator,
            (ir, _, _) =>
            {
                summed = ir;
                return EmptyCurve;
            });
        long revision = coordinator.Invalidate();

        AnalysisCurve? result = await metrics.ComputeOppositeSumCurveAsync(
            [ResolvedChannel("A", 48_000), ResolvedChannel("B", 48_000)],
            oppositeRight: false,
            revision);

        // Two participating sides → a sum curve is built from a non-empty response.
        Assert.Same(EmptyCurve, result);
        Assert.NotNull(summed);
        Assert.NotEmpty(summed);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_SkipsAStereoPairWithOnlyOneSideResolved()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);
        long revision = coordinator.Invalidate();

        // A stereo pair (not mono) with only the left side resolved is not eligible
        // for a stereo Δ — it needs both sides present and unbypassed.
        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([ResolvedChannel("A", 48_000)], revision);

        Assert.Empty(deltas);
    }

    // A longer impulse so the band-limited arrival analysis has real bins to work
    // with; both physical slots are resolved for a stereo pair.
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
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("A");
        Resolve(channel, rightSide: false);
        Resolve(channel, rightSide: true);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.Equal("A", delta.Channel);
        // No crossover configured, so the shared band is the full audio range.
        Assert.Equal(20, delta.LowHz);
        Assert.Equal(20_000, delta.HighHz);
        // The arrival result is cached on the side for reuse on the next redraw.
        Assert.NotNull(channel.PhysicalSideState(false).ArrivalCache);
    }

    // A Hann-windowed tone burst: toneHz for cycles periods, scaled by
    // amplitude, placed at startMs.
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
        // The left side reproduces the field failure the alignment engine's
        // cross-side links detect: a weak direct wavelet (34 dB below the
        // late modal ringing — under the first-arrival detector's −25 dB
        // prominence floor) followed by a huge low-frequency build-up. The
        // full 100–400 Hz band then times the build-up, while the band's
        // upper half (where the 130 Hz ringing is filtered out) times the
        // wavelet — the disagreement IS the latch. The right side has a
        // clean dominant direct and must stay unflagged.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);
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
        Assert.True(delta.LeftLatched);
        Assert.False(delta.RightLatched);
        // The latch flag rides in the per-side cache with the arrival.
        Assert.True(channel.PhysicalSideState(false).ArrivalCache!.Value.Latched);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_MonoChannelReportsNoRightSide()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _) => EmptyCurve);
        long revision = coordinator.Invalidate();
        var channel = new VirtualCrossoverChannel("Sub") { Pair = { Mono = true } };
        Resolve(channel, rightSide: false);

        List<VirtualCrossoverMetric.StereoDelta> deltas =
            await metrics.ComputeStereoDeltasAsync([channel], revision);

        VirtualCrossoverMetric.StereoDelta delta = Assert.Single(deltas);
        Assert.Equal("Sub", delta.Channel);
        // One physical driver serving both sides: no right-side arrival or delta.
        Assert.Null(delta.RightMs);
    }
}
