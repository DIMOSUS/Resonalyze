using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Runtime state of one L/R channel block; members delegate to the active side, a mono pair routes both to the left. UI-free.</summary>
internal sealed class VirtualCrossoverChannel : IVirtualCrossoverAlignmentChannel
{
    private readonly VirtualCrossoverChannelState leftState = new();
    private readonly VirtualCrossoverChannelState rightState = new();

    public VirtualCrossoverChannel(string name)
    {
        Name = name;
    }

    // The block's position letter, not an identity: moving a block re-letters it and nothing keys off it.
    public string Name { get; set; }

    public VirtualCrossoverChannelPairSettings Pair { get; set; } = new();

    private bool activeRight;

    /// <summary>The side the shorthand members (<see cref="Settings"/>, <see cref="TransferImpulseResponse"/>, ...) read.
    /// A block in a session follows its shown side through <see cref="ActiveRightProvider"/> and cannot be set apart
    /// from it; a standalone block keeps its own.</summary>
    public bool ActiveRight
    {
        get => ActiveRightProvider?.Invoke() ?? activeRight;
        set
        {
            if (ActiveRightProvider != null)
            {
                throw new InvalidOperationException(
                    $"Block {Name} shows its session's side; move the session's side instead.");
            }

            activeRight = value;
        }
    }

    public Func<bool>? ActiveRightProvider { get; set; }

    public VirtualCrossoverChannelState SideState(bool rightSide) =>
        Pair.Mono || !rightSide ? leftState : rightState;

    // Lifetime management must use the physical slot: a stale right-side measurement could hide behind mono routing.
    public VirtualCrossoverChannelState PhysicalSideState(bool rightSide) =>
        rightSide ? rightState : leftState;
    public VirtualCrossoverChannelSettings SideSettings(bool rightSide) =>
        Pair.SideFor(rightSide);

    /// <summary>"A L", "A R", or "A (mono)" for a block with one side.</summary>
    public string SideLabel(bool rightSide) =>
        Pair.Mono
            ? $"{Name} (mono)"
            : $"{Name} {(rightSide ? "R" : "L")}";

    // Clear() bumps SourceRevision, so an in-flight load for a removed channel can no longer land.
    public void Invalidate()
    {
        leftState.Clear();
        rightState.Clear();
    }
    private VirtualCrossoverChannelState Active => SideState(ActiveRight);

    public VirtualCrossoverChannelSettings Settings => Pair.SideFor(ActiveRight);

    VirtualCrossoverChannel IVirtualCrossoverAlignmentChannel.Runtime => this;
    public LiveCaptureDocument? SpatialAverage
    {
        get => Active.SpatialAverage;
        set => Active.SpatialAverage = value;
    }

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

    /// <summary>Read on demand rather than copied into channels (a copy goes stale); unset falls back to the measurement's rate.</summary>
    public Func<int>? ProcessorSampleRateProvider { get; set; }

    public int ProcessorSampleRate => ProcessorSampleRateFor(ActiveRight);

    public int ProcessorSampleRateFor(bool rightSide) =>
        ProcessorSampleRateProvider?.Invoke() is int rate && rate > 0
            ? rate
            : SideState(rightSide).SampleRate;
}
