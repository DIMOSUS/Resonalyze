using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Placing a whole group against a settled reference: the delay that makes the
/// two arrive together, and the relative polarity that falls out of the same
/// measurement.
/// </summary>
public sealed class VirtualCrossoverGroupPlacementTests
{
    private const int Rate = 48_000;

    // A band-limited click: enough of a packet for an arrival read and a
    // correlation peak, with nothing periodic to offer a rival lobe.
    private static Complex[] Packet(double delayMs, double scale = 1.0)
    {
        var ir = new Complex[16_384];
        int center = 2_048 + (int)Math.Round(delayMs * Rate / 1_000.0);
        // A short raised-cosine burst around 1 kHz: real enough to time, cheap
        // enough to keep the test fast.
        const double ToneHz = 1_000.0;
        const int HalfWidth = 96;
        for (int i = -HalfWidth; i <= HalfWidth; i++)
        {
            int index = center + i;
            if (index < 0 || index >= ir.Length)
            {
                continue;
            }

            double window = 0.5 * (1.0 + Math.Cos(Math.PI * i / HalfWidth));
            ir[index] += scale * window * Math.Sin(2.0 * Math.PI * ToneHz * i / Rate);
        }

        return ir;
    }

    [Fact]
    public void Place_ReadsBackADelayItWasGiven()
    {
        // The group sits 6 ms behind the reference, so aligning it means taking
        // 6 ms OFF — the delay to add is negative, and the normalization pass is
        // what later turns that into something a processor can dial.
        Complex[] reference = Packet(0);
        Complex[] group = Packet(6.0);

        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            reference, group, Rate, 500, 2_000);

        Assert.NotNull(placement);
        Assert.InRange(placement.CoArrivalDelayMs, -6.15, -5.85);
        Assert.False(placement.Inverted);
        Assert.InRange(placement.Coefficient, 0.5, 1.0);
    }

    [Fact]
    public void Place_ReadsPolarityFromTheSameMeasurement()
    {
        // Inverting the group must not change WHERE it is, only how it reads.
        // Polarity is a measurement here rather than a guess, which is why the
        // caller may apply it rather than merely suggest it.
        Complex[] reference = Packet(0);
        Complex[] inverted = Packet(4.0, scale: -1.0);

        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            reference, inverted, Rate, 500, 2_000);

        Assert.NotNull(placement);
        Assert.True(placement.Inverted);
        Assert.InRange(placement.CoArrivalDelayMs, -4.15, -3.85);
    }

    [Fact]
    public void Place_RefusesABandTooNarrowToTimeAnythingIn()
    {
        Complex[] reference = Packet(0);
        Complex[] group = Packet(3.0);

        Assert.Null(VirtualCrossoverGroupPlacement.Place(
            reference, group, Rate, 1_000, 1_050));
    }

    [Fact]
    public void Midpoint_PutsTheCentreBetweenTheTwoSides()
    {
        // The centre reads 5.00 ms against one side and 5.50 against the other,
        // which is exactly the 0.5 ms the sides themselves are apart. It belongs
        // in the middle, and the two readings corroborate each other.
        var near = new GroupPlacement(-5.00, false, 0.8);
        var far = new GroupPlacement(-5.50, false, 0.8);

        (double delayMs, bool inverted, bool confident) =
            VirtualCrossoverGroupPlacement.Midpoint(near, far, 0.5, 0.25);

        Assert.Equal(-5.25, delayMs, 3);
        Assert.False(inverted);
        Assert.True(confident);
    }

    [Fact]
    public void Midpoint_StillPlacesTheCentreWhenTheSidesDisagreeButSaysSoInstead()
    {
        // The sides are 3 ms apart where the scene offset says 0.5: one of the
        // two readings landed on the wrong lobe. The midpoint is still the best
        // available answer, so it is returned — but the run must not present it
        // with the confidence of one the sides corroborated.
        var near = new GroupPlacement(-5.0, false, 0.8);
        var far = new GroupPlacement(-8.0, false, 0.8);

        (double delayMs, _, bool confident) =
            VirtualCrossoverGroupPlacement.Midpoint(near, far, 0.5, 0.25);

        Assert.Equal(-6.5, delayMs, 3);
        Assert.False(confident);
    }

    [Fact]
    public void Midpoint_WillNotFlipPolarityOnHalfAMeasurement()
    {
        // A centre reading inverted against one side and normal against the
        // other is not a centre wired backwards; it is a measurement that has
        // not settled. Flipping on that would be a coin toss dressed as a
        // reading, so the polarity stays and the confidence goes.
        var near = new GroupPlacement(-5.0, true, 0.8);
        var far = new GroupPlacement(-5.5, false, 0.8);

        (_, bool inverted, bool confident) =
            VirtualCrossoverGroupPlacement.Midpoint(near, far, 0.5, 0.25);

        Assert.False(inverted);
        Assert.False(confident);
    }

    [Fact]
    public void Midpoint_WithholdsConfidenceFromAWeakReading()
    {
        var near = new GroupPlacement(-5.0, false, 0.8);
        var far = new GroupPlacement(-5.5, false, 0.05);

        (_, _, bool confident) =
            VirtualCrossoverGroupPlacement.Midpoint(near, far, 0.5, 0.25);

        Assert.False(confident);
    }
}
