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

        LineAnnotation mark = Assert.IsType<LineAnnotation>(
            Assert.Single(model.Annotations));
        Assert.Equal(Tag, mark.Tag);
        Assert.Equal(10.0, mark.X, 12);
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
