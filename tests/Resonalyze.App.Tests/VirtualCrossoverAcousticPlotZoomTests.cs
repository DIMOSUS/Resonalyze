using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Phase height is the whole wrapped range and stays locked; other axes zoom only if redraws stop re-arming them.</summary>
public sealed class VirtualCrossoverAcousticPlotZoomTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void PhaseView_LocksTheHeightAndLeavesTheOtherViewsFree()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Magnitude);
        Axis value = ValueAxis(view);

        Assert.True(value.IsZoomEnabled);
        Assert.True(value.IsPanEnabled);

        plot.ConfigureForView(AcousticView.Phase);
        Assert.False(value.IsZoomEnabled);
        Assert.False(value.IsPanEnabled);

        plot.ConfigureForView(AcousticView.Magnitude);
        Assert.True(value.IsZoomEnabled);
        Assert.True(value.IsPanEnabled);

        plot.ConfigureForView(AcousticView.Impulse);
        Assert.True(value.IsZoomEnabled);
    }

    [Fact]
    public void GroupDelayView_FitsItselfAndZoomsFreely_AndThePhaseLockComesBack()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Phase);
        Axis value = ValueAxis(view);
        Assert.False(value.IsZoomEnabled);

        plot.ConfigureForView(AcousticView.GroupDelay);
        Assert.Equal("ms", value.Title);
        Assert.True(value.IsZoomEnabled);
        Assert.True(value.IsPanEnabled);
        Assert.True(double.IsNaN(value.Minimum));
        Assert.True(double.IsNaN(value.Maximum));

        plot.Draw(GroupDelayRender(10, 20));
        UpdateData(view);
        Assert.True(value.ActualMinimum < 10 && value.ActualMinimum > 5);
        Assert.True(value.ActualMaximum > 20 && value.ActualMaximum < 25);

        value.Zoom(12, 14);
        Update(view);
        plot.Draw(GroupDelayRender(10, 20));
        Update(view);
        Assert.Equal(12, value.ActualMinimum, 6);
        Assert.Equal(14, value.ActualMaximum, 6);

        plot.ConfigureForView(AcousticView.Phase);
        Assert.False(value.IsZoomEnabled);
        Assert.False(value.IsPanEnabled);
        Assert.Equal(-180, value.Minimum);
        Assert.Equal(180, value.Maximum);
    }

    [Fact]
    public void ImpulseView_RedrawnOnTheSameWindow_KeepsTheZoom()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Impulse);
        plot.Draw(Render(gateOffsetMs: 10));

        Axis time = TimeAxis(view);
        Assert.True(time.IsZoomEnabled);

        time.Zoom(9.8, 10.2);
        Update(view);
        Assert.Equal(9.8, time.ActualMinimum, 6);
        Assert.Equal(10.2, time.ActualMaximum, 6);

        plot.Draw(Render(gateOffsetMs: 10));
        Update(view);

        Assert.Equal(9.8, time.ActualMinimum, 6);
        Assert.Equal(10.2, time.ActualMaximum, 6);
    }

    [Fact]
    public void ImpulseView_RedrawnOnAMovedWindow_ReArmsToIt()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Impulse);
        plot.Draw(Render(gateOffsetMs: 10));

        Axis time = TimeAxis(view);
        time.Zoom(9.8, 10.2);
        Update(view);

        // A gate move is a new timeline, so the zoom is dropped.
        plot.Draw(Render(gateOffsetMs: 25));
        Update(view);

        Assert.True(time.ActualMaximum - time.ActualMinimum > 1);
        Assert.Equal(time.AbsoluteMinimum, time.ActualMinimum, 6);
        Assert.Equal(time.AbsoluteMaximum, time.ActualMaximum, 6);
    }

    [Fact]
    public void StepView_SharesTheImpulseViewsAxes_AndTheZoomBetweenThem()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Impulse);
        plot.Draw(Render(gateOffsetMs: 10));
        Axis time = TimeAxis(view);
        Axis value = ValueAxis(view);
        time.Zoom(9.8, 10.2);
        Update(view);

        plot.ConfigureForView(AcousticView.Step);
        plot.Draw(StepRender(gateOffsetMs: 10));
        Update(view);

        Assert.Same(time, TimeAxis(view));
        Assert.DoesNotContain(view.Model!.Axes, axis => axis is LogarithmicAxis);
        Assert.Equal(string.Empty, value.Title);
        Assert.Equal(-1.05, value.AbsoluteMinimum);
        Assert.Equal(1.05, value.AbsoluteMaximum);
        Assert.True(value.IsZoomEnabled);
        Assert.Equal(9.8, time.ActualMinimum, 6);
        Assert.Equal(10.2, time.ActualMaximum, 6);

        plot.ConfigureForView(AcousticView.Magnitude);
        Assert.Contains(view.Model!.Axes, axis => axis is LogarithmicAxis);
        Assert.DoesNotContain(view.Model!.Axes, axis => ReferenceEquals(axis, time));
    }

    [Fact]
    public void LossAxis_HidesOnTheStepView()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Magnitude);
        plot.Draw(MagnitudeRender(lossDepthDb: -10));
        Axis loss = LossAxis(view);
        Assert.True(loss.IsAxisVisible);

        plot.ConfigureForView(AcousticView.Step);
        Assert.False(loss.IsAxisVisible);
        plot.Draw(StepRender(gateOffsetMs: 10));
        Assert.False(loss.IsAxisVisible);
    }

    [Fact]
    public void LossAxis_ShowsOnlyWhileALossCurveIsDrawn()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Magnitude);
        Axis loss = LossAxis(view);
        Assert.False(loss.IsAxisVisible);

        plot.Draw(MagnitudeRender(lossDepthDb: -10));
        Assert.True(loss.IsAxisVisible);
        Assert.Equal(AxisPosition.Right, loss.Position);
        List<LineSeries> drawn = view.Model!.Series.OfType<LineSeries>().ToList();
        Assert.Equal(loss.Key, Assert.Single(drawn, s => s.Title == "Sum loss").YAxisKey);
        Assert.Null(Assert.Single(drawn, s => s.Title == "A").YAxisKey);

        plot.Draw(MagnitudeRender(lossDepthDb: null));
        Assert.False(loss.IsAxisVisible);
    }

    [Fact]
    public void LossAxis_HidesOnThePhaseAndImpulseViews()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Magnitude);
        plot.Draw(MagnitudeRender(lossDepthDb: -10));
        Axis loss = LossAxis(view);
        Assert.True(loss.IsAxisVisible);

        plot.ConfigureForView(AcousticView.Phase);
        Assert.False(loss.IsAxisVisible);

        plot.ConfigureForView(AcousticView.Magnitude);
        plot.Draw(MagnitudeRender(lossDepthDb: -10));
        Assert.True(loss.IsAxisVisible);

        plot.ConfigureForView(AcousticView.Impulse);
        plot.Draw(Render(gateOffsetMs: 10));
        Assert.False(loss.IsAxisVisible);
    }

    [Fact]
    public void LossAxis_OpensOnTheNominalDepthAndGrowsToHoldADeeperNotch()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Magnitude);
        plot.Draw(MagnitudeRender(lossDepthDb: -10));
        Axis loss = LossAxis(view);
        Update(view);

        Assert.Equal(3, loss.ActualMaximum, 6);
        Assert.Equal(-24, loss.ActualMinimum, 6);

        plot.Draw(MagnitudeRender(lossDepthDb: -31));
        Update(view);
        Assert.Equal(-36, loss.ActualMinimum, 6);
        Assert.Equal(-36, loss.AbsoluteMinimum, 6);

        plot.Draw(MagnitudeRender(lossDepthDb: -200));
        Update(view);
        Assert.Equal(-60, loss.ActualMinimum, 6);
    }

    [Fact]
    public void LossAxis_RedrawnOnTheSameDepth_KeepsTheZoom()
    {
        using var view = new PlotView();
        var plot = new VirtualCrossoverAcousticPlot(view, "hint", AcousticView.Magnitude);
        plot.Draw(MagnitudeRender(lossDepthDb: -10));
        Axis loss = LossAxis(view);
        Assert.True(loss.IsZoomEnabled);

        loss.Zoom(-12, 0);
        Update(view);
        Assert.Equal(-12, loss.ActualMinimum, 6);
        Assert.Equal(0, loss.ActualMaximum, 6);

        plot.Draw(MagnitudeRender(lossDepthDb: -11));
        Update(view);
        Assert.Equal(-12, loss.ActualMinimum, 6);
        Assert.Equal(0, loss.ActualMaximum, 6);
    }

    private static AcousticRender MagnitudeRender(double? lossDepthDb)
    {
        var channel = new List<SignalPoint>();
        var loss = new List<SignalPoint>();
        for (double hz = 20; hz <= 20_000; hz *= 1.1)
        {
            channel.Add(new SignalPoint(hz, -20));
            loss.Add(new SignalPoint(hz, hz is > 900 and < 1100 ? lossDepthDb ?? 0 : -1));
        }

        var curves = new List<AcousticCurve>
        {
            new("A", channel, OxyColors.White, 1.8, LineStyle.Solid)
        };
        if (lossDepthDb.HasValue)
        {
            curves.Add(new AcousticCurve(
                "Sum loss", loss, OxyColors.Yellow, 1.8, LineStyle.Dash, OnLossAxis: true));
        }

        return new AcousticRender(string.Empty, curves, null);
    }

    private static AcousticRender GroupDelayRender(double firstMs, double secondMs)
    {
        var first = new List<SignalPoint>();
        var second = new List<SignalPoint>();
        for (double hz = 20; hz <= 20_000; hz *= 1.1)
        {
            first.Add(new SignalPoint(hz, firstMs));
            second.Add(new SignalPoint(hz, secondMs));
        }

        return new AcousticRender(
            string.Empty,
            [
                new AcousticCurve("A", first, OxyColors.White, 1.8, LineStyle.Solid),
                new AcousticCurve("B", second, OxyColors.Red, 1.8, LineStyle.Solid)
            ],
            null);
    }

    private static AcousticRender Render(double gateOffsetMs)
    {
        var impulse = new AcousticImpulseRender(
            [MakeTrace("A", peakSample: 480), MakeTrace("B", peakSample: 960)],
            SampleRate,
            gateOffsetMs,
            LeftMs: 0.5,
            PlateauMs: 15,
            RightMs: 5);
        return new AcousticRender(string.Empty, [], impulse);
    }

    private static AcousticRender StepRender(double gateOffsetMs)
    {
        var step = new AcousticImpulseRender(
            [MakeTrace("A", peakSample: 480), MakeTrace("B", peakSample: 960)],
            SampleRate,
            gateOffsetMs,
            LeftMs: 0.5,
            PlateauMs: 15,
            RightMs: 5,
            Step: true);
        return new AcousticRender(string.Empty, [], step);
    }

    private static IrPreviewTrace MakeTrace(string title, int peakSample)
    {
        var samples = new Complex[4096];
        samples[peakSample] = new Complex(1.0, 0);
        return new IrPreviewTrace(samples, title, OxyColors.White);
    }

    private static Axis ValueAxis(PlotView view) =>
        view.Model!.Axes.First(axis => axis.Position == AxisPosition.Left);

    private static Axis LossAxis(PlotView view) =>
        view.Model!.Axes.First(axis => axis.Position == AxisPosition.Right);

    private static Axis TimeAxis(PlotView view) =>
        view.Model!.Axes.First(axis =>
            axis.Position == AxisPosition.Bottom && axis is LinearAxis);

    // ActualMinimum/Maximum are only recomputed on model update, which headless tests never trigger by painting.
    private static void Update(PlotView view) =>
        ((IPlotModel)view.Model!).Update(false);

    private static void UpdateData(PlotView view) =>
        ((IPlotModel)view.Model!).Update(true);
}
