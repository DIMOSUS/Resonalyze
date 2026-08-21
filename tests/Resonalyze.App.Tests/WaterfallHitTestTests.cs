using OxyPlot;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

// Pressing a mouse button over a plot makes OxyPlot hit-test its series before any
// binding gets a look in (ControllerBase.HandleMouseDown asks the model first), so
// a series that throws while answering "what is under the cursor" takes the whole
// app down on a click.
public sealed class WaterfallHitTestTests
{
    [Fact]
    public void GetNearestPoint_OnTheWaterfall_AnswersWithoutThrowing()
    {
        var series = new WaterfallSeries();
        PlotModel model = PlotModelStyle.CreateWaterfallModel(
            "Fourier Waterfall",
            new WaterfallGenerateOptions());
        model.Series.Add(series);
        ((IPlotModel)model).Update(false);

        // The waterfall draws a projected surface: the cursor is not over a data
        // point of a curve, so there is nothing to report.
        Assert.Null(series.GetNearestPoint(new ScreenPoint(200, 150), interpolate: false));
        Assert.Null(series.GetNearestPoint(new ScreenPoint(200, 150), interpolate: true));
    }

    [Theory]
    [InlineData(OxyMouseButton.Left)]
    [InlineData(OxyMouseButton.Middle)]
    [InlineData(OxyMouseButton.Right)]
    public void MouseDownOverTheWaterfall_DoesNotThrow(OxyMouseButton button)
    {
        using var view = new PlotView();
        PlotInteraction.Enable(view);
        PlotModel model = PlotModelStyle.CreateWaterfallModel(
            "Fourier Waterfall",
            new WaterfallGenerateOptions());
        model.Series.Add(new WaterfallSeries());
        view.Model = model;
        ((IPlotModel)model).Update(false);

        view.ActualController.HandleMouseDown(
            view,
            new OxyMouseDownEventArgs
            {
                ChangedButton = button,
                ClickCount = 1,
                Position = new ScreenPoint(200, 150),
            });
        view.ActualController.HandleMouseMove(
            view,
            new OxyMouseEventArgs { Position = new ScreenPoint(260, 120) });
        view.ActualController.HandleMouseUp(
            view,
            new OxyMouseEventArgs { Position = new ScreenPoint(260, 120) });
    }
}
