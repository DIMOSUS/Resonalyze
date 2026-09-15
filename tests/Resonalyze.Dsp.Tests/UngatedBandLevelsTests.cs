using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>Whole-record, unwindowed band mean of POWER on the spatial-average grid (see <see cref="SpatialAverage.FromTransferMagnitude"/>).</summary>
public sealed class UngatedBandLevelsTests
{
    private const int SampleRate = 48_000;

    /// <summary>A mean, not a sum: integrating would climb 3 dB per octave on a unit impulse.</summary>
    [Fact]
    public void AUnitImpulseReadsFlatAtEveryBand()
    {
        double[] levels = DataHelper.GetUngatedBandLevels(Response(0.0, 0));

        Assert.Equal(SpatialAverage.GridBandCount, levels.Length);
        Assert.All(levels, level => Assert.Equal(0.0, level, 9));
    }

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

    [Fact]
    public void ALateReflectionIsInTheCurve()
    {
        double[] levels = DataHelper.GetUngatedBandLevels(
            Response(0.9, SampleRate * 5 / 1_000));

        Assert.True(LevelAt(levels, 200) > 5.0, $"200 Hz read {LevelAt(levels, 200):0.0} dB");
        Assert.True(LevelAt(levels, 100) < -12.0, $"100 Hz read {LevelAt(levels, 100):0.0} dB");
    }

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
