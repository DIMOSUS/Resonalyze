using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Steps share ONE scale (unlike the per-trace normalized impulse view); the Sum includes hidden summing channels.</summary>
public sealed class VirtualCrossoverStepViewTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int SampleRate = 48_000;
    private const int FirstArrival = 480;
    private const int SecondArrival = 960;
    private const double SecondAmplitude = 0.5;

    [Fact]
    public void ShownChannels_AndTheSum_GoIn_AsStepTraces()
    {
        using VirtualCrossoverPanel panel = Loaded();
        ((CheckBox)Field(panel, "checkBoxShowSum")).Checked = true;
        List<ProcessedChannel> processed = Processed(panel);

        AcousticImpulseRender render = Build(panel, processed, processed)!;

        Assert.True(render.Step);
        Assert.Equal(SampleRate, render.SampleRate);
        Assert.Equal(3, render.Traces.Count);
        Assert.Equal(processed[0].Channel.Name, render.Traces[0].Title);
        Assert.Equal(processed[1].Channel.Name, render.Traces[1].Title);

        IrPreviewTrace sum = render.Traces[2];
        Assert.Equal("Sum", sum.Title);
        Assert.Equal(2.4, sum.Thickness);
        Assert.Equal(1.0, sum.Samples[FirstArrival].Real);
        Assert.Equal(SecondAmplitude, sum.Samples[SecondArrival].Real);
    }

    [Fact]
    public void AHiddenChannel_LeavesTheTraces_ButStaysInTheSum()
    {
        using VirtualCrossoverPanel panel = Loaded();
        ((CheckBox)Field(panel, "checkBoxShowSum")).Checked = true;
        List<ProcessedChannel> processed = Processed(panel);
        processed[1].Channel.Pair.ShowProcessedCurve = false;

        AcousticImpulseRender render = Build(panel, processed, processed)!;

        Assert.Equal(2, render.Traces.Count);
        Assert.Equal(processed[0].Channel.Name, render.Traces[0].Title);
        Assert.Equal("Sum", render.Traces[1].Title);
        Assert.Equal(SecondAmplitude, render.Traces[1].Samples[SecondArrival].Real);
    }

    [Fact]
    public void TheSum_NeedsTheToggle_AndTwoSummingChannels()
    {
        using VirtualCrossoverPanel panel = Loaded();
        var showSum = (CheckBox)Field(panel, "checkBoxShowSum");
        List<ProcessedChannel> processed = Processed(panel);

        showSum.Checked = false;
        Assert.Equal(2, Build(panel, processed, processed)!.Traces.Count);

        showSum.Checked = true;
        Assert.Equal(2, Build(panel, processed, [processed[0]])!.Traces.Count);
    }

    [Fact]
    public void NothingShown_DrawsNothing()
    {
        using VirtualCrossoverPanel panel = Loaded();
        ((CheckBox)Field(panel, "checkBoxShowSum")).Checked = true;
        List<ProcessedChannel> processed = Processed(panel);
        processed[0].Channel.Pair.ShowProcessedCurve = false;
        processed[1].Channel.Pair.ShowProcessedCurve = false;

        Assert.Null(Build(panel, processed, processed));
    }

    [Fact]
    public void StepTraces_ShareOneScale_WhereImpulseTracesEachFillTheAxis()
    {
        // Common scale is the Sum's 1.5 excursion, so steps read 2/3, 1/3 and 1.
        IrPreviewTrace first = Trace("A", FirstArrival, 1.0);
        IrPreviewTrace second = Trace("B", SecondArrival, SecondAmplitude);
        IrPreviewTrace sum = new(
            Dsp.VirtualCrossoverAnalysis.SumImpulseResponses([first.Samples, second.Samples]),
            "Sum",
            OxyColors.White,
            2.4);
        IrPreviewTrace[] traces = [first, second, sum];

        var impulseModel = new PlotModel();
        (double StartMs, double EndMs)? impulseWindow = ImpulseWindowPreview.AddGatedTraceSeries(
            impulseModel, traces, SampleRate, gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5);
        var stepModel = new PlotModel();
        (double StartMs, double EndMs)? stepWindow = ImpulseWindowPreview.AddStepTraceSeries(
            stepModel, traces, SampleRate, gateOffsetMs: 10, leftMs: 0.5, plateauMs: 15, rightMs: 5);

        Assert.Equal(impulseWindow, stepWindow);

        Assert.Equal(1.0, PeakOf(impulseModel, "A"), 9);
        Assert.Equal(1.0, PeakOf(impulseModel, "B"), 9);

        Assert.Equal(2.0 / 3.0, LastOf(stepModel, "A"), 9);
        Assert.Equal(1.0 / 3.0, LastOf(stepModel, "B"), 9);
        Assert.Equal(1.0, LastOf(stepModel, "Sum"), 9);
        Assert.Equal(2.4, Series(stepModel, "Sum").StrokeThickness);

        LineSeries late = Series(stepModel, "B");
        double secondMs = SecondArrival * 1_000.0 / SampleRate;
        Assert.All(
            late.Points.Where(point => point.X < secondMs - 0.05),
            point => Assert.Equal(0.0, point.Y));
    }

    [Fact]
    public void TheGate_FramesTheView_AndNeverEntersTheCurve()
    {
        // The step integrates from the record start, not the window edge, so a gate edit must not change it.
        IrPreviewTrace first = Trace("A", FirstArrival, 1.0);
        IrPreviewTrace second = Trace("B", SecondArrival, SecondAmplitude);
        IrPreviewTrace[] traces = [first, second];

        var wide = new PlotModel();
        ImpulseWindowPreview.AddStepTraceSeries(
            wide, traces, SampleRate, gateOffsetMs: 9, leftMs: 2, plateauMs: 30, rightMs: 10);
        var narrow = new PlotModel();
        ImpulseWindowPreview.AddStepTraceSeries(
            narrow, traces, SampleRate, gateOffsetMs: 12, leftMs: 0.5, plateauMs: 12, rightMs: 3);

        foreach (string title in new[] { "A", "B" })
        {
            Dictionary<double, double> wideByMs = Series(wide, title).Points
                .ToDictionary(point => point.X, point => point.Y);
            List<DataPoint> narrowPoints = Series(narrow, title).Points;
            Assert.NotEmpty(narrowPoints);
            Assert.All(narrowPoints, point =>
            {
                Assert.True(wideByMs.ContainsKey(point.X));
                Assert.Equal(wideByMs[point.X], point.Y, 12);
            });
        }
    }

    private static double PeakOf(PlotModel model, string title) =>
        Series(model, title).Points.Max(point => Math.Abs(point.Y));

    private static double LastOf(PlotModel model, string title) =>
        Series(model, title).Points[^1].Y;

    private static LineSeries Series(PlotModel model, string title) =>
        model.Series.OfType<LineSeries>().Single(series => series.Title == title);

    private static IrPreviewTrace Trace(string title, int sample, double amplitude) =>
        new(Delta(sample, amplitude), title, OxyColors.White);

    [Fact]
    public void TheOppositeSidesSum_RidesAlong_ThinDashedTranslucent_OnTheSameClock()
    {
        using VirtualCrossoverPanel panel = Loaded();
        var showSum = (CheckBox)Field(panel, "checkBoxShowSum");
        List<ProcessedChannel> processed = Processed(panel);
        VirtualCrossoverSideSum opposite = new(
            Delta(SecondArrival + 48, 1.5), SecondArrival + 48, SampleRate, processed);

        showSum.Checked = true;
        Project(panel).ActiveSideRight = true;
        AcousticImpulseRender render = Build(panel, processed, processed, opposite)!;
        Assert.Equal(4, render.Traces.Count);
        IrPreviewTrace trace = render.Traces[3];
        Assert.Equal("Sum L", trace.Title);
        Assert.Same(opposite.ImpulseResponse, trace.Samples);
        Assert.Equal(1.0, trace.Thickness);
        Assert.Equal(LineStyle.Dash, trace.Style);
        Assert.Equal(110, trace.Color.A);
        Assert.Equal(OxyColors.White.R, trace.Color.R);

        showSum.Checked = false;
        Assert.Equal(2, Build(panel, processed, processed, opposite)!.Traces.Count);
        showSum.Checked = true;
        VirtualCrossoverSideSum otherRate = opposite with { SampleRate = 96_000 };
        Assert.Equal(3, Build(panel, processed, processed, otherRate)!.Traces.Count);
    }

    private static AcousticImpulseRender? Build(
        VirtualCrossoverPanel panel,
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel> summed,
        VirtualCrossoverSideSum? opposite = null) =>
        (AcousticImpulseRender?)panel.GetType()
            .GetMethod("BuildStepRender", Hidden)!
            .Invoke(panel, [processed, summed, opposite]);

    private static List<ProcessedChannel> Processed(VirtualCrossoverPanel panel)
    {
        List<VirtualCrossoverChannel> channels = Channels(panel);
        channels[0].Pair.ShowProcessedCurve = true;
        channels[1].Pair.ShowProcessedCurve = true;
        return
        [
            new ProcessedChannel(
                channels[0], Delta(FirstArrival, 1.0), FirstArrival, SampleRate, OxyColors.Red),
            new ProcessedChannel(
                channels[1], Delta(SecondArrival, SecondAmplitude), SecondArrival, SampleRate,
                OxyColors.Blue)
        ];
    }

    private static Complex[] Delta(int sample, double amplitude)
    {
        var impulse = new Complex[8_192];
        impulse[sample] = new Complex(amplitude, 0.0);
        return impulse;
    }

    private static VirtualCrossoverPanel Loaded()
    {
        var panel = new VirtualCrossoverPanel();
        List<VirtualCrossoverChannel> channels = Channels(panel);
        for (int index = 0; index < channels.Count; index++)
        {
            channels[index].Pair = Project(panel).Pairs[index];
        }

        return panel;
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Hidden)!.GetValue(target)!;

    private static List<VirtualCrossoverChannel> Channels(VirtualCrossoverPanel panel) =>
        panel.Session.Channels;

    private static VirtualCrossoverProjectFile Project(VirtualCrossoverPanel panel) =>
        panel.Session.Project;
}
