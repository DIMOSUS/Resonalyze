using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Runtime state of one channel block — since the stereo rework, one L/R PAIR.
/// The block's controls and every interactive computation read the ACTIVE side
/// through the delegating members below, so the rest of the panel works
/// unchanged; the side toggle just flips <see cref="ActiveRight"/> and rebinds.
/// A mono pair (shared subwoofer) routes both sides to the left settings and
/// state. The model owns no WinForms control: the panel keeps the
/// model-to-control binding, so the algorithmic paths stay UI-free.
/// </summary>
internal sealed class VirtualCrossoverChannel : IAlignmentChannel
{
    private readonly VirtualCrossoverChannelState leftState = new();
    private readonly VirtualCrossoverChannelState rightState = new();

    public VirtualCrossoverChannel(string name)
    {
        Name = name;
    }

    // The alignment engine's log identity; the channel letter (A, B, C…) is a
    // plain string, safe to read off the UI thread.
    public string Name { get; }

    public VirtualCrossoverChannelPairSettings Pair { get; set; } = new();
    public bool ActiveRight { get; set; }

    // The EFFECTIVE side slot: what the views and calculations read — a
    // mono pair routes both sides to its single left slot.
    public VirtualCrossoverChannelState SideState(bool rightSide) =>
        Pair.Mono || !rightSide ? leftState : rightState;

    // The PHYSICAL side slot, mono routing ignored. Lifetime management
    // (project load, mono toggling) must use this one: through the
    // effective accessor a mono pair's real right slot is unreachable, so
    // a stale measurement could hide there and resurface the moment the
    // pair stops being mono.
    public VirtualCrossoverChannelState PhysicalSideState(bool rightSide) =>
        rightSide ? rightState : leftState;
    public VirtualCrossoverChannelSettings SideSettings(bool rightSide) =>
        Pair.SideFor(rightSide);

    // Invalidates both physical slots when the channel leaves the panel (removed
    // by the user or dropped by importing a smaller project). Clear() bumps each
    // slot's SourceRevision, so a source load still in flight when the channel
    // was removed captures a now-stale revision and can no longer write its
    // result back or reach the channel's detached control.
    public void Invalidate()
    {
        leftState.Clear();
        rightState.Clear();
    }
    private VirtualCrossoverChannelState Active => SideState(ActiveRight);

    public VirtualCrossoverChannelSettings Settings => Pair.SideFor(ActiveRight);
    public Complex[]? TransferImpulseResponse
    {
        get => Active.TransferImpulseResponse;
        set => Active.TransferImpulseResponse = value;
    }
    public int TransferPeakIndex
    {
        get => Active.TransferPeakIndex;
        set => Active.TransferPeakIndex = value;
    }
    public double[]? TransferCoherence
    {
        get => Active.TransferCoherence;
        set => Active.TransferCoherence = value;
    }
    public IReadOnlyList<SignalPoint>? DistortionCurve
    {
        get => Active.DistortionCurve;
        set => Active.DistortionCurve = value;
    }
    public int SampleRate
    {
        get => Active.SampleRate;
        set => Active.SampleRate = value;
    }
}
