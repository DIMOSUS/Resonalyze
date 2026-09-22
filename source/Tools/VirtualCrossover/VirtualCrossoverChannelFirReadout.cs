using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A block's FIR row: the button, the read-out beside it and their tooltips. Latency is read at the kernel
/// peak (the bulk delay of a linear-phase kernel); a file rate the processor does not run is amber, since the taps are
/// used as they are (see <see cref="FirFilter"/>) and so make a different filter.</summary>
internal sealed record VirtualCrossoverChannelFirReadout(
    string ButtonText,
    Color ButtonColor,
    string Info,
    Color InfoColor,
    string ButtonTip,
    string InfoTip,
    string? Conflict)
{
    private const string ButtonTooltip =
        "The channel's FIR filter — a kernel the processor convolves the channel" + "\r\n" +
        "with, designed in the FIR Constructor or imported from a file, kept in" + "\r\n" +
        "the session, and run AT THE PROCESSOR'S RATE." + "\r\n" +
        "Click to design, import, export or clear it.";

    public static VirtualCrossoverChannelFirReadout Read(
        FirFilter? kernel,
        string? sourceName,
        FirCrossoverDesign? design,
        int processorRateHz,
        CrossoverKind crossoverKind)
    {
        string buttonText;
        string info;
        Color infoColor = UiPalette.TextSecondary;
        string infoTip;
        if (kernel == null)
        {
            buttonText = "Add…";
            info = "off";
            infoColor = UiPalette.TextDisabled;
            infoTip = "No FIR filter on this channel.";
        }
        else
        {
            buttonText = "Edit…";
            string name = design is { } named
                ? FirCrossoverDescription.Short(named)
                : sourceName ?? "FIR";
            double peakMs = kernel.PeakIndex * 1_000.0 / processorRateHz;
            double lengthMs = kernel.Length * 1_000.0 / processorRateHz;
            bool rateMismatch = kernel.DeclaredSampleRateHz is { } declared &&
                declared != processorRateHz;
            info = rateMismatch
                ? $"{kernel.Length} taps · file {FormatRate(kernel.DeclaredSampleRateHz!.Value)} ≠ {FormatRate(processorRateHz)}"
                : $"{kernel.Length} taps · {peakMs:0.0} ms";
            infoColor = rateMismatch ? UiPalette.Warning : UiPalette.TextSecondary;
            infoTip =
                $"{kernel.Length} taps, {lengthMs:0.0} ms at {FormatRate(processorRateHz)}; " +
                $"peak at {peakMs:0.00} ms — roughly the bulk delay of a linear-phase kernel," +
                Environment.NewLine + "no delay at all for a minimum-phase one." +
                (kernel.IsSilent
                    ? Environment.NewLine + "Every tap is zero: the kernel MUTES the channel."
                    : string.Empty) +
                (rateMismatch
                    ? Environment.NewLine +
                      $"The file states {FormatRate(kernel.DeclaredSampleRateHz!.Value)}, the processor runs " +
                      $"{FormatRate(processorRateHz)}. The taps are convolved as they are, at the" +
                      Environment.NewLine +
                      "processor's rate — so this is not the filter its designer drew."
                    : string.Empty);
            if (design is { } built)
            {
                // At the processor's rate: a design made at another rate delays by the same samples, a different time.
                double runLatencyMs = built.LatencySamples * 1_000.0 / processorRateHz;
                info = $"{kernel.Length} taps · {runLatencyMs:0.0} ms";
                infoColor = UiPalette.TextSecondary;
                infoTip = FirCrossoverDescription.Long(built) + "." + Environment.NewLine +
                    $"Linear-phase: the channel is delayed by {runLatencyMs:0.00} ms, half the kernel" +
                    (built.SampleRateHz == processorRateHz
                        ? "."
                        : $" ({built.LatencyMs:0.00} ms as designed at {FormatRate(built.SampleRateHz)}).");
            }
            else
            {
                infoTip = name + ": " + infoTip;
            }

            info = $"{name}: {info}";
        }

        string? conflict = ConflictOf(kernel, design, processorRateHz, crossoverKind);
        return new VirtualCrossoverChannelFirReadout(
            buttonText,
            conflict != null ? UiPalette.Danger : UiPalette.TextPrimary,
            info,
            conflict != null ? UiPalette.Error : infoColor,
            conflict == null
                ? ButtonTooltip
                : conflict + Environment.NewLine + Environment.NewLine + ButtonTooltip,
            conflict ?? infoTip,
            conflict);
    }

    /// <summary>Why the FIR button is red (a FIR crossover at a stale rate, or beside an IIR crossover), or null.</summary>
    public static string? ConflictOf(
        FirFilter? kernel,
        FirCrossoverDesign? design,
        int processorRateHz,
        CrossoverKind crossoverKind)
    {
        if (kernel == null || design is not { } built)
        {
            return null;
        }

        if (built.SampleRateHz != processorRateHz)
        {
            return $"This FIR crossover was designed at {FormatRate(built.SampleRateHz)}, and the " +
                $"processor runs at {FormatRate(processorRateHz)}:" + Environment.NewLine +
                "the taps are convolved as they are, so it cuts somewhere else. Open it in the" +
                Environment.NewLine + "FIR Constructor and return it to rebuild it at the processor's rate.";
        }

        if (crossoverKind != CrossoverKind.Off)
        {
            return "This side runs a FIR crossover AND an IIR crossover: both filter the channel," +
                Environment.NewLine + "so it is cut twice. Legitimate, but rarely meant — turn one of them off" +
                Environment.NewLine + "unless the two are designed to work together.";
        }

        return null;
    }

    private static string FormatRate(int sampleRateHz) => $"{sampleRateHz / 1_000.0:0.###} kHz";
}
