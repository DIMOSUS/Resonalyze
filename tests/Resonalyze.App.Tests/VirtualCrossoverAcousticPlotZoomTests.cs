using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// What the Virtual DSP acoustic plot lets the mouse do per view, and when its
/// right-hand sum-loss axis is there to be moved. Phase is a wrapped angle, so
/// its height is the whole range there is and stays locked; the impulse view's
/// time axis and the loss axis zoom, which only works if the constant redraws
/// stop re-arming them behind the user's back.
/// </summary>
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

        // Locked for the phase view only — the toggle back must give the dB and
        // the normalized impulse axes their zoom again.
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

        // Two flat channels at 10 and 20 ms: the axis fits itself around them.
        plot.Draw(GroupDelayRender(10, 20));
        UpdateData(view);
        Assert.True(value.ActualMinimum < 10 && value.ActualMinimum > 5);
        Assert.True(value.ActualMaximum > 20 && value.ActualMaximum < 25);

        // Every chain edit redraws this view; a zoom the user set survives it.
        value.Zoom(12, 14);
        Update(view);
        plot.Draw(GroupDelayRender(10, 20));
        Update(view);
        Assert.Equal(12, value.ActualMinimum, 6);
        Assert.Equal(14, value.ActualMaximum, 6);

        // Back to the phase view: the ±180° lock, and its own range.
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

        // Every chain edit redraws this view. The window has not moved, so the
        // zoom must still be there afterwards.
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

        // A gate move is a new timeline, not a redraw of the old one: the zoom
        // was taken on a window that no longer exists.
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

        // The step view draws on the impulse view's ms axis and unitless value
        // axis; the window is the same one, so the zoom taken on it survives.
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

        // Back on the frequency views, the ms axis leaves with the step view.
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
        // The curve binds to the right axis, the channel stays on the left one.
        List<LineSeries> drawn = view.Model!.Series.OfType<LineSeries>().ToList();
        Assert.Equal(loss.Key, Assert.Single(drawn, s => s.Title == "Sum loss").YAxisKey);
        Assert.Null(Assert.Single(drawn, s => s.Title == "A").YAxisKey);

        // The toggle off redraws without the curve: no scale for nothing.
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

        // An ordinary junction reads on the nominal scale: 0 dB just below the
        // top, -24 dB at the bottom.
        Assert.Equal(3, loss.ActualMaximum, 6);
        Assert.Equal(-24, loss.ActualMinimum, 6);

        // A notch past the nominal depth extends the range a whole step at a
        // time — the curve must not fall off its own axis.
        plot.Draw(MagnitudeRender(lossDepthDb: -31));
        Update(view);
        Assert.Equal(-36, loss.ActualMinimum, 6);
        Assert.Equal(-36, loss.AbsoluteMinimum, 6);

        // Below the floor a cancellation is total; the scale stops there.
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

        // Every chain edit redraws this view; a loss that stayed within the
        // nominal depth is not a new scale, so the zoom must survive it.
        plot.Draw(MagnitudeRender(lossDepthDb: -11));
        Update(view);
        Assert.Equal(-12, loss.ActualMinimum, 6);
        Assert.Equal(0, loss.ActualMaximum, 6);
    }

    // A magnitude frame: one channel on the left dB axis and, unless the
    // depth is null, a sum-loss curve dipping to that depth at 1 kHz.
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

    // A group-delay frame: two flat channels at the given arrivals.
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

    // ActualMinimum/Maximum are only recomputed while the model updates, which
    // a headless test never triggers by painting.
    private static void Update(PlotView view) =>
        ((IPlotModel)view.Model!).Update(false);

    // The auto-fit reads the series' own ranges, which only a data update fills.
    private static void UpdateData(PlotView view) =>
        ((IPlotModel)view.Model!).Update(true);
}
