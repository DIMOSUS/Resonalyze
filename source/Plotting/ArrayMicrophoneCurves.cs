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
    /// <para>
    /// It chooses which of two READINGS to draw and never re-places the array: both
    /// come off one <see cref="ArrayPlacement"/>, computed on the calibrated curves.
    /// Deriving a second placement from the raw ones — which is what this used to do
    /// — made the same mixed array read three decibels apart here and in the Virtual
    /// DSP, each answer looking perfectly reasonable on its own.
    /// </para>
    /// </param>
    public static ArrayMicrophoneDisplay Build(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        bool useCalibration,
        double smoothingInverseOctaves)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        if (ArrayPlacement.Resolve(microphones, grid) is not { } placed)
        {
            return Empty;
        }

        IReadOnlyList<double[]?> curves = useCalibration ? placed.CalibratedDb : placed.RawDb;
        double[] average = useCalibration
            ? placed.CalibratedAverageDb
            : placed.RawAverageDb;
        double[] spreadDb = useCalibration
            ? placed.CalibratedSpreadDb
            : placed.RawSpreadDb;

        double smoothing = SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves);
        bool psychoacoustic = SpectrumSmoothing.IsPsychoacoustic(smoothingInverseOctaves);

        var drawn = new List<AnalysisCurve>(microphones.Count);
        for (int i = 0; i < microphones.Count; i++)
        {
            if (curves[i] is not { } curve)
            {
                continue;
            }

            drawn.Add(new AnalysisCurve(
                DescribeMicrophone(microphones[i]),
                Smooth(grid, curve, smoothing, psychoacoustic),
                AnalysisCurveKind.ArrayMicrophone));
        }

        if (drawn.Count == 0)
        {
            return Empty;
        }

        return new ArrayMicrophoneDisplay(
            new AnalysisCurve(
                "Array average",
                Smooth(grid, average, smoothing, psychoacoustic),
                AnalysisCurveKind.ArrayAverage),
            drawn,
            drawn.Count < 2
                ? null
                : new AnalysisCurve(
                    "Array spread",
                    SmoothSpread(grid, spreadDb, smoothing, psychoacoustic),
                    AnalysisCurveKind.ArraySpread));
    }

    private static string DescribeMicrophone(ArrayMicrophoneCurve microphone)
    {
        string where = string.IsNullOrWhiteSpace(microphone.Note)
            ? $"Input {microphone.ChannelOffset + 1}"
            : microphone.Note!;
        return microphone.IsMeasurementMicrophone ? $"{where} (measurement)" : where;
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
