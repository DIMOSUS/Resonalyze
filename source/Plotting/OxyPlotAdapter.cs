using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

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

    public static OxyColor GetCurveColor(AnalysisCurveKind kind) => GetColor(kind);

    private static OxyColor GetColor(AnalysisCurveKind kind)
    {
        return kind switch
        {
            AnalysisCurveKind.SecondHarmonic => OxyColor.FromRgb(255, 64, 0),
            AnalysisCurveKind.ThirdHarmonic => OxyColor.FromRgb(128, 64, 127),
            AnalysisCurveKind.FourthHarmonic => OxyColor.FromRgb(1, 64, 254),
            AnalysisCurveKind.ThdPlusNoise => OxyColors.White,
            AnalysisCurveKind.NoiseFloor => OxyColor.FromRgb(128, 128, 128),
            AnalysisCurveKind.MinimumPhase => OxyColor.FromRgb(0, 200, 255),
            AnalysisCurveKind.ExcessPhase => OxyColor.FromRgb(130, 220, 90),
            // GD counterparts reuse the phase pair's hues across modes.
            AnalysisCurveKind.MinimumPhaseGroupDelay => OxyColor.FromRgb(0, 200, 255),
            AnalysisCurveKind.ExcessGroupDelay => OxyColor.FromRgb(130, 220, 90),
            AnalysisCurveKind.ImpulseEnvelope => OxyColor.FromRgb(255, 210, 80),
            AnalysisCurveKind.ImpulseStep => OxyColor.FromRgb(120, 200, 255),
            AnalysisCurveKind.ArrayAverage => OxyColor.FromRgb(120, 230, 190),
            AnalysisCurveKind.ArrayMicrophone => OxyColor.FromRgb(70, 130, 115),
            AnalysisCurveKind.ArraySpread => OxyColor.FromRgb(200, 140, 220),
            _ => OxyColor.FromRgb(255, 127, 0)
        };
    }
}
