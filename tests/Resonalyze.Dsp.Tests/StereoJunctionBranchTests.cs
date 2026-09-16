namespace Resonalyze.Dsp.Tests;

public sealed class StereoJunctionBranchTests
{
    private const double HalfPeriodMs = 2.5;

    /// <summary>A scorer whose branch optimum sits at <paramref name="bestDeltaMs"/> on each side.</summary>
    private static Func<bool, double, bool, double> Scorer(
        double referenceBase,
        double farBase,
        double bestDeltaMs,
        double referencePeak,
        double farPeak) =>
        (farSide, deltaMs, flip) =>
        {
            if (!flip)
            {
                return farSide ? farBase : referenceBase;
            }

            double distance = Math.Abs(deltaMs - bestDeltaMs);
            double peak = farSide ? farPeak : referencePeak;
            double baseline = farSide ? farBase : referenceBase;
            return peak - (peak - baseline) * Math.Min(1, distance / HalfPeriodMs);
        };

    [Fact]
    public void Read_FindsTheFlipPartnerAndReportsBothSides()
    {
        Func<bool, double, bool, double> score = Scorer(
            referenceBase: -1.0, farBase: -2.0, bestDeltaMs: 2.5,
            referencePeak: -0.95, farPeak: -1.0);

        StereoBranchReading reading = StereoJunctionBranch.Read(score, HalfPeriodMs)!;

        // The scan walks a 0.1 ms grid from a quarter period out, so it lands beside the optimum, not on it.
        Assert.Equal(2.5, reading.DeltaMs, 1);
        Assert.True(reading.Flip);
        Assert.Equal(1.0, reading.FarGainDb, 1);
        Assert.Equal(0.05, reading.ReferenceGainDb, 1);
        Assert.True(StereoJunctionBranch.Adopt(reading));
    }

    [Fact]
    public void Read_PrefersADeltaTheReferenceSideCanLiveWith()
    {
        // The far side's own optimum costs the reference 0.6 dB; a delta 0.5 ms away still gains it plenty.
        Func<bool, double, bool, double> score = (farSide, deltaMs, flip) =>
        {
            if (!flip)
            {
                return farSide ? -2.0 : -1.0;
            }

            return farSide
                ? -1.0 - Math.Abs(deltaMs - 2.5) * 0.2
                : (Math.Abs(deltaMs - 2.5) < 0.25 ? -1.6 : -1.02);
        };

        StereoBranchReading reading = StereoJunctionBranch.Read(score, HalfPeriodMs)!;

        Assert.True(Math.Abs(reading.DeltaMs - 2.5) >= 0.25);
        Assert.True(reading.ReferenceGainDb > -StereoJunctionBranch.ReferenceLossDb);
        Assert.True(StereoJunctionBranch.Adopt(reading));
    }

    [Fact]
    public void Adopt_RefusesToBuyTheFarJunctionWithTheNearOne()
    {
        var reading = new StereoBranchReading(2.5, true, -0.9, 1.4);

        Assert.False(StereoJunctionBranch.Adopt(reading));
    }

    [Fact]
    public void Adopt_RefusesAFarGainInsideTheNoise()
    {
        var reading = new StereoBranchReading(2.5, true, 0.0, 0.2);

        Assert.False(StereoJunctionBranch.Adopt(reading));
    }

    [Fact]
    public void Read_NullWhereTheJunctionCannotBeScored()
    {
        Assert.Null(StereoJunctionBranch.Read(
            (_, _, _) => double.NegativeInfinity, HalfPeriodMs));
    }

    [Fact]
    public void MeanGain_IsTheTwoSidesTogether()
    {
        var reading = new StereoBranchReading(2.5, true, -0.1, 0.9);

        Assert.Equal(0.4, reading.MeanGainDb, 3);
    }
}
