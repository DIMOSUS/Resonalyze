using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// What the Virtual DSP acoustic plot lets the mouse do per view. Phase is a
/// wrapped angle, so its height is the whole range there is and stays locked;
/// the impulse view's time axis zooms, which only works if the constant redraws
/// stop re-arming it behind the user's back.
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

    private static IrPreviewTrace MakeTrace(string title, int peakSample)
    {
        var samples = new Complex[4096];
        samples[peakSample] = new Complex(1.0, 0);
        return new IrPreviewTrace(samples, title, OxyColors.White);
    }

    private static Axis ValueAxis(PlotView view) =>
        view.Model!.Axes.First(axis => axis.Position == AxisPosition.Left);

    private static Axis TimeAxis(PlotView view) =>
        view.Model!.Axes.First(axis =>
            axis.Position == AxisPosition.Bottom && axis is LinearAxis);

    // ActualMinimum/Maximum are only recomputed while the model updates, which
    // a headless test never triggers by painting.
    private static void Update(PlotView view) =>
        ((IPlotModel)view.Model!).Update(false);
}
