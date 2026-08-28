using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>
/// A measurement prepared for the Virtual DSP tool: the validated loopback
/// transfer IR plus the derived data every side needs. Built once from a
/// measurement snapshot — a picked file, a history entry, or a persisted
/// reference on restore — then written into a channel side's runtime state.
/// One <see cref="FromSnapshot"/>/<see cref="ApplyTo"/> pair for all three
/// paths, so the file, history and restore flows share the conversion instead of
/// each hand-rolling a copy of it.
/// </summary>
internal sealed class ResolvedVirtualDspSource
{
    public required Complex[] TransferImpulseResponse { get; init; }
    public required int TransferPeakIndex { get; init; }
    public required int SampleRate { get; init; }
    public double[]? TransferCoherence { get; init; }
    public IReadOnlyList<SignalPoint>? DistortionCurve { get; init; }

    /// <summary>
    /// The spatial average this measurement carries in itself, when it was recorded
    /// with a microphone array; null otherwise.
    /// </summary>
    /// <remarks>
    /// Kept apart from the attached moving-microphone capture rather than folded
    /// into one slot: they are two sources of the same quantity, tethered
    /// differently, and which of them a project uses is the project's choice. A
    /// channel that has both keeps both, and switching the method does not have to
    /// re-read anything.
    /// </remarks>
    public LiveCaptureDocument? ArrayCapture { get; init; }

    /// <summary>
    /// The spread between <see cref="ArrayCapture"/>'s microphones, band by band.
    /// </summary>
    public double[]? ArraySpreadDb { get; init; }

    /// <summary>
    /// What this measurement actually measured, from the protective high-pass
    /// divided back out of it and the band its sweep swept.
    /// </summary>
    public MeasuredBand MeasuredBand { get; init; } = MeasuredBand.Everything;

    /// <summary>
    /// The microphone calibration this measurement was read through, as its file
    /// recorded it. Null when the file names none — every measurement written before
    /// the format carried one, which is why the panel's own selection is still the
    /// answer for those.
    /// </summary>
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration { get; init; }

    /// <summary>
    /// Prepares a source from a measurement snapshot, or returns null when the
    /// snapshot has no loopback transfer IR — the virtual sum only has physical
    /// meaning for loopback-referenced responses — or when it was imported from a
    /// recorded sweep, which is the same objection in a different form: summing
    /// two drivers is summing their arrivals, and an imported measurement's
    /// arrival is set by when its recorder was started.
    /// </summary>
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

    /// <summary>Writes the prepared measurement data into a channel side's slot.</summary>
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

    // Computes the channel's harmonic distortion (THD, dB vs the fundamental) from
    // a source's sweep deconvolution, for the crossover wizard's distortion-clean
    // band read. Returns null when the source carried no sweep deconvolution (only a
    // loopback transfer) or the sweep metadata is missing — the wizard then falls
    // back to the class-based sensible range.
    private static IReadOnlyList<SignalPoint>? ComputeDistortionCurve(
        MeasurementHistorySnapshot snapshot)
    {
        if (snapshot.SweepDeconvolutionImpulseResponse is not { Length: > 0 } ir ||
            snapshot.SampleRate <= 0 ||
            !double.IsFinite(snapshot.SweepDurationSeconds) ||
            snapshot.SweepDurationSeconds <= 0 ||
            // No sweep band recorded (neither explicit band nor legacy octaves):
            // the wizard falls back to the class-based range rather than a
            // fabricated one.
            (snapshot.AchievedHighFrequencyHz <= 0 &&
                snapshot.HighFrequencyHz <= 0 &&
                snapshot.Octaves <= 0))
        {
            return null;
        }

        // The ACHIEVED edges: harmonic packets sit at ln(harmonic)/ln(ratio) of
        // the sweep, so the requested band would place them wrong by the width of
        // the guard bands.
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
