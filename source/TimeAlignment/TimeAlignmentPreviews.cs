using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The two preview plots: the band-pass window and the envelope around the arrival with its markers.</summary>
internal static class TimeAlignmentPreviews
{
    private const string EnvelopeDecibelAxisTitle = "dB re Main peak";

    // Each curve is floored CurveFloorDb under its own max; the plot opens EnvelopeOpeningSpanDb tall so a quiet Compare record does not squeeze the arrivals (the axis still pans the full range).
    private const double CurveFloorDb = 80.0;

    private const double EnvelopeOpeningSpanDb = 100.0;

    /// <summary>The window the read uses: the Manual numbers as the fields show them, or the last drawn Auto band.</summary>
    public static PlotModel Bandpass(
        TimeAlignmentSession session,
        double manualCenterHz,
        double manualPassOctaves,
        double manualFadeOctaves)
    {
        bool addCurve = session.Options.BandMode == TimeAlignmentBandMode.ManualBand ||
            (session.Options.BandMode == TimeAlignmentBandMode.AutoBand && session.AutoBand != null);
        int sampleRate = session.Main?.SampleRate ?? 0;
        double maxFrequency = Math.Min(20_000, sampleRate > 0
            ? sampleRate * 0.5
            : 20_000);
        var model = CreatePreviewPlotModel("Bandpass Window");
        var frequencyAxis = new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = maxFrequency,
            AbsoluteMaximum = 20_000,
            AbsoluteMinimum = 20
        };
        ApplyPreviewAxisStyle(frequencyAxis);
        model.Axes.Add(frequencyAxis);
        var dbAxis = CreateDecibelAxis();
        dbAxis.AbsoluteMinimum = -80;
        dbAxis.AbsoluteMaximum = 0;
        model.Axes.Add(dbAxis);

        if (!addCurve)
        {
            return model;
        }

        var series = new LineSeries
        {
            Color = UiPalette.CurveEnvelope.ToOxy(),
            StrokeThickness = 2
        };
        (double f1, double f2, double f3, double f4) =
            session.Options.BandMode == TimeAlignmentBandMode.AutoBand && session.AutoBand is { } band
                ? BandpassWindow.BandAround(
                    Math.Sqrt(band.LowHz * band.HighHz),
                    Math.Log2(band.HighHz / band.LowHz),
                    TimeAlignmentBand.AutoBandFadeOctaves)
                : BandpassWindow.BandAround(
                    manualCenterHz,
                    manualPassOctaves,
                    manualFadeOctaves);
        const int pointCount = 240;
        double minLog = Math.Log10(20);
        double maxLog = Math.Log10(maxFrequency);
        for (int i = 0; i < pointCount; i++)
        {
            double t = i / (double)(pointCount - 1);
            double frequency = Math.Pow(10.0, minLog + (maxLog - minLog) * t);
            double weight = BandpassWindow.Weight(frequency, f1, f2, f3, f4);
            double decibels = weight > 0
                ? DataHelper.AmplitudeToDecibels(weight)
                : -80;
            series.Points.Add(new DataPoint(frequency, Math.Max(-80, decibels)));
        }

