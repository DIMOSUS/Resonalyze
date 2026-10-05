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
    public void Quantize_ReReadsTheBetterOfTheTwoNeighbouringDspTicks()
    {
        // The scan's grid lands on 0.225 ms; the optimum sits at 0.229, so 0.23 is the tick to play — and the
        // reading's gains are those of 0.23, not of the point the processor cannot reach.
        Func<bool, double, bool, double> score = Scorer(
            referenceBase: -1.0, farBase: -2.0, bestDeltaMs: 0.229,
            referencePeak: -0.9, farPeak: -1.0);
        var scanned = new StereoBranchReading(0.225, true, 0.09, 0.99);

        StereoBranchReading quantized = StereoJunctionBranch.Quantize(scanned, score);

        Assert.Equal(0.23, quantized.DeltaMs);
        Assert.True(quantized.Flip);
        Assert.Equal(score(true, 0.23, true) - -2.0, quantized.FarGainDb, 9);
        Assert.Equal(score(false, 0.23, true) - -1.0, quantized.ReferenceGainDb, 9);
    }

    [Fact]
    public void Quantize_PrefersTheTickTheReferenceSideCanLiveWith()
    {
        // Both ticks straddle the optimum; the lower one costs the reference side more than it may pay, the
        // upper one does not, and the upper one is taken even though the far side likes the lower one more.
        static double Score(bool farSide, double deltaMs, bool flip)
        {
            if (!flip)
            {
                return farSide ? -2.0 : -1.0;
            }

            return farSide
                ? -1.0 - 0.5 * Math.Abs(deltaMs - 0.22)
                : (deltaMs < 0.225 ? -1.3 : -1.05);
        }

        StereoBranchReading quantized = StereoJunctionBranch.Quantize(
            new StereoBranchReading(0.225, true, 0, 0), Score);

        Assert.Equal(0.23, quantized.DeltaMs);
    }

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
    public void Read_AskedForAWholePeriod_ProbesTheSameRelationAPeriodOff()
    {
        // The far side sums best with the stack a whole period later and unflipped; a flip offers nothing.
        static double Score(bool farSide, double deltaMs, bool flip) =>
            flip ? -3.0
            : farSide ? -2.0 + Math.Max(0, 1.0 - Math.Abs(deltaMs - (2 * HalfPeriodMs)))
            : -1.0;

        StereoBranchReading reading = StereoJunctionBranch.Read(Score, HalfPeriodMs, wholePeriod: true)!;

        Assert.False(reading.Flip);
        Assert.InRange(reading.DeltaMs, (2 * HalfPeriodMs) - 0.05, (2 * HalfPeriodMs) + 0.05);
        Assert.InRange(reading.FarGainDb, 0.95, 1.0);
        Assert.Equal(0.0, reading.ReferenceGainDb, 6);
    }

    [Theory]
    // The far side comes into step and the reference barely moves: the cell of the field case.
    [InlineData(0.36, 0.76, 0.73, 0.68, true)]
    // The far side gains too little, or does not reach a coherent read.
    [InlineData(0.50, 0.70, 0.73, 0.73, false)]
    [InlineData(0.20, 0.55, 0.73, 0.73, false)]
    // The reference side falls out of step.
    [InlineData(0.36, 0.76, 0.80, 0.50, false)]
    public void WavefrontsBack_NeedsTheFarSideInStepAndTheReferenceStillThere(
        double farBefore, double farAfter, double referenceBefore, double referenceAfter, bool expected)
    {
        Assert.Equal(
            expected,
            StereoJunctionBranch.WavefrontsBack(farBefore, farAfter, referenceBefore, referenceAfter));
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
