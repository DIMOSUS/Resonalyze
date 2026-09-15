using Resonalyze.Dsp;

namespace Resonalyze;

/// <param name="Spread">Loudest minus quietest position per frequency (a dB range, own axis); null below two placed microphones.</param>
internal sealed record ArrayMicrophoneDisplay(
    AnalysisCurve? Average,
    IReadOnlyList<AnalysisCurve> Microphones,
    AnalysisCurve? Spread);

/// <remarks>Average raw power first, then smooth: the cubic psychoacoustic mean does not commute with the power mean
/// (smoothing first read 0.11 dB high on a midrange, 0.39 dB on a tweeter, on a seven-position set).</remarks>
internal static class ArrayMicrophoneCurves
{
    public static readonly ArrayMicrophoneDisplay Empty = new(null, [], null);

    /// <param name="useCalibration">Picks which reading to draw; both come off one <see cref="ArrayPlacement"/> on calibrated curves.
    /// A second raw placement made the same array read 3 dB apart here and in Virtual DSP.</param>
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

    // A spread is a dB difference: the cubic magnitude smoother would bias it.
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
