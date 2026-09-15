using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.History;

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

    /// <summary>Null without a loopback transfer IR, or for an imported sweep: its arrival is set by when the recorder started.</summary>
    public static ResolvedVirtualDspSource? FromSnapshot(MeasurementHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TransferImpulseResponse is not { Length: > 0 } transferIr ||
            snapshot.TimingReference == TimingReference.RecordedSweep)
        {
            return null;
        }

        (LiveCaptureDocument? arrayCapture, double[]? arraySpreadDb) =
            ArrayCaptureDocument.TryCreateWithSpread(
                snapshot.ArrayMicrophones,
                snapshot.SampleRate,
                snapshot.ProtectiveHighPass,
                snapshot.MeasuredAtUtc);
        return new ResolvedVirtualDspSource
        {
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = Math.Clamp(
                snapshot.TransferPeakIndex ?? 0, 0, transferIr.Length - 1),
            SampleRate = snapshot.SampleRate,
            TransferCoherence = snapshot.TransferCoherence,
            DistortionCurve = ComputeDistortionCurve(snapshot),
            ArrayCapture = arrayCapture,
            ArraySpreadDb = arraySpreadDb,
            MeasuredBand = MeasuredBand.Resolve(
                snapshot.ProtectiveHighPass,
                snapshot.MeasuredLowFrequencyHz,
                snapshot.MeasuredHighFrequencyHz,
                snapshot.SampleRate),
            MicrophoneCalibration = snapshot.MicrophoneCalibration
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
    private static IReadOnlyList<SignalPoint>? ComputeDistortionCurve(
        MeasurementHistorySnapshot snapshot)
    {
        if (snapshot.SweepDeconvolutionImpulseResponse is not { Length: > 0 } ir ||
            snapshot.SampleRate <= 0 ||
            !double.IsFinite(snapshot.SweepDurationSeconds) ||
            snapshot.SweepDurationSeconds <= 0 ||
            (snapshot.AchievedHighFrequencyHz <= 0 &&
                snapshot.HighFrequencyHz <= 0 &&
                snapshot.Octaves <= 0))
        {
            return null;
        }

        // ACHIEVED edges: harmonic packets sit at ln(h)/ln(ratio) of the sweep, so the requested band misplaces them.
        (double lowHz, double highHz) = snapshot.ResolveAchievedSweepBand();
        if (!(lowHz > 0) || !(highHz > lowHz))
        {
            return null;
        }

        try
        {
            int sweepSamples = (int)Math.Round(snapshot.SweepDurationSeconds * snapshot.SampleRate);
            var sweep = new EssSweepMetadata(
                lowHz,
                highHz,
                snapshot.SweepDurationSeconds,
                snapshot.SampleRate,
                sweepSamples,
                snapshot.SweepDeconvolutionPeakIndex);

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