        model.Series.Add(series);
        return model;
    }

    public static PlotModel Envelope(
        TimeAlignmentAnalysisResult result,
        int sampleRate,
        TimeAlignmentAnalysisResult? compareResult = null)
    {
        double[] envelope = result.EnvelopeSamples;
        if (envelope.Length == 0 || result.StrongestEnvelopePeak <= 0 || sampleRate <= 0)
        {
            return EmptyEnvelope();
        }

        // ONE reference for both curves (Main's strongest peak): per-curve first-arrival normalization drew equal levels 19 dB apart when picks sat 6 and 25 dB under their peaks.
        double referenceAmplitude = result.StrongestEnvelopePeak;

        int radius = Math.Min(
            envelope.Length / 2,
            Math.Max(1, (int)Math.Round(sampleRate * 0.025)));
        double minMilliseconds = -radius * 1000.0 / sampleRate;
        double maxMilliseconds = radius * 1000.0 / sampleRate;
        double compareOffsetMilliseconds = 0.0;
        if (compareResult.HasValue)
        {
            compareOffsetMilliseconds =
                compareResult.Value.FirstArrivalDelayMilliseconds -
                result.FirstArrivalDelayMilliseconds;
            minMilliseconds = Math.Min(
                minMilliseconds,
                compareOffsetMilliseconds - radius * 1000.0 / sampleRate);
            maxMilliseconds = Math.Max(
                maxMilliseconds,
                compareOffsetMilliseconds + radius * 1000.0 / sampleRate);
        }

        int step = Math.Max(1, radius * 2 / 600);
        LineSeries mainSeries = CreateEnvelopeSeries(
            result,
            referenceAmplitude,
            sampleRate,
            radius,
            step,
            xOffsetMilliseconds: 0.0,
            UiPalette.CurveEnvelope.ToOxy(),
            strokeThickness: 2,
            out double maxDb,
            out double minDb);

        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(minMilliseconds, maxMilliseconds));
        var dbAxis = CreateDecibelAxis();
        dbAxis.Title = EnvelopeDecibelAxisTitle;
        ApplyEnvelopeDecibelRange(dbAxis, maxDb, minDb);
        model.Axes.Add(dbAxis);

        model.Series.Add(mainSeries);
        if (compareResult.HasValue)
        {
            LineSeries compareSeries = CreateEnvelopeSeries(
                compareResult.Value,
                referenceAmplitude,
                sampleRate,
                radius,
                step,
                compareOffsetMilliseconds,
                OxyColor.FromAColor(155, UiPalette.CurveCompare.ToOxy()),
                strokeThickness: 1.75,
                out double compareMaxDb,
                out double compareMinDb);
            maxDb = Math.Max(maxDb, compareMaxDb);
            minDb = Math.Min(minDb, compareMinDb);
            ApplyEnvelopeDecibelRange(dbAxis, maxDb, minDb);
            model.Series.Add(compareSeries);
        }

        if (compareResult.HasValue)
        {
            AddComparePeakMarkers(
                model,
                result,
                compareResult.Value,
                referenceAmplitude,
                compareOffsetMilliseconds);
        }
        else
        {
            AddMainPeakMarkers(model, result, referenceAmplitude);
        }
        return model;
    }

    private static void ApplyEnvelopeDecibelRange(
        LinearAxis axis,
        double maxDb,
        double minDb)
    {
        axis.AbsoluteMaximum = maxDb + 30;
        axis.AbsoluteMinimum = minDb - 10;
        axis.Maximum = maxDb + 2;
        axis.Minimum = Math.Max(minDb - 2, maxDb - EnvelopeOpeningSpanDb);
    }

    public static LineSeries CreateEnvelopeSeries(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude,
        int sampleRate,
        int radius,
        int step,
        double xOffsetMilliseconds,
        OxyColor color,
        double strokeThickness,
        out double maxDb,
        out double minDb)
    {
        double[] envelope = result.EnvelopeSamples;
        maxDb = -10000;
        minDb = +10000;
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = strokeThickness
        };
        double localMaxDb = maxDb;
        double localMinDb = minDb;
        void AddPoint(int offset)
        {
            int index = DspMath.WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length);
            double milliseconds = offset * 1000.0 / sampleRate + xOffsetMilliseconds;
            double relativeAmplitude = envelope[index] / referenceAmplitude;
            double decibels = DataHelper.AmplitudeToDecibels(relativeAmplitude);
            series.Points.Add(new DataPoint(milliseconds, decibels));
            localMaxDb = Math.Max(localMaxDb, decibels);
            localMinDb = Math.Min(localMinDb, decibels);
        }

        // Min/max pooling: every-Nth sampling would skip the narrow reflection peaks the markers point at.
        for (int bucketStart = -radius; bucketStart <= radius; bucketStart += step)
        {
            int bucketEnd = Math.Min(radius, bucketStart + step - 1);
            int minOffset = bucketStart;
            int maxOffset = bucketStart;
            double minValue = double.PositiveInfinity;
            double maxValue = double.NegativeInfinity;
            for (int offset = bucketStart; offset <= bucketEnd; offset++)
            {
                double value = envelope[
                    DspMath.WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length)];
                if (value < minValue)
                {
                    minValue = value;
                    minOffset = offset;
                }
                if (value > maxValue)
                {
                    maxValue = value;
                    maxOffset = offset;
                }
            }

            AddPoint(Math.Min(minOffset, maxOffset));
            if (minOffset != maxOffset)
            {
                AddPoint(Math.Max(minOffset, maxOffset));
            }
        }

        // Floor under THIS curve's max, so a genuinely quieter Compare record is drawn whole, not flattened.
        double floorDb = localMaxDb - CurveFloorDb;
        for (int i = 0; i < series.Points.Count; i++)
        {
            DataPoint point = series.Points[i];
            if (point.Y < floorDb)
            {
                series.Points[i] = new DataPoint(point.X, floorDb);
            }
        }

        maxDb = localMaxDb;
        minDb = Math.Max(localMinDb, floorDb);
        return series;
    }

    public static void AddMainPeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult,
        double referenceAmplitude)
    {
        double strongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        AddCalloutMarker(
            model,
            "M First",
            0.0,
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.EnvelopePeakIndex),
            UiPalette.MarkerFirstArrival.ToOxy(),
            PlotCalloutDirection.LeftUp);
        if (Math.Abs(strongestMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "M Peak",
                strongestMilliseconds,
                GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.StrongestEnvelopePeakIndex),
                UiPalette.MarkerStrongestPeak.ToOxy(),
                PlotCalloutDirection.RightUp);
        }

        AddCalloutMarker(
            model,
            "M Onset",
            mainResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, GetEnergyOnsetIndex(mainResult)),
            UiPalette.MarkerEnergyOnset.ToOxy(),
            PlotCalloutDirection.LeftDown);
    }

    // Wrapped into the circular envelope: complete records report positions as signed delays.
    public static int GetEnergyOnsetIndex(TimeAlignmentAnalysisResult result)
    {
        int length = result.EnvelopeSamples.Length;
        if (length == 0)
        {
            return 0;
        }

        long rounded = (long)Math.Round(result.EnergyOnsetSample);
        return (int)(((rounded % length) + length) % length);
    }

    public static void AddComparePeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentAnalysisResult compareResult,
        double referenceAmplitude,
        double compareFirstArrivalMilliseconds)
    {
        double mainFirstArrivalDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.EnvelopePeakIndex);
        double compareFirstArrivalDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, compareResult.EnvelopePeakIndex);
        double mainStrongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double compareStrongestMilliseconds =
            compareResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double mainStrongestDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.StrongestEnvelopePeakIndex);
        double compareStrongestDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, compareResult.StrongestEnvelopePeakIndex);

        AddCalloutMarker(
            model,
            "M First",
            0.0,
            mainFirstArrivalDecibels,
            UiPalette.MarkerFirstArrival.ToOxy(),
            mainFirstArrivalDecibels >= compareFirstArrivalDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
        AddCalloutMarker(
            model,
            "C First",
            compareFirstArrivalMilliseconds,
            compareFirstArrivalDecibels,
            OxyColor.FromAColor(145, UiPalette.MarkerFirstArrival.ToOxy()),
            compareFirstArrivalDecibels > mainFirstArrivalDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);

        if (Math.Abs(mainStrongestMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "M Peak",
                mainStrongestMilliseconds,
                mainStrongestDecibels,
                UiPalette.MarkerStrongestPeak.ToOxy(),
                mainStrongestDecibels >= compareStrongestDecibels
                    ? PlotCalloutDirection.RightUp
                    : PlotCalloutDirection.RightDown);
        }

        if (Math.Abs(compareStrongestMilliseconds - compareFirstArrivalMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "C Peak",
                compareStrongestMilliseconds,
                compareStrongestDecibels,
                OxyColor.FromAColor(145, UiPalette.MarkerStrongestPeak.ToOxy()),
                compareStrongestDecibels > mainStrongestDecibels
                    ? PlotCalloutDirection.RightUp
                    : PlotCalloutDirection.RightDown);
        }

        double mainOnsetDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, GetEnergyOnsetIndex(mainResult));
        double compareOnsetDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, GetEnergyOnsetIndex(compareResult));
        AddCalloutMarker(
            model,
            "M Onset",
            mainResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            mainOnsetDecibels,
            UiPalette.MarkerEnergyOnset.ToOxy(),
            mainOnsetDecibels >= compareOnsetDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
        AddCalloutMarker(
            model,
            "C Onset",
            compareResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            compareOnsetDecibels,
            OxyColor.FromAColor(145, UiPalette.MarkerEnergyOnset.ToOxy()),
            compareOnsetDecibels > mainOnsetDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
    }

    private static void AddCalloutMarker(
        PlotModel model,
        string label,
        double milliseconds,
        double decibels,
        OxyColor color,
        PlotCalloutDirection direction)
    {
        model.Annotations.Add(new PlotCalloutMarkerAnnotation
        {
            Text = label,
            AnchorPoint = new DataPoint(milliseconds, decibels),
            Color = color,
            Direction = direction,
            Layer = AnnotationLayer.AboveSeries
        });
    }

    public static double GetPeakMarkerDecibels(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude,
        int peakIndex)
    {
        if ((uint)peakIndex >= (uint)result.EnvelopeSamples.Length ||
            referenceAmplitude <= 0)
        {
            return 0.0;
        }

        // Same floor as the curve, so a marker never parks under its own line.
        double peakDecibels = DataHelper.AmplitudeToDecibels(
            result.StrongestEnvelopePeak / referenceAmplitude);
        double relativeAmplitude = result.EnvelopeSamples[peakIndex] / referenceAmplitude;
        return Math.Max(
            peakDecibels - CurveFloorDb,
            DataHelper.AmplitudeToDecibels(relativeAmplitude));
    }

    public static PlotModel EmptyEnvelope()
    {
        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(-50, 50));
        var dbAxis = CreateDecibelAxis();
        dbAxis.Title = EnvelopeDecibelAxisTitle;
        dbAxis.AbsoluteMaximum = 0;
        dbAxis.AbsoluteMinimum = -80;
        dbAxis.Maximum = 0;
        dbAxis.Minimum = -80;
        model.Axes.Add(dbAxis);
        return model;
    }

    private static PlotModel CreatePreviewPlotModel(string title) =>
        PlotModelStyle.CreatePreviewModel(title);

    private static LinearAxis CreateDecibelAxis()
    {
        var axis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -80,
            Maximum = 3,
            MajorStep = 20,
            Title = "dB"
        };
        ApplyPreviewAxisStyle(axis);
        return axis;
    }

    private static LinearAxis CreateMillisecondsAxis(double minimum, double maximum)
    {
        var axis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = minimum,
            AbsoluteMaximum = maximum,
            Minimum = minimum,
            Maximum = maximum,
            MajorStep = 25,
            Title = "ms from peak"
        };
        ApplyPreviewAxisStyle(axis);
        return axis;
    }

    private static void ApplyPreviewAxisStyle(Axis axis)
    {
        axis.MajorGridlineStyle = LineStyle.Solid;
        axis.MinorGridlineStyle = LineStyle.Dot;
        PlotModelStyle.StyleAxis(axis);
    }
}
