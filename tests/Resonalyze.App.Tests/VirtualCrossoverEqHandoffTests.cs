using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The handoff over a live session: the guards read the session as it is when the bank comes back.</summary>
public sealed class VirtualCrossoverEqHandoffTests : IDisposable
{
    private readonly VirtualCrossoverProcessingCoordinator coordinator = new();
    private readonly VirtualCrossoverSession session = new();
    private readonly VirtualCrossoverEqHandoff handoff;

    public VirtualCrossoverEqHandoffTests()
    {
        for (int index = 0; index < session.Project.Pairs.Count; index++)
        {
            session.Channels.Add(new VirtualCrossoverChannel(VirtualCrossoverSheet.ChannelName(index))
            {
                Pair = session.Project.Pairs[index],
                ActiveRightProvider = () => session.ActiveSideRight,
                ProcessorSampleRateProvider = () => session.ProcessorSampleRateHz
            });
        }

        foreach (bool rightSide in new[] { false, true })
        {
            VirtualCrossoverChannelState state = session.Channels[0].SideState(rightSide);
            var impulse = new System.Numerics.Complex[16_384];
            impulse[480] = System.Numerics.Complex.One;
            state.TransferImpulseResponse = impulse;
            state.TransferPeakIndex = 480;
            state.SampleRate = 48_000;
        }

        var metrics = VirtualCrossoverMetrics.Through(
            coordinator, () => session.MagnitudeGate, oppositeSide: false, channel => session.Calibration.For(channel));
        handoff = new VirtualCrossoverEqHandoff(session, coordinator, metrics, new VirtualCrossoverHybrid(session));
    }

    public void Dispose() => coordinator.Dispose();

    [Fact]
    public void ABankFittedOnTheLeft_ComesBackWhileTheRightIsShown_AgainstTheLeftsOwnPin()
    {
        session.Project.PhaseGateLeft.OffsetMs = 9.0;
        session.Project.PhaseGateRight.OffsetMs = 11.0;
        ShowSide(rightSide: false);
        VirtualCrossoverChannel channel = session.Channels[0];
        VirtualDspEqHandoffRequest request = Assert.IsType<VirtualDspEqHandoffRequest>(
            handoff.Request(channel, withChain: true, hybridRequested: false, (null, 0.0)));
        var fitted = new EqualizationCurve([new PeqBand(1_000, 2, -3)], -1);

        ShowSide(rightSide: true);

        Assert.True(handoff.TryReturn(request.Token, fitted, spatialAverage: null));
        Assert.Equal(fitted.Bands, channel.SideSettings(rightSide: false).PeqBands);
        Assert.Empty(channel.SideSettings(rightSide: true).PeqBands);
    }

    [Fact]
    public void ABankWhoseOwnSidesPinMoved_IsRefused_WhicheverSideIsShown()
    {
        session.Project.PhaseGateLeft.OffsetMs = 9.0;
        ShowSide(rightSide: false);
        VirtualCrossoverChannel channel = session.Channels[0];
        VirtualDspEqHandoffRequest request = Assert.IsType<VirtualDspEqHandoffRequest>(
            handoff.Request(channel, withChain: true, hybridRequested: false, (null, 0.0)));

        session.Project.PhaseGateLeft.OffsetMs = 10.0;
        ShowSide(rightSide: true);

        Assert.False(handoff.TryReturn(
            request.Token, new EqualizationCurve([new PeqBand(1_000, 2, -3)], -1), spatialAverage: null));
    }

    [Fact]
    public async Task MidRedraw_TheSpatialAverageStillTravels_WithTheOffsetOfItsOwnFamily()
    {
        VirtualCrossoverChannel channel = session.Channels[0];
        LiveCaptureDocument capture = MovingMicCapture(-20);
        channel.SideState(rightSide: false).SpatialAverage = capture;
        session.Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic;

        (LiveCaptureDocument? Capture, double OffsetDb) resolved =
            Assert.NotNull(await handoff.SpatialAverageAsync(channel, rightSide: false, hybridRequested: true));
        session.LastHybridOffset = (coordinator.CurrentRevision, VirtualCrossoverSpatialAverageMode.MicArray, 42.0);
        (LiveCaptureDocument? Capture, double OffsetDb) afterSwitch =
            Assert.NotNull(await handoff.SpatialAverageAsync(channel, rightSide: false, hybridRequested: true));

        Assert.Same(capture, resolved.Capture);
        Assert.True(double.IsFinite(resolved.OffsetDb));
        Assert.Equal(resolved, afterSwitch);
    }

