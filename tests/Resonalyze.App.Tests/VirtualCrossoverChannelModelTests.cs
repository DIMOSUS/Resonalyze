using System.Numerics;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverChannelModelTests
{
    private static Complex[] Ir(double marker) => [new Complex(marker, 0), Complex.Zero];

    [Fact]
    public void Invalidate_MakesInFlightSourceLoadsStaleAndClearsBothPhysicalSlots()
    {
        // A channel removed while a source load is in flight must invalidate it, so it cannot write to a detached channel.
        var channel = new VirtualCrossoverChannel("A");
        channel.PhysicalSideState(false).TransferImpulseResponse = Ir(1);
        channel.PhysicalSideState(true).TransferImpulseResponse = Ir(2);
        int leftRevision = channel.PhysicalSideState(false).BeginSourceLoad();
        int rightRevision = channel.PhysicalSideState(true).BeginSourceLoad();

        channel.Invalidate();

        Assert.NotEqual(leftRevision, channel.PhysicalSideState(false).SourceRevision);
        Assert.NotEqual(rightRevision, channel.PhysicalSideState(true).SourceRevision);
        Assert.Null(channel.PhysicalSideState(false).TransferImpulseResponse);
        Assert.Null(channel.PhysicalSideState(true).TransferImpulseResponse);
    }

    [Fact]
    public void State_TransferImpulseResponse_TracksProcessingSource()
    {
        var state = new VirtualCrossoverChannelState();
        Assert.Null(state.ProcessingSource);

        state.TransferImpulseResponse = Ir(1);
        Assert.NotNull(state.ProcessingSource);

        state.TransferImpulseResponse = null;
        Assert.Null(state.ProcessingSource);
    }

    [Fact]
    public void State_Clear_ResetsEverythingAndBumpsRevision()
    {
        var state = new VirtualCrossoverChannelState
        {
            TransferImpulseResponse = Ir(1),
            TransferPeakIndex = 5,
            SampleRate = 48_000,
            TransferCoherence = [1.0, 0.5]
        };
        int captured = state.SourceRevision;

        state.Clear();

        Assert.Null(state.TransferImpulseResponse);
        Assert.Null(state.ProcessingSource);
        Assert.Equal(0, state.TransferPeakIndex);
        Assert.Equal(0, state.SampleRate);
        Assert.Null(state.TransferCoherence);
        Assert.Null(state.ArrivalCache);
        Assert.NotEqual(captured, state.SourceRevision);
    }

    [Fact]
    public void State_OverlappingLoads_OnlyTheLatestRevisionStaysCurrent()
    {
        var state = new VirtualCrossoverChannelState();

        int first = state.BeginSourceLoad();
        int second = state.BeginSourceLoad();

        Assert.NotEqual(first, state.SourceRevision);
        Assert.Equal(second, state.SourceRevision);
    }

    [Fact]
    public void StereoChannel_EffectiveAndPhysicalSlots_Coincide()
    {
        var channel = new VirtualCrossoverChannel("A");

        Assert.Same(channel.SideState(false), channel.PhysicalSideState(false));
        Assert.Same(channel.SideState(true), channel.PhysicalSideState(true));
        Assert.NotSame(channel.SideState(false), channel.SideState(true));
    }

    [Fact]
    public void MonoChannel_RoutesEffectiveRightToLeftButKeepsPhysicalRight()
    {
        var channel = new VirtualCrossoverChannel("A");
        channel.PhysicalSideState(true).TransferImpulseResponse = Ir(2);

        Assert.NotNull(channel.SideState(true).TransferImpulseResponse);

        channel.Pair.Mono = true;
        // Mono routes right to the empty left slot; the physical right slot retains its measurement across the toggle.
        Assert.Same(channel.SideState(false), channel.SideState(true));
        Assert.Null(channel.SideState(true).TransferImpulseResponse);
        Assert.NotNull(channel.PhysicalSideState(true).TransferImpulseResponse);

        channel.Pair.Mono = false;
        Assert.NotNull(channel.SideState(true).TransferImpulseResponse);
    }

    [Fact]
    public void SideSettings_FollowMonoAndActiveSide()
    {
        var channel = new VirtualCrossoverChannel("A");
        channel.Pair.Left.GainDb = 1;
        channel.Pair.Right.GainDb = 2;

        channel.ActiveRight = false;
        Assert.Equal(1, channel.Settings.GainDb);
        Assert.Equal(2, channel.SideSettings(true).GainDb);

        channel.ActiveRight = true;
        Assert.Equal(2, channel.Settings.GainDb);

        channel.Pair.Mono = true;
        Assert.Equal(1, channel.Settings.GainDb);
        Assert.Same(channel.Pair.Left, channel.SideSettings(true));
    }

    [Fact]
    public void ActiveSide_DelegatingState_ReadsTheActiveSlot()
    {
        var channel = new VirtualCrossoverChannel("A");
        channel.PhysicalSideState(false).SampleRate = 44_100;
        channel.PhysicalSideState(true).SampleRate = 48_000;

        channel.ActiveRight = false;
        Assert.Equal(44_100, channel.SampleRate);

        channel.ActiveRight = true;
        Assert.Equal(48_000, channel.SampleRate);

        channel.Pair.Mono = true;
        Assert.Equal(44_100, channel.SampleRate);
    }

    [Fact]
    public void SideAlignmentChannel_NamesAndSampleRate_FollowRouting()
    {
        var channel = new VirtualCrossoverChannel("A");
        channel.PhysicalSideState(false).SampleRate = 44_100;
        channel.PhysicalSideState(true).SampleRate = 48_000;

        var left = new VirtualCrossoverSideAlignmentChannel(channel, false);
        var right = new VirtualCrossoverSideAlignmentChannel(channel, true);

        Assert.Equal("A L", left.Name);
        Assert.Equal("A R", right.Name);
        Assert.Equal(44_100, left.SampleRate);
        Assert.Equal(48_000, right.SampleRate);
        Assert.Same(channel.PhysicalSideState(true), right.State);

        channel.Pair.Mono = true;
        Assert.Equal("A (mono)", left.Name);
        Assert.Same(channel.SideState(false), right.State);
    }
}
