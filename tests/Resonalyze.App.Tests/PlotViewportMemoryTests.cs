using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

// What keeps a zoom alive across the constant model rebuilds: which axes are
// carried over, which are left to the new model, and how a mode gets its own.
public sealed class PlotViewportMemoryTests
{
    [Fact]
    public void Show_CarriesAZoomedAxisOntoTheNextModelOfTheSameMode()
    {
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        PlotModel first = FrequencyModel();
        memory.Show(first, Mode.FrequencyResponse);

        Axis(first, PlotModelFactory.FrequencyAxisKey).Zoom(100, 500);

        PlotModel second = FrequencyModel();
        memory.Show(second, Mode.FrequencyResponse);
        Update(second);

        Axis frequency = Axis(second, PlotModelFactory.FrequencyAxisKey);
        Assert.Equal(100, frequency.ActualMinimum, 6);
        Assert.Equal(500, frequency.ActualMaximum, 6);
    }

    [Fact]
    public void Show_CarriesTheZoomAcrossSeveralRebuildsWithoutFurtherGestures()
    {
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        memory.Show(FrequencyModel(), Mode.FrequencyResponse);
        Axis(view.Model, PlotModelFactory.FrequencyAxisKey).Zoom(100, 500);

        // A settings change, then another one: the second rebuild must not lose what
        // the first one carried over.
        memory.Show(FrequencyModel(), Mode.FrequencyResponse);
        PlotModel third = FrequencyModel();
        memory.Show(third, Mode.FrequencyResponse);
        Update(third);

        Assert.Equal(100, Axis(third, PlotModelFactory.FrequencyAxisKey).ActualMinimum, 6);
        Assert.Equal(500, Axis(third, PlotModelFactory.FrequencyAxisKey).ActualMaximum, 6);
    }

    [Fact]
    public void Show_LeavesAnUntouchedAxisToTheNewModel()
    {
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        memory.Show(FrequencyModel(), Mode.FrequencyResponse);
        Axis(view.Model, PlotModelFactory.FrequencyAxisKey).Zoom(100, 500);

        // The next model opens its dB axis higher — the ceiling a padded loopback
        // lifts. The frequency zoom must survive; the dB axis must NOT be pinned
        // back to the old window.
        PlotModel second = FrequencyModel(decibelMaximum: 30);
        memory.Show(second, Mode.FrequencyResponse);
        Update(second);

        Assert.Equal(100, Axis(second, PlotModelFactory.FrequencyAxisKey).ActualMinimum, 6);
        Assert.Equal(30, Axis(second, PlotModelFactory.DecibelAxisKey).ActualMaximum, 6);
    }

    [Fact]
    public void Show_KeepsOneZoomPerMode()
    {
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        memory.Show(FrequencyModel(), Mode.FrequencyResponse);
        Axis(view.Model, PlotModelFactory.FrequencyAxisKey).Zoom(100, 500);

        // Away to another mode with its own zoom, and back.
        memory.Show(FrequencyModel(), Mode.PhaseResponse);
        Axis(view.Model, PlotModelFactory.FrequencyAxisKey).Zoom(2_000, 8_000);
        PlotModel back = FrequencyModel();
        memory.Show(back, Mode.FrequencyResponse);
        Update(back);

        Assert.Equal(100, Axis(back, PlotModelFactory.FrequencyAxisKey).ActualMinimum, 6);
        Assert.Equal(500, Axis(back, PlotModelFactory.FrequencyAxisKey).ActualMaximum, 6);
    }

    [Fact]
    public void Show_AfterAResetTheModeScalesItselfAgain()
    {
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        memory.Show(FrequencyModel(), Mode.FrequencyResponse);
        Axis frequency = Axis(view.Model, PlotModelFactory.FrequencyAxisKey);
        frequency.Zoom(100, 500);
        frequency.Reset();

        PlotModel second = FrequencyModel();
        memory.Show(second, Mode.FrequencyResponse);
        Update(second);

        Assert.Equal(20, Axis(second, PlotModelFactory.FrequencyAxisKey).ActualMinimum, 6);
        Assert.Equal(20_000, Axis(second, PlotModelFactory.FrequencyAxisKey).ActualMaximum, 6);
    }