    [Theory]
    [InlineData(SumLossWindow.Off, SumLossWindow.Full)]
    [InlineData(SumLossWindow.Direct, SumLossWindow.Direct)]
    public void AChainHandoff_CarriesTheSidesOtherSummedChannels_InThePanelsWindow(
        SumLossWindow shown,
        SumLossWindow expected)
    {
        ShowSide(rightSide: false);
        VirtualCrossoverChannel low = session.Channels[0];
        VirtualCrossoverChannel high = session.Channels[1];
        VirtualCrossoverChannelState highState = high.SideState(rightSide: false);
        highState.TransferImpulseResponse = low.SideState(rightSide: false).TransferImpulseResponse!.ToArray();
        highState.TransferPeakIndex = 480;
        highState.SampleRate = 48_000;
        low.Settings.CrossoverKind = CrossoverKind.LowPass;
        low.Settings.LowPassEdge = low.Settings.LowPassEdge with { FrequencyHz = 300 };
        high.Settings.CrossoverKind = CrossoverKind.HighPass;
        high.Settings.HighPassEdge = high.Settings.HighPassEdge with { FrequencyHz = 300 };
        ProcessedChannel processedHigh = Processed(high);
        // A rear channel arriving earlier: the Front + Sub view leaves it out of the plot's shared anchor.
        VirtualCrossoverChannel rear = session.Channels[2];
        rear.Pair.Zone = VirtualCrossoverZone.Rear;
        var rearResponse = new System.Numerics.Complex[16_384];
        rearResponse[240] = System.Numerics.Complex.One;
        var processedRear = new ProcessedChannel(
            rear, rearResponse, 240, 48_000, OxyPlot.OxyColors.White, default, MeasuredBand.Everything);
        session.LastRender = new VirtualCrossoverProcessedRender(
            coordinator.CurrentRevision, [Processed(low), processedHigh, processedRear]);
        var view = new VirtualCrossoverViewState(
            AcousticView.Magnitude,
            VirtualCrossoverGroupView.FrontAndSub,
            RightSide: false,
            ShowSum: true,
            shown,
            HybridRequested: false,
            Target: null,
            TargetLevelDb: -40);

        VirtualDspEqHandoffRequest chain = handoff.Request(low, withChain: true, hybridRequested: false, (null, 0.0), view: view)!;
        VirtualDspEqHandoffRequest raw = handoff.Request(low, withChain: false, hybridRequested: false, (null, 0.0), view: view)!;
        VirtualDspEqHandoffRequest headless = handoff.Request(low, withChain: true, hybridRequested: false, (null, 0.0))!;

        EqWizardSumLossContext context = Assert.IsType<EqWizardSumLossContext>(chain.Source.SumLossContext);
        Assert.Equal(expected, context.Window);
        SumLossMember other = Assert.Single(context.Others);
        // The other channel as the panel processed it, not its raw measurement.
        Assert.Same(processedHigh.ImpulseResponse, other.ImpulseResponse);
        Assert.Null(raw.Source.SumLossContext);
        Assert.Null(headless.Source.SumLossContext);
        Assert.Equal(
            ProcessedChannels.SharedStartAnchorIndex([Processed(low), processedHigh]),
            chain.Source.Measurement!.PeakIndex);
        Assert.Equal(
            ProcessedChannels.SharedStartAnchorIndex(session.LastRender.Channels),
            headless.Source.Measurement!.PeakIndex);
        Assert.NotEqual(chain.Source.Measurement.PeakIndex, headless.Source.Measurement.PeakIndex);

        // A stale render names channels the plot no longer shows.
        coordinator.Invalidate();
        Assert.Null(handoff.Request(low, withChain: true, hybridRequested: false, (null, 0.0), view: view)!.Source.SumLossContext);
    }

    private static ProcessedChannel Processed(VirtualCrossoverChannel channel) =>
        new(
            channel,
            channel.SideState(rightSide: false).TransferImpulseResponse!,
            480,
            48_000,
            OxyPlot.OxyColors.White,
            default,
            MeasuredBand.Everything);

    private static LiveCaptureDocument MovingMicCapture(double db)
    {
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 64);
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "average",
            Method = SpatialAverageMethod.MovingMic,
            CurveDb = [.. grid.Select(_ => db)],
            GridStartHz = grid[0],
            GridStopHz = grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = 48_000 }
        };
    }

    // As the panel's redraw does: the snapshot carries the shown side's pin and the other side's stored one.
    private void ShowSide(bool rightSide)
    {
        session.Project.ActiveSideRight = rightSide;
        session.MagnitudeGate = session.Gate.MagnitudeGate(
            session.GateFor(!rightSide).StoredOffsetMs, smoothingInverseOctaves: 12);
    }
}
