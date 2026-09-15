using System.Numerics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

// Pins the shared gated-IR rendering used by both the gate dialog's preview
// and the Virtual DSP impulse view: per-trace normalization, the Tukey gate
// outline, the gate-offset mark, and the tag the host sweeps on redraw.
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
        // Two arrivals 10 ms apart with very different amplitudes: independent
        // normalization must bring BOTH peaks to ±1 so each stays visible.
        IrPreviewTrace loud = MakeTrace("A", peakSample: 480, amplitude: 0.5);
        IrPreviewTrace quiet = MakeTrace("B", peakSample: 960, amplitude: -0.02);

        (double StartMs, double EndMs)? window =
            ImpulseWindowPreview.AddGatedTraceSeries(
                model, [loud, quiet], SampleRate,
                gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5, Tag);

        Assert.NotNull(window);
        // The display window covers the whole gate (9.5 ms to 30.5 ms) plus
        // context on both sides.
        Assert.True(window.Value.StartMs < 9.5);
        Assert.True(window.Value.EndMs > 30.5);

        List<LineSeries> series = model.Series
            .OfType<LineSeries>()
            .Where(item => Equals(item.Tag, Tag))
            .ToList();
        // Two traces plus the untitled gate-window outline.
        Assert.Equal(3, series.Count);
        Assert.Equal("A", series[0].Title);
        Assert.Equal("B", series[1].Title);
        Assert.Null(series[2].Title);

        Assert.Equal(1.0, series[0].Points.Max(point => Math.Abs(point.Y)), 12);
        Assert.Equal(1.0, series[1].Points.Max(point => Math.Abs(point.Y)), 12);
        // The Tukey gate plateau reaches weight 1 and never leaves [0, 1].
        Assert.Equal(1.0, series[2].Points.Max(point => point.Y), 12);
        Assert.True(series[2].Points.All(point => point.Y is >= 0 and <= 1));
        // The window outline is dashed so it reads apart from the solid IR traces.
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
        // A tone burst whose envelope crests where its carrier crosses zero: the
        // sample peak sits a quarter cycle off, ~12 % under the crest, so on the
        // sample peak's scale the envelope would run past the ±1 axis.
        IrPreviewTrace burst = MakeBurst("A", centerSample: 720, carrierHz: 500);

        ImpulseWindowPreview.AddGatedTraceSeries(
            model, [burst], SampleRate,
            gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5, Tag,
            envelopes: true);

        List<LineSeries> series = model.Series.OfType<LineSeries>().ToList();
        // The upper and lower guide, then the trace over them, then the gate.
        Assert.Equal(4, series.Count);
        (LineSeries upper, LineSeries lower, LineSeries trace) =
            (series[0], series[1], series[2]);
        Assert.Equal("A", trace.Title);
        foreach (LineSeries guide in new[] { upper, lower })
        {
            Assert.Equal("A envelope", guide.Title);
            Assert.False(guide.RenderInLegend);
            Assert.Equal(OxyColor.FromAColor(50, burst.Color), guide.Color);
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

        // The step of an envelope is nothing; the step view draws none.
        plot.ConfigureForView(AcousticView.Step);
        plot.Draw(new AcousticRender(string.Empty, [], impulse with { Step = true }));
        Assert.DoesNotContain(
            view.Model!.Series.OfType<LineSeries>(),
            item => item.Title?.EndsWith(" envelope", StringComparison.Ordinal) == true);

        // Nor does the gate dialog's compact preview.
        using var preview = new OxyPlot.WindowsForms.PlotView();
        ImpulseWindowPreview.UpdateGatedMulti(
            preview, traces, SampleRate,
            gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5);
        Assert.Equal(0, EnvelopeGuides(preview.Model!, "A"));
    }

    [Fact]
    public void EnvelopeOf_IsMemoizedPerArray_SoAWarmUpServesTheDraw()
    {
        // The Virtual DSP computes the envelopes off the UI thread before the
        // frame; the draw only stays cheap if it reads that very result.
        IrPreviewTrace burst = MakeBurst("A", centerSample: 720, carrierHz: 500);

        double[] warmed = ImpulseWindowPreview.EnvelopeOf(burst.Samples);

        Assert.Same(warmed, ImpulseWindowPreview.EnvelopeOf(burst.Samples));
        Assert.NotSame(
            warmed,
            ImpulseWindowPreview.EnvelopeOf((Complex[])burst.Samples.Clone()));
    }

    private static int EnvelopeGuides(PlotModel model, string channel) =>
        model.Series.OfType<LineSeries>().Count(item => item.Title == channel + " envelope");

    // A Gaussian-modulated sine, 1 ms sigma, its carrier crossing zero at the
    // envelope's crest.
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
