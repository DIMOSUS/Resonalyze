using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

// The zoom arithmetic and hit tests behind the REW-shaped plot gestures: which axis
// a wheel notch over an axis strip moves, how far a step zooms, and where the
// on-graph zoom buttons sit.
public sealed class PlotAxisZoomTests
{
    private const int PlotWidth = 800;
    private const int PlotHeight = 600;

    [Fact]
    public void ScaleFromWheelDelta_ZoomsInOnAPositiveNotchAndOutOnANegativeOne()
    {
        double zoomIn = PlotAxisZoom.ScaleFromWheelDelta(120, factor: 1);
        double zoomOut = PlotAxisZoom.ScaleFromWheelDelta(-120, factor: 1);

        Assert.True(zoomIn > 1);
        Assert.True(zoomOut < 1);
        // A notch each way must land back where it started, or a wheel wobble would
        // walk the axis.
        Assert.Equal(1.0, zoomIn * zoomOut, 12);
    }

    [Fact]
    public void ScaleFromWheelDelta_FineFactorMovesLessThanAFullNotch()
    {
        double coarse = PlotAxisZoom.ScaleFromWheelDelta(120, factor: 1);
        double fine = PlotAxisZoom.ScaleFromWheelDelta(120, PlotAxisZoom.FineWheelFactor);

        Assert.True(fine > 1);
        Assert.True(fine < coarse);
    }

    [Fact]
    public void TryGetAxisEnd_InsideThePlotArea_IsNotAnAxisEnd()
    {
        PlotModel model = RenderedModel();
        OxyRect area = model.PlotArea;

        Assert.False(PlotAxisZoom.TryGetAxisEnd(
            model,
            new ScreenPoint((area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2),
            out _,
            out _));
    }

    [Fact]
    public void TryGetAxisEnd_OverTheMiddleOfTheFrequencyStrip_IsNotAnAxisEnd()
    {
        PlotModel model = RenderedModel();
        OxyRect area = model.PlotArea;

        // The middle of an axis is the "zoom this axis" zone, not the "move one
        // limit" zone.
        Assert.False(PlotAxisZoom.TryGetAxisEnd(
            model,
            new ScreenPoint((area.Left + area.Right) / 2, area.Bottom + 8),
            out _,
            out _));
    }

    [Theory]
    [InlineData(0.05, false)]
    [InlineData(0.95, true)]
    public void TryGetAxisEnd_OverAnEndOfTheFrequencyStrip_NamesThatEnd(
        double fraction,
        bool expectedMaximumEnd)
    {
        PlotModel model = RenderedModel();
        OxyRect area = model.PlotArea;

        bool hit = PlotAxisZoom.TryGetAxisEnd(
            model,
            new ScreenPoint(area.Left + (area.Width * fraction), area.Bottom + 8),
            out Axis? axis,
            out bool maximumEnd);

        Assert.True(hit);
        Assert.Equal(PlotModelFactory.FrequencyAxisKey, axis?.Key);
        Assert.Equal(expectedMaximumEnd, maximumEnd);
    }

    [Fact]
    public void TryGetAxisEnd_OverTheCornerWhereBothStripsMeet_IsAmbiguousAndRefused()
    {
        PlotModel model = RenderedModel();
        OxyRect area = model.PlotArea;

        Assert.False(PlotAxisZoom.TryGetAxisEnd(
            model,
            new ScreenPoint(area.Left - 8, area.Bottom + 8),
            out _,
            out _));
    }

    [Fact]
    public void ZoomEnd_MovesOneLimitAndPinsTheOther()
    {
        PlotModel model = RenderedModel();
        Axis frequency = model.Axes.First(axis => axis.Key == PlotModelFactory.FrequencyAxisKey);
        double minimumBefore = frequency.ActualMinimum;
        double maximumBefore = frequency.ActualMaximum;

        PlotAxisZoom.ZoomEnd(frequency, maximumEnd: true, scale: 2);
        Update(model);

        Assert.Equal(minimumBefore, frequency.ActualMinimum, 6);
        Assert.True(frequency.ActualMaximum < maximumBefore);
    }

    [Fact]
    public void ZoomAxisAt_MovesOnlyTheRequestedAxis()
    {
        PlotModel model = RenderedModel();
        Axis frequency = model.Axes.First(axis => axis.Key == PlotModelFactory.FrequencyAxisKey);
        Axis decibel = model.Axes.First(axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        double decibelMinimum = decibel.ActualMinimum;
        double decibelMaximum = decibel.ActualMaximum;
        OxyRect area = model.PlotArea;

        bool zoomed = PlotAxisZoom.ZoomAxisAt(
            model,
            new ScreenPoint((area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2),
            horizontal: true,
            PlotAxisZoom.StepZoomInScale);
        Update(model);

        Assert.True(zoomed);
        Assert.True(frequency.ActualMaximum - frequency.ActualMinimum < 20_000 - 20);
        Assert.Equal(decibelMinimum, decibel.ActualMinimum, 6);
        Assert.Equal(decibelMaximum, decibel.ActualMaximum, 6);
    }

    [Fact]
    public void ZoomAxisAt_RefusesAnAxisThatDoesNotZoom()
    {
        var model = new PlotModel();
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 0,
            Maximum = 10,
            IsZoomEnabled = false,
        });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0, Maximum = 1 });
        Render(model);

