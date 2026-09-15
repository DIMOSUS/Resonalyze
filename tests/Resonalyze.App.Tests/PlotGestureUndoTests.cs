using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

// The same axis key means different quantities across models (dBr vs dB SPL), so replay targets matter.
public sealed class PlotGestureUndoTests
{
    [Fact]
    public void Undo_PutsBackTheRangeAFitReplaced()
    {
        using var view = new PlotView();
        PlotInteraction.Enable(view);
        PlotModel model = FrequencyModel();
        view.Model = model;
        // A headless view never paints; axes learn their range only on a manual update.
        Update(model);
        Axis decibel = Axis(model, PlotModelFactory.DecibelAxisKey);
        double minimum = decibel.ActualMinimum;
        double maximum = decibel.ActualMaximum;

        Send(view, OxyKey.F, OxyModifierKeys.Control | OxyModifierKeys.Alt);
        Update(model);
        Assert.NotEqual(maximum, decibel.ActualMaximum, 6);

        Send(view, OxyKey.Z, OxyModifierKeys.Control);
        Update(model);

        Assert.Equal(minimum, decibel.ActualMinimum, 6);
        Assert.Equal(maximum, decibel.ActualMaximum, 6);
    }

    [Fact]
    public void Undo_AfterTheModelWasRebuilt_LeavesTheNewOneAlone()
    {
        using var view = new PlotView();
        PlotInteraction.Enable(view);
        view.Model = FrequencyModel();

        Axis(view.Model, PlotModelFactory.FrequencyAxisKey).Zoom(100, 500);
        Send(view, OxyKey.F, OxyModifierKeys.Control | OxyModifierKeys.Alt);

        PlotModel rebuilt = FrequencyModel(soundPressureLevel: true);
        view.Model = rebuilt;
        Update(rebuilt);
        double frequencyMinimum = Axis(rebuilt, PlotModelFactory.FrequencyAxisKey).ActualMinimum;
        double decibelMaximum = Axis(rebuilt, PlotModelFactory.DecibelAxisKey).ActualMaximum;

        Send(view, OxyKey.Z, OxyModifierKeys.Control);
        Update(rebuilt);

        Assert.Equal(frequencyMinimum, Axis(rebuilt, PlotModelFactory.FrequencyAxisKey).ActualMinimum, 6);
        Assert.Equal(decibelMaximum, Axis(rebuilt, PlotModelFactory.DecibelAxisKey).ActualMaximum, 6);
    }

    [Fact]
    public void Undo_AfterAnAxisIsRearmedInPlace_LeavesItAlone()
    {
        using var view = new PlotView();
        PlotInteraction.Enable(view);
        PlotModel model = FrequencyModel();
        view.Model = model;
        Update(model);
        Axis value = Axis(model, PlotModelFactory.DecibelAxisKey);

        Send(view, OxyKey.F, OxyModifierKeys.Control | OxyModifierKeys.Alt);

        value.Title = "deg";
        value.AbsoluteMinimum = -180;
        value.AbsoluteMaximum = 180;
        value.Minimum = -180;
        value.Maximum = 180;
        value.Reset();
        Update(model);

        Send(view, OxyKey.Z, OxyModifierKeys.Control);
        Update(model);

        Assert.Equal(-180, value.ActualMinimum, 6);
        Assert.Equal(180, value.ActualMaximum, 6);
    }

    private static void Send(PlotView view, OxyKey key, OxyModifierKeys modifiers) =>
        view.ActualController.HandleKeyDown(
            view,
            new OxyKeyEventArgs { Key = key, ModifierKeys = modifiers });

    private static PlotModel FrequencyModel(bool soundPressureLevel = false)
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(
            model,
            title: soundPressureLevel ? "dB SPL" : "dB",
            minimum: soundPressureLevel
                ? PlotModelStyle.SplDecibelMinimum
                : PlotModelStyle.RelativeDecibelMinimum,
            maximum: soundPressureLevel
                ? PlotModelStyle.SplDecibelMaximum
                : PlotModelStyle.RelativeDecibelMaximum,
            absoluteMinimum: soundPressureLevel
                ? PlotModelStyle.SplDecibelAbsoluteMinimum
                : PlotModelStyle.RelativeDecibelAbsoluteMinimum,
            absoluteMaximum: soundPressureLevel
                ? PlotModelStyle.SplDecibelAbsoluteMaximum
                : PlotModelStyle.RelativeDecibelAbsoluteMaximum);
        var series = new LineSeries
        {
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = PlotModelFactory.DecibelAxisKey,
        };
        series.Points.Add(new DataPoint(100, -30));
        series.Points.Add(new DataPoint(5_000, -20));
        model.Series.Add(series);
        return model;
    }

    private static Axis Axis(PlotModel? model, string key) =>
        Assert.IsType<PlotModel>(model).Axes.First(axis => axis.Key == key);

    private static void Update(PlotModel model) => ((IPlotModel)model).Update(true);
}
