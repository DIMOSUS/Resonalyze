using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The peak-hold envelope: a per-point maximum that ramp-up frames and a changed transform never enter.</summary>
public sealed class LivePeakHoldTests
{
    private long now = 10_000;

    [Fact]
    public void TheEnvelopeIsThePerPointMaximum()
    {
        var hold = new LivePeakHold(() => now);

        hold.Hold(Curve(1.0, 5.0, 3.0));
        hold.Hold(Curve(2.0, 4.0, 3.5));

        Assert.Equal([2.0, 5.0, 3.5], hold.Points!.Select(point => point.Y));
    }

    [Fact]
    public void FramesWithinASecondOfASuspensionAreNotHeld()
    {
        var hold = new LivePeakHold(() => now);
        hold.Hold(Curve(9.0, 9.0, 9.0));

        hold.Suspend();
        Assert.Null(hold.Points);
        now += 999;
        hold.Hold(Curve(1.0, 1.0, 1.0));
        Assert.Null(hold.Points);

        now += 1;
        hold.Hold(Curve(1.0, 2.0, 1.0));
        Assert.Equal([1.0, 2.0, 1.0], hold.Points!.Select(point => point.Y));
    }

    [Fact]
    public void AnotherGridStartsOver()
    {
        var hold = new LivePeakHold(() => now);
        hold.Hold(Curve(9.0, 9.0, 9.0));

        hold.Hold(Curve(1.0, 2.0));

        Assert.Equal([1.0, 2.0], hold.Points!.Select(point => point.Y));
    }

    [Fact]
    public void AMovedTransformDropsTheEnvelopeAndAnUnmovedOneKeepsIt()
    {
        var hold = new LivePeakHold(() => now);
        var relative = new LivePeakHoldKey(MagnitudeScale.Relative, true, 6, null, null);
        hold.Drawn(relative);
        hold.Hold(Curve(1.0, 2.0));

        hold.Follow(relative);
        Assert.NotNull(hold.Points);

        hold.Follow(relative with { SmoothingCode = 12 });
        Assert.Null(hold.Points);
    }

    private static List<SignalPoint> Curve(params double[] levels) =>
        levels.Select((level, index) => new SignalPoint(100.0 * (index + 1), level)).ToList();
}
