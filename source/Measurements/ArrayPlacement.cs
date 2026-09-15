using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One placement of an array on the anchor, read calibrated and uncalibrated. See docs/tech/sweep-measurement.md#array-placement.</summary>
internal sealed record ArrayPlacement(
    IReadOnlyList<double[]?> CalibratedDb,
    IReadOnlyList<double[]?> RawDb,
    IReadOnlyList<double?> TrimsDb,
    double[] CalibratedAverageDb,
    double[] RawAverageDb,
    double[] CalibratedSpreadDb,
    double[] RawSpreadDb)
{
    public int PlacedCount => TrimsDb.Count(trim => trim != null);

    /// <summary>Null when a curve is not on this build's grid.</summary>
    public static ArrayPlacement? Resolve(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        IReadOnlyList<double> grid)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        ArgumentNullException.ThrowIfNull(grid);
        if (microphones.Count == 0)
        {
            return null;
        }

        var calibrated = new List<IReadOnlyList<double>>(microphones.Count);
        var corrections = new List<double[]>(microphones.Count);
        int anchorIndex = -1;
        for (int i = 0; i < microphones.Count; i++)
        {
            if (microphones[i].LevelsDb.Length != grid.Count)
            {
                return null;
            }

            double[] correction = CorrectionOf(microphones[i], grid);
            corrections.Add(correction);
            double[] levels = microphones[i].LevelsDb;
            var curve = new double[grid.Count];
            for (int band = 0; band < curve.Length; band++)
            {
                curve[band] = double.IsFinite(levels[band])
                    ? levels[band] - correction[band]
                    : double.NaN;
            }

            calibrated.Add(curve);
            if (microphones[i].IsMeasurementMicrophone && anchorIndex < 0)
            {
                anchorIndex = i;
            }
        }

        // Anchor = measurement mic (tied to SPL and the IR); a set without one levels onto its first.
        SpatialAverageResult placed = SpatialAverage.Average(
            calibrated, anchorIndex < 0 ? 0 : anchorIndex);

        var raw = new double[]?[microphones.Count];
        var rawPlaced = new List<double[]>(microphones.Count);
        for (int i = 0; i < microphones.Count; i++)
        {
            if (placed.TrimmedCurvesDb[i] is not { } curve)
            {
                continue;
            }

            double[] correction = corrections[i];
            var uncalibrated = new double[curve.Length];
            for (int band = 0; band < curve.Length; band++)
            {
                uncalibrated[band] = double.IsFinite(curve[band])
                    ? curve[band] + correction[band]
                    : double.NaN;
            }

            raw[i] = uncalibrated;
            rawPlaced.Add(uncalibrated);
        }

        return new ArrayPlacement(
            placed.TrimmedCurvesDb,
            raw,
            placed.TrimsDb,
            placed.AverageDb,
            SpatialAverage.RmsAverageDb(rawPlaced),
            placed.SpreadDb,
            SpatialAverage.SpreadDb(rawPlaced));
    }

    /// <summary>Per-band correction baked into <see cref="CalibratedAverageDb"/> (subtracted; add back to undo), measured as the difference of the two averages.</summary>
    public double[] CorrectionDb()
    {
        var correction = new double[CalibratedAverageDb.Length];
        for (int band = 0; band < correction.Length; band++)
        {
            double calibrated = CalibratedAverageDb[band];
            double raw = RawAverageDb[band];
            // Zero, not NaN, where unmeasured: NaN would spread into whatever undoes it.
            correction[band] = double.IsFinite(calibrated) && double.IsFinite(raw)
                ? raw - calibrated
                : 0.0;
        }

        return correction;
    }

    public static bool IsMixed(IReadOnlyList<ArrayMicrophoneCurve> microphones) =>
        SharedCalibration(microphones) == null &&
        microphones.Any(microphone => microphone.Calibration != null);

    public static VirtualCrossoverCalibrationSettings? SharedCalibration(
        IReadOnlyList<ArrayMicrophoneCurve> microphones)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        if (microphones.Count == 0)
        {
            return null;
        }

        VirtualCrossoverCalibrationSettings? first = microphones[0].Calibration;
        if (first == null)
        {
            return null;
        }

        CalibrationFile firstCurve = first.ToCalibrationFile();
        foreach (ArrayMicrophoneCurve microphone in microphones)
        {
            if (microphone.Calibration is not { } settings ||
                !CalibrationFile.SameCurve(settings.ToCalibrationFile(), firstCurve))
            {
                return null;
            }
        }

        return first;
    }

    private static double[] CorrectionOf(
        ArrayMicrophoneCurve microphone,
        IReadOnlyList<double> grid)
    {
        var correction = new double[grid.Count];
        if (microphone.Calibration is not { } settings)
        {
            return correction;
        }

        CalibrationFile curve = settings.ToCalibrationFile();
        for (int band = 0; band < correction.Length; band++)
        {
            correction[band] = curve.GetDecibelCorrection(grid[band]);
        }

        return correction;
    }
}
