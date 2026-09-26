using System.Numerics;

namespace Resonalyze.App.Tests;

[Trait("Category", "Slow")]
public sealed class VirtualCrossoverSharedScaleTests : IDisposable
{
    private readonly VirtualCrossoverProcessingCoordinator coordinator = new();
    private readonly VirtualCrossoverSession session = new();
    private readonly VirtualCrossoverSharedScale scale;

    public VirtualCrossoverSharedScaleTests()
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

        foreach (VirtualCrossoverChannel channel in session.Channels.Take(2))
        {
            foreach (bool rightSide in new[] { false, true })
            {
                VirtualCrossoverChannelState state = channel.SideState(rightSide);
                var impulse = new Complex[16_384];
                impulse[480] = Complex.One;
                state.TransferImpulseResponse = impulse;
                state.TransferPeakIndex = 480;
                state.SampleRate = 48_000;
            }
        }

        var metrics = VirtualCrossoverMetrics.Through(
            coordinator, () => session.MagnitudeGate, oppositeSide: false, channel => session.Calibration.For(channel));
        var hybrid = new VirtualCrossoverHybrid(session);
        scale = new VirtualCrossoverSharedScale(
            session, coordinator, metrics, hybrid, new AcousticViewBuilder(session, hybrid));
    }

    public void Dispose() => coordinator.Dispose();

    [Fact]
    public async Task AnUnchangedHiddenSide_AnswersWithItsOwnRead_NotWithWhatItDrewBesideTheOtherSidesOldSum()
    {
        var view = new VirtualCrossoverViewState(
            AcousticView.Magnitude, VirtualCrossoverGroupView.FrontAndSub, RightSide: true, ShowSum: true,
            SumLossWindow.Off, HybridRequested: false, Target: null, session.Project.TargetLevelDb);
        (bool current, ScaleExtent? measured) = await scale.MeasureOtherSideAsync(view, coordinator.CurrentRevision);
        Assert.True(current);
        Assert.NotNull(measured);

        // Shown meanwhile, the left side remembered its drawing, the right side's dashed sum at an old level included.
        scale.Remember(rightSide: false, view, new ScaleExtent(-200, 100, null));
        (bool again, ScaleExtent? reread) = await scale.MeasureOtherSideAsync(view, coordinator.CurrentRevision);

        Assert.True(again);
        Assert.Equal(measured, reread);
    }
}
