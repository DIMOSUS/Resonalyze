using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

public sealed class PlotCurveTogglesTests
{
    [Fact]
    public void APressOnABox_TogglesThatBoxAndNamesIt()
    {
        (PlotView view, PlotCurveTogglesAnnotation toggles) = Build();
        var raised = new List<int>();
        toggles.Toggled += raised.Add;
        ScreenPoint at = Centre(toggles, 1);

        Click(view, at);

        Assert.Equal([1], raised);
        Assert.Equal([true, false, true], Enumerable.Range(0, 3).Select(toggles.IsChecked));

        Click(view, at);

        Assert.Equal([1, 1], raised);
        Assert.True(toggles.IsChecked(1));
    }

    [Fact]
    public void APressBesideTheBoxes_AndTheWheelOverOne_StayThePlots()
    {
        (PlotView view, PlotCurveTogglesAnnotation toggles) = Build();
        var raised = new List<int>();
        toggles.Toggled += raised.Add;
        OxyRect row = toggles.RowBounds(0)!.Value;
        Axis frequency = view.Model!.Axes.Single(axis => axis.Key == PlotModelFactory.FrequencyAxisKey);
        double minimum = frequency.ActualMinimum;

        Click(view, new ScreenPoint(row.Right + 20, row.Top + (row.Height / 2)));
        view.ActualController.HandleMouseWheel(
            view,
            new OxyMouseWheelEventArgs { Delta = 120, Position = Centre(toggles, 0), ModifierKeys = OxyModifierKeys.Shift });
        ((IPlotModel)view.Model).Update(false);

        Assert.Empty(raised);
        Assert.NotEqual(minimum, frequency.ActualMinimum, 3);
        Assert.False(toggles.Wheel(0, 120));
    }

    [Fact]
    public void TheBoxesStackDownward_ClearOfTheLeftZoomPair()
    {
        (PlotView view, PlotCurveTogglesAnnotation toggles) = Build();
        OxyRect area = view.Model!.PlotArea;
        List<OxyRect> rows = Enumerable.Range(0, 3).Select(index => toggles.RowBounds(index)!.Value).ToList();

        Assert.All(rows, row =>
        {
            Assert.True(row.Left >= area.Left + PlotZoomButtons.LeftPairClearance);
            Assert.True(area.Contains(row.Left, row.Top) && area.Contains(row.Right, row.Bottom));
        });
        Assert.Equal(rows[0].Bottom, rows[1].Top, 9);
        Assert.Equal(rows[1].Bottom, rows[2].Top, 9);
        Assert.Equal([0, 1, 2], rows.Select(row => toggles.HitTest(row.Center)!.Value));
    }

    [Fact]
    public void BoxesNotDrawn_TakeNoPress()
    {
        (PlotView view, PlotCurveTogglesAnnotation toggles) = Build();
        ScreenPoint at = Centre(toggles, 0);
        var raised = new List<int>();
        toggles.Toggled += raised.Add;

        toggles.Shown = false;
        Click(view, at);

        Assert.Null(toggles.RowBounds(0));
        Assert.Empty(raised);

        // A plot too small to spare the corner draws none either.
        (_, PlotCurveTogglesAnnotation cramped) = Build(height: 150);
        Assert.Null(cramped.RowBounds(0));
        Assert.Null(cramped.HitTest(at));
    }

    [Fact]
    public void SetChecked_MovesTheBoxWithoutRaising()
    {
        (_, PlotCurveTogglesAnnotation toggles) = Build();
        var raised = new List<int>();
        toggles.Toggled += raised.Add;

        toggles.SetChecked(2, false);

        Assert.False(toggles.IsChecked(2));
        Assert.Empty(raised);
    }

    [Fact]
    public void Hover_ReportsOnlyAChange()
    {
        (_, PlotCurveTogglesAnnotation toggles) = Build();

        Assert.True(toggles.Hover(1));
        Assert.False(toggles.Hover(1));
        Assert.True(toggles.Hover(null));
    }

    private static (PlotView View, PlotCurveTogglesAnnotation Toggles) Build(int height = 400)
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        var toggles = new PlotCurveTogglesAnnotation(["PHAT", "phase", "score"]);
        model.Annotations.Add(toggles);
        var view = new PlotView();
        PlotInteraction.Enable(view);
        view.Model = model;
        // PlotArea is computed while rendering; a throwaway PNG export lays the model out.
        using var stream = new MemoryStream();
        new PngExporter { Width = 800, Height = height }.Export(model, stream);
        return (view, toggles);
    }

    private static ScreenPoint Centre(PlotCurveTogglesAnnotation toggles, int index) =>
        toggles.RowBounds(index)!.Value.Center;

    private static void Click(PlotView view, ScreenPoint at)
    {
        view.ActualController.HandleMouseDown(
            view,
            new OxyMouseDownEventArgs { ChangedButton = OxyMouseButton.Left, ClickCount = 1, Position = at });
        view.ActualController.HandleMouseUp(view, new OxyMouseEventArgs { Position = at });
    }
}
