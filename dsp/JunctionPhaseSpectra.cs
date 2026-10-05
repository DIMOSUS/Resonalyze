using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>Junction read-out spectra: each channel through the given gate at an 8-cycle FDW, re-referenced to one time origin.
/// See docs/tech/junction-phase-and-group-placement.md#junction-read-out-spectra.</summary>
public static class JunctionPhaseSpectra
{
    /// <summary>Fixed 8 cycles, not the dialog's 4/6/8 selector (4 cycles moved φ a median 36° against the steady-state reference, 8 cycles 5°).</summary>
    public const int FdwCycles = 8;

    /// <param name="sampleRate">Decides the placement's sample rounding; each spectrum is transformed at its own channel's rate.</param>
    public static List<Complex[]> Build(
        IReadOnlyList<PlacementChannel> channels,
        IReadOnlyList<int> channelSampleRates,
        int sampleRate,
        double? pinnedOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(channelSampleRates);
        if (channelSampleRates.Count != channels.Count)
        {
            throw new ArgumentException("One sample rate per channel is required.", nameof(channelSampleRates));
        }

        double sharedOffsetMs = PhaseGatePlacement.ResolveSharedOffsetMs(
            channels, sampleRate, pinnedOffsetMs);
        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            channels, sharedOffsetMs, sampleRate, pinnedOffsetMs,
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
            Complex[] gated = DataHelper.GetPhaseAnalysisSpectrum(
                new RecordOriginMeasurement(channels[i].ImpulseResponse, channelSampleRates[i]),
                template with { GateOffsetMs = offsets[i] },
                out int extractionStart);
            spectra.Add(DataHelper.SumGatedSpectra(
                [(gated, extractionStart)], targetExtractionStart: 0));
        }

        return spectra;
    }
}
