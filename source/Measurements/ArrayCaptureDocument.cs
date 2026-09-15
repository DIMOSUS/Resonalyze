using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Presents a measurement's array as a <see cref="LiveCaptureDocument"/>. See docs/tech/sweep-measurement.md#array-capture-document.</summary>
internal static class ArrayCaptureDocument
{
    public static LiveCaptureDocument? TryCreate(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        int sampleRateHz,
        ProtectiveHighPassConfiguration? protectiveHighPass,
        DateTimeOffset? measuredAtUtc = null) =>
        TryCreateWithSpread(microphones, sampleRateHz, protectiveHighPass, measuredAtUtc)
            .Document;

    /// <param name="measuredAtUtc">Null only for a measurement taken now; a loaded one must keep its own date.</param>
    public static (LiveCaptureDocument? Document, double[]? SpreadDb) TryCreateWithSpread(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        int sampleRateHz,
        ProtectiveHighPassConfiguration? protectiveHighPass,
        DateTimeOffset? measuredAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        if (ArrayPlacement.Resolve(microphones, grid) is not { } placed)
        {
            return (null, null);
        }

        // Fewer than two placed positions is a point measurement, not a spatial average.
        if (placed.PlacedCount < 2)
        {
            return (null, null);
        }

        var placedMicrophones = new List<ArrayMicrophoneCurve>(placed.PlacedCount);
        for (int i = 0; i < microphones.Count; i++)
        {
            if (placed.TrimsDb[i] != null)
            {
                placedMicrophones.Add(microphones[i]);
            }
        }

        return (new LiveCaptureDocument
        {
            Format = LiveCaptureDocument.CurrentFormat,
            Version = LiveCaptureDocument.CurrentVersion,
            SavedAtUtc = measuredAtUtc ?? DateTimeOffset.UtcNow,
            // Counts PLACED positions, not configured ones.
            Title = placed.PlacedCount == 1
                ? "Array of 1 microphone"
                : $"Array of {placed.PlacedCount} microphones",
            Method = SpatialAverageMethod.MicArray,
            CaptureSessionId = Guid.NewGuid(),
            Recipe = BuildRecipe(sampleRateHz, placed.PlacedCount, protectiveHighPass),
            // Only placed positions vote, or a non-contributing mic would make the set a false aggregate.
            Calibration = ArrayPlacement.SharedCalibration(placedMicrophones),
            CurveDb = placed.CalibratedAverageDb,
            CalibrationCorrectionDb = placed.CorrectionDb(),
            CalibrationIsAggregate = ArrayPlacement.IsMixed(placedMicrophones),
            GridStartHz = grid[0],
            GridStopHz = grid[^1]
        },
        placed.CalibratedSpreadDb);
    }

    /// <remarks>Analyzer fields stay default on purpose: an array has no analyzer.</remarks>
    private static LiveCaptureRecipe BuildRecipe(
        int sampleRateHz,
        int microphoneCount,
        ProtectiveHighPassConfiguration? protectiveHighPass)
    {
        ProtectiveHighPassConfiguration filter =
            ProtectiveHighPassConfiguration.Normalize(protectiveHighPass);
        return new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            SampleRateHz = sampleRateHz,
            MicrophoneCount = microphoneCount,
            MagnitudeScale = MagnitudeScale.Relative,
            SlopeCompensation = false,
            SmoothingCode = 0,
            ProtectiveHighPassKind = filter.Kind,
            ProtectiveHighPassFrequencyHz = filter.FrequencyHz,
            ProtectiveHighPassSlopeDbPerOctave = filter.SlopeDbPerOctave
        };
    }
}
