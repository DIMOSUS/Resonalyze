using System.Numerics;
using System.Runtime.CompilerServices;
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
    // Referenced at the estimated start, where magnitude extraction opens its window (DataHelper MagnitudeAnchorIndex).
    PrimaryAtStart,
    TransferFromStart
}

internal sealed record IrPreviewTrace(
    Complex[] Samples,
    string Title,
    OxyColor Color,
    double Thickness = 1.2,
    LineStyle Style = LineStyle.Solid);

internal static class ImpulseWindowPreview
{
    // Every trace normalized independently so each arrival is visible.
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

    // Shared by the gate dialog and the Virtual DSP impulse view so window, gate and traces cannot drift.
    // With envelopes, the peak is the envelope's in-window peak, else the envelope runs off ±1 between carrier crests.
    // Series carry seriesTag for removal. Returns the window (ms), or null when nothing to draw.
    public static (double StartMs, double EndMs)? AddGatedTraceSeries(
        PlotModel model,
        IReadOnlyList<IrPreviewTrace> traces,
        int sampleRate,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        object? seriesTag = null,
        bool envelopes = false)
    {
        if (GatedDisplay.Resolve(
                traces, sampleRate, gateOffsetMs, leftMs, plateauMs, rightMs)
            is not { } display)
        {
            return null;
        }

        foreach (IrPreviewTrace trace in traces)
        {
            double[]? envelope = envelopes && trace.Samples.Length > 0
                ? EnvelopeOf(trace.Samples)
                : null;
            double maxMagnitude = 0;
            for (int s = display.Start; s <= display.End && s < trace.Samples.Length; s++)
            {
                maxMagnitude = Math.Max(
                    maxMagnitude,
                    envelope?[s] ?? Math.Abs(trace.Samples[s].Real));
            }
            double scale = maxMagnitude > 0 ? 1.0 / maxMagnitude : 1.0;

            if (envelope != null)
            {
                AddEnvelopeGuides(model, trace, envelope, scale, display, sampleRate, seriesTag);
            }

            LineSeries series = CreateTraceSeries(trace, seriesTag);
            for (int s = display.Start; s <= display.End; s++)
            {
                double value = s < trace.Samples.Length
                    ? trace.Samples[s].Real * scale
                    : 0.0;
                series.Points.Add(new DataPoint(s * 1000.0 / sampleRate, value));
            }

            model.Series.Add(series);
        }

        AddGateOutline(model, display, sampleRate, gateOffsetMs, seriesTag);
        return display.BoundsMs(sampleRate);
    }

    // Steps share ONE scale (largest in-window excursion) so relative amplitudes and the Sum stay meaningful.
    // The sum runs from the record start whatever the gate (VirtualCrossoverAnalysis.StepResponse); no Tukey taper.
    public static (double StartMs, double EndMs)? AddStepTraceSeries(
        PlotModel model,
        IReadOnlyList<IrPreviewTrace> traces,
        int sampleRate,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        object? seriesTag = null)
    {
        if (GatedDisplay.Resolve(
                traces, sampleRate, gateOffsetMs, leftMs, plateauMs, rightMs)
            is not { } display)
        {
            return null;
        }

        int count = display.End - display.Start + 1;
        var steps = new List<double[]>(traces.Count);
        double largest = 0.0;
        foreach (IrPreviewTrace trace in traces)
        {
            double[] step = VirtualCrossoverAnalysis.StepResponse(
                trace.Samples, display.Start, count);
            foreach (double value in step)
            {
                largest = Math.Max(largest, Math.Abs(value));
            }

            steps.Add(step);
        }

        double scale = largest > 0 ? 1.0 / largest : 1.0;
        for (int index = 0; index < traces.Count; index++)
        {
            LineSeries series = CreateTraceSeries(traces[index], seriesTag);
            double[] step = steps[index];
            for (int i = 0; i < count; i++)
            {
                series.Points.Add(new DataPoint(
                    (display.Start + i) * 1000.0 / sampleRate, step[i] * scale));
            }

            model.Series.Add(series);
        }

        AddGateOutline(model, display, sampleRate, gateOffsetMs, seriesTag);
        return display.BoundsMs(sampleRate);
    }

