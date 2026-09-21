using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

public sealed class PlotGestureRedrawTests
{
    [Fact]
    public void APressBeforeTheNextPaint_FindsTheNewCurveOnItsAxes()
    {
        // A redraw adds series and leaves binding them to the next paint; the press arrives first.
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        var view = new PlotView();
        PlotInteraction.Enable(view);
        view.Model = model;
        using (var stream = new MemoryStream())
        {
            new PngExporter { Width = 800, Height = 400 }.Export(model, stream);
        }

        var fill = new AreaSeries();
        foreach (double hz in new[] { 100.0, 1_000, 10_000 })
        {
            fill.Points.Add(new DataPoint(hz, -3));
            fill.Points2.Add(new DataPoint(hz, 0));
        }

        model.Series.Add(fill);

        view.ActualController.HandleMouseDown(
            view,
            new OxyMouseDownEventArgs
            {
                ChangedButton = OxyMouseButton.Left,
                ClickCount = 1,
                Position = new ScreenPoint(400, 200)
            });

        Assert.NotNull(fill.XAxis);
    }
}
