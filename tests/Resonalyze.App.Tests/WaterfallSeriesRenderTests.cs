using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>NaN tails past the measured window must never reach the render context: a NaN point overflows GDI+ DrawLine.</summary>
public sealed class WaterfallSeriesRenderTests
{
    [Fact]
    public void BurstDecay_WithNaNTail_RendersWithoutEmittingNonFiniteCoordinates()
    {
        using var measurement = CreateBroadbandTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        PlotModel model = factory.CreateBurstDecay(includeCurves: true);
        WaterfallSeries waterfall = model.Series.OfType<WaterfallSeries>().Single();

        var context = new RecordingRenderContext();
        IPlotModel plot = model;
        plot.Update(true);
        plot.Render(context, new OxyRect(0, 0, 900, 600));

        // Guards against a vacuous pass: the tails must actually be NaN.
        bool producedNaNTail = waterfall.ResampleSlices
            .Any(slice => slice.Data.Any(point => double.IsNaN(point.Y)));
        Assert.True(producedNaNTail,
            "test setup did not produce a NaN tail, so the guard was not exercised.");

        Assert.True(context.LineCount > 0, "the burst-decay series drew nothing.");
        Assert.False(context.SawNonFinite,
            $"a non-finite screen coordinate reached the renderer: {context.FirstNonFinite}");
    }

    [Fact]
    public void AWaterfallWithTooFewSlices_SaysSo_InsteadOfDrawingNothing()
    {
        using var measurement = CreateBroadbandTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, waterfall: new WaterfallGenerateOptions { SliceCount = 7 });
        PlotModel fourier = factory.CreateWaterfall(includeCurves: true);
        PlotModel enough = CreateFactory(measurement).CreateWaterfall(includeCurves: true);
        using var fast = new TestAnalyzer();
        fast.Open(ModeSettingsWiringTests.Transfer(96_000, peak: 960));
        PlotModel burst = CreateFactory(
                fast,
                burst: new WaterfallGenerateOptions
                {
                    WaterfallMode = WaterfallMode.BurstDecay,
                    Window = 32,
                    LeftTukeyWindow = 0,
                    RightTukeyWindow = 16,
                    SmoothingInverseOctaves = 1
                })
            .CreateBurstDecay(includeCurves: true);

