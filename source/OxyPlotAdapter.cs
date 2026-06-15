using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Converts framework-independent DSP results into OxyPlot presentation models.
/// </summary>
internal static class OxyPlotAdapter
{
    public static LineSeries ToLineSeries(AnalysisCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var series = new LineSeries
        {
            Color = GetColor(curve.Kind),
            Title = curve.Name
        };
        series.Points.AddRange(ToDataPoints(curve.Points));
        return series;
    }

    public static List<DataPoint> ToDataPoints(IEnumerable<SignalPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        return points
            .Select(point => new DataPoint(point.X, point.Y))
            .ToList();
    }

    public static List<SignalPoint> ToSignalPoints(IEnumerable<DataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        return points
            .Select(point => new SignalPoint(point.X, point.Y))
            .ToList();
    }

    private static OxyColor GetColor(AnalysisCurveKind kind)
    {
        return kind switch
        {
            AnalysisCurveKind.SecondHarmonic => OxyColor.FromRgb(255, 64, 0),
            AnalysisCurveKind.ThirdHarmonic => OxyColor.FromRgb(128, 64, 127),
            AnalysisCurveKind.FourthHarmonic => OxyColor.FromRgb(1, 64, 254),
            AnalysisCurveKind.ThdPlusNoise => OxyColors.White,
            _ => OxyColor.FromRgb(255, 127, 0)
        };
    }
}
