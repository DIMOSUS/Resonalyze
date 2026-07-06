namespace Resonalyze.Dsp.Tests;

public sealed class WindowingTests
{
    [Fact]
    public void TukeyWindow_BuildsFadePlateauFade()
    {
        double[] window = Windowing.TukeyWindow(
            1024,
            leftTukeyWindow: 0.25,
            rightTukeyWindow: 0.25);

        Assert.Equal(0.0, window[0], precision: 12);
        Assert.Equal(1.0, window[512], precision: 12);
        Assert.True(window[^1] < 0.01);
        Assert.All(window, value => Assert.InRange(value, 0.0, 1.0));
    }

    [Fact]
    public void TukeyWindow_ZeroRightFadeKeepsPlateauToTheEnd()
    {
        double[] window = Windowing.TukeyWindow(
            256,
            leftTukeyWindow: 0.5,
            rightTukeyWindow: 0.0);

        Assert.Equal(1.0, window[^1], precision: 12);
        Assert.Equal(1.0, window[128], precision: 12);
        Assert.Equal(0.0, window[0], precision: 12);
    }

    [Fact]
    public void TukeyWindow_NormalizesOverlappingFades()
    {
        // left + right > 2 used to let the two fade loops overwrite each other,
        // producing a malformed shape; the fades must scale down and meet instead.
        double[] window = Windowing.TukeyWindow(
            256,
            leftTukeyWindow: 2.0,
            rightTukeyWindow: 2.0);

        Assert.Equal(0.0, window[0], precision: 12);
        Assert.True(window[^1] < 0.01);
        Assert.True(window[128] > 0.95);
        Assert.All(window, value => Assert.InRange(value, 0.0, 1.0));

        // Monotone rise then fall: no dips from overlapping fades.
        for (int i = 1; i < 128; i++)
        {
            Assert.True(window[i] >= window[i - 1] - 1e-12);
        }
        for (int i = 129; i < 256; i++)
        {
            Assert.True(window[i] <= window[i - 1] + 1e-12);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TukeyWindow_HandlesDegenerateLengths(int length)
    {
        double[] window = Windowing.TukeyWindow(length, 0.5, 0.5);

        Assert.Equal(length, window.Length);
        Assert.All(window, value => Assert.Equal(1.0, value));
    }

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
