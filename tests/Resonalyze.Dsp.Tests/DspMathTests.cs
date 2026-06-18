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
}