        Assert.Equal(
            "Only 7 slices; the waterfall draws from 8. Raise Slices.",
            Assert.Single(fourier.Annotations.OfType<OverlayTextAnnotation>()).Text);
        Assert.Empty(enough.Annotations.OfType<OverlayTextAnnotation>());
        Assert.StartsWith(
            $"Only {burst.Series.OfType<WaterfallSeries>().Single().RawSlices.Count} frequencies fit",
            Assert.Single(burst.Annotations.OfType<OverlayTextAnnotation>()).Text,
            StringComparison.Ordinal);
    }

    // A driver whose group delay puts the peak milliseconds behind its onset: the first slice must be the curve
    // Frequency Response reads, whose window opens at the onset.
    [Fact]
    public void TheWaterfallsFirstSlice_OpensWhereTheMagnitudeWindowDoes_NotAtALatePeak()
    {
        var ir = new Complex[16384];
        const int onset = 2000;
        const int peak = onset + 240;
        for (int i = 0; i < 6000; i++)
        {
            double rise = Math.Min(1.0, i / 240.0);
            ir[onset + i] = new Complex(rise * rise * Math.Exp(-Math.Max(0, i - 240) / 600.0) * Math.Cos(i * 0.05), 0);
        }

        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            20, 20_000, 48_000, 24, 1.0, PlaybackChannel.Mono, ir, peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir,
            transferPeakIndex: peak));
        IImpulseMeasurement view = new MeasurementPlotContext(measurement.Document).CreatePrimaryMeasurement();
        var options = new WaterfallGenerateOptions { Window = 4096, LeftTukeyWindow = 64, RightTukeyWindow = 1024 };
        var waterfall = new WaterfallSeries { GenerateOptions = options };

        waterfall.FillFourierWaterfallData(view);

        int anchor = DataHelper.MagnitudeAnchorIndex(view);
        Assert.InRange(anchor, onset - 64, peak - 64);
        double[] window = Windowing.TukeyWindow(4096, 64.0 / 4096 * 2.0, 1024.0 / 4096 * 2.0);
        List<SignalPoint> expected = DataHelper.GetOversampledSpectrumData(view, anchor - 64, window, wrapPreRoll: true);
        Assert.Equal(expected.Select(point => point.Y), waterfall.RawSlices[0].Data.Select(point => point.Y));
    }

    // A start less than a left fade into the record: the window reads the circular pre-roll from the record's end, as
    // Frequency Response's does, not zeros.
    [Fact]
    public void AWaterfallOpeningBeforeTheRecord_ReadsTheCircularPreRoll_AsFrequencyResponseDoes()
    {
        var ir = new Complex[16384];
        const int onset = 40;
        const int peak = onset + 240;
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] = new Complex(0.02 * Math.Sin(i * 0.013), 0);
        }

        for (int i = 0; i < 6000; i++)
        {
            double rise = Math.Min(1.0, i / 240.0);
            ir[onset + i] += new Complex(rise * rise * Math.Exp(-Math.Max(0, i - 240) / 600.0) * Math.Cos(i * 0.05), 0);
        }

        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            20, 20_000, 48_000, 24, 1.0, PlaybackChannel.Mono, ir, peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir,
            transferPeakIndex: peak));
        IImpulseMeasurement view = new MeasurementPlotContext(measurement.Document).CreatePrimaryMeasurement();
        var options = new WaterfallGenerateOptions { Window = 4096, LeftTukeyWindow = 256, RightTukeyWindow = 1024 };
        var waterfall = new WaterfallSeries { GenerateOptions = options };

        waterfall.FillFourierWaterfallData(view);

        int start = DataHelper.MagnitudeAnchorIndex(view) - 256;
        Assert.True(start < 0, $"the window opens at {start}, inside the record");
        double[] window = Windowing.TukeyWindow(4096, 256.0 / 4096 * 2.0, 1024.0 / 4096 * 2.0);
        IEnumerable<double> shown = waterfall.RawSlices[0].Data.Select(point => point.Y);
        Assert.Equal(
            DataHelper.GetOversampledSpectrumData(view, start, window, wrapPreRoll: true).Select(point => point.Y),
            shown);
        Assert.NotEqual(DataHelper.GetOversampledSpectrumData(view, start, window).Select(point => point.Y), shown);
    }

    private static TestAnalyzer CreateBroadbandTransferMeasurement()
    {
        var ir = new Complex[8192];
        int peak = 256;
        for (int i = 0; i < 4000 && peak + i < ir.Length; i++)
        {
            ir[peak + i] = new Complex(Math.Exp(-i / 400.0) * Math.Cos(i * 0.2), 0);
        }

        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir,
            sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir,
            transferPeakIndex: peak));
        return measurement;
    }

    private static PlotModelFactory CreateFactory(
        TestAnalyzer measurement,
        WaterfallGenerateOptions? waterfall = null,
        WaterfallGenerateOptions? burst = null)
    {
        string calibrationPath = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");

        return new PlotModelFactory(
            measurement.Document,
            measurement.Engine,
            mode => new CalibrationFile(calibrationPath),
            new AnalyzerViewSettings
            {
                PhaseResponse = new FrequencyResponseOptions(),
                GroupDelay = new FrequencyResponseOptions(),
                Waterfall = waterfall ?? new WaterfallGenerateOptions(),
                BurstDecay = burst ?? new WaterfallGenerateOptions { WaterfallMode = WaterfallMode.BurstDecay }
            });
    }

    private sealed class RecordingRenderContext : IRenderContext
    {
        public int LineCount { get; private set; }
        public bool SawNonFinite { get; private set; }
        public string FirstNonFinite { get; private set; } = string.Empty;

        public int ClipCount { get; private set; }
        public bool RendersToScreen { get; set; } = true;

        private void Check(ScreenPoint point, string where)
        {
            if (!SawNonFinite && (!double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            {
                SawNonFinite = true;
                FirstNonFinite = $"{where}: ({point.X}, {point.Y})";
            }
        }

        private void Check(OxyRect rect, string where)
        {
            if (!SawNonFinite &&
                (!double.IsFinite(rect.Left) || !double.IsFinite(rect.Top) ||
                 !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom)))
            {
                SawNonFinite = true;
                FirstNonFinite = $"{where}: ({rect.Left}, {rect.Top}, {rect.Width}, {rect.Height})";
            }
        }

        public void DrawLine(
            IList<ScreenPoint> points, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode, double[]? dashArray, LineJoin lineJoin)
        {
            LineCount++;
            foreach (ScreenPoint point in points)
            {
                Check(point, nameof(DrawLine));
            }
        }

        public void DrawLineSegments(
            IList<ScreenPoint> points, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode, double[]? dashArray, LineJoin lineJoin)
        {
            foreach (ScreenPoint point in points)
            {
                Check(point, nameof(DrawLineSegments));
            }
        }

        public void DrawPolygon(
            IList<ScreenPoint> points, OxyColor fill, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode, double[]? dashArray, LineJoin lineJoin)
        {
            foreach (ScreenPoint point in points)
            {
                Check(point, nameof(DrawPolygon));
            }
        }

        public void DrawPolygons(
            IList<IList<ScreenPoint>> polygons, OxyColor fill, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode, double[]? dashArray, LineJoin lineJoin)
        {
            foreach (IList<ScreenPoint> polygon in polygons)
            {
                foreach (ScreenPoint point in polygon)
                {
                    Check(point, nameof(DrawPolygons));
                }
            }
        }

        public void DrawRectangle(
            OxyRect rectangle, OxyColor fill, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode) => Check(rectangle, nameof(DrawRectangle));

        public void DrawRectangles(
            IList<OxyRect> rectangles, OxyColor fill, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode)
        {
            foreach (OxyRect rectangle in rectangles)
            {
                Check(rectangle, nameof(DrawRectangles));
            }
        }

        public void DrawEllipse(
            OxyRect rect, OxyColor fill, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode) => Check(rect, nameof(DrawEllipse));

        public void DrawEllipses(
            IList<OxyRect> rectangles, OxyColor fill, OxyColor stroke, double thickness,
            EdgeRenderingMode edgeRenderingMode)
        {
            foreach (OxyRect rectangle in rectangles)
            {
                Check(rectangle, nameof(DrawEllipses));
            }
        }

        public void DrawText(
            ScreenPoint p, string text, OxyColor fill, string? fontFamily, double fontSize,
            double fontWeight, double rotation, HorizontalAlignment horizontalAlignment,
            VerticalAlignment verticalAlignment, OxySize? maxSize)
        {
        }

        public OxySize MeasureText(string text, string? fontFamily, double fontSize, double fontWeight) =>
            new((text?.Length ?? 0) * fontSize * 0.6, fontSize * 1.2);

        public void DrawImage(
            OxyImage source, double srcX, double srcY, double srcWidth, double srcHeight,
            double destX, double destY, double destWidth, double destHeight, double opacity,
            bool interpolate)
        {
        }

        public void SetToolTip(string text)
        {
        }

        public void CleanUp()
        {
        }

        public void PushClip(OxyRect clippingRectangle) => ClipCount++;

        public void PopClip() => ClipCount--;
    }
}
