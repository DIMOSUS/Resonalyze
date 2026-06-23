using System.Numerics;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal enum IrPreviewSource
{
    SweepDeconvolution,
    Primary,
    TransferFromStart
}

internal static class ImpulseWindowPreview
{
    public static void Update(
        OxyPlot.WindowsForms.PlotView plotView,
        ExpSweepMeasurement measurement,
        int windowLength,
        int leftWindow,
        int rightWindow,
        int offset,
        IrPreviewSource source)
    {
        plotView.Model = CreatePlotModel(
            measurement,
            windowLength,
            leftWindow,
            rightWindow,
            offset,
            source);
        plotView.InvalidatePlot(true);
    }

    private static PlotModel CreatePlotModel(
        ExpSweepMeasurement measurement,
        int windowLength,
        int leftWindow,
        int rightWindow,
        int offset,
        IrPreviewSource source)
    {
        WindowedImpulse? impulse = CreateWindowedImpulse(
            measurement,
            windowLength,
            leftWindow,
            rightWindow,
            offset,
            source);
        var model = CreatePreviewPlotModel(impulse?.Title ?? "IR Window");

        double timeMin = impulse?.Points[0].X ?? -10;
        double timeMax = impulse?.Points[^1].X ?? 10;
        model.Axes.Add(CreateTimeAxis(timeMin, timeMax));
        model.Axes.Add(CreateAmplitudeAxis());

        if (impulse == null)
        {
            return model;
        }

        var impulseSeries = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 210, 70),
            StrokeThickness = 1.5,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };
        impulseSeries.Points.AddRange(impulse.Points);
        model.Series.Add(impulseSeries);

        var windowSeries = new LineSeries
        {
            Color = OxyColor.FromRgb(50, 210, 120),
            StrokeThickness = 1.5,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };
        windowSeries.Points.AddRange(impulse.Window);
        model.Series.Add(windowSeries);

        return model;
    }

    private static WindowedImpulse? CreateWindowedImpulse(
        ExpSweepMeasurement measurement,
        int windowLength,
        int leftWindow,
        int rightWindow,
        int offset,
        IrPreviewSource source)
    {
        if (measurement.SampleRate <= 0)
        {
            return null;
        }

        IrSource? irSource = SelectImpulseResponse(measurement, source);
        if (irSource == null)
        {
            return null;
        }

        int start = irSource.ReferenceIndex - leftWindow + offset;
        double normalizedLeftWindow = (double)leftWindow / windowLength * 2.0;
        double normalizedRightWindow = (double)rightWindow / windowLength * 2.0;
        double[] window = Windowing.TukeyWindow(
            windowLength,
            normalizedLeftWindow,
            normalizedRightWindow);
        var measurementView = new ImpulseMeasurementView(
            irSource.Samples,
            irSource.ReferenceIndex,
            measurement.SampleRate);
        Complex[] impulse = DataHelper.ExtractWindow(
            measurementView,
            start,
            windowLength,
            null,
            wrap: irSource.Wrap);

        double maxMagnitude = impulse.Length == 0
            ? 0
            : impulse.Max(sample => sample.Magnitude);
        double scale = maxMagnitude > 0 ? 1.0 / maxMagnitude : 1.0;
        var points = new List<DataPoint>(impulse.Length);
        var windowPoints = new List<DataPoint>(impulse.Length);

        for (int i = 0; i < impulse.Length; i++)
        {
            double milliseconds = (i - leftWindow + offset) *
                1000.0 /
                measurement.SampleRate;
            points.Add(new DataPoint(milliseconds, impulse[i].Real * scale));
            windowPoints.Add(new DataPoint(milliseconds, window[i]));
        }

        return new WindowedImpulse(irSource.Title, points, windowPoints);
    }

    private static IrSource? SelectImpulseResponse(
        ExpSweepMeasurement measurement,
        IrPreviewSource source)
    {
        return source switch
        {
            IrPreviewSource.SweepDeconvolution =>
                measurement.SweepDeconvolutionImpulseResponse is { Length: > 0 } sweepIr
                    ? new IrSource(
                        sweepIr,
                        measurement.SweepDeconvolutionPeakIndex,
                        false,
                        "Sweep IR Window")
                    : null,
            IrPreviewSource.TransferFromStart =>
                measurement.TransferImpulseResponse is { Length: > 0 } transferIr
                    ? new IrSource(
                        transferIr,
                        0,
                        true,
                        "Transfer IR Window")
                    : SelectImpulseResponse(
                        measurement,
                        IrPreviewSource.SweepDeconvolution),
            _ =>
                measurement.TransferImpulseResponse is { Length: > 0 } primaryTransferIr
                    ? new IrSource(
                        primaryTransferIr,
                        measurement.TransferPeakIndex,
                        false,
                        "Transfer IR Window")
                    : SelectImpulseResponse(
                        measurement,
                        IrPreviewSource.SweepDeconvolution)
        };
    }

    private static PlotModel CreatePreviewPlotModel(string title) =>
        new()
        {
            Background = OxyColor.FromRgb(32, 36, 46),
            PlotAreaBackground = OxyColor.FromRgb(32, 36, 46),
            TextColor = OxyColors.White,
            Title = title,
            TitleColor = OxyColors.White,
            TitleFontSize = 10
        };

    private static LinearAxis CreateTimeAxis(double timeMin, double timeMax) =>
        new()
        {
            Position = AxisPosition.Bottom,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            Title = "ms",
            IsPanEnabled = false,
            IsZoomEnabled = false,
            AbsoluteMaximum = timeMax,
            AbsoluteMinimum = timeMin,
            Maximum = timeMax,
            Minimum = timeMin
        };

    private static LinearAxis CreateAmplitudeAxis() =>
        new()
        {
            Position = AxisPosition.Left,
            Minimum = -1.05,
            Maximum = 1.05,
            MajorStep = 0.5,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            IsPanEnabled = false,
            IsZoomEnabled = false
        };

    private sealed record IrSource(
        Complex[] Samples,
        int ReferenceIndex,
        bool Wrap,
        string Title);

    private sealed record WindowedImpulse(
        string Title,
        IReadOnlyList<DataPoint> Points,
        IReadOnlyList<DataPoint> Window);
}
