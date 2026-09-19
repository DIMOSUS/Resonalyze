using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>NaN tails past the measured window must never reach the render context: a NaN point overflows GDI+ DrawLine.</summary>
public sealed class WaterfallSeriesRenderTests
{
    [Fact]
    public void BurstDecay_WithNaNTail_RendersWithoutEmittingNonFiniteCoordinates()
    {
        using var measurement = CreateBroadbandTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

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
        NoiseMeasurement noiseMeasurement)
    {
        string calibrationPath = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");

        return new PlotModelFactory(
            measurement.Document,
            measurement.Engine,
            noiseMeasurement,
            mode => new CalibrationFile(calibrationPath),
            new PlotPresentationOptions(
                FrequencyResponse: new FrequencyResponseOptions(),
                PhaseResponse: new FrequencyResponseOptions(),
                GroupDelay: new FrequencyResponseOptions(),
                FrequencyResponseVisibility: new CurveVisibilityOptions(),
                PhaseResponseVisibility: new CurveVisibilityOptions(),
                GroupDelayVisibility: new CurveVisibilityOptions(),
                ImpulseResponse: new ImpulseResponseOptions(),
                LiveSpectrum: new LiveSpectrumOptions(),
                Waterfall: new WaterfallGenerateOptions(),
                BurstDecay: new WaterfallGenerateOptions { WaterfallMode = WaterfallMode.BurstDecay }));
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
