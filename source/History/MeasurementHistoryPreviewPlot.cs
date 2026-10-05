using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.History;

/// <summary>The History panel's fixed-scale preview; raw dBr, so a padded loopback lifts its ceiling.</summary>
internal static class MeasurementHistoryPreviewPlot
{
    public static PlotModel Create(MeasurementHistoryPreview? preview)
    {
        PlotModel model = PlotModelStyle.CreatePreviewModel();
        PlotModelStyle.AddAxis(model, new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = 20,
            AbsoluteMaximum = 20000,
            Minimum = 20,
            Maximum = 20000,
            IsPanEnabled = false,
            IsZoomEnabled = false,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = PlotModelFactory.DecibelAxisKey,
            Position = AxisPosition.Left,
            AbsoluteMinimum = PlotModelStyle.RelativeDecibelAbsoluteMinimum,
            AbsoluteMaximum = PlotModelStyle.RelativeDecibelAbsoluteMaximum,
            MajorStep = 10,
            Minimum = PlotModelStyle.RelativeDecibelMinimum,
            Maximum = PlotModelStyle.RelativeDecibelMaximum,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "dB",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
        if (preview == null)
        {
            return model;
        }

        var series = new LineSeries
        {
            Color = UiPalette.CurveFallback.ToOxy(),
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        double maxDb = double.NegativeInfinity;
        foreach (SignalPoint point in preview.ToSignalPoints())
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
            maxDb = Math.Max(maxDb, point.Y);
        }

        model.Series.Add(series);
        PlotModelStyle.RaiseDecibelViewCeiling(model, maxDb);
        return model;
    }
}
