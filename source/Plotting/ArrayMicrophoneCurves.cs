using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// What the frequency-response view draws for a measurement's array: the spatial
/// average, the individual positions behind it, and how far apart they were.
/// </summary>
/// <param name="Average">
/// The curve the array exists to produce. Null when the measurement has no array,
/// or when nothing in it could be placed.
/// </param>
/// <param name="Microphones">
/// The positions themselves, in the order they were recorded, each on the
/// anchor's level. Drawn thin behind the average: they are what the average is
/// made of, and a microphone that disagrees with the rest is a thing to see.
/// </param>
/// <param name="Spread">
/// The loudest position minus the quietest, per frequency — a dB RANGE and not a
/// level, which is why it belongs on an axis of its own. Null below two placed
/// microphones: one position has no spread, and a spread of zero would read as
/// perfect agreement.
/// </param>
internal sealed record ArrayMicrophoneDisplay(
    AnalysisCurve? Average,
    IReadOnlyList<AnalysisCurve> Microphones,
    AnalysisCurve? Spread);

/// <summary>
/// Turns a measurement's stored array microphones into drawable curves.
/// </summary>
/// <remarks>
/// The stored curves are raw — uncalibrated, unsmoothed, untrimmed — so every
/// step happens here, and the ORDER of two of them was settled by measurement
/// rather than by taste.
/// <para>
/// The average is taken over RAW curves and smoothed afterwards, never the other
/// way round. Smoothing is not interchangeable with averaging: the spatial
/// average is a mean of power across positions, while the psychoacoustic
/// smoothing is a cubic mean of amplitude across frequency (it favours peaks, as
/// the ear does), and a cubic mean does not commute with a quadratic one.
/// Measured on a seven-position field set, smoothing first read 0.11 dB high on a
/// midrange and 0.39 dB high on a tweeter, and sat further from what a moving
/// microphone measures. It is also the order the moving microphone itself works
/// in: it accumulates power, and its smoothing is a separate second pass.
/// </para>
/// </remarks>
internal static class ArrayMicrophoneCurves
{
    public static readonly ArrayMicrophoneDisplay Empty = new(null, [], null);

    /// <param name="useCalibration">
    /// Whether the view is showing calibrated curves. Each microphone is corrected
    /// by its OWN stored calibration — an array is not required to be one model of
    /// capsule — but whether any of them is corrected at all follows the view, so
    /// turning calibration off means the same thing for these curves as for every
    /// other one on the plot.
    /// </param>
    public static ArrayMicrophoneDisplay Build(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        bool useCalibration,
        double smoothingInverseOctaves)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        if (microphones.Count == 0)
        {
            return Empty;
        }

        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        var calibrated = new List<double[]>(microphones.Count);
        int anchorIndex = -1;
        for (int i = 0; i < microphones.Count; i++)
        {
            if (microphones[i].LevelsDb.Length != grid.Count)
            {
                // A curve from a grid this build does not use cannot be placed
                // beside the others; drawing it anyway would shift it in frequency.
                return Empty;
            }

            calibrated.Add(Calibrate(microphones[i], grid, useCalibration));
            if (microphones[i].IsMeasurementMicrophone && anchorIndex < 0)
            {
                anchorIndex = i;
            }
        }

        // The measurement microphone is the anchor because its level is the one
        // tied to the SPL calibration and to the impulse response. A set without
        // it — every microphone of an imported array, say — levels onto its first.
        SpatialAverageResult placed = SpatialAverage.Average(
            calibrated.Select(curve => (IReadOnlyList<double>)curve).ToList(),
            anchorIndex < 0 ? 0 : anchorIndex);

        double smoothing = SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves);
        bool psychoacoustic = SpectrumSmoothing.IsPsychoacoustic(smoothingInverseOctaves);

        var drawn = new List<AnalysisCurve>(microphones.Count);
        int placedCount = 0;
        for (int i = 0; i < microphones.Count; i++)
        {
            if (placed.TrimmedCurvesDb[i] is not { } curve)
            {
                continue;
            }

            placedCount++;
            drawn.Add(new AnalysisCurve(
                DescribeMicrophone(microphones[i]),
                Smooth(grid, curve, smoothing, psychoacoustic),
                AnalysisCurveKind.ArrayMicrophone));
        }

        if (placedCount == 0)
        {
            return Empty;
        }

        var average = new AnalysisCurve(
            "Array average",
            Smooth(grid, placed.AverageDb, smoothing, psychoacoustic),
            AnalysisCurveKind.ArrayAverage);
        AnalysisCurve? spread = placedCount < 2
            ? null
            : new AnalysisCurve(
                "Array spread",
                SmoothSpread(grid, placed.SpreadDb, smoothing, psychoacoustic),
                AnalysisCurveKind.ArraySpread);
        return new ArrayMicrophoneDisplay(average, drawn, spread);
    }

    private static string DescribeMicrophone(ArrayMicrophoneCurve microphone)
    {
        string where = string.IsNullOrWhiteSpace(microphone.Note)
            ? $"Input {microphone.ChannelOffset + 1}"
            : microphone.Note!;
        return microphone.IsMeasurementMicrophone ? $"{where} (measurement)" : where;
    }

    // The stored curve is uncalibrated, and the pipeline SUBTRACTS a microphone
    // correction from a level, so applying one here means subtracting it too.
    private static double[] Calibrate(
        ArrayMicrophoneCurve microphone,
        IReadOnlyList<double> grid,
        bool useCalibration)
    {
        double[] levels = microphone.LevelsDb.ToArray();
        if (!useCalibration || microphone.Calibration is not { } settings)
        {
            return levels;
        }

        CalibrationFile calibration = settings.ToCalibrationFile();
        for (int band = 0; band < levels.Length; band++)
        {
            if (double.IsFinite(levels[band]))
            {
                levels[band] -= calibration.GetDecibelCorrection(grid[band]);
            }
        }

        return levels;
    }

    private static IReadOnlyList<SignalPoint> Smooth(
        IReadOnlyList<double> grid,
        double[] levels,
        double smoothingOctaves,
        bool psychoacoustic) =>
        DataHelper.SmoothBandLevels(Points(grid, levels), smoothingOctaves, psychoacoustic);

    // A spread is a dB DIFFERENCE, not a level, so it takes the ratio smoother:
    // the magnitude path averages power and weights peaks with a cubic mean,
    // which on a difference reads as a bias toward whichever side of the window
    // is closer to zero.
    private static IReadOnlyList<SignalPoint> SmoothSpread(
        IReadOnlyList<double> grid,
        double[] spread,
        double smoothingOctaves,
        bool psychoacoustic) =>
        DataHelper.SmoothRatioLevels(Points(grid, spread), smoothingOctaves, psychoacoustic);

    private static List<SignalPoint> Points(IReadOnlyList<double> grid, double[] values)
    {
        var points = new List<SignalPoint>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            points.Add(new SignalPoint(grid[i], values[i]));
        }

        return points;
    }
}
