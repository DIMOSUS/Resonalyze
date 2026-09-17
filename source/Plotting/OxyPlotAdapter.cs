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
            AnalysisCurveKind.SecondHarmonic => UiPalette.CurveHarmonic2.ToOxy(),
            AnalysisCurveKind.ThirdHarmonic => UiPalette.CurveHarmonic3.ToOxy(),
            AnalysisCurveKind.FourthHarmonic => UiPalette.CurveHarmonic4.ToOxy(),
            AnalysisCurveKind.ThdPlusNoise => UiPalette.CurveNeutral.ToOxy(),
            AnalysisCurveKind.NoiseFloor => UiPalette.CurveMuted.ToOxy(),
            AnalysisCurveKind.MinimumPhase => UiPalette.CurveMinimumPhase.ToOxy(),
            AnalysisCurveKind.ExcessPhase => UiPalette.CurveExcessPhase.ToOxy(),
            // GD counterparts reuse the phase pair's hues across modes.
            AnalysisCurveKind.MinimumPhaseGroupDelay => UiPalette.CurveMinimumPhase.ToOxy(),
            AnalysisCurveKind.ExcessGroupDelay => UiPalette.CurveExcessPhase.ToOxy(),
            AnalysisCurveKind.ImpulseEnvelope => UiPalette.CurveEnvelope.ToOxy(),
            AnalysisCurveKind.ImpulseStep => UiPalette.CurveStep.ToOxy(),
            AnalysisCurveKind.ArrayAverage => UiPalette.CurveArrayAverage.ToOxy(),
            AnalysisCurveKind.ArrayMicrophone => UiPalette.CurveArrayMicrophone.ToOxy(),
            AnalysisCurveKind.ArraySpread => UiPalette.CurveArraySpread.ToOxy(),
            _ => UiPalette.CurveFallback.ToOxy()
        };
    }
}
