using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Junction read-out spectra: each channel through the panel's own gate at an 8-cycle FDW, re-referenced to one time origin.
/// See docs/tech/junction-phase-and-group-placement.md#junction-read-out-spectra.</summary>
internal static class JunctionPhaseSpectra
{
    /// <summary>Fixed 8 cycles, not the dialog's 4/6/8 selector (4 cycles moved φ a median 36° against the steady-state reference, 8 cycles 5°).</summary>
    public const int FdwCycles = 8;

    public static List<Complex[]> Build(
        IReadOnlyList<ProcessedChannel> channels,
        int sampleRate,
        double? pinnedOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs)
    {
        ArgumentNullException.ThrowIfNull(channels);
        IReadOnlyList<PlacementChannel> placement = PlacementChannel.From(channels);
        double sharedOffsetMs = PhaseGatePlacement.ResolveSharedOffsetMs(
            placement, sampleRate, pinnedOffsetMs);
        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            placement, sharedOffsetMs, sampleRate, pinnedOffsetMs,
            leftMs, plateauMs, rightMs);
        var template = new PhaseAnalysisSettings(
            PhaseWindowMode.FrequencyDependent,
            FdwCycles,
            // No detrend: a shared τ cancels from the cross-phase, a per-channel one would BE the answer.
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GateOffsetMs: 0.0,
            leftMs,
            plateauMs,
            rightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        var spectra = new List<Complex[]>(channels.Count);
        for (int i = 0; i < channels.Count; i++)
        {
            ProcessedChannel channel = channels[i];
            Complex[] gated = DataHelper.GetPhaseAnalysisSpectrum(
                new ImpulseMeasurementView(
                    channel.ImpulseResponse, 0, channel.SampleRate),
                template with { GateOffsetMs = offsets[i] },
                out int extractionStart);
            spectra.Add(DataHelper.SumGatedSpectra(
                [(gated, extractionStart)], targetExtractionStart: 0));
        }

        return spectra;
    }
}
