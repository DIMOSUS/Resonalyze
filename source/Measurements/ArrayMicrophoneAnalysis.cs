using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One microphone of the spatial average. See docs/tech/sweep-measurement.md#array-microphones.</summary>
/// <param name="LevelsDb">Steady-state level on <see cref="SpatialAverage.BuildGrid"/>: high-pass divided out, NO mic calibration.</param>
internal sealed record ArrayMicrophoneCurve(
    int ChannelOffset,
    bool IsMeasurementMicrophone,
    double[] LevelsDb,
    int AcceptedRuns)
{
    public string? Note { get; init; }

    /// <summary>Carried for portability only; the analysis never applies it.</summary>
    public VirtualCrossoverCalibrationSettings? Calibration { get; init; }
}

internal sealed record ArrayMicrophoneMetadata(
    int ChannelOffset,
    string? Note,
    VirtualCrossoverCalibrationSettings? Calibration);

/// <summary>Array mic runs as true transfer functions against the shared loopback, through the measurement mic's estimator.</summary>
internal static class ArrayMicrophoneAnalysis
{
    /// <summary>No credibility verdict: RequireCredibleTransferIr judges these frames with a better diagnosis.</summary>
    public static double[] BuildMeasurementCurve(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass) =>
        BuildCurve(frames, excitationGate, sampleRate, protectiveHighPass, arrayInput: null);

    public static double[] BuildArrayCurve(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass,
        int channelOffset) =>
        BuildCurve(frames, excitationGate, sampleRate, protectiveHighPass, channelOffset);

    private static double[] BuildCurve(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass,
        int? arrayInput)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException(
                "A microphone needs at least one accepted run.",
                nameof(frames));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        (TransferMagnitudeEstimate estimate, Complex[]? transfer) =
            TransferFunction.ComputeAveragedMagnitudeAndIr(
                frames,
                excitationGate,
                wantImpulseResponse: arrayInput.HasValue);
        if (arrayInput is { } channelOffset)
        {
            RequireCredible(transfer, sampleRate, channelOffset);
        }
        double[] levels = SpatialAverage.FromTransferMagnitude(
            estimate.Magnitude,
            (double)sampleRate / estimate.FftLength);
        return RemoveProtectiveHighPass(levels, sampleRate, protectiveHighPass);
    }

    /// <summary>Refuses a live-but-wrong array channel (no arrival) on the measurement mic's compactness floor.</summary>
    private static void RequireCredible(
        Complex[]? transfer,
        int sampleRate,
        int channelOffset)
    {
        if (DescribeIncredibleResponse(transfer, sampleRate) is not { } shape)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The array microphone on input {channelOffset + 1} recorded a signal, but " +
            $"it did not divide into a credible response: {shape}. It is live and " +
            "wrong rather than absent — a hissing preamp, the wrong socket, or a " +
            "failed capsule — and averaging it in would give a channel that measured " +
            "nothing a full share of the result. Check that input, then measure again.");
    }

    /// <summary>Single definition of the verdict, shared by the run check and the averaged backstop.</summary>
    public static string? DescribeIncredibleResponse(
        Complex[]? transfer,
        int sampleRate,
        double floorDb = TransferIrDiagnostics.MinimumCompactnessDb) =>
        DescribeIncredibleShape(
            transfer is null ? null : TransferIrDiagnostics.MeasureCompactness(transfer, sampleRate),
            floorDb);

    public static string? DescribeIncredibleShape(
        TransferIrCompactness? compactness,
        double floorDb = TransferIrDiagnostics.MinimumCompactnessDb)
    {
        // Fail-closed: an unmeasurable shape is a refusal.
        if (compactness is { } measured &&
            double.IsFinite(measured.InsideOutsideDb) &&
            measured.InsideOutsideDb >= floorDb)
        {
            return null;
        }

        return compactness is { } value && double.IsFinite(value.InsideOutsideDb)
            ? FormattableString.Invariant(
                $"the energy around its peak is only {value.InsideOutsideDb:0.0} dB above the rest of the capture (a real measurement reads 29-49 dB)")
            : "its shape could not be measured at all";
    }

    /// <summary>Per-run floor lowered by 10*log10(N): averaging lifts noise-surrounded arrivals by up to that. See docs/tech/sweep-measurement.md#array-microphones.</summary>
    public static double RunFloorDb(int averagedRuns) =>
        TransferIrDiagnostics.MinimumCompactnessDb -
        10.0 * Math.Log10(Math.Max(1, averagedRuns));

    /// <summary>Divides the protective high-pass out (mics hear it, the loopback does not); same model as the IR path.</summary>
    private static double[] RemoveProtectiveHighPass(
        double[] levelsDb,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass)
    {
        if (protectiveHighPass is not { Enabled: true } filter)
        {
            return levelsDb;
        }

        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            filter.ToEdge(),
            sampleRate,
            ProtectiveHighPassConfiguration.MaximumCompensationBoostDb,
            SpatialAverage.BuildGrid());
        for (int band = 0; band < levelsDb.Length; band++)
        {
            // NaN where the filter cannot be inverted: nothing to recover, nothing an EQ should fill.
            levelsDb[band] = double.IsFinite(levelsDb[band]) && double.IsFinite(correction[band])
                ? levelsDb[band] + correction[band]
                : double.NaN;
        }

        return levelsDb;
    }
}
