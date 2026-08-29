using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// An impulse response read onto the shared spatial-average grid: the whole record,
/// no window, as the band mean of POWER.
/// </summary>
/// <remarks>
/// The estimator is the point. A capture's bands are read this way, and a difference
/// between a response and a capture is only a difference when both sides are the same
/// quantity — the two alternatives already in the library are each wrong here for
/// their own reason, and <see cref="SpatialAverage.FromTransferMagnitude"/> says which.
/// </remarks>
public sealed class UngatedBandLevelsTests
{
    private const int SampleRate = 48_000;

    /// <summary>
    /// A unit impulse is flat at every frequency and reads flat at every BAND — the
    /// low ones, which hold a single bin, and the high ones, which hold dozens. That
    /// is the difference between a mean and a sum: integrating the band instead would
    /// climb 3 dB per octave on this input, because a wider band holds more bins.
    /// </summary>
    [Fact]
    public void AUnitImpulseReadsFlatAtEveryBand()
    {
        double[] levels = DataHelper.GetUngatedBandLevels(Response(0.0, 0));

        Assert.Equal(SpatialAverage.GridBandCount, levels.Length);
        Assert.All(levels, level => Assert.Equal(0.0, level, 9));
    }

    /// <summary>
    /// A response twice as loud reads exactly 6 dB higher everywhere: the level is a
    /// level, and nothing about the estimator scales with the band it was read over.
    /// </summary>
    [Fact]
    public void AGainShiftsEveryBandByExactlyIt()
    {
        double[] quiet = DataHelper.GetUngatedBandLevels(Response(0.0, 0));
        double[] loud = DataHelper.GetUngatedBandLevels(Response(0.0, 0, amplitude: 2.0));

        for (int band = 0; band < quiet.Length; band++)
        {
            Assert.Equal(6.0206, loud[band] - quiet[band], 6);
        }
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
        double[] levels = DataHelper.GetUngatedBandLevels(
            Response(0.9, SampleRate * 5 / 1_000));

        Assert.True(LevelAt(levels, 200) > 5.0, $"200 Hz read {LevelAt(levels, 200):0.0} dB");
        Assert.True(LevelAt(levels, 100) < -12.0, $"100 Hz read {LevelAt(levels, 100):0.0} dB");
    }

    /// <summary>
    /// A response the sweep never reached at all has no level to report, and says so
    /// rather than reporting the arithmetic of an empty band.
    /// </summary>
    [Fact]
    public void ASilentResponseReportsNothing()
    {
        double[] levels = DataHelper.GetUngatedBandLevels(
            new SyntheticMeasurement(new Complex[4_096], SampleRate, 0));

        Assert.All(levels, level => Assert.True(double.IsNaN(level)));
    }

    private static SyntheticMeasurement Response(
        double reflection, int delaySamples, double amplitude = 1.0)
    {
        var impulse = new Complex[32_768];
        impulse[0] = amplitude;
        if (reflection != 0.0)
        {
            impulse[delaySamples] = amplitude * reflection;
        }

        return new SyntheticMeasurement(impulse, SampleRate, 0);
    }

    private static double LevelAt(double[] levels, double frequency)
    {
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        int nearest = 0;
        for (int band = 1; band < grid.Count; band++)
        {
            if (Math.Abs(Math.Log(grid[band] / frequency)) <
                Math.Abs(Math.Log(grid[nearest] / frequency)))
            {
                nearest = band;
            }
        }

        return levels[nearest];
    }
}
