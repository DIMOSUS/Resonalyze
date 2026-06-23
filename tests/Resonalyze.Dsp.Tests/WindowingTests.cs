namespace Resonalyze.Dsp.Tests;

public sealed class WindowingTests
{
    [Fact]
    public void TukeyWindowHalfZeroPadded_KeepsSecondHalfZero()
    {
        double[] window = Windowing.TukeyWindowHalfZeroPadded(
            16,
            leftTukeyWindow: 0.25,
            rightTukeyWindow: 0.25);

        Assert.All(window.Skip(8), value => Assert.Equal(0.0, value));
    }

    [Fact]
    public void TukeyWindowHalfZeroPadded_PreservesFadeWidthInSamples()
    {
        double[] window = Windowing.TukeyWindowHalfZeroPadded(
            16,
            leftTukeyWindow: 0.25,
            rightTukeyWindow: 0.25);

        Assert.Equal(0.0, window[0], precision: 12);
        Assert.InRange(window[1], 0.49, 0.51);
        Assert.Equal(1.0, window[2], precision: 12);
        Assert.Equal(1.0, window[5], precision: 12);
        Assert.InRange(window[6], 0.49, 0.51);
        Assert.Equal(0.0, window[7], precision: 12);
    }

    [Fact]
    public void TukeyWindowHalfZeroPadded_DoesNotOverwriteOverlappingFades()
    {
        double[] window = Windowing.TukeyWindowHalfZeroPadded(
            16,
            leftTukeyWindow: 1.0,
            rightTukeyWindow: 1.0);

        Assert.Equal(0.0, window[0], precision: 12);
        Assert.Equal(0.0, window[7], precision: 12);
        Assert.True(window.Take(8).Max() > 0.85);
        Assert.True(window[3] > window[1]);
        Assert.True(window[4] > window[6]);
    }

    [Fact]
    public void TukeyWindowHalfZeroPadded_ScalesFadesWhenTheyExceedActiveHalf()
    {
        double[] window = Windowing.TukeyWindowHalfZeroPadded(
            1024,
            leftTukeyWindow: 1.0,
            rightTukeyWindow: 1.0);

        Assert.Equal(0.0, window[0], precision: 12);
        Assert.True(window.Take(512).Max() > 0.99);
        Assert.Equal(0.0, window[511], precision: 12);
        Assert.All(window.Skip(512), value => Assert.Equal(0.0, value));
    }
}
