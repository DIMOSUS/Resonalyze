using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAutoSetupOrderTests
{
    private static DriverBandEstimate Band(double lowHz, double highHz) =>
        new(lowHz, highHz, 0, DriverType.Subwoofer);

    [Fact]
    public void EffectiveBand_IsTheMeasuredBandNarrowedByTheCornersAlreadySet()
    {
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            Band(25, 300), highPassHz: 50, lowPassHz: 110);

        Assert.Equal(50, low, 3);
        Assert.Equal(110, high, 3);
    }

    [Fact]
    public void EffectiveBand_IgnoresCornersThatWidenTheBand()
    {
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            Band(80, 400), highPassHz: 20, lowPassHz: 8_000);

        Assert.Equal(80, low, 3);
        Assert.Equal(400, high, 3);
    }

    [Fact]
    public void EffectiveBand_FallsBackWhenTheCornersLeaveNothing()
    {
        // Inverted or out-of-band corners describe no band and must not outrank the measurement.
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            Band(80, 400), highPassHz: 900, lowPassHz: 1_200);

        Assert.Equal(80, low, 3);
        Assert.Equal(400, high, 3);
    }

    [Fact]
    public void CenterHz_OrdersTwoSubwoofersTheirCornersHaveSeparated()
    {
        DriverBandEstimate measured = Band(20, 300);
        double rear = VirtualCrossoverAutoSetupOrder.CenterHz(
            measured, highPassHz: null, lowPassHz: 50);
        double front = VirtualCrossoverAutoSetupOrder.CenterHz(
            measured, highPassHz: 50, lowPassHz: 110);

        Assert.True(rear < front, $"Rear sub at {rear:0} Hz, front at {front:0} Hz.");
        Assert.Equal(
            VirtualCrossoverChainOrder.AsMeasured,
            VirtualCrossoverAutoSetupOrder.Judge(rear, front));
    }

    [Fact]
    public void Judge_CallsAPairNothingHasSeparatedUnclear()
    {
        double one = VirtualCrossoverAutoSetupOrder.CenterHz(Band(20, 300), null, null);
        double other = VirtualCrossoverAutoSetupOrder.CenterHz(Band(22, 290), null, null);

        Assert.Equal(
            VirtualCrossoverChainOrder.Unclear,
            VirtualCrossoverAutoSetupOrder.Judge(one, other));
    }

    [Fact]
    public void Judge_CallsAChainRunningBackwardsReversed()
    {
        double midbass = VirtualCrossoverAutoSetupOrder.CenterHz(Band(60, 900), null, null);
        double tweeter =
            VirtualCrossoverAutoSetupOrder.CenterHz(Band(2_200, 20_000), null, null);

        Assert.Equal(
            VirtualCrossoverChainOrder.Reversed,
            VirtualCrossoverAutoSetupOrder.Judge(tweeter, midbass));
        Assert.Equal(
            VirtualCrossoverChainOrder.AsMeasured,
            VirtualCrossoverAutoSetupOrder.Judge(midbass, tweeter));
    }

    [Fact]
    public void Judge_SplitsAtHalfAnOctaveInBothDirections()
    {
        double lower = 100;
        double justInside = lower * Math.Pow(2.0, 0.49);
        double justOutside = lower * Math.Pow(2.0, 0.51);

        Assert.Equal(
            VirtualCrossoverChainOrder.Unclear,
            VirtualCrossoverAutoSetupOrder.Judge(lower, justInside));
        Assert.Equal(
            VirtualCrossoverChainOrder.Unclear,
            VirtualCrossoverAutoSetupOrder.Judge(justInside, lower));
        Assert.Equal(
            VirtualCrossoverChainOrder.AsMeasured,
            VirtualCrossoverAutoSetupOrder.Judge(lower, justOutside));
        Assert.Equal(
            VirtualCrossoverChainOrder.Reversed,
            VirtualCrossoverAutoSetupOrder.Judge(justOutside, lower));
    }

    [Fact]
    public void Judge_SaysNothingAboutAnUnreadableCenter()
    {
        // A collapsed band estimate (zero/NaN centre) is reported separately, not as a wrong order.
        Assert.Equal(
            VirtualCrossoverChainOrder.AsMeasured,
            VirtualCrossoverAutoSetupOrder.Judge(0, 100));
        Assert.Equal(
            VirtualCrossoverChainOrder.AsMeasured,
            VirtualCrossoverAutoSetupOrder.Judge(100, double.NaN));
    }
}
