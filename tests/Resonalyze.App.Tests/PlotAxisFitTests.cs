using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Resonalyze.App.Tests;

// "Fit to data" and "Fit Y to data" — REW's Ctrl+Alt+F and Ctrl+Alt+Y.
public sealed class PlotAxisFitTests
{
    [Fact]
    public void FitToData_BringsBothAxesOntoTheCurve()
    {
        PlotModel model = ModelWithCurve();

        Assert.True(PlotAxisFit.FitToData(model, verticalOnly: false));
        Update(model);

        Axis frequency = Axis(model, PlotModelFactory.FrequencyAxisKey);
        Axis decibel = Axis(model, PlotModelFactory.DecibelAxisKey);
        // The frequency axis lands exactly on the data: padding an axis whose whole
        // range IS the audio band would open every fit on empty decades.
        Assert.Equal(100, frequency.ActualMinimum, 6);
        Assert.Equal(5_000, frequency.ActualMaximum, 6);
        // The value axis keeps a margin around the curve.
        Assert.True(decibel.ActualMinimum < -30);
        Assert.True(decibel.ActualMinimum > -35);
        Assert.True(decibel.ActualMaximum > -10);
        Assert.True(decibel.ActualMaximum < -5);
    }

    [Fact]
    public void FitToData_VerticalOnly_LeavesTheFrequencySpanWhereTheUserPutIt()
    {
        PlotModel model = ModelWithCurve();
        Axis frequency = Axis(model, PlotModelFactory.FrequencyAxisKey);
        frequency.Zoom(200, 400);
        Update(model);

        Assert.True(PlotAxisFit.FitToData(model, verticalOnly: true));
        Update(model);

        Assert.Equal(200, frequency.ActualMinimum, 6);
        Assert.Equal(400, frequency.ActualMaximum, 6);
        Assert.True(Axis(model, PlotModelFactory.DecibelAxisKey).ActualMaximum > -10);
    }

    [Fact]
    public void FitToData_LeavesAPinnedAxisAlone()
    {
        PlotModel model = ModelWithCurve();
        Axis decibel = Axis(model, PlotModelFactory.DecibelAxisKey);
        decibel.IsZoomEnabled = false;
        double minimum = decibel.ActualMinimum;
        double maximum = decibel.ActualMaximum;

        PlotAxisFit.FitToData(model, verticalOnly: false);
        Update(model);

        Assert.Equal(minimum, decibel.ActualMinimum, 6);
        Assert.Equal(maximum, decibel.ActualMaximum, 6);
    }

    [Fact]
    public void FitToData_WithoutData_ChangesNothing()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);

        Assert.False(PlotAxisFit.FitToData(model, verticalOnly: false));
    }

    private static PlotModel ModelWithCurve()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        var series = new LineSeries
        {
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = PlotModelFactory.DecibelAxisKey,
        };
        series.Points.Add(new DataPoint(100, -30));
        series.Points.Add(new DataPoint(1_000, -10));
        series.Points.Add(new DataPoint(5_000, -20));
        model.Series.Add(series);
        Update(model);
        return model;
    }

    private static Axis Axis(PlotModel model, string key) =>
        model.Axes.First(axis => axis.Key == key);

    private static void Update(PlotModel model) => ((IPlotModel)model).Update(true);
}
