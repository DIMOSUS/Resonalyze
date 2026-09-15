using System.Numerics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class ImpulseWindowPreviewTests
{
    private const int SampleRate = 48_000;
    private const string Tag = "test-tag";

    [Fact]
    public void AddGatedTraceSeries_NoTraces_AddsNothingAndReturnsNull()
    {
        var model = new PlotModel();

        (double, double)? window = ImpulseWindowPreview.AddGatedTraceSeries(
            model, [], SampleRate,
            gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5);

        Assert.Null(window);
        Assert.Empty(model.Series);
        Assert.Empty(model.Annotations);
    }

    [Fact]
    public void AddGatedTraceSeries_NormalizesEachTraceAndTagsEverything()
    {
        var model = new PlotModel();
        IrPreviewTrace loud = MakeTrace("A", peakSample: 480, amplitude: 0.5);
        IrPreviewTrace quiet = MakeTrace("B", peakSample: 960, amplitude: -0.02);

        (double StartMs, double EndMs)? window =
            ImpulseWindowPreview.AddGatedTraceSeries(
                model, [loud, quiet], SampleRate,
                gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5, Tag);

        Assert.NotNull(window);
        Assert.True(window.Value.StartMs < 9.5);
        Assert.True(window.Value.EndMs > 30.5);

        List<LineSeries> series = model.Series
            .OfType<LineSeries>()
            .Where(item => Equals(item.Tag, Tag))
            .ToList();
        Assert.Equal(3, series.Count);
        Assert.Equal("A", series[0].Title);
        Assert.Equal("B", series[1].Title);
        Assert.Null(series[2].Title);

        Assert.Equal(1.0, series[0].Points.Max(point => Math.Abs(point.Y)), 12);
        Assert.Equal(1.0, series[1].Points.Max(point => Math.Abs(point.Y)), 12);
        Assert.Equal(1.0, series[2].Points.Max(point => point.Y), 12);
        Assert.True(series[2].Points.All(point => point.Y is >= 0 and <= 1));
        Assert.Equal(LineStyle.Dash, series[2].LineStyle);

        LineAnnotation mark = Assert.IsType<LineAnnotation>(
            Assert.Single(model.Annotations));
        Assert.Equal(Tag, mark.Tag);
        Assert.Equal(10.0, mark.X, 12);
    }

    [Fact]
    public void AddGatedTraceSeries_WithEnvelopes_WrapsEachTraceOnTheEnvelopesScale()
    {
        var model = new PlotModel();
        // The envelope crests where the carrier crosses zero: the sample peak is ~12 % under the crest.
        IrPreviewTrace burst = MakeBurst("A", centerSample: 720, carrierHz: 500);

        ImpulseWindowPreview.AddGatedTraceSeries(
            model, [burst], SampleRate,
            gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5, Tag,
            envelopes: true);

        List<LineSeries> series = model.Series.OfType<LineSeries>().ToList();
        Assert.Equal(4, series.Count);
        (LineSeries upper, LineSeries lower, LineSeries trace) =
            (series[0], series[1], series[2]);
        Assert.Equal("A", trace.Title);
        foreach (LineSeries guide in new[] { upper, lower })
        {
            Assert.Equal("A envelope", guide.Title);
            Assert.False(guide.RenderInLegend);
            Assert.Equal(OxyColor.FromAColor(30, burst.Color), guide.Color);
            Assert.True(guide.StrokeThickness < trace.StrokeThickness);
            Assert.Equal(Tag, guide.Tag);
        }

        Assert.Equal(1.0, upper.Points.Max(point => point.Y), 9);
        Assert.Equal(-1.0, lower.Points.Min(point => point.Y), 9);
        for (int i = 0; i < trace.Points.Count; i++)
        {
            Assert.Equal(trace.Points[i].X, upper.Points[i].X);
            Assert.Equal(-upper.Points[i].Y, lower.Points[i].Y, 12);
            Assert.True(Math.Abs(trace.Points[i].Y) <= upper.Points[i].Y + 1e-9);
        }

        double tracePeak = trace.Points.Max(point => Math.Abs(point.Y));
        Assert.InRange(tracePeak, 0.85, 0.92);
    }

    [Fact]
    public void Envelopes_DrawOnTheVirtualDspImpulseViewOnly()
    {
        IrPreviewTrace[] traces =
        [
            MakeBurst("A", centerSample: 720, carrierHz: 500),
            MakeBurst("B", centerSample: 960, carrierHz: 2_000)
        ];
        var impulse = new AcousticImpulseRender(
            traces, SampleRate, GateOffsetMs: 10, LeftMs: 0.5, PlateauMs: 15, RightMs: 5);

        using var view = new OxyPlot.WindowsForms.PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Impulse);
        plot.Draw(new AcousticRender(string.Empty, [], impulse));
        Assert.Equal(2, EnvelopeGuides(view.Model!, "A"));
        Assert.Equal(2, EnvelopeGuides(view.Model!, "B"));

        plot.ConfigureForView(AcousticView.Step);
        plot.Draw(new AcousticRender(string.Empty, [], impulse with { Step = true }));
        Assert.DoesNotContain(
            view.Model!.Series.OfType<LineSeries>(),
            item => item.Title?.EndsWith(" envelope", StringComparison.Ordinal) == true);

        using var preview = new OxyPlot.WindowsForms.PlotView();
        ImpulseWindowPreview.UpdateGatedMulti(
            preview, traces, SampleRate,
            gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5);
        Assert.Equal(0, EnvelopeGuides(preview.Model!, "A"));
    }

    [Fact]
    public void Envelopes_AreReadOverTheWholeRecord_NotTheDisplayedWindow()
    {
        var model = new PlotModel();
        // A burst straddling the window edge: a transform over displayed samples alone wraps the cut onto the start.
        const int displayEnd = 1_563;
        IrPreviewTrace burst = MakeBurst("A", centerSample: displayEnd, carrierHz: 500);

        ImpulseWindowPreview.AddGatedTraceSeries(
            model, [burst], SampleRate,
            gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5, Tag,
            envelopes: true);
        LineSeries upper = model.Series.OfType<LineSeries>().First();
        int first = (int)Math.Round(upper.Points[0].X * SampleRate / 1_000.0);
        Assert.Equal(displayEnd, first + upper.Points.Count - 1);

        double[] whole = SignalEnvelope.Envelope(
            [.. burst.Samples.Select(sample => sample.Real)]);
        double[] windowed = SignalEnvelope.Envelope(
            [.. burst.Samples.Skip(first).Take(upper.Points.Count).Select(sample => sample.Real)]);
        double wholePeak = whole.Skip(first).Take(upper.Points.Count).Max();
        double windowedPeak = windowed.Max();

        double largestWindowedGap = 0;
        for (int i = 0; i < upper.Points.Count; i++)
        {
            Assert.Equal(whole[first + i] / wholePeak, upper.Points[i].Y, 9);
            largestWindowedGap = Math.Max(
                largestWindowedGap,
                Math.Abs(windowed[i] / windowedPeak - upper.Points[i].Y));
        }

        Assert.True(largestWindowedGap > 0.05, $"gap {largestWindowedGap}");
    }

    [Fact]
    public void EnvelopeOf_IsMemoizedPerArray_SoAWarmUpServesTheDraw()
    {
        // Envelopes are computed off the UI thread; the draw must read that result.
        IrPreviewTrace burst = MakeBurst("A", centerSample: 720, carrierHz: 500);

        double[] warmed = ImpulseWindowPreview.EnvelopeOf(burst.Samples);

        Assert.Same(warmed, ImpulseWindowPreview.EnvelopeOf(burst.Samples));
        Assert.NotSame(
            warmed,
            ImpulseWindowPreview.EnvelopeOf((Complex[])burst.Samples.Clone()));
    }

    private static int EnvelopeGuides(PlotModel model, string channel) =>
        model.Series.OfType<LineSeries>().Count(item => item.Title == channel + " envelope");

    private static IrPreviewTrace MakeBurst(string title, int centerSample, double carrierHz)
    {
        const double sigmaSamples = SampleRate / 1_000.0;
        var samples = new Complex[SampleRate / 10];
        for (int i = 0; i < samples.Length; i++)
        {
            double offset = i - centerSample;
            samples[i] = Math.Exp(-offset * offset / (2 * sigmaSamples * sigmaSamples)) *
                Math.Sin(2 * Math.PI * carrierHz * offset / SampleRate);
        }

        return new IrPreviewTrace(samples, title, OxyColors.Orange);
    }

    private static IrPreviewTrace MakeTrace(
        string title,
        int peakSample,
        double amplitude)
    {
        var samples = new Complex[SampleRate / 10];
        samples[peakSample] = amplitude;
        return new IrPreviewTrace(samples, title, OxyColors.White);
    }
}
