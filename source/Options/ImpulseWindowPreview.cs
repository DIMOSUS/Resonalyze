using System.Numerics;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
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

// One impulse response drawn on a gated preview: the samples on the absolute
// timeline plus the color/title it is drawn with.
internal sealed record IrPreviewTrace(
    Complex[] Samples,
    string Title,
    OxyColor Color);

internal static class ImpulseWindowPreview
{
    // Gated preview for an arbitrary set of impulse responses on one shared
    // absolute timeline (the Virtual DSP phase gate): every trace is
    // normalized independently so each arrival is visible, and the Tukey gate is
    // drawn where it actually sits.
    public static void UpdateGatedMulti(
        OxyPlot.WindowsForms.PlotView plotView,
        IReadOnlyList<IrPreviewTrace> traces,
        int sampleRate,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs)
    {
        var model = CreatePreviewPlotModel("IR Gate");

        (double StartMs, double EndMs)? window = AddGatedTraceSeries(
            model, traces, sampleRate, gateOffsetMs, leftMs, plateauMs, rightMs);

        model.Axes.Add(window is { } bounds
            ? CreateTimeAxis(bounds.StartMs, bounds.EndMs)
            : CreateTimeAxis(-10, 10));
        model.Axes.Add(CreateAmplitudeAxis());

        plotView.Model = model;
        plotView.InvalidatePlot(true);
    }