    [Fact]
    public void Forget_DropsTheModesZoomForAScaleChange()
    {
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        memory.Show(FrequencyModel(), Mode.FrequencyResponse);
        Axis(view.Model, PlotModelFactory.FrequencyAxisKey).Zoom(100, 500);

        memory.Forget(Mode.FrequencyResponse);
        PlotModel second = FrequencyModel();
        memory.Show(second, Mode.FrequencyResponse);
        Update(second);

        Assert.Equal(20, Axis(second, PlotModelFactory.FrequencyAxisKey).ActualMinimum, 6);
        Assert.Equal(20_000, Axis(second, PlotModelFactory.FrequencyAxisKey).ActualMaximum, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Show_KeepsAnOverlayWideningOutOfTheRememberedZoom(bool overlayArrivesAfterTheDraw)
    {
        // The autocorrelation shape: axes that take their range from the data, and a
        // mode that allows overlays. An overlay can join the model at the draw or
        // well after it — a mode switch restores the saved slots only after
        // ModeController has drawn, and Show All acts later still — and neither is a
        // user zoom.
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        PlotModel first = AutoScaledModel();
        if (!overlayArrivesAfterTheDraw)
        {
            AddCurve(first, -40, 40);
        }

        memory.Show(first, Mode.Autocorrelation);
        if (overlayArrivesAfterTheDraw)
        {
            AddCurve(first, -40, 40);
        }

        // What the repaint that follows an overlay does: recompute the data ranges,
        // which is what widens an auto-scaled axis.
        ((IPlotModel)first).Update(true);

        PlotModel second = AutoScaledModel();
        memory.Show(second, Mode.Autocorrelation);
        Update(second);

        // The second model carries no overlay, so its axis must show its OWN data,
        // not the range the overlay stretched the first one to.
        Axis time = Axis(second, PlotModelFactory.TimeAxisKey);
        Assert.True(time.ActualMinimum > -20);
        Assert.True(time.ActualMaximum < 20);
    }

    [Fact]
    public void Show_KeepsAZoomWhileAnOverlayWidensTheOtherAxis()
    {
        // Both at once: the user forced the time axis, an overlay widened the value
        // axis. One is a zoom to carry, the other is the model scaling itself.
        using var view = new PlotView();
        var memory = new PlotViewportMemory(view);
        PlotModel first = AutoScaledModel();
        memory.Show(first, Mode.Autocorrelation);
        Axis(first, PlotModelFactory.TimeAxisKey).Zoom(-1, 1);
        AddCurve(first, -40, 40);
        ((IPlotModel)first).Update(true);

        PlotModel second = AutoScaledModel();
        memory.Show(second, Mode.Autocorrelation);
        Update(second);

        Assert.Equal(-1, Axis(second, PlotModelFactory.TimeAxisKey).ActualMinimum, 6);
        Assert.Equal(1, Axis(second, PlotModelFactory.TimeAxisKey).ActualMaximum, 6);
        Assert.True(Axis(second, PlotModelFactory.AutocorrelationAxisKey).ActualMaximum < 2);
    }

    [Fact]
    public void Apply_RestoresByAxisKeyAndNotByPositionAlone()
    {
        // Phase and group delay both hang a left-hand LinearAxis off the same plot
        // shape; a range restored by position alone would land on the wrong one.
        var captured = new PlotAxisViewport(
            PlotModelFactory.PhaseAxisKey,
            AxisPosition.Left,
            typeof(LinearAxis),
            -90,
            90);
        var model = new PlotModel();
        var groupDelay = new LinearAxis
        {
            Key = PlotModelFactory.GroupDelayAxisKey,
            Position = AxisPosition.Left,
            Minimum = -5,
            Maximum = 5,
        };
        model.Axes.Add(groupDelay);

        PlotAxisViewport.Apply(model, [captured]);
        Update(model);

        Assert.Equal(-5, groupDelay.ActualMinimum, 6);
        Assert.Equal(5, groupDelay.ActualMaximum, 6);
    }

    // The autocorrelation pair: neither axis states a range, so both follow the data.
    private static PlotModel AutoScaledModel()
    {
        var model = new PlotModel();
        model.Axes.Add(new LinearAxis
        {
            Key = PlotModelFactory.TimeAxisKey,
            Position = AxisPosition.Bottom,
        });
        model.Axes.Add(new LinearAxis
        {
            Key = PlotModelFactory.AutocorrelationAxisKey,
            Position = AxisPosition.Left,
        });
        AddCurve(model, -5, 5);
        return model;
    }

    private static void AddCurve(PlotModel model, double fromMs, double toMs)
    {
        var series = new LineSeries
        {
            XAxisKey = PlotModelFactory.TimeAxisKey,
            YAxisKey = PlotModelFactory.AutocorrelationAxisKey,
        };
        series.Points.Add(new DataPoint(fromMs, 0));
        series.Points.Add(new DataPoint(0, 1));
        series.Points.Add(new DataPoint(toMs, 0));
        model.Series.Add(series);
    }

    private static PlotModel FrequencyModel(double decibelMaximum = 0)
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model, maximum: decibelMaximum);
        var series = new LineSeries
        {
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = PlotModelFactory.DecibelAxisKey,
        };
        series.Points.Add(new DataPoint(20, -40));
        series.Points.Add(new DataPoint(20_000, -20));
        model.Series.Add(series);
        return model;
    }

    private static Axis Axis(PlotModel? model, string key) =>
        Assert.IsType<PlotModel>(model).Axes.First(axis => axis.Key == key);

    private static void Update(PlotModel model) => ((IPlotModel)model).Update(false);
}
