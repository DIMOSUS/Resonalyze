using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardPlotFitTests
{
    [Fact]
    public void EqGainAxisRange_FollowsTheBudgetWhenTheCurveFitsInsideIt()
    {
        (double min, double max) = EqWizardPlotFit.EqGainAxisRange(-6, 6, -1, 2);

        Assert.Equal(-12, min);
        Assert.Equal(12, max);
    }

    [Fact]
    public void EqGainAxisRange_ExpandsToContainASummedCurveTallerThanTheBudget()
    {
        // Overlapping bands sum past the single-band ±6 dB budget; the axis grows instead of clipping.
        (double min, double max) = EqWizardPlotFit.EqGainAxisRange(-6, 6, -16.2, 17.3);

        Assert.True(min <= -16.2, $"Axis floor {min} clips the -16.2 dB trough.");
        Assert.True(max >= 17.3, $"Axis ceiling {max} clips the +17.3 dB peak.");
        Assert.Equal(-24, min);
        Assert.Equal(24, max);
    }

    [Fact]
    public void ForCurve_BringsAnAbsoluteSplCurveInsideTheAxis()
    {
        // ~80 dB SPL is outside the absolute IR bounds, so without fitting it could not be panned into view.
        SignalPoint[] points =
        [
            new SignalPoint(20, 78.4),
            new SignalPoint(1_000, 85.1),
            new SignalPoint(20_000, 62.7)
        ];

        EqWizardAxisRange range = EqWizardPlotFit.ForCurve(points);

        Assert.Equal(50, range.Minimum);
        Assert.Equal(100, range.Maximum);
        Assert.True(range.AbsoluteMinimum < range.Minimum);
        Assert.True(range.AbsoluteMaximum > range.Maximum);
        Assert.All(points, point =>
        {
            Assert.True(point.Y > range.Minimum);
            Assert.True(point.Y < range.Maximum);
        });
    }

    // Padding the loopback lifts an IR curve; the pan ceiling is shared with the FR and Live Spectrum plots.
    [Fact]
    public void ImpulseResponseRange_ClearsAPaddedLoopback()
    {
        EqWizardAxisRange range = EqWizardPlotFit.ImpulseResponseRange;

        Assert.Equal(PlotModelStyle.RelativeDecibelAbsoluteMaximum, range.AbsoluteMaximum);
        Assert.True(
            range.AbsoluteMaximum >= 40,
            $"the wizard's dB ceiling of {range.AbsoluteMaximum} dB cannot show a padded loopback");
    }

    [Fact]
    public void ForCurve_IgnoresUnmeasuredBandsWhenFitting()
    {
        SignalPoint[] points =
        [
            new SignalPoint(20, double.NaN),
            new SignalPoint(1_000, -12),
            new SignalPoint(20_000, -18)
        ];

        EqWizardAxisRange range = EqWizardPlotFit.ForCurve(points);

        Assert.Equal(-30, range.Minimum);
        Assert.Equal(0, range.Maximum);
    }

    [Fact]
    public void ForCurve_FallsBackWhenNothingWasMeasured()
    {
        EqWizardAxisRange range = EqWizardPlotFit.ForCurve(
        [
            new SignalPoint(20, double.NaN),
            new SignalPoint(1_000, double.NaN)
        ]);

        Assert.Equal(EqWizardPlotFit.ImpulseResponseRange, range);
    }

    [Fact]
    public void ForCurve_KeepsAFlatCurveFromCollapsingTheAxis()
    {
        EqWizardAxisRange range = EqWizardPlotFit.ForCurve(
        [
            new SignalPoint(20, 75),
            new SignalPoint(20_000, 75)
        ]);

        Assert.True(range.Maximum - range.Minimum >= 10);
    }
}
