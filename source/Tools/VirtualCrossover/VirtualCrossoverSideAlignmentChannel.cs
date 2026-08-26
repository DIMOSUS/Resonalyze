using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One side of a channel pair as the STEREO alignment engine sees it: the
/// engine needs distinct identities for the left and right drivers of one
/// block, while the interactive paths keep using <see cref="VirtualCrossoverChannel"/>
/// itself. A mono pair contributes a single instance (rightSide: false) to both
/// sides' channel lists.
/// </summary>
internal sealed class VirtualCrossoverSideAlignmentChannel : IAlignmentChannel
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

    // The project's processing rate where the panel installed one; this side's
    // measurement rate otherwise — see
    // VirtualCrossoverChannel.ProcessorSampleRateProvider.
    public int ProcessorSampleRate =>
        Runtime.ProcessorSampleRateProvider?.Invoke() is int rate && rate > 0
            ? rate
            : State.SampleRate;
}
