using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A measurement prepared for Virtual DSP; the one conversion shared by file, history and restore flows.</summary>
internal sealed class ResolvedVirtualDspSource
{
    public required Complex[] TransferImpulseResponse { get; init; }
    public required int TransferPeakIndex { get; init; }
    public required int SampleRate { get; init; }
    public double[]? TransferCoherence { get; init; }
    public IReadOnlyList<SignalPoint>? DistortionCurve { get; init; }

    /// <summary>The array average this measurement carries; kept apart from the attached moving-mic capture (the project chooses).</summary>
    public LiveCaptureDocument? ArrayCapture { get; init; }

    public double[]? ArraySpreadDb { get; init; }

    public MeasuredBand MeasuredBand { get; init; } = MeasuredBand.Everything;

    /// <summary>Calibration recorded by the file; null for older measurements, which use the panel's selection.</summary>
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; init; }

    /// <summary>Null without a loopback transfer IR, or without absolute time (an imported sweep, a loopback on another clock): summing sums arrivals.</summary>
    public static ResolvedVirtualDspSource? FromResult(MeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Transfer is not { ImpulseResponse.Length: > 0 } transfer ||
            !result.TimingReference.HasAbsoluteTime())
        {
            return null;
        }

        Complex[] transferIr = transfer.ImpulseResponse;
        (LiveCaptureDocument? arrayCapture, double[]? arraySpreadDb) =
            ArrayCaptureDocument.TryCreateWithSpread(
                result.ArrayMicrophones,
                result.SampleRate,
                result.ProtectiveHighPass,
                result.MeasuredAtUtc);
        return new ResolvedVirtualDspSource
        {
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = Math.Clamp(transfer.PeakIndex, 0, transferIr.Length - 1),
            SampleRate = result.SampleRate,
            TransferCoherence = result.TransferCoherence,
            DistortionCurve = ComputeDistortionCurve(result),
            ArrayCapture = arrayCapture,
            ArraySpreadDb = arraySpreadDb,
            MeasuredBand = result.MeasuredBand,
            MicrophoneCalibration = result.MicrophoneCalibration
        };
    }

    public void ApplyTo(VirtualCrossoverChannelState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.TransferImpulseResponse = TransferImpulseResponse;
        state.TransferPeakIndex = TransferPeakIndex;
        state.SampleRate = SampleRate;
        state.TransferCoherence = TransferCoherence;
        state.DistortionCurve = DistortionCurve;
        state.ArrayCapture = ArrayCapture;
        state.ArraySpreadDb = ArraySpreadDb;
        state.MeasuredBand = MeasuredBand;
        state.MicrophoneCalibration = MicrophoneCalibration;
    }

    // THD (dB vs fundamental) for the crossover wizard; null without sweep deconvolution (wizard uses class-based range).
    private static IReadOnlyList<SignalPoint>? ComputeDistortionCurve(MeasurementResult result)
    {
        Complex[] ir = result.SweepDeconvolution.ImpulseResponse;
        if (ir.Length == 0 ||
            result.SampleRate <= 0 ||
            !double.IsFinite(result.SweepDurationSeconds) ||
            result.SweepDurationSeconds <= 0)
        {
            return null;
        }

        // ACHIEVED edges: harmonic packets sit at ln(h)/ln(ratio) of the sweep, so the requested band misplaces them.
        double lowHz = result.AchievedLowFrequencyHz;
        double highHz = result.AchievedHighFrequencyHz;
        if (!(lowHz > 0) || !(highHz > lowHz))
        {
            return null;
        }

        try
        {
            var sweep = new EssSweepMetadata(
                lowHz,
                highHz,
                result.SweepDurationSeconds,
                result.SampleRate,
                result.SweepSampleCount,
                result.SweepDeconvolution.PeakIndex);

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

/// <summary>Persisted reference to a side's source; written after an interactive pick (silent restore keeps the old one).</summary>
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
