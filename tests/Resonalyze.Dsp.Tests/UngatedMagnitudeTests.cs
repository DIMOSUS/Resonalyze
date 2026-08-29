using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The ungated magnitude of an impulse response — the whole record, no window — which
/// is what a steady-state measurement of the same source reads, and therefore the only
/// curve a spatially averaged capture may be differenced against.
/// </summary>
public sealed class UngatedMagnitudeTests
{
    private const int SampleRate = 48_000;

    /// <summary>
    /// A unit impulse is flat at every frequency, and reads flat: the resampler's own
    /// weighting adds nothing of its own to a curve a difference will be taken from.
    /// </summary>
    [Fact]
    public void AUnitImpulseReadsFlat()
    {
        List<SignalPoint> curve = DataHelper.GetUngatedMagnitude(
            Response(0.0, 0), smoothingOctaves: 1.0 / 6.0);

        Assert.NotEmpty(curve);
        Assert.All(curve, point => Assert.Equal(0.0, point.Y, 6));
    }

    /// <summary>
    /// A reflection five milliseconds behind the arrival is IN the curve, as the comb
    /// it makes — the energy a window would have cut away, and exactly what a
    /// steady-state capture of the same room holds.
    /// </summary>
    [Fact]
    public void ALateReflectionIsInTheCurve()
    {
        // 0.9 of the arrival, 5 ms later: constructive at every 200 Hz, destructive
        // half way between.
        List<SignalPoint> curve = DataHelper.GetUngatedMagnitude(
            Response(0.9, SampleRate * 5 / 1_000), smoothingOctaves: 1.0 / 6.0);

        Assert.True(LevelAt(curve, 200) > 5.0, $"200 Hz read {LevelAt(curve, 200):0.0} dB");
        Assert.True(LevelAt(curve, 100) < -12.0, $"100 Hz read {LevelAt(curve, 100):0.0} dB");
    }

    private static SyntheticMeasurement Response(double reflection, int delaySamples)
    {
        var impulse = new Complex[32_768];
        impulse[0] = Complex.One;
        if (reflection != 0.0)
        {
            impulse[delaySamples] = reflection;
        }

        return new SyntheticMeasurement(impulse, SampleRate, 0);
    }

    private static double LevelAt(List<SignalPoint> curve, double frequency) =>
        curve
            .OrderBy(point => Math.Abs(Math.Log(point.X / frequency)))
            .First()
            .Y;
}