    // The shared body of the gated multi-trace view: each trace normalized to
    // its own in-window peak, the Tukey gate outline, and a vertical mark at
    // the gate offset. Used by the gate dialog's preview and the Virtual DSP
    // impulse view alike, so the two renderings cannot drift apart. Everything
    // added carries seriesTag so a host redrawing an existing model can find
    // and remove it. Returns the display window bounds (ms), or null when
    // there is nothing to draw.
    public static (double StartMs, double EndMs)? AddGatedTraceSeries(
        PlotModel model,
        IReadOnlyList<IrPreviewTrace> traces,
        int sampleRate,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        object? seriesTag = null)
    {
        if (traces.Count == 0 || sampleRate <= 0)
        {
            return null;
        }

        int gateOffset = MillisecondsToSamples(gateOffsetMs, sampleRate);
        int left = MillisecondsToSamples(leftMs, sampleRate);
        int plateau = MillisecondsToSamples(plateauMs, sampleRate);
        int right = MillisecondsToSamples(rightMs, sampleRate);
        int gate = Math.Max(1, left + plateau + right);
        int gateStart = gateOffset - left;

        double[] tukey = Windowing.TukeyWindow(
            gate,
            (double)left / gate * 2.0,
            (double)right / gate * 2.0);

        int longest = traces.Max(trace => trace.Samples.Length);
        int context = Math.Max(gate / 8, MillisecondsToSamples(0.2, sampleRate));
        int displayStart = Math.Max(0, gateStart - context);
        int displayEnd = Math.Min(longest - 1, gateStart + gate + context);
        if (displayEnd < displayStart)
        {
            displayEnd = displayStart;
        }

        foreach (IrPreviewTrace trace in traces)
        {
            double maxMagnitude = 0;
            for (int s = displayStart; s <= displayEnd && s < trace.Samples.Length; s++)
            {
                maxMagnitude = Math.Max(maxMagnitude, Math.Abs(trace.Samples[s].Real));
            }
            double scale = maxMagnitude > 0 ? 1.0 / maxMagnitude : 1.0;

            var series = new LineSeries
            {
                Color = trace.Color,
                StrokeThickness = 1.2,
                Title = trace.Title,
                Tag = seriesTag,
                TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
            };
            for (int s = displayStart; s <= displayEnd; s++)
            {
                double value = s < trace.Samples.Length
                    ? trace.Samples[s].Real * scale
                    : 0.0;
                series.Points.Add(new DataPoint(s * 1000.0 / sampleRate, value));
            }

            model.Series.Add(series);
        }

        var windowSeries = new LineSeries
        {
            Color = OxyColor.FromArgb(127, 50, 210, 120),
            StrokeThickness = 1.0,
            LineStyle = LineStyle.Dash,
            Tag = seriesTag,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };
        for (int s = displayStart; s <= displayEnd; s++)
        {
            double w = s >= gateStart && s < gateStart + gate ? tukey[s - gateStart] : 0.0;
            windowSeries.Points.Add(new DataPoint(s * 1000.0 / sampleRate, w));
        }
        model.Series.Add(windowSeries);

        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = gateOffsetMs,
            Color = OxyColor.FromArgb(127, 80, 150, 255),
            LineStyle = LineStyle.Dot,
            StrokeThickness = 1.0,
            Tag = seriesTag
        });

        return (
            displayStart * 1000.0 / sampleRate,
            displayEnd * 1000.0 / sampleRate);
    }

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

    // Preview for the gated phase / group-delay modes: the IR is shown on its own
    // (absolute) timeline, the Tukey gate is drawn where it actually sits, and a blue
    // dotted vertical line marks the gate offset (the end of the left shoulder).
    public static void UpdateGated(
        OxyPlot.WindowsForms.PlotView plotView,
        ExpSweepMeasurement measurement,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        IrPreviewSource source,
        CompareAnalysisSource? compare = null)
    {
        plotView.Model = CreateGatedPlotModel(
            measurement,
            gateOffsetMs,
            leftMs,
            plateauMs,
            rightMs,
            source,
            compare);
        plotView.InvalidatePlot(true);
    }

    private static PlotModel CreateGatedPlotModel(
        ExpSweepMeasurement measurement,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        IrPreviewSource source,
        CompareAnalysisSource? compare)
    {
        var model = CreatePreviewPlotModel("IR Gate");

        IrSource? irSource = measurement.SampleRate > 0
            ? SelectImpulseResponse(measurement, source)
            : null;
        if (irSource == null)
        {
            model.Axes.Add(CreateTimeAxis(-10, 10));
            model.Axes.Add(CreateAmplitudeAxis());
            return model;
        }

        int sampleRate = measurement.SampleRate;
        int gateOffset = MillisecondsToSamples(gateOffsetMs, sampleRate);
        int left = MillisecondsToSamples(leftMs, sampleRate);
        int plateau = MillisecondsToSamples(plateauMs, sampleRate);
        int right = MillisecondsToSamples(rightMs, sampleRate);
        int gate = Math.Max(1, left + plateau + right);
        int gateStart = gateOffset - left;

        double[] tukey = Windowing.TukeyWindow(
            gate,
            (double)left / gate * 2.0,
            (double)right / gate * 2.0);

        int context = Math.Max(gate / 8, MillisecondsToSamples(0.2, sampleRate));
        int displayStart = Math.Max(0, gateStart - context);
        int displayEnd = Math.Min(irSource.Samples.Length - 1, gateStart + gate + context);

        double maxMagnitude = 0;
        for (int s = displayStart; s <= displayEnd; s++)
        {
            maxMagnitude = Math.Max(maxMagnitude, Math.Abs(irSource.Samples[s].Real));
        }
        double scale = maxMagnitude > 0 ? 1.0 / maxMagnitude : 1.0;

        var irPoints = new List<DataPoint>(Math.Max(0, displayEnd - displayStart + 1));
        var windowPoints = new List<DataPoint>(irPoints.Capacity);
        for (int s = displayStart; s <= displayEnd; s++)
        {
            double ms = s * 1000.0 / sampleRate;
            irPoints.Add(new DataPoint(ms, irSource.Samples[s].Real * scale));
            double w = s >= gateStart && s < gateStart + gate ? tukey[s - gateStart] : 0.0;
            windowPoints.Add(new DataPoint(ms, w));
        }

        double timeMin = displayStart * 1000.0 / sampleRate;
        double timeMax = displayEnd * 1000.0 / sampleRate;
        model.Axes.Add(CreateTimeAxis(timeMin, timeMax));
        model.Axes.Add(CreateAmplitudeAxis());

        var impulseSeries = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 210, 70),
            StrokeThickness = 1.5,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };
        impulseSeries.Points.AddRange(irPoints);
        model.Series.Add(impulseSeries);

        var windowSeries = new LineSeries
        {
            Color = OxyColor.FromRgb(50, 210, 120),
            StrokeThickness = 1.5,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };
        windowSeries.Points.AddRange(windowPoints);
        model.Series.Add(windowSeries);

        AddCompareImpulse(model, compare, sampleRate, displayStart, displayEnd);

        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = gateOffsetMs,
            Color = OxyColor.FromArgb(127, 80, 150, 255),
            LineStyle = LineStyle.Dot,
            StrokeThickness = 1.0
        });

        return model;
    }

    // Overlays the Compare transfer IR on the same absolute timeline (index = index),
    // dashed / dimmed / in a distinct hue. Normalised independently so both peaks show.
    // Only drawn when the sample rate matches, so the shared ms axis stays meaningful.
    private static void AddCompareImpulse(
        PlotModel model,
        CompareAnalysisSource? compare,
        int sampleRate,
        int displayStart,
        int displayEnd)
    {
        if (compare is not { } source ||
            source.SampleRate != sampleRate ||
            source.TransferImpulseResponse is not { Length: > 0 } samples)
        {
            return;
        }

        double maxMagnitude = 0;
        for (int s = displayStart; s <= displayEnd && s < samples.Length; s++)
        {
            maxMagnitude = Math.Max(maxMagnitude, Math.Abs(samples[s].Real));
        }
        double scale = maxMagnitude > 0 ? 1.0 / maxMagnitude : 1.0;

        var comparePoints = new List<DataPoint>(Math.Max(0, displayEnd - displayStart + 1));
        for (int s = displayStart; s <= displayEnd; s++)
        {
            double ms = s * 1000.0 / sampleRate;
            double value = s >= 0 && s < samples.Length ? samples[s].Real * scale : 0.0;
            comparePoints.Add(new DataPoint(ms, value));
        }

        var compareSeries = new LineSeries
        {
            Color = OxyColor.FromArgb(180, 120, 200, 255),
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash,
            Title = source.DisplayName,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };
        compareSeries.Points.AddRange(comparePoints);
        model.Series.Add(compareSeries);
    }

    private static int MillisecondsToSamples(double milliseconds, int sampleRate) =>
        (int)Math.Round(Math.Max(0.0, milliseconds) * sampleRate / 1000.0);

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
                measurement.SweepDeconvolution is { ImpulseResponse.Length: > 0 } sweepResult
                    ? new IrSource(
                        sweepResult.ImpulseResponse,
                        sweepResult.PeakIndex,
                        false,
                        "Sweep IR Window")
                    : null,
            IrPreviewSource.TransferFromStart =>
                measurement.Transfer is { ImpulseResponse.Length: > 0 } transferResult
                    ? new IrSource(
                        transferResult.ImpulseResponse,
                        0,
                        true,
                        "Transfer IR Window")
                    : SelectImpulseResponse(
                        measurement,
                        IrPreviewSource.SweepDeconvolution),
            _ =>
                measurement.Transfer is { ImpulseResponse.Length: > 0 } primaryTransferResult
                    ? new IrSource(
                        primaryTransferResult.ImpulseResponse,
                        primaryTransferResult.PeakIndex,
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
