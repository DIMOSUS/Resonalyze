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

    // As the panel's redraw does: the snapshot carries the shown side's pin and the other side's stored one.
    private void ShowSide(bool rightSide)
    {
        session.Project.ActiveSideRight = rightSide;
        session.MagnitudeGate = session.Gate.MagnitudeGate(
            session.GateFor(!rightSide).StoredOffsetMs, smoothingInverseOctaves: 12);
    }
}
