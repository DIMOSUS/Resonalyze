using System.Drawing;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

public sealed class PlotViewPaintStateTests
{
    [Fact]
    public void ApplyPendingUpdate_ReadsDataOnlyWhenAnInvalidationAskedForIt()
    {
        var series = new CountingLineSeries();
        var model = new PlotModel();
        model.Series.Add(series);
        using var view = new PlotView { Model = model };

        PlotViewPaintState.ApplyPendingUpdate(view);
        PlotViewPaintState.ApplyPendingUpdate(view);
        Assert.Equal(1, series.DataUpdates);

        view.InvalidatePlot(false);
        PlotViewPaintState.ApplyPendingUpdate(view);
        Assert.Equal(1, series.DataUpdates);

        view.InvalidatePlot(true);
        PlotViewPaintState.ApplyPendingUpdate(view);
        Assert.Equal(2, series.DataUpdates);
    }

    [Fact]
    public void ZoomRectangle_FollowsShowAndHide()
    {
        using var view = new PlotView();

        view.ShowZoomRectangle(new OxyRect(10, 20, 30, 40));
        Assert.Equal(new Rectangle(10, 20, 30, 40), PlotViewPaintState.ZoomRectangle(view));

        view.HideZoomRectangle();
        Assert.Equal(Rectangle.Empty, PlotViewPaintState.ZoomRectangle(view));
    }

    private sealed class CountingLineSeries : LineSeries
    {
        public int DataUpdates { get; private set; }

        protected override void UpdateData()
        {
            DataUpdates++;
            base.UpdateData();
        }
    }
}