        OxyRect area = model.PlotArea;
        Assert.False(PlotAxisZoom.ZoomAxisAt(
            model,
            new ScreenPoint((area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2),
            horizontal: true,
            PlotAxisZoom.StepZoomInScale));
    }

    [Fact]
    public void ZoomButtons_SitAgainstTheirOwnAxisAndAnswerAHit()
    {
        PlotModel model = RenderedModel();
        OxyRect area = model.PlotArea;

        IReadOnlyList<PlotZoomButton> buttons = PlotZoomButtons.Layout(model);

        Assert.Equal(4, buttons.Count);
        Assert.Equal(2, buttons.Count(button => button.Horizontal));
        Assert.All(
            buttons.Where(button => button.Horizontal),
            button => Assert.True(button.Center.Y > (area.Top + area.Bottom) / 2));
        Assert.All(
            buttons.Where(button => !button.Horizontal),
            button => Assert.True(button.Center.X < (area.Left + area.Right) / 2));

        PlotZoomButton zoomIn = buttons.First(button => button.Horizontal && button.ZoomIn);
        Assert.True(PlotZoomButtons.TryHit(model, zoomIn.Center, out PlotZoomButton hit));
        Assert.Equal(zoomIn, hit);
        Assert.False(PlotZoomButtons.TryHit(
            model,
            new ScreenPoint(zoomIn.Center.X, zoomIn.Center.Y - (PlotZoomButtons.Radius * 3)),
            out _));
    }

    [Fact]
    public void ZoomButtons_AreLeftOutOfAPlotTooSmallToSpareTheRoom()
    {
        Assert.Empty(PlotZoomButtons.Layout(new OxyRect(0, 0, 90, 70)));
    }

    [Fact]
    public void ZoomButtons_AreLeftOutForAnAxisThatCannotBeMoved()
    {
        // The waterfall shape: every axis pinned by the mode, so a pair of buttons
        // there would be a control that does nothing.
        PlotModel model = RenderedModel();
        foreach (Axis axis in model.Axes)
        {
            axis.IsZoomEnabled = false;
        }

        Assert.Empty(PlotZoomButtons.Layout(model));

        model.Axes.First(axis => axis.Key == PlotModelFactory.FrequencyAxisKey).IsZoomEnabled = true;
        Assert.All(PlotZoomButtons.Layout(model), button => Assert.True(button.Horizontal));
    }

    private static PlotModel RenderedModel()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        var series = new LineSeries { XAxisKey = PlotModelFactory.FrequencyAxisKey };
        series.Points.Add(new DataPoint(20, -40));
        series.Points.Add(new DataPoint(1000, -20));
        series.Points.Add(new DataPoint(20_000, -60));
        model.Series.Add(series);
        Render(model);
        return model;
    }

    // PlotArea is computed while rendering, and the hit tests read it; exporting to a
    // throwaway PNG is the cheapest way to get a laid-out model in a test.
    private static void Render(PlotModel model)
    {
        var exporter = new PngExporter { Width = PlotWidth, Height = PlotHeight };
        using var stream = new MemoryStream();
        exporter.Export(model, stream);
    }

    private static void Update(PlotModel model) => ((IPlotModel)model).Update(false);
}
