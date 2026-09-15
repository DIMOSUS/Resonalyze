using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

public sealed class PlotZoomRectangleReadoutTests
{
    private const int PlotWidth = 800;
    private const int PlotHeight = 600;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void Describe_ReportsTheFramedAreaInTheUnitsOfBothAxes()
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        Assert.Equal("1.90 kHz \u00D7 20.0 dB", box.Describe(Culture));
    }

    [Fact]
    public void Describe_KeepsNarrowFrequencySpansInHertz()
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Frame(model, 100, 340, -40, -39);

        Assert.Equal("240 Hz \u00D7 1.00 dB", box.Describe(Culture));
    }

    [Fact]
    public void Describe_MeasuresAnAxisThatCannotBeZoomed()
    {
        PlotModel model = RenderedModel();
        YAxis(model).IsZoomEnabled = false;
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        Assert.Equal("1.90 kHz \u00D7 20.0 dB", box.Describe(Culture));
    }

    [Fact]
    public void Frame_DrawsTheDraggedRectangleEvenWhereAnAxisRefusesZoom()
    {
        PlotModel model = RenderedModel();
        YAxis(model).IsZoomEnabled = false;

        PlotZoomBox box = PlotZoomBox.Frame(
            XAxis(model),
            YAxis(model),
            new ScreenPoint(300, 200),
            new ScreenPoint(200, 400));
        OxyRect screen = box.Screen(model.PlotArea);

        Assert.Equal(200, screen.Left, 6);
        Assert.Equal(100, screen.Width, 6);
        Assert.Equal(200, screen.Top, 6);
        Assert.Equal(200, screen.Height, 6);
    }

    [Fact]
    public void Frame_SpansThePlotAreaWhereThereIsNoAxisToMeasureAgainst()
    {
        PlotModel model = RenderedModel();
        YAxis(model).IsAxisVisible = false;

        PlotZoomBox box = PlotZoomBox.Frame(
            XAxis(model),
            YAxis(model),
            new ScreenPoint(300, 200),
            new ScreenPoint(200, 400));
        OxyRect screen = box.Screen(model.PlotArea);

        Assert.Equal(100, screen.Width, 6);
        Assert.Equal(model.PlotArea.Top, screen.Top, 6);
        Assert.Equal(model.PlotArea.Height, screen.Height, 6);
        Assert.Equal("1.90 kHz", Frame(model, 100, 2_000, -40, -20).Describe(Culture));
    }

    [Fact]
    public void Frame_KeepsFramingTheSameDataWhenTheViewMovesUnderIt()
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);
        OxyRect before = box.Screen(model.PlotArea);

        // The box is held in data units: the wheel can zoom while it waits for a click.
        XAxis(model).Zoom(50, 5_000);
        Render(model);
        OxyRect after = box.Screen(model.PlotArea);

        Assert.NotEqual(before.Left, after.Left, 3);
        Assert.Equal(100, XAxis(model).InverseTransform(after.Left), 6);
        Assert.Equal(2_000, XAxis(model).InverseTransform(after.Right), 6);
        Assert.Equal("1.90 kHz \u00D7 20.0 dB", box.Describe(Culture));
    }

    [Fact]
    public void Zoom_TakesTheAxesToWhatTheBoxFrames()
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        box.Zoom();
        Render(model);

        Assert.Equal(100, XAxis(model).ActualMinimum, 6);
        Assert.Equal(2_000, XAxis(model).ActualMaximum, 6);
        Assert.Equal(-40, YAxis(model).ActualMinimum, 6);
        Assert.Equal(-20, YAxis(model).ActualMaximum, 6);
    }

    [Fact]
    public void Zoom_MovesOnlyTheAxesThatAllowIt()
    {
        PlotModel model = RenderedModel();
        YAxis(model).IsZoomEnabled = false;
        double top = YAxis(model).ActualMaximum;
        double bottom = YAxis(model).ActualMinimum;
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        Assert.True(box.CanZoom);
        box.Zoom();
        Render(model);

        Assert.Equal(100, XAxis(model).ActualMinimum, 6);
        Assert.Equal(bottom, YAxis(model).ActualMinimum, 6);
        Assert.Equal(top, YAxis(model).ActualMaximum, 6);
    }

    [Fact]
    public void CanZoom_IsFalseWhereEveryScaleIsLocked()
    {
        PlotModel model = RenderedModel();
        XAxis(model).IsZoomEnabled = false;
        YAxis(model).IsZoomEnabled = false;
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        Assert.False(box.CanZoom);
        Assert.False(box.IsEmpty);
        box.Zoom();
        Render(model);

        Assert.Equal(20, XAxis(model).ActualMinimum, 6);
        Assert.Equal(20_000, XAxis(model).ActualMaximum, 6);
    }

    [Fact]
    public void Contains_AnswersTheClickThatZoomsAndTheOneThatDoesNot()
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);
        OxyRect screen = box.Screen(model.PlotArea);

        Assert.True(box.Contains(
            model.PlotArea,
            new ScreenPoint(screen.Left + (screen.Width / 2), screen.Top + (screen.Height / 2))));
        Assert.False(box.Contains(
            model.PlotArea,
            new ScreenPoint(screen.Right + 20, screen.Top + (screen.Height / 2))));
    }

    [Theory]
    [InlineData(60, 40, null)]
    [InlineData(6, 40, "That box is too narrow to zoom in to")]
    [InlineData(60, 4, "That box is too short to zoom in to")]
    [InlineData(6, 4, "That box is too small to zoom in to")]
    public void RefusalFor_NamesTheSideThatIsTooSmall(
        double width,
        double height,
        string? expected)
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Dragged(model, width, height);

        Assert.Equal(
            expected,
            PlotZoomRectangleReadout.RefusalFor(box, box.Screen(model.PlotArea)));
    }

    [Fact]
    public void RefusalFor_JudgesOnlyTheSidesAZoomWouldMove()
    {
        PlotModel model = RenderedModel();
        YAxis(model).IsZoomEnabled = false;
        PlotZoomBox box = Dragged(model, width: 60, height: 2);

        Assert.Null(PlotZoomRectangleReadout.RefusalFor(box, box.Screen(model.PlotArea)));
    }

    [Fact]
    public void RefusalFor_JudgesTheBoxAsItStandsWhenItIsClicked()
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Dragged(model, width: 6, height: 40);
        Assert.NotNull(PlotZoomRectangleReadout.RefusalFor(box, box.Screen(model.PlotArea)));

        XAxis(model).Zoom(
            box.HorizontalFrom - ((box.HorizontalTo - box.HorizontalFrom) * 2),
            box.HorizontalTo + ((box.HorizontalTo - box.HorizontalFrom) * 2));
        Render(model);

        Assert.Null(PlotZoomRectangleReadout.RefusalFor(box, box.Screen(model.PlotArea)));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, 2, false)]
    [InlineData(0, 4, true)]
    [InlineData(40, 0, true)]
    public void WasDrawn_TellsABoxFromAClick(double dx, double dy, bool expected)
    {
        var start = new ScreenPoint(120, 90);

        Assert.Equal(
            expected,
            PlotZoomRectangleReadout.WasDrawn(start, new ScreenPoint(120 + dx, 90 + dy)));
    }

    [Fact]
    public void HintFor_AppearsOnlyOnceTheBoxIsWaitingToBeClicked()
    {
        PlotModel model = RenderedModel();
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        Assert.Equal(string.Empty, PlotZoomRectangleReadout.HintFor(box, pending: false));
        Assert.Equal("click inside to zoom", PlotZoomRectangleReadout.HintFor(box, pending: true));
    }

    [Fact]
    public void HintFor_IsLeftOutWhereNothingCanZoom()
    {
        PlotModel model = RenderedModel();
        XAxis(model).IsZoomEnabled = false;
        YAxis(model).IsZoomEnabled = false;
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        Assert.Equal("1.90 kHz \u00D7 20.0 dB", box.Describe(Culture));
        Assert.Equal(string.Empty, PlotZoomRectangleReadout.HintFor(box, pending: true));
    }

    [Fact]
    public void Describe_SaysNothingWithoutAnAxisToMeasureAgainst()
    {
        PlotModel model = RenderedModel();
        XAxis(model).IsAxisVisible = false;
        YAxis(model).IsAxisVisible = false;
        PlotZoomBox box = Frame(model, 100, 2_000, -40, -20);

        Assert.True(box.IsEmpty);
        Assert.Equal(string.Empty, box.Describe(Culture));
        Assert.Equal(string.Empty, PlotZoomRectangleReadout.HintFor(box, pending: true));
    }

    [Theory]
    [InlineData("dB", "12.4 dB")]
    [InlineData("ms", "12.4 ms")]
    [InlineData("samples", "12.4 samples")]
    [InlineData("Sum loss (dB)", "12.4 dB")]
    [InlineData("delay added to the upper channel (ms)", "12.4 ms")]
    [InlineData("ms from peak", "12.4 ms")]
    [InlineData("dB re Main peak", "12.4 dB")]
    // Short names like "step" and "r" are dimensionless quantities, not units.
    [InlineData("Coherence \u03B3\u00B2", "12.4")]
    [InlineData("step", "12.4")]
    [InlineData("r", "12.4")]
    [InlineData("envelope r", "12.4")]
    [InlineData("junction score (dB)", "12.4 dB")]
    public void FormatSpan_TakesItsUnitFromTheAxisTitle(string title, string expected)
    {
        var axis = new LinearAxis { Position = AxisPosition.Left, Title = title };

        Assert.Equal(expected, PlotZoomRectangleReadout.FormatSpan(axis, 12.4, Culture));
    }

    [Theory]
    [InlineData(999, "999 Hz")]
    [InlineData(1_000, "1.00 kHz")]
    [InlineData(15_300, "15.3 kHz")]
    public void FormatSpan_PassesFrequencySpansToKilohertzWhereRewDoes(
        double span,
        string expected)
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);

        Assert.Equal(
            expected,
            PlotZoomRectangleReadout.FormatSpan(model.Axes[0], span, Culture));
    }

    [Fact]
    public void FormatSpan_ReadsAnUntitledPhaseAxisInDegrees()
    {
        var phase = new LinearAxis
        {
            Key = PlotModelFactory.PhaseAxisKey,
            Position = AxisPosition.Left,
        };

        Assert.Equal("45.0\u00B0", PlotZoomRectangleReadout.FormatSpan(phase, 45, Culture));
    }

    [Fact]
    public void PlaceLabel_SitsBesideTheDraggedCornerAndOutsideTheBox()
    {
        var plotArea = new OxyRect(50, 50, 700, 500);
        var box = new OxyRect(200, 200, 100, 100);
        var text = new OxySize(80, 16);

        OxyRect downRight = PlotZoomRectangleReadout.PlaceLabel(
            plotArea,
            box,
            new ScreenPoint(300, 300),
            text);
        Assert.True(downRight.Left > box.Right);
        Assert.True(downRight.Top > box.Bottom);

        OxyRect upLeft = PlotZoomRectangleReadout.PlaceLabel(
            plotArea,
            box,
            new ScreenPoint(200, 200),
            text);
        Assert.True(upLeft.Right < box.Left);
        Assert.True(upLeft.Bottom < box.Top);
    }

    [Fact]
    public void PlaceLabel_StaysInsideThePlotAreaAtEveryCorner()
    {
        var plotArea = new OxyRect(50, 50, 700, 500);
        var box = new OxyRect(60, 60, 680, 480);
        var text = new OxySize(120, 16);

        foreach (ScreenPoint corner in new[]
        {
            new ScreenPoint(plotArea.Left, plotArea.Top),
            new ScreenPoint(plotArea.Right, plotArea.Top),
            new ScreenPoint(plotArea.Left, plotArea.Bottom),
            new ScreenPoint(plotArea.Right, plotArea.Bottom),
        })
        {
            OxyRect label = PlotZoomRectangleReadout.PlaceLabel(plotArea, box, corner, text);

            Assert.True(label.Left >= plotArea.Left);
            Assert.True(label.Top >= plotArea.Top);
            Assert.True(label.Right <= plotArea.Right);
            Assert.True(label.Bottom <= plotArea.Bottom);
        }
    }

    private static PlotZoomBox Frame(
        PlotModel model,
        double fromHz,
        double toHz,
        double fromDb,
        double toDb)
    {
        Axis x = XAxis(model);
        Axis y = YAxis(model);
        return PlotZoomBox.Frame(
            x,
            y,
            new ScreenPoint(x.Transform(fromHz), y.Transform(fromDb)),
            new ScreenPoint(x.Transform(toHz), y.Transform(toDb)));
    }

    private static PlotZoomBox Dragged(PlotModel model, double width, double height)
    {
        var start = new ScreenPoint(model.PlotArea.Left + 40, model.PlotArea.Top + 40);
        return PlotZoomBox.Frame(
            XAxis(model),
            YAxis(model),
            start,
            new ScreenPoint(start.X + width, start.Y + height));
    }

    private static Axis XAxis(PlotModel model) =>
        model.Axes.First(axis => axis.Key == PlotModelFactory.FrequencyAxisKey);

    private static Axis YAxis(PlotModel model) =>
        model.Axes.First(axis => axis.Key == PlotModelFactory.DecibelAxisKey);

    private static PlotModel RenderedModel()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        var series = new LineSeries { XAxisKey = PlotModelFactory.FrequencyAxisKey };
        series.Points.Add(new DataPoint(20, -40));
        series.Points.Add(new DataPoint(20_000, -60));
        model.Series.Add(series);
        Render(model);
        return model;
    }

    // PlotArea and axis transforms are computed while rendering; a throwaway PNG export lays the model out.
    private static void Render(PlotModel model)
    {
        var exporter = new PngExporter { Width = PlotWidth, Height = PlotHeight };
        using var stream = new MemoryStream();
        exporter.Export(model, stream);
    }
}
