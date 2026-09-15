using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Return address for a designed kernel. Channel and Kernel compare by instance, generation and rate by value:
/// any change since the session opened (reload, import, Clear, Lock mirror, processor change) refuses the landing.</summary>
internal sealed record FirConstructorReturnToken(
    VirtualCrossoverChannel Channel,
    bool RightSide,
    long ProjectGeneration,
    bool Mono,
    int ProcessorSampleRateHz,
    FirFilter? Kernel);

/// <param name="Design">Null for a bare kernel; a design at another rate is rebuilt at <see cref="ProcessorSampleRateHz"/>.</param>
/// <param name="SeedCrossover">The side's active IIR crossover, where a first design starts; null otherwise.</param>
internal sealed record FirConstructorHandoffRequest(
    string ChannelLabel,
    FirFilter? Kernel,
    string? KernelName,
    FirCrossoverDesign? Design,
    CrossoverSpec? SeedCrossover,
    int ProcessorSampleRateHz,
    FirConstructorReturnToken Token);

/// <summary>UI-free, like <see cref="VirtualDspEqHandoff"/>, so the landing rules are testable.</summary>
internal static class FirConstructorHandoff
{
    public static FirConstructorHandoffRequest Build(
        VirtualCrossoverChannel channel,
        bool rightSide,
        long projectGeneration,
        int processorSampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(channel);

        bool mono = channel.Pair.Mono;
        VirtualCrossoverChannelSettings settings = channel.Pair.SideFor(rightSide);
        string side = mono ? "mono" : rightSide ? "right side" : "left side";
        string name = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? $"Channel {channel.Name}"
            : $"Channel {channel.Name} — {settings.DisplayName}";
        CrossoverSpec? seed = settings.CrossoverKind == CrossoverKind.Off
            ? null
            : new CrossoverSpec(settings.CrossoverKind, settings.LowPassEdge, settings.HighPassEdge);
        return new FirConstructorHandoffRequest(
            $"{name}, {side}",
            settings.Fir,
            settings.FirSourceName,
            settings.Fir != null ? settings.FirDesign : null,
            seed,
            processorSampleRateHz,
            new FirConstructorReturnToken(
                channel, rightSide, projectGeneration, mono, processorSampleRateHz, settings.Fir));
    }

    /// <summary>False, with nothing written, when the side is no longer the one the token names or has no FIR stage.</summary>
    public static bool TryApplyReturn(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        FirConstructorReturnToken token,
        FirFilter kernel,
        FirCrossoverDesign design,
        long projectGeneration,
        int processorSampleRateHz,
        bool firStageAvailable)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(design);

        if (!firStageAvailable ||
            token.ProjectGeneration != projectGeneration ||
            !channels.Contains(token.Channel) ||
            token.Mono != token.Channel.Pair.Mono ||
            token.ProcessorSampleRateHz != processorSampleRateHz ||
            design.SampleRateHz != processorSampleRateHz ||
            kernel.Length != design.TapCount)
        {
            return false;
        }

        VirtualCrossoverChannelSettings settings = token.Channel.Pair.SideFor(token.RightSide);
        if (!ReferenceEquals(settings.Fir, token.Kernel))
        {
            return false;
        }

        settings.Fir = kernel;
        settings.FirSourceName = null;
        settings.FirDesign = design;
        return true;
    }
}
