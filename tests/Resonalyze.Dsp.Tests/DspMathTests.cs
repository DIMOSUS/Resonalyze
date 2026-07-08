namespace Resonalyze.Dsp.Tests;

public sealed class DspMathTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(1025, 2048)]
    public void NextPowerOfTwo_ReturnsSmallestPowerAtLeastInput(
        int value,
        int expected)
    {
        Assert.Equal(expected, DspMath.NextPowerOfTwo(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextPowerOfTwo_RejectsNonPositiveInput(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DspMath.NextPowerOfTwo(value));
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(7, 8, 7)]
    [InlineData(8, 8, 0)]
    [InlineData(-1, 8, 7)]
    [InlineData(-9, 8, 7)]
    public void WrapIndex_WrapsIntoRange(int index, int length, int expected)
    {
        Assert.Equal(expected, DspMath.WrapIndex(index, length));
    }

    [Fact]
    public void LanczosKernel_IsUnityAtTheCentre()
    {
        Assert.Equal(1.0, DspMath.LanczosKernel(0.0, 2.0), precision: 12);
    }

    [Theory]
    [InlineData(1.0, 2.0)] // integer offset -> sinc zero
    [InlineData(2.0, 2.0)] // support edge |x| >= a -> zero
    [InlineData(3.0, 2.0)] // outside support -> zero
    public void LanczosKernel_IsZeroAtIntegerOffsetsAndBeyondSupport(double x, double a)
    {
        Assert.Equal(0.0, DspMath.LanczosKernel(x, a), precision: 12);
    }

    [Fact]
    public void LanczosKernel_MatchesTheWindowedSincFormulaAtAnInteriorPoint()
    {
        // a * sinc(pi x) * sinc(pi x / a) at x = 0.5, a = 2:
        // 2 * sin(pi/2) * sin(pi/4) / (pi/2)^2 = 0.5731591682...
        Assert.Equal(0.5731591682, DspMath.LanczosKernel(0.5, 2.0), precision: 9);
    }

    [Fact]
    public void LanczosKernel_IsSymmetric()
    {
        Assert.Equal(
            DspMath.LanczosKernel(0.7, 2.0),
            DspMath.LanczosKernel(-0.7, 2.0),
            precision: 12);
    }
}
