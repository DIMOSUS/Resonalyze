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
    public void BuildCurves_ReadsTheSnapshotRate_NotTheLiveChannel()
    {
        // Importing a session rebinds every channel's runtime state on the UI
        // thread while a metric rebuild is still in flight, so the LIVE
        // channel can momentarily report a zero rate against a real processed
        // response — the ArgumentOutOfRangeException crash on opening a
        // session over a loaded one. The render is a snapshot: zeroing the
        // live channel after processing must change nothing the metric reads.
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
        // One channel has no metric: its sum is itself and its summation loss is zero
        // by definition. It still has a CURVE, and withholding that was a bug with a
        // long reach — everything downstream gates on the magnitudes being present,
        // so muting every channel but one silently turned off the hybrid view and the
        // spatial average the EQ Wizard would have been handed.
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
        // A channel whose peak is a later, louder feature than its front: the
        // shared window must open at the FRONT, or it opens after part of the
        // response it is supposed to measure. (The other channel arrives after
        // both, so the anchor is the first one's front either way — what is
        // pinned here is front vs peak, not which channel wins.)
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
        // Two channel spectra + one sum spectrum, all anchored to the SAME
        // sample: one shared window is what keeps the drawn Sum the vector sum
        // of the drawn channels and the loss under its 0 dB ceiling. These
        // records are 64 samples — too short for the front estimator, which
        // refuses rather than guesses — so the anchor falls back to the
        // earliest declared peak, the rule this one used to follow outright.
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
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

        Assert.Empty(metrics.BuildEntries(
            [Processed("A", Impulse(), 5, 48_000)], lossCurve: null));
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
            impulse, channel.Settings.ToChain(), 48_000, 48_000);
        return new ProcessedChannel(
            channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir), 48_000,
            OxyColors.White);
    }

    [Fact]
    public void BuildPhaseEntries_IsEmptyForFewerThanTwoChannels()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

        Assert.Empty(metrics.BuildPhaseEntries(
            [ProcessedThroughChain("A", CrossoverKind.LowPass, 200)]));
    }

    [Fact]
    public void BuildPhaseEntries_ReadsTheJunctionAndRecoversAMisalignment()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);

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

    /// <summary>
    /// The side sum hands back the PARTS it was made of, not only the total. The
    /// hybrid view rebuilds that sum a different way — magnitudes added, with the
    /// summation loss on top, because a spatial average carries no phase — and with
    /// one summed response and nothing else it could not take part at all.
    /// </summary>
    [Fact]
    public async Task ComputeSideSumAsync_HandsBackThePartsThatWentIntoTheSum()
    {
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        VirtualCrossoverChannel a = ResolvedChannel("A", 48_000);
        VirtualCrossoverChannel b = ResolvedChannel("B", 48_000);
        // A channel with nothing behind it takes no part, so it must not appear
        // among the parts either — a caller walking them beside its own per-channel
        // data would be one out from there on.
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

        // And they really are that sum's parts: added back up they reproduce it.
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

        // One resolved channel: enough for the audition (minimum 1), not for
        // the opposite-side overlay (minimum 2).
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
        // A mono channel (a sub) resolved on its single slot: program material
        // routes it into BOTH ears at full level, so both side sums must carry
        // its response unattenuated.
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
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
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

    [Fact]
    public async Task ComputeStereoDeltasAsync_TakesTheLevelFromTheHybridReaderWhenSupplied()
    {
        // The panel supplies the reader while the hybrid mode is on: the level
        // row then reports what the spatial averages say — asked once, in the
        // pair's shared band — and the row says which measurement it read.
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
        // An array set may have gaps: a pair the captures cannot speak for keeps
        // its gated point-measured level, unmarked as spatial.
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
        // The two sides are byte-identical impulses, so the point level says 0.
        Assert.Equal(0.0, delta.LevelDeltaDb!.Value, 6);
        Assert.False(delta.LevelFromSpatialAverage);
    }

    [Fact]
    public async Task ComputeStereoDeltasAsync_NeverAsksTheHybridReaderForAMonoChannel()
    {
        // One physical driver serving both sides has no L−R to compare, so the
        // reader must not be consulted for it — a capture could answer, and the
        // row would then invent an imbalance for a channel that cannot have one.
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
        Assert.True(delta.LeftLatched);
        Assert.False(delta.RightLatched);
        // The latch flag rides in the per-side cache with the arrival.
        Assert.True(channel.PhysicalSideState(false).ArrivalCache!.Value.Latched);
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
        // One physical driver serving both sides: no right-side arrival or delta.
        Assert.Null(delta.RightMs);
    }

    [Fact]
    public void BuildEntries_KeepsTheRealJunctionAndWithholdsTheTotalAcrossAHole()
    {
        // The reference car's Rear + Sub view, read end to end rather than through
        // the predicate underneath: two subwoofers that genuinely cross, then a
        // rear fill above a hole. The subwoofers' row is information the tuner
        // wants and must survive; the total must not, because averaging that real
        // handover with a span only one member plays in presents a figure about a
        // chain that is not one.
        using var coordinator = new VirtualCrossoverProcessingCoordinator();
        var metrics = new VirtualCrossoverMetrics(coordinator, (_, _, _, _, _) => EmptyMagnitude);
        // A flat loss across the audio band: the entries' arithmetic is not what
        // is under test here, only which rows are minted at all.
        List<SignalPoint> loss =
            [.. Enumerable.Range(0, 400).Select(i =>
                new SignalPoint(20.0 * Math.Pow(1_000.0, i / 399.0), -1.0))];
        List<ProcessedChannel> brokenChain =
        [
            ProcessedThroughChain("A", CrossoverKind.LowPass, 50),
            ProcessedThroughChain("B", CrossoverKind.HighPass, 50),
            ProcessedThroughChain("C", CrossoverKind.HighPass, 290)
        ];
        // B is the 50-110 Hz subwoofer; give it its upper corner so the hole in
        // front of C is real rather than an artefact of a missing low-pass.
        brokenChain[1].Channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        brokenChain[1].Channel.Settings.LowPassEdge =
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 110, 24);

        List<VirtualCrossoverMetric.Entry> entries =
            metrics.BuildEntries(brokenChain, loss);

        Assert.Equal("A/B", Assert.Single(entries).Junction);
        Assert.DoesNotContain(entries, entry => entry.IsTotal);

        // Drop the rear fill and the same two subwoofers ARE a chain, so the total
        // comes back — the rule bites on the hole, not on the view.
        List<VirtualCrossoverMetric.Entry> whole =
            metrics.BuildEntries([brokenChain[0], brokenChain[1]], loss);

        Assert.Contains(whole, entry => entry.IsTotal);
    }

    [Fact]
    public void BuildCurves_SumsOnlyTheSubsetItIsGiven_ButStillDrawsEveryChannel()
    {
        // The grouped views draw a centre beside the front stage without adding it
        // to anything, so the drawn set and the summed set differ. Both must come
        // out right at once: a curve missing from the plot is a channel the user
        // cannot see, and a channel silently inside the sum is a number they
        // cannot explain.
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
        // A front stage of one driver beside an unsummed centre: there is no sum
        // and no loss to state, but both curves are still what the plot is for.
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
        // The opposite side's sum has to be the same part of the installation as
        // the side on screen, or the dashed comparison curve reads as an L/R
        // difference that is really a difference of scope.
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

        // And the filter can starve the sum below its minimum, which is a null
        // rather than a sum of one.
        VirtualCrossoverSideSum? rearOnly = await metrics.ComputeSideSumAsync(
            [front, rear, sub],
            rightSide: false,
            coordinator.Invalidate(),
            minimumChannels: 2,
            includePair: pair => pair.Zone == VirtualCrossoverZone.Rear);

        Assert.Null(rearOnly);
    }

    // A channel whose processed response is a lone impulse at a known sample,
    // long enough for the band-limited arrival analysis to have something to
    // work with, tagged with the zone the grouped views sort it by.
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
        // The group Δ read-out costs arrival FFTs over the summed groups, and the
        // whole redraw waits on them. A view switch rebuilds the frame without
        // touching a single response, so it must be answered from memory the way
        // the coordinator answers for the responses themselves — otherwise
        // Front + Center pays for those FFTs again on every magnitude/phase/
        // impulse toggle while Front + Sub, which quotes no group Δ, is instant.
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
        // The second frame is asked the way a real toggle asks: RequestRedraw
        // invalidates first and the frame then carries the NEW revision, so a
        // repeat call at the old one would prove nothing about the path the user
        // takes.
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> second =
            await metrics.ComputeGroupDeltasAsync(
                shown, VirtualCrossoverGroupView.FrontAndCenter, coordinator.Invalidate());

        Assert.Single(first);
        Assert.Equal(VirtualCrossoverZone.Center, first[0].Zone);
        // The same instance: a recompute builds a new list, so identity is what
        // says the arrival analysis did not run a second time.
        Assert.Same(first, second);
    }

    [Fact]
    public async Task ComputeGroupDeltas_TakesTheLevelFromTheHybridReaderAndKeysTheCacheOnIt()
    {
        // The panel supplies the reader while the hybrid mode is on: the ΔdB
        // then reports what the groups' spatial averages say. The answer joins
        // the cache key — toggling the hybrid leaves every response standing,
        // and the remembered point-measured set must not answer for the
        // capture-based one.
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
        // Asked once, with the compared group, the front and their shared band.
        Assert.Equal((1, 1, 20.0, 20_000.0), Assert.Single(asked));

        // The hybrid off again: the same responses must be re-read rather than
        // answered from the capture-based memory.
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> point =
            await metrics.ComputeGroupDeltasAsync(
                shown, VirtualCrossoverGroupView.FrontAndCenter, coordinator.Invalidate());

        Assert.NotSame(hybrid, point);
        Assert.False(Assert.Single(point).LevelFromSpatialAverage);
    }

    [Fact]
    public async Task ComputeGroupDeltas_AnswerNothingForASupersededFrame()
    {
        // The other half of remembering the set: a frame the user has already
        // overtaken is answered as it was before the cache existed — with
        // nothing. Cheap to get wrong, because a cache hit is tempting to serve
        // unconditionally, and then a stale frame carries a read-out the caller
        // was supposed to drop.
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
        // The other half of the bargain. The coordinator manufactures a NEW array
        // for a channel the moment anything feeding it moves, so a remembered
        // read-out must be answered for by the very arrays it was measured from.
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
        // And the answer moved with the response: the centre now arrives later.
        Assert.NotNull(first[0].DelayMs);
        Assert.NotNull(second[0].DelayMs);
        Assert.True(second[0].DelayMs > first[0].DelayMs);
    }

    [Fact]
    public async Task ComputeGroupDeltas_StaySilentInASingleGroupView()
    {
        // Front + Sub spans one listening group, so it quotes a summation loss
        // and no group Δ at all — which is why it was the fast view to begin with.
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
        // Everything compares two groups against the front, and both are timed
        // over the same span here — the path where the front stage's own arrival
        // is read once for the band rather than once per group. The rows must
        // still be the two the view promises, each against the same reference.
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
        // 100 samples at 48 kHz is 2.083 ms behind the front, 50 is 1.042 ms.
        Assert.Equal(100.0 / 48.0, deltas[0].DelayMs!.Value, 2);
        Assert.Equal(50.0 / 48.0, deltas[1].DelayMs!.Value, 2);
    }

    [Fact]
    public async Task ComputeStereoDeltas_DoesNotEvictTheFramesProcessedResponses()
    {
        // A block's POSITION in the list handed to the stereo Δ read-out is its
        // identity in the coordinator's cache. Hand over a filtered list and every
        // block after the first omission is renumbered onto a slot belonging to
        // another channel: the read-out overwrites responses the frame had just
        // processed, the next frame misses on them, reprocesses, overwrites the
        // read-out's in turn — and the two thrash each other for as long as the
        // view stays open. On the reference car that is every view except the one
        // whose blocks happen to sit at the head of the list.
        int processCount = 0;
        using var coordinator = new VirtualCrossoverProcessingCoordinator(
            (source, chain, sampleRate, _, _) =>
            {
                Interlocked.Increment(ref processCount);
                return source.Apply(chain, sampleRate, sampleRate);
            });
        var metrics = new VirtualCrossoverMetrics(
            coordinator, (_, _, _, _, _) => EmptyMagnitude);
        // Three mono blocks: the read-out below covers the LAST one only, so with
        // a shortened list it would take slot 0 — the first block's.
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
                channel.Settings.ToChain())));

        Assert.NotNull(await coordinator.ProcessAsync(frame));
        int afterFirstFrame = processCount;

        await metrics.ComputeStereoDeltasAsync(
            channels, revision, includePair: pair => ReferenceEquals(pair, channels[2].Pair));
        // The frame runs again with nothing changed, exactly as a view or mode
        // toggle makes it: every response must still be in the cache.
        Assert.NotNull(await coordinator.ProcessAsync(frame));

        Assert.Equal(3, afterFirstFrame);
        Assert.Equal(3, processCount);
    }

    // A mono block with a resolved source on the side the panel draws.
    private static VirtualCrossoverChannel ResolvedMono(string name)
    {
        VirtualCrossoverChannel channel = ResolvedChannel(name, 48_000);
        channel.Pair.Mono = true;
        return channel;
    }
}
