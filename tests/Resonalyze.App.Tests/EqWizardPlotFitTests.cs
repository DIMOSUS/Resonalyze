using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardPlotFitTests
{
    [Fact]
    public void EqGainAxisRange_FollowsTheBudgetWhenTheCurveFitsInsideIt()
    {
        // A flat/small curve: the axis reads as the ±6 dB budget, snapped out one step.
        (double min, double max) = EqWizardPlotFit.EqGainAxisRange(-6, 6, -1, 2);

        Assert.Equal(-12, min);
        Assert.Equal(12, max);
    }

    [Fact]
    public void EqGainAxisRange_ExpandsToContainASummedCurveTallerThanTheBudget()
    {
        // Several overlapping +6 dB bands sum to ~+17 dB, and a stack of cuts reaches
        // ~-16 dB — both well past the single-band ±6 dB budget. The axis must grow to
        // contain them (rounded out to the 6 dB step with a step of headroom) rather than
        // clip the drawn curve, which the old budget-only range did.
        (double min, double max) = EqWizardPlotFit.EqGainAxisRange(-6, 6, -16.2, 17.3);

        Assert.True(min <= -16.2, $"Axis floor {min} clips the -16.2 dB trough.");
        Assert.True(max >= 17.3, $"Axis ceiling {max} clips the +17.3 dB peak.");
        Assert.Equal(-24, min);
        Assert.Equal(24, max);
    }

    [Fact]
    public void ForCurve_BringsAnAbsoluteSplCurveInsideTheAxis()
    {
        // A moving-microphone room average sits near 80 dB SPL — completely outside the
        // impulse-response bounds, and those are ABSOLUTE, so without fitting the curve
        // could not even be panned into view.
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

    [Fact]
    public void SuggestTargetOffsetDb_LandsAFlatTargetOnTheSourceMean()
    {
        SignalPoint[] points =
        [
            new SignalPoint(100, 80),
            new SignalPoint(1_000, 84),
            new SignalPoint(10_000, 76)
        ];

        double offset = EqWizardPlotFit.SuggestTargetOffsetDb(
            points,
            _ => 0,
            20,
            20_000);

        Assert.Equal(80, offset);
    }

    [Fact]
    public void SuggestTargetOffsetDb_AccountsForTheTargetsOwnShape()
    {
        SignalPoint[] points =
        [
            new SignalPoint(100, 80),
            new SignalPoint(1_000, 80)
        ];

        // A target that already sits 6 dB up needs 6 dB less offset to meet the source.
        double offset = EqWizardPlotFit.SuggestTargetOffsetDb(
            points,
            _ => 6,
            20,
            20_000);

        Assert.Equal(74, offset);
    }

    [Fact]
    public void SuggestTargetOffsetDb_OnlyAveragesInsideTheTuningWindow()
    {
        SignalPoint[] points =
        [
            new SignalPoint(30, 20),        // below the window: excluded
            new SignalPoint(500, 80),
            new SignalPoint(1_000, 80),
            new SignalPoint(15_000, 20)     // above the window: excluded
        ];

        double offset = EqWizardPlotFit.SuggestTargetOffsetDb(points, _ => 0, 100, 5_000);

        Assert.Equal(80, offset);
    }

    [Fact]
    public void SuggestTargetOffsetDb_IsZeroWhenTheWindowHoldsNothingUsable()
    {
        SignalPoint[] points =
        [
            new SignalPoint(100, double.NaN),
            new SignalPoint(1_000, double.NaN)
        ];

        Assert.Equal(0, EqWizardPlotFit.SuggestTargetOffsetDb(points, _ => 0, 20, 20_000));
    }
}
