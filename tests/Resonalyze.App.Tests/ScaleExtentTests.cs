using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ScaleExtentTests
{
    [Fact]
    public void TheExtentReadsLevelsAndLossApart()
    {
        ScaleExtent extent = ScaleExtent.Of(
        [
            Curve(false, -30, -12, double.NaN),
            Curve(true, -3, -19)
        ])!;

        Assert.Equal(-30, extent.LowDb);
        Assert.Equal(-12, extent.HighDb);
        Assert.Equal(-19, extent.DeepestLossDb);
    }

    [Fact]
    public void TheUnionIsTheSameWhicheverSideComesFirst()
    {
        var left = new ScaleExtent(-40, -10, -12);
        var right = new ScaleExtent(-52, -16, null);

        Assert.Equal(new ScaleExtent(-52, -10, -12), ScaleExtent.Union(left, right));
        Assert.Equal(ScaleExtent.Union(left, right), ScaleExtent.Union(right, left));
        Assert.Same(left, ScaleExtent.Union(left, null));
    }

    [Fact]
    public void TheAxisRoundsOutwardToWholeSteps_WithinItsBounds()
    {
        Assert.Equal((-55.0, -5.0), new ScaleExtent(-52.3, -9.8, null).AxisRange(5, -90, 60));
        Assert.Equal((-90.0, -5.0), new ScaleExtent(-140, -9.8, null).AxisRange(5, -90, 60));
        Assert.Equal((55.0, 60.0), new ScaleExtent(70, 80, null).AxisRange(5, -90, 60));
        Assert.Equal((-90.0, -85.0), new ScaleExtent(-140, -120, null).AxisRange(5, -90, 60));
        Assert.Null(new ScaleExtent(double.NaN, double.NaN, -20).AxisRange(5, -90, 60));
    }

    private static AcousticCurve Curve(bool loss, params double[] levels) =>
        new(string.Empty, [.. levels.Select((level, i) => new SignalPoint(100 * (i + 1), level))],
            OxyColors.White, 1, LineStyle.Solid, OnLossAxis: loss);
}
