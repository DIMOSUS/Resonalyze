using Resonalyze.Ui.Dialogs;

namespace Resonalyze.App.Tests;

// The limits dialog reads an axis's absolute bounds into decimal spinners. An axis that
// was never given bounds carries OxyPlot's own defaults, which are finite doubles far
// outside decimal's range — casting one took the double click down with an
// OverflowException.
public sealed class GraphLimitsDialogTests
{
    [Theory]
    [InlineData(double.MinValue, -1_000_000)]
    [InlineData(double.MaxValue, 1_000_000)]
    [InlineData(-1e300, -1_000_000)]
    [InlineData(1e300, 1_000_000)]
    public void EditorLimit_ClampsAnUnboundedAxisInsteadOfOverflowing(
        double absoluteLimit, decimal expected)
    {
        decimal fallback = absoluteLimit < 0 ? -1_000_000m : 1_000_000m;

        Assert.Equal(
            expected,
            GraphLimitsDialog.EditorLimit(absoluteLimit, fallback, logarithmic: false));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EditorLimit_FallsBackWhenTheBoundIsNotANumber(double absoluteLimit)
    {
        Assert.Equal(
            42m,
            GraphLimitsDialog.EditorLimit(absoluteLimit, 42m, logarithmic: false));
    }

    [Fact]
    public void EditorLimit_KeepsALogarithmicAxisAboveZero()
    {
        Assert.Equal(
            0.01m,
            GraphLimitsDialog.EditorLimit(double.MinValue, -1_000_000m, logarithmic: true));
    }

    [Fact]
    public void EditorLimit_PassesAnOrdinaryBoundThrough()
    {
        Assert.Equal(
            -60m,
            GraphLimitsDialog.EditorLimit(-60.0, -1_000_000m, logarithmic: false));
    }
}
