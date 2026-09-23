using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>What the Gate dialog reads off the curves it previews: the phase reference (the earliest trace), the τ its
/// buttons estimate there, and the auto detrend it resolves to.</summary>
internal static class VirtualCrossoverGateEstimate
{
    /// <summary>The earliest trace, the shared phase reference; null without traces or a rate.</summary>
    public static IrPreviewTrace? Reference(IReadOnlyList<IrPreviewTrace> traces, int sampleRate) =>
        sampleRate <= 0
            ? null
            : traces.OrderBy(trace => VirtualCrossoverAnalysis.FindPeakIndex(trace.Samples)).FirstOrDefault();

    /// <summary>τ from the reference: the slope flattens the excess-phase trend, the peak references the dominant arrival.
    /// Null when there is no reference to read.</summary>
    public static (double SlopeMs, double PeakMs)? Tau(
        IReadOnlyList<IrPreviewTrace> traces, int sampleRate, VirtualCrossoverGatePreview gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        return Reference(traces, sampleRate) is { } reference
            ? DataHelper.EstimatePhaseDetrend(View(reference, sampleRate), Settings(gate))
            : null;
    }

    public static string AutoDetrendLabel(
        IReadOnlyList<IrPreviewTrace> traces, int sampleRate, VirtualCrossoverGatePreview gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        if (Reference(traces, sampleRate) is not { } reference)
        {
            return "Auto detrend: —";
        }

        double resolved = DataHelper.ResolveCommonPhaseDetrendMilliseconds(View(reference, sampleRate), Settings(gate));
        return $"Auto detrend: {resolved:0.00} ms, reference: {reference.Title}";
    }

    private static ImpulseMeasurementView View(IrPreviewTrace trace, int sampleRate) =>
        new(trace.Samples, VirtualCrossoverAnalysis.FindPeakIndex(trace.Samples), sampleRate);

    private static PhaseAnalysisSettings Settings(VirtualCrossoverGatePreview gate) =>
        new(
            gate.WindowMode, gate.FdwCycles, PhaseDetrendMode.Auto, gate.DetrendMs,
            gate.OffsetMs, gate.LeftMs, gate.PlateauMs, gate.RightMs, Unwrap: false, 0.0);
}
