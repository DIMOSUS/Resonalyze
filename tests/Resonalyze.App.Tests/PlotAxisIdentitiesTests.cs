using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze.App.Tests;

// The VDSP acoustic view re-arms one axis between dB, degrees and impulse scale inside one model, so the model reference alone does not tell.
public sealed class PlotAxisIdentitiesTests
{
    [Fact]
    public void AnUntouchedModelStillMatchesWhatWasTakenFromIt()
    {
        PlotModel model = AcousticModel(out _, out _);
        IReadOnlyList<PlotAxisIdentity> taken = PlotAxisIdentities.Describe(model);

        Assert.True(PlotAxisIdentities.Match(model, model, taken));

        model.Axes[0].Zoom(100, 2_000);
        Assert.True(PlotAxisIdentities.Match(model, model, taken));
    }

    [Fact]
    public void ADifferentModelNeverMatches()
    {
        PlotModel model = AcousticModel(out _, out _);
        IReadOnlyList<PlotAxisIdentity> taken = PlotAxisIdentities.Describe(model);

        Assert.False(PlotAxisIdentities.Match(AcousticModel(out _, out _), model, taken));
        Assert.False(PlotAxisIdentities.Match(null, model, taken));
    }

    [Fact]
    public void AValueAxisRearmedToAnotherQuantityStopsMatching()
    {
        PlotModel model = AcousticModel(out _, out LinearAxis value);
        IReadOnlyList<PlotAxisIdentity> takenOnMagnitude = PlotAxisIdentities.Describe(model);

        value.Title = "deg";
        value.AbsoluteMinimum = -180;
        value.AbsoluteMaximum = 180;

        Assert.False(PlotAxisIdentities.Match(model, model, takenOnMagnitude));

        IReadOnlyList<PlotAxisIdentity> takenOnPhase = PlotAxisIdentities.Describe(model);
        value.Title = string.Empty;
        value.AbsoluteMinimum = -1.05;
        value.AbsoluteMaximum = 1.05;

        Assert.False(PlotAxisIdentities.Match(model, model, takenOnPhase));
    }

    [Fact]
    public void SwappingTheBottomAxisInPlaceStopsMatching()
    {
        PlotModel model = AcousticModel(out LogarithmicAxis frequency, out _);
        IReadOnlyList<PlotAxisIdentity> taken = PlotAxisIdentities.Describe(model);

        model.Axes.Remove(frequency);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "ms" });

        Assert.False(PlotAxisIdentities.Match(model, model, taken));
    }

    [Fact]
    public void ADrawnBoxStopsDrawingItselfTheMomentTheAxesAreRearmed()
    {
        PlotModel model = AcousticModel(out LogarithmicAxis frequency, out LinearAxis value);
        var box = new PlotZoomRectangleAnnotation
        {
            Box = new PlotZoomBox(frequency, 500, 2_000, value, -40, -20),
            Text = "1.50 kHz",
            Axes = PlotAxisIdentities.Describe(model),
        };
        model.Annotations.Add(box);

        Assert.True(box.StillDescribesItsPlot());

        // The view switch repaints at once; the controller hears only on the next mouse event, so the box checks at paint.
        value.Title = "deg";
        value.AbsoluteMinimum = -180;
        value.AbsoluteMaximum = 180;

        Assert.False(box.StillDescribesItsPlot());
    }

    private static PlotModel AcousticModel(out LogarithmicAxis frequency, out LinearAxis value)
    {
        var model = new PlotModel();
        frequency = new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = 20,
            AbsoluteMaximum = 20_000,
            Minimum = 20,
            Maximum = 20_000,
        };
        value = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "dB",
            AbsoluteMinimum = -90,
            AbsoluteMaximum = 60,
        };
        model.Axes.Add(frequency);
        model.Axes.Add(value);
        return model;
    }
}
