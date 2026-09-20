using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

public sealed class PlotDragHandlesTests
{
    private const int PlotWidth = 800;
    private const int PlotHeight = 400;

    [Fact]
    public void APressOnAHandle_GrabsIt_AndJitterIsNotADrag()
    {
        (PlotView view, FakeHandles handles) = Build();
        ScreenPoint at = handles.At;

        Down(view, at);
        Move(view, new ScreenPoint(at.X + 1, at.Y + 1));
        Move(view, new ScreenPoint(at.X + 20, at.Y - 10));
        Up(view, new ScreenPoint(at.X + 20, at.Y - 10));

        Assert.Equal(["press", "drag 420,190", "release"], handles.Calls);
    }

    [Fact]
    public void ThePlainWheelOverAHandle_GoesToIt_AndLeavesTheAxesAlone()
    {
        (PlotView view, FakeHandles handles) = Build();
        Axis frequency = Frequency(view);
        double minimum = frequency.ActualMinimum;

        Wheel(view, handles.At, OxyModifierKeys.None);
        Update(view.Model);

        Assert.Equal(["wheel 120"], handles.Calls);
        Assert.Equal(minimum, frequency.ActualMinimum, 9);
    }

    [Fact]
    public void AModifiedWheel_OrOneBesideTheHandle_Zooms()
    {
        (PlotView view, FakeHandles handles) = Build();
        Axis frequency = Frequency(view);
        double minimum = frequency.ActualMinimum;

        Wheel(view, handles.At, OxyModifierKeys.Shift);
        Update(view.Model);
        Assert.NotEqual(minimum, frequency.ActualMinimum, 3);

        minimum = frequency.ActualMinimum;
        Wheel(view, new ScreenPoint(handles.At.X - 100, handles.At.Y), OxyModifierKeys.None);
        Update(view.Model);
        Assert.NotEqual(minimum, frequency.ActualMinimum, 3);

        Assert.Empty(handles.Calls);
    }

    private static (PlotView View, FakeHandles Handles) Build()
    {
        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddDecibelAxis(model);
        var handles = new FakeHandles();
        model.Annotations.Add(handles);
        var view = new PlotView();
        PlotInteraction.Enable(view);
        view.Model = model;
        // PlotArea is computed while rendering; a throwaway PNG export lays the model out.
        using var stream = new MemoryStream();
        new PngExporter { Width = PlotWidth, Height = PlotHeight }.Export(model, stream);
        handles.At = new ScreenPoint(400, 200);
        Assert.True(model.PlotArea.Contains(400, 200));
        return (view, handles);
    }

    private static Axis Frequency(PlotView view) =>
        view.Model!.Axes.Single(axis => axis.Key == PlotModelFactory.FrequencyAxisKey);

    private static void Down(PlotView view, ScreenPoint at) =>
        view.ActualController.HandleMouseDown(
            view,
            new OxyMouseDownEventArgs { ChangedButton = OxyMouseButton.Left, ClickCount = 1, Position = at });

    private static void Move(PlotView view, ScreenPoint at) =>
        view.ActualController.HandleMouseMove(view, new OxyMouseEventArgs { Position = at });

    private static void Up(PlotView view, ScreenPoint at) =>
        view.ActualController.HandleMouseUp(view, new OxyMouseEventArgs { Position = at });

    private static void Wheel(PlotView view, ScreenPoint at, OxyModifierKeys modifiers) =>
        view.ActualController.HandleMouseWheel(
            view,
            new OxyMouseWheelEventArgs { Delta = 120, Position = at, ModifierKeys = modifiers });

    private static void Update(PlotModel model) => ((IPlotModel)model).Update(false);

    private sealed class FakeHandles : Annotation, IPlotDragHandles
    {
        public ScreenPoint At { get; set; }

        public List<string> Calls { get; } = [];

        public int? HitTest(ScreenPoint point) =>
            Math.Abs(point.X - At.X) <= 5 && Math.Abs(point.Y - At.Y) <= 5 ? 0 : null;

        public bool Hover(int? handle) => false;

        public void Press(int handle, ScreenPoint point) => Calls.Add("press");

        public void Drag(ScreenPoint point) => Calls.Add($"drag {point.X:0},{point.Y:0}");

        public void Release() => Calls.Add("release");

        public bool Wheel(int handle, int delta)
        {
            Calls.Add($"wheel {delta}");
            return true;
        }
    }
}
