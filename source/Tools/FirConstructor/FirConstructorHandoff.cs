using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where a kernel designed in the FIR Constructor goes back to: one side of one
/// Virtual DSP channel, as the project stood when the session opened.
/// </summary>
/// <param name="Channel">The channel, by instance: a removed or re-imported one is not it.</param>
/// <param name="RightSide">The side the session was opened on.</param>
/// <param name="ProjectGeneration">
/// The panel's project generation at the handoff; a loaded or reset project moves it,
/// and a kernel must not land in a project it was not taken from.
/// </param>
/// <param name="Mono">Whether the pair was mono, which decides which slot the side names.</param>
/// <param name="ProcessorSampleRateHz">
/// The processor's rate the session designs at. The kernel is those numbers only at
/// that rate, so a project that moved to another processor meanwhile refuses it.
/// </param>
/// <param name="Kernel">
/// The kernel the side held when the session opened, or null. Compared by instance: an
/// import, a Clear, a copy from the other side or a Lock mirror made in the panel
/// meanwhile replaced it, and landing over that would lose it without a word.
/// </param>
internal sealed record FirConstructorReturnToken(
    VirtualCrossoverChannel Channel,
    bool RightSide,
    long ProjectGeneration,
    bool Mono,
    int ProcessorSampleRateHz,
    FirFilter? Kernel);

/// <summary>
/// Everything a Virtual DSP channel side sends into the FIR Constructor: the kernel it
/// carries and the design that kernel came from (either may be null), the corners to
/// start a new design from, the processor's rate, and the return address.
/// </summary>
/// <param name="ChannelLabel">How the constructor names the side it is editing.</param>
/// <param name="Kernel">The side's kernel, shown as it is when it has no design.</param>
/// <param name="KernelName">The file the kernel was imported from, if any.</param>
/// <param name="Design">
/// The design the kernel was built from, or null. A design made at another rate is
/// still handed over: the constructor rebuilds it at <see cref="ProcessorSampleRateHz"/>,
/// which is exactly the rebuild the block's red button asks for.
/// </param>
/// <param name="SeedCrossover">
/// The side's IIR crossover when it has one on — the corners a first design starts at,
/// so a FIR crossover begins where the channel is already cut. Null otherwise.
/// </param>
/// <param name="ProcessorSampleRateHz">The rate the constructor designs at for this session.</param>
/// <param name="Token">The return address.</param>
internal sealed record FirConstructorHandoffRequest(
    string ChannelLabel,
    FirFilter? Kernel,
    string? KernelName,
    FirCrossoverDesign? Design,
    CrossoverSpec? SeedCrossover,
    int ProcessorSampleRateHz,
    FirConstructorReturnToken Token);

/// <summary>
/// Builds and lands handoffs between the Virtual DSP tool and the FIR Constructor.
/// UI-free, like <see cref="VirtualDspEqHandoff"/>: the rules for what travels and
/// where a kernel may land live here, where a test can hold them.
/// </summary>
internal static class FirConstructorHandoff
{
    /// <summary>The handoff for a channel side as the panel shows it now.</summary>
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
            // A design is only ever the description of the kernel beside it.
            settings.Fir != null ? settings.FirDesign : null,
            seed,
            processorSampleRateHz,
            new FirConstructorReturnToken(
                channel, rightSide, projectGeneration, mono, processorSampleRateHz, settings.Fir));
    }

    /// <summary>
    /// Lands a designed kernel on the side the session was opened on. False, with
    /// nothing written, when that side is no longer the one the session was opened
    /// on — see the token for each line.
    /// </summary>
    /// <param name="firStageAvailable">
    /// Whether the project's processor still takes a FIR kernel; a project whose FIR
    /// tick was cleared meanwhile has no stage to land on.
    /// </param>
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
        // The design names the kernel; there is no file behind it.
        settings.FirSourceName = null;
        settings.FirDesign = design;
        return true;
    }
}
