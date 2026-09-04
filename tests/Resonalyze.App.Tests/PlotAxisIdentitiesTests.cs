using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze.App.Tests;

// What tells a remembered range that the plot has stopped showing what it was taken
// from. The model reference alone does not: the Virtual DSP acoustic view re-arms one
// axis object between dB, degrees and a unitless impulse scale, and swaps its bottom
// axis between frequency and time, all inside ONE model.
public sealed class PlotAxisIdentitiesTests
{
    [Fact]
    public void AnUntouchedModelStillMatchesWhatWasTakenFromIt()
    {
        PlotModel model = AcousticModel(out _, out _);
        IReadOnlyList<PlotAxisIdentity> taken = PlotAxisIdentities.Describe(model);

        Assert.True(PlotAxisIdentities.Match(model, model, taken));

        // A range or a pan is not a change of meaning: what the axis IS stays what
        // it was, so a box drawn before a wheel notch survives it.
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

        // Virtual DSP's Magnitude → Phase: the same axis OBJECT, now degrees.
        value.Title = "deg";
        value.AbsoluteMinimum = -180;
        value.AbsoluteMaximum = 180;

        Assert.False(PlotAxisIdentities.Match(model, model, takenOnMagnitude));

        // And → Impulse, where it carries no title at all and a unitless range.
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

        // Virtual DSP's impulse view: frequency out, milliseconds in, same model.
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

        // The Virtual DSP view switch re-arms the axes and repaints in the same
        // breath; the controller hears about it only on the next mouse event, which
        // may never come. So the box answers for itself, at the paint.
        value.Title = "deg";
        value.AbsoluteMinimum = -180;
        value.AbsoluteMaximum = 180;

        Assert.False(box.StillDescribesItsPlot());
    }

    // The shape of the Virtual DSP acoustic plot: a frequency axis and one value axis
    // that gets re-armed rather than replaced.
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
