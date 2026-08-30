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
        // The front sub of the reference car: it measures down to 25 Hz like the
        // rear one does, and what separates them is the 50 Hz corner the owner
        // already set on it.
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            Band(25, 300), highPassHz: 50, lowPassHz: 110);

        Assert.Equal(50, low, 3);
        Assert.Equal(110, high, 3);
    }

    [Fact]
    public void EffectiveBand_IgnoresCornersThatWidenTheBand()
    {
        // A corner outside what the driver measured is not information about the
        // driver: the measurement still bounds it.
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            Band(80, 400), highPassHz: 20, lowPassHz: 8_000);

        Assert.Equal(80, low, 3);
        Assert.Equal(400, high, 3);
    }

    [Fact]
    public void EffectiveBand_FallsBackWhenTheCornersLeaveNothing()
    {
        // A high-pass above the low-pass, or a pair set outside the measured band
        // entirely, describes no band at all — and something that describes
        // nothing must not outrank the measurement.
        (double low, double high) = VirtualCrossoverAutoSetupOrder.EffectiveBand(
            Band(80, 400), highPassHz: 900, lowPassHz: 1_200);

        Assert.Equal(80, low, 3);
        Assert.Equal(400, high, 3);
    }

    [Fact]
    public void CenterHz_OrdersTwoSubwoofersTheirCornersHaveSeparated()
    {
        // Two subs whose MEASURED bands are the same shape. Nothing in the
        // magnitude tells them apart; the corners do, and that is the whole point
        // of reading the order off the effective band.
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
        // The same two subs before either has a corner: identical bands, so the
        // order shown is a guess and the wizard has to say so.
        double one = VirtualCrossoverAutoSetupOrder.CenterHz(Band(20, 300), null, null);
        double other = VirtualCrossoverAutoSetupOrder.CenterHz(Band(22, 290), null, null);

        Assert.Equal(
            VirtualCrossoverChainOrder.Unclear,
            VirtualCrossoverAutoSetupOrder.Judge(one, other));
    }

    [Fact]
    public void Judge_CallsAChainRunningBackwardsReversed()
    {
        // A row moved the wrong way, or a driver type confirmed against what the
        // channel actually plays: the tweeter placed under the midbass. Nothing
        // ambiguous about it — the measurement says plainly that it is wrong way
        // round, and that is a different thing to tell the user than "cannot say".
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
        // A band estimate that collapsed leaves a zero or NaN centre; that is not
        // a wrong order to warn about, it is a channel with no usable band, which
        // the wizard reports separately.
        Assert.Equal(
            VirtualCrossoverChainOrder.AsMeasured,
            VirtualCrossoverAutoSetupOrder.Judge(0, 100));
        Assert.Equal(
            VirtualCrossoverChainOrder.AsMeasured,
            VirtualCrossoverAutoSetupOrder.Judge(100, double.NaN));
    }
}
