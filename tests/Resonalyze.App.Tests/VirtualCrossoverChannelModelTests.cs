using System.Numerics;

namespace Resonalyze.App.Tests;

/// <summary>
/// Characterization tests for the UI-free Virtual DSP runtime model
/// (<see cref="VirtualCrossoverChannel"/>, <see cref="VirtualCrossoverChannelState"/>,
/// <see cref="VirtualCrossoverSideAlignmentChannel"/>): the physical/effective
/// side routing, the Mono⇄Stereo transitions, per-side settings/state selection
/// and the source-load revision guard — all without touching WinForms.
/// </summary>
public sealed class VirtualCrossoverChannelModelTests
{
    private static Complex[] Ir(double marker) => [new Complex(marker, 0), Complex.Zero];

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public void Invalidate_MakesInFlightSourceLoadsStaleAndClearsBothPhysicalSlots()
    {
        // Reproduces the guard behind the removed-channel fix: a channel removed
        // (or dropped by importing a smaller project) while a source load is in
        // flight must invalidate that load, so it cannot write back to a detached
        // channel. Both physical slots are cleared and their revisions bumped.
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

    // ---------------------------------------------------------------- state

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
        // A revision captured before Clear is now stale — a load in flight when
        // the slot was wiped may no longer write back.
        Assert.NotEqual(captured, state.SourceRevision);
    }

    [Fact]
    public void State_OverlappingLoads_OnlyTheLatestRevisionStaysCurrent()
    {
        var state = new VirtualCrossoverChannelState();

        int first = state.BeginSourceLoad();
        int second = state.BeginSourceLoad();

        // The later pick wins regardless of completion order: only its revision
        // still matches, so a stale first load is refused at write-back.
        Assert.NotEqual(first, state.SourceRevision);
        Assert.Equal(second, state.SourceRevision);
    }

    // -------------------------------------------------------- side routing

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
        // Load a measurement into the real right slot while stereo.
        channel.PhysicalSideState(true).TransferImpulseResponse = Ir(2);

        // Stereo: the effective right slot IS the physical right slot.
        Assert.NotNull(channel.SideState(true).TransferImpulseResponse);

        channel.Pair.Mono = true;
        // Mono: the effective right routes to the (empty) left slot, so the
        // right measurement is unreachable — nothing stale can surface.
        Assert.Same(channel.SideState(false), channel.SideState(true));
        Assert.Null(channel.SideState(true).TransferImpulseResponse);
        // …yet the physical right slot still holds it, retained across the toggle.
        Assert.NotNull(channel.PhysicalSideState(true).TransferImpulseResponse);

        channel.Pair.Mono = false;
        // Back to stereo: the right measurement is revealed again.
        Assert.NotNull(channel.SideState(true).TransferImpulseResponse);
    }

    // ---------------------------------------------------- settings selection

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

        // A mono pair always answers with its single (left) settings, on either side.
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

        // Mono routes the active side to the left slot too.
        channel.Pair.Mono = true;
        Assert.Equal(44_100, channel.SampleRate);
    }

    // ------------------------------------------------- side alignment view

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
        // A mono pair's right alignment view routes its state to the left slot.
        Assert.Same(channel.SideState(false), right.State);
    }
}
