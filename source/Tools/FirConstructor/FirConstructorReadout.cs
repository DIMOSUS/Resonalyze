using System.Globalization;

namespace Resonalyze;

/// <summary>The constructor's texts: what it is editing, and the latency and deviation of the kernel it shows.</summary>
internal static class FirConstructorReadout
{
    public static string Session(FirConstructorSession session)
    {
        if (session.Handoff is not { } request)
        {
            return "Standalone: design a kernel and export it to a file.";
        }

        string note = request.Design is { } arrived && arrived.SampleRateHz != request.ProcessorSampleRateHz
            ? $" It was designed at {FirCrossoverDescription.Rate(arrived.SampleRateHz)} and is " +
                $"rebuilt here at {FirCrossoverDescription.Rate(request.ProcessorSampleRateHz)}."
            : string.Empty;
        return $"Editing {request.ChannelLabel}. The rate is the processor's." + note;
    }

    public static string Latency(FirConstructorSession session)
    {
        if (session.Kernel is not { } shown)
        {
            return string.Empty;
        }

        return session.Design is { } designed
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Latency {designed.LatencyMs:0.00} ms ({designed.LatencySamples} samples at {FirCrossoverDescription.Rate(session.RateHz)})")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{session.KernelName ?? "Kernel"}: {shown.Length} taps, shown as it is at {FirCrossoverDescription.Rate(session.RateHz)}");
    }

    public static string Deviation(FirConstructorSession session)
    {
        if (session.Kernel == null)
        {
            return string.Empty;
        }

        if (session.Design == null)
        {
            return "Any change to the controls designs a new kernel in its place.";
        }

        double deviation = session.Rendering!.DeviationDb;
        return double.IsNaN(deviation)
            ? "A brick wall has no slope to compare with: read the plot."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Worst deviation from the target: {deviation:0.00} dB above −30 dB");
    }
}
