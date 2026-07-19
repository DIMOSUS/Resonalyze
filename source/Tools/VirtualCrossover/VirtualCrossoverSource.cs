using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// A measurement prepared for the Virtual DSP tool: the validated loopback
/// transfer IR plus the derived data every side needs. Built once from a
/// measurement snapshot — a picked file, a history entry, or a persisted
/// reference on restore — then written into a channel side's runtime state.
/// The single <see cref="FromSnapshot"/>/<see cref="ApplyTo"/> pair replaces the
/// per-path copies the file, history and restore flows used to each hand-roll.
/// </summary>
internal sealed class ResolvedVirtualDspSource
{
    public required Complex[] TransferImpulseResponse { get; init; }
    public required int TransferPeakIndex { get; init; }
    public required int SampleRate { get; init; }
    public double[]? TransferCoherence { get; init; }
    public IReadOnlyList<SignalPoint>? DistortionCurve { get; init; }

    /// <summary>
    /// Prepares a source from a measurement snapshot, or returns null when the
    /// snapshot has no loopback transfer IR — the virtual sum only has physical
    /// meaning for loopback-referenced responses.
    /// </summary>
    public static ResolvedVirtualDspSource? FromSnapshot(MeasurementHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TransferImpulseResponse is not { Length: > 0 } transferIr)
        {
            return null;
        }

        return new ResolvedVirtualDspSource
        {
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = Math.Clamp(
                snapshot.TransferPeakIndex ?? 0, 0, transferIr.Length - 1),
            SampleRate = snapshot.SampleRate,
            TransferCoherence = snapshot.TransferCoherence,
            DistortionCurve = ComputeDistortionCurve(snapshot)
        };
    }

    /// <summary>Writes the prepared measurement data into a channel side's slot.</summary>
    public void ApplyTo(VirtualCrossoverChannelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.TransferImpulseResponse = TransferImpulseResponse;
        state.TransferPeakIndex = TransferPeakIndex;
        state.SampleRate = SampleRate;
        state.TransferCoherence = TransferCoherence;
        state.DistortionCurve = DistortionCurve;
    }

    // Computes the channel's harmonic distortion (THD, dB vs the fundamental) from
    // a source's sweep deconvolution, for the crossover wizard's distortion-clean
    // band read. Returns null when the source carried no sweep deconvolution (only a
    // loopback transfer) or the sweep metadata is missing — the wizard then falls
    // back to the class-based sensible range.
    private static IReadOnlyList<SignalPoint>? ComputeDistortionCurve(
        MeasurementHistorySnapshot snapshot)
    {
        if (snapshot.SweepDeconvolutionImpulseResponse is not { Length: > 0 } ir ||
            snapshot.Octaves <= 0 ||
            snapshot.SampleRate <= 0 ||
            !double.IsFinite(snapshot.SweepDurationSeconds) ||
            snapshot.SweepDurationSeconds <= 0)
        {
            return null;
        }

        try
        {
            int sweepSamples = (int)Math.Round(snapshot.SweepDurationSeconds * snapshot.SampleRate);
            var sweep = EssSweepMetadata.FromExponentialSweep(
                snapshot.SampleRate, snapshot.Octaves, sweepSamples, snapshot.SweepDeconvolutionPeakIndex);

            double[] real = new double[ir.Length];
            for (int i = 0; i < ir.Length; i++)
            {
                real[i] = ir[i].Real;
            }

            EssHarmonicDecomposition decomposition = EssHarmonicAnalysis.AnalyzeEssHarmonics(
                real, sweep, new HarmonicAnalysisOptions(MaxHarmonic: 5));
            DistortionSpectrum spectrum = EssDistortion.ComputeDistortion(
                decomposition, calibration: null, new DistortionOptions(MaxHarmonic: 5));

            var points = new List<SignalPoint>(spectrum.Frequencies.Length);
            for (int i = 0; i < spectrum.Frequencies.Length; i++)
            {
                double thd = spectrum.ThdRatio[i];
                points.Add(new SignalPoint(
                    spectrum.Frequencies[i],
                    double.IsFinite(thd) && thd > 0.0 ? 20.0 * Math.Log10(thd) : double.NaN));
            }

            return points;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// The persisted reference to a channel side's source: the display name plus the
/// history entry and/or file path it re-resolves from. Written as a unit after an
/// interactive pick lands (the silent restore keeps the existing reference).
/// </summary>
internal sealed record VirtualCrossoverSourceReference(
    string DisplayName,
    string? SourceFilePath,
    Guid? HistoryEntryId)
{
    public void ApplyTo(VirtualCrossoverChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DisplayName = DisplayName;
        settings.SourceFilePath = SourceFilePath;
        settings.HistoryEntryId = HistoryEntryId;
    }
}