    // One resolution for impulse and step views so toggling keeps the zoom.
    private readonly record struct GatedDisplay(
        int Start,
        int End,
        int GateStart,
        int Gate,
        double[] Tukey)
    {
        public static GatedDisplay? Resolve(
            IReadOnlyList<IrPreviewTrace> traces,
            int sampleRate,
            double gateOffsetMs,
            double leftMs,
            double plateauMs,
            double rightMs)
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

            return new GatedDisplay(displayStart, displayEnd, gateStart, gate, tukey);
        }

        public (double StartMs, double EndMs) BoundsMs(int sampleRate) => (
            Start * 1000.0 / sampleRate,
            End * 1000.0 / sampleRate);
    }

    private static LineSeries CreateTraceSeries(IrPreviewTrace trace, object? seriesTag) =>
        new()
        {
            Color = trace.Color,
            StrokeThickness = trace.Thickness,
            LineStyle = trace.Style,
            Title = trace.Title,
            Tag = seriesTag,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };

    private const byte EnvelopeGuideAlpha = 30;
    private const double EnvelopeGuideThickness = 0.8;

    private static void AddEnvelopeGuides(
        PlotModel model,
        IrPreviewTrace trace,
        double[] envelope,
        double scale,
        GatedDisplay display,
        int sampleRate,
        object? seriesTag)
    {
        foreach (int sign in new[] { 1, -1 })
        {
            var series = new LineSeries
            {
                Title = trace.Title + " envelope",
                RenderInLegend = false,
                Color = OxyColor.FromAColor(EnvelopeGuideAlpha, trace.Color),
                StrokeThickness = EnvelopeGuideThickness,
                Tag = seriesTag,
                TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
            };
            for (int s = display.Start; s <= display.End; s++)
            {
                double value = s < envelope.Length ? envelope[s] * scale : 0.0;
                series.Points.Add(new DataPoint(s * 1000.0 / sampleRate, sign * value));
            }

            model.Series.Add(series);
        }
    }

    // Envelope over the whole record, not the window: a transform over a cut record wobbles at the cut.
    // Memoized per sample array (unchanged channels reuse arrays); warmed off the UI thread by RedrawMainPlotAsync.
    // Thread-safe; a race computes twice and keeps one.
    private static readonly ConditionalWeakTable<Complex[], double[]> envelopeCache = new();

    internal static double[] EnvelopeOf(Complex[] samples) =>
        envelopeCache.GetValue(samples, static record =>
        {
            var real = new double[record.Length];
            for (int i = 0; i < real.Length; i++)
            {
                real[i] = record[i].Real;
            }

            return SignalEnvelope.Envelope(real);
        });

    private static void AddGateOutline(
        PlotModel model,
        GatedDisplay display,
        int sampleRate,
        double gateOffsetMs,
        object? seriesTag)
    {
        var windowSeries = new LineSeries
        {
            Color = OxyColor.FromArgb(127, 50, 210, 120),
            StrokeThickness = 1.0,
            LineStyle = LineStyle.Dash,
            Tag = seriesTag,
            TrackerFormatString = "{0}\n{2:0.000} ms\n{4:0.000}"
        };
        for (int s = display.Start; s <= display.End; s++)
        {
            double w = s >= display.GateStart && s < display.GateStart + display.Gate
                ? display.Tukey[s - display.GateStart]
                : 0.0;
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

    // Only when sample rates match, so the shared ms axis stays meaningful.
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
            IrPreviewSource.PrimaryAtStart =>
                measurement.Transfer is { ImpulseResponse.Length: > 0 } startTransferResult
                    ? new IrSource(
                        startTransferResult.ImpulseResponse,
                        TransferIrStartCache.ResolveStartIndex(
                            startTransferResult.ImpulseResponse,
                            measurement.SampleRate,
                            startTransferResult.PeakIndex),
                        false,
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
