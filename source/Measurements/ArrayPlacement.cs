using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One array's microphones placed on the anchor's level, in both readings: each
/// position through its own calibration, and the same positions with those
/// corrections removed again.
/// </summary>
/// <remarks>
/// It exists because the two readings must not be two placements. A trim is the
/// answer to "how much more sensitive is this capsule", measured as the median
/// difference from the anchor over the driver's working band — and it is answered
/// on the CALIBRATED curves, where a level difference is a level difference rather
/// than a difference between two microphones' responses. Turning a view's
/// calibration off asks to see different curves; it does not ask to re-measure
/// where the microphones sat.
/// <para>
/// Deriving the placement twice is what the frequency response and the Virtual DSP
/// used to do, and on a mixed array they parted by three decibels while each
/// looked entirely reasonable. It also broke the document's contract:
/// <c>CalibrationCorrectionDb</c> promises that ADDING it back gives the level that
/// was measured, and no correction on an average can undo a different placement.
/// </para>
/// </remarks>
internal sealed record ArrayPlacement(
    IReadOnlyList<double[]?> CalibratedDb,
    IReadOnlyList<double[]?> RawDb,
    IReadOnlyList<double?> TrimsDb,
    double[] CalibratedAverageDb,
    double[] RawAverageDb,
    double[] CalibratedSpreadDb,
    double[] RawSpreadDb)
{
    /// <summary>How many positions could be placed at all.</summary>
    public int PlacedCount => TrimsDb.Count(trim => trim != null);

    /// <summary>
    /// Places the array, or null when a curve does not belong to this build's grid.
    /// </summary>
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
                // A curve from a grid this build does not use cannot be placed beside
                // the others without shifting it in frequency.
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

        // The measurement microphone is the anchor: its level is the one tied to the
        // SPL calibration and to the impulse response. A set without one — an
        // imported array, say — levels onto its first.
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

        // rawPlaced is never empty: the anchor's own trim is zero rather than null,
        // so at least one position always places.
        return new ArrayPlacement(
            placed.TrimmedCurvesDb,
            raw,
            placed.TrimsDb,
            placed.AverageDb,
            SpatialAverage.RmsAverageDb(rawPlaced),
            placed.SpreadDb,
            SpatialAverage.SpreadDb(rawPlaced));
    }

    /// <summary>
    /// The correction baked into <see cref="CalibratedAverageDb"/>, per band, in the
    /// convention the pipeline uses — it SUBTRACTS, so undoing means adding back.
    /// </summary>
    /// <remarks>
    /// Measured rather than copied from a file, and that is the whole point: each
    /// position is corrected by its OWN curve before the averaging, so when they
    /// carry different files there is no single correction to name. The difference
    /// between the two averages IS that correction, exactly, for a matched array and
    /// a mixed one alike — and exact only because both averages stand on one
    /// placement.
    /// </remarks>
    public double[] CorrectionDb()
    {
        var correction = new double[CalibratedAverageDb.Length];
        for (int band = 0; band < correction.Length; band++)
        {
            double calibrated = CalibratedAverageDb[band];
            double raw = RawAverageDb[band];
            // Zero rather than NaN where nothing was measured: a correction of zero
            // is the honest "nothing was subtracted here", and a NaN would spread out
            // of the gap into whatever undoes it.
            correction[band] = double.IsFinite(calibrated) && double.IsFinite(raw)
                ? raw - calibrated
                : 0.0;
        }

        return correction;
    }

    /// <summary>
    /// Whether the positions were corrected by more than one calibration, so the
    /// aggregate above belongs to no single microphone.
    /// </summary>
    public static bool IsMixed(IReadOnlyList<ArrayMicrophoneCurve> microphones) =>
        SharedCalibration(microphones) == null &&
        microphones.Any(microphone => microphone.Calibration != null);

    /// <summary>
    /// The one calibration every position shared, or null when they did not share one.
    /// </summary>
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

    // The microphone's own correction sampled onto the shared grid, or zeros when it
    // carries none.
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
