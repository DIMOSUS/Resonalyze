using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A channel an Auto delay run aligns: the chain settings it is searched with and the block it belongs to.</summary>
internal interface IVirtualCrossoverAlignmentChannel : IAlignmentChannel
{
    VirtualCrossoverChannelSettings Settings { get; }

    VirtualCrossoverChannel Runtime { get; }
}

/// <summary>Distinct left/right identity for the stereo engine; a mono pair contributes one instance to both sides.</summary>
internal sealed class VirtualCrossoverSideAlignmentChannel : IVirtualCrossoverAlignmentChannel
{
    public VirtualCrossoverSideAlignmentChannel(VirtualCrossoverChannel runtime, bool rightSide)
    {
        Runtime = runtime;
        RightSide = rightSide;
    }

    public VirtualCrossoverChannel Runtime { get; }
    public bool RightSide { get; }
    public VirtualCrossoverChannelSettings Settings =>
        Runtime.SideSettings(RightSide);
    public VirtualCrossoverChannelState State => Runtime.SideState(RightSide);
    public string Name => Runtime.Pair.Mono
        ? $"{Runtime.Name} (mono)"
        : $"{Runtime.Name} {(RightSide ? "R" : "L")}";
    public int SampleRate => State.SampleRate;

    public int ProcessorSampleRate =>
        Runtime.ProcessorSampleRateProvider?.Invoke() is int rate && rate > 0
            ? rate
            : State.SampleRate;
}
