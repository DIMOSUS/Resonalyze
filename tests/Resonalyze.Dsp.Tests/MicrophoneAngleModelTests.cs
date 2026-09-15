using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>Reproduces the GRAS table where it applies and interpolates without smoothing or extrapolation; spread = disagreement of references.</summary>
public sealed class MicrophoneAngleModelTests
{
    private const double HalfInchMm = 12.7;
    private const double OneInchMm = 25.4;

    [Fact]
    public void OnAxis_IsExactlyZeroEverywhere()
    {
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(0, HalfInchMm));

        foreach (double frequency in new[] { 20.0, 1000.0, 10_000.0, 20_000.0 })
        {
            MicrophoneAngleBounds bounds = estimate.Deltas(frequency);
            Assert.Equal(0.0, bounds.CenterDb);
            Assert.Equal(0.0, bounds.LowerDb);
            Assert.Equal(0.0, bounds.UpperDb);
        }
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(90.0)]
    public void AtTheReferenceDiameter_TabulatedAnglesReproduceTheTableDifference(
        double angleDegrees)
    {
        GrasReferenceCurve reference = Single(OneInchMm, MicrophoneProtectionGrid.Fitted);
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(
                angleDegrees,
                OneInchMm,
                MicrophoneProtectionGrid.Fitted));

        foreach (double frequency in new[] { 800.0, 3150.0, 10_000.0, 16_000.0 })
        {
            Assert.True(reference.TryGetAngleDeltas(frequency, out GrasAngleDeltas deltas));
            double expected = angleDegrees switch
            {
                30.0 => deltas.At30,
                60.0 => deltas.At60,
                _ => deltas.At90
            };
            Assert.Equal(expected, estimate.DeltaDb(frequency), precision: 9);
        }
    }

    [Fact]
    public void BetweenTabulatedAngles_InterpolatesPiecewiseLinearlyInOneMinusCosine()
    {
        GrasReferenceCurve reference = Single(OneInchMm, MicrophoneProtectionGrid.Fitted);
        Assert.True(reference.TryGetAngleDeltas(10_000, out GrasAngleDeltas deltas));

        double u = 1.0 - Math.Cos(45.0 * Math.PI / 180.0);
        const double u30 = 1.0 - 0.86602540378443865;
        double position = (u - u30) / (0.5 - u30);
        double expected = deltas.At30 + (deltas.At60 - deltas.At30) * position;

        double actual = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(45, OneInchMm, MicrophoneProtectionGrid.Fitted))
            .DeltaDb(10_000);

        Assert.Equal(expected, actual, precision: 9);
    }

    [Fact]
    public void ADifferentDiameter_ScalesTheReferenceFrequencyByTheDiameterRatio()
    {
        // 25.4 mm without grid has no reference: read the half-inch one at twice the frequency (equal ka).
        GrasReferenceCurve reference = Single(HalfInchMm, MicrophoneProtectionGrid.Removed);
        Assert.True(reference.TryGetAngleDeltas(9_000, out GrasAngleDeltas deltas));

        double actual = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(
                90,
                OneInchMm,
                MicrophoneProtectionGrid.Removed))
            .DeltaDb(4_500);

        Assert.Equal(deltas.At90, actual, precision: 9);
    }

    [Fact]
    public void WithSeveralCandidates_TheEstimateIsTheirMedianAndTheBoundsTheirSpread()
    {
        const double frequency = 16_000;
        List<double> candidates = GrasFreeFieldCorrections.Curves
            .Where(curve => curve.DiameterMm == HalfInchMm)
            .Select(curve =>
            {
                Assert.True(curve.TryGetAngleDeltas(frequency, out GrasAngleDeltas deltas));
                return deltas.At90;
            })
            .OrderBy(delta => delta)
            .ToList();

        MicrophoneAngleBounds bounds = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, HalfInchMm))
            .Deltas(frequency);

        Assert.True(candidates.Count >= 3);
        int middle = candidates.Count / 2;
        double median = candidates.Count % 2 == 1
            ? candidates[middle]
            : (candidates[middle - 1] + candidates[middle]) / 2.0;
        Assert.Equal(median, bounds.CenterDb, precision: 9);
        Assert.Equal(candidates[0], bounds.LowerDb, precision: 9);
        Assert.Equal(candidates[^1], bounds.UpperDb, precision: 9);
    }

    [Fact]
    public void BelowTheTabulatedRange_TheAngleMakesNoDifference()
    {
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(90, HalfInchMm));

        Assert.Equal(0.0, estimate.DeltaDb(100), precision: 12);
        Assert.Equal(0.0, estimate.DeltaDb(20), precision: 12);
    }

    [Fact]
    public void AboveEveryReference_TheEstimateHoldsInsteadOfExtrapolating()
    {
        // Beyond the smallest reference's table (7.4 kHz for 60 mm) the estimate holds its last value.
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(90, 60.0));

        double top = estimate.HighestSupportedFrequencyHz;
        Assert.InRange(top, 1_000.0, 20_000.0);
        Assert.Equal(estimate.DeltaDb(top), estimate.DeltaDb(20_000), precision: 12);
    }

    [Fact]
    public void WhereAReferenceRunsOut_TheCurveHoldsInsteadOfSwitchingFamilies()
    {
        // Switching to a quarter-inch reference past 18 kHz stepped the correction ~9 dB.
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(90, OneInchMm, MicrophoneProtectionGrid.Fitted));

        Assert.Equal(18_000.0, estimate.HighestSupportedFrequencyHz);
        Assert.Equal(
            estimate.DeltaDb(18_000),
            estimate.DeltaDb(20_000),
            precision: 12);
        Assert.Equal(
            Single(OneInchMm, MicrophoneProtectionGrid.Fitted).Label,
            Assert.Single(estimate.References));
        AssertNoStepsWiderThan(estimate, 0.5);
    }

    [Fact]
    public void ADiameterJustPastATabulatedSize_ReadsAlmostLikeThatSize()
    {
        // Sizes blend by log-diameter, so an infinitesimal diameter change cannot jump the answer.
        double atSize = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, 6.35, MicrophoneProtectionGrid.Fitted))
            .DeltaDb(18_000);
        double justPast = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(
                90,
                6.3500001,
                MicrophoneProtectionGrid.Fitted))
            .DeltaDb(18_000);

        Assert.Equal(atSize, justPast, precision: 4);
    }

    [Theory]
    [InlineData(12_000)]
    [InlineData(16_000)]
    [InlineData(18_000)]
    [InlineData(20_000)]
    public void ADiameterJustBelowATabulatedSize_ReadsAlmostLikeThatSize(double frequencyHz)
    {
        // A shared limit let the half-inch references cut the one-inch one short: 12 dB step over 0.01 mm.
        double justBelow = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, 25.39, MicrophoneProtectionGrid.Fitted))
            .DeltaDb(frequencyHz);
        double atSize = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, 25.40, MicrophoneProtectionGrid.Fitted))
            .DeltaDb(frequencyHz);

        Assert.InRange(Math.Abs(justBelow - atSize), 0.0, 0.05);
    }

    [Theory]
    [InlineData(12_000, MicrophoneProtectionGrid.Fitted)]
    [InlineData(18_000, MicrophoneProtectionGrid.Fitted)]
    [InlineData(20_000, MicrophoneProtectionGrid.Unknown)]
    [InlineData(20_000, MicrophoneProtectionGrid.Removed)]
    public void TheEstimateMovesSmoothlyWithTheStatedDiameter(
        double frequencyHz,
        MicrophoneProtectionGrid grid)
    {
        double previous = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, 3.0, grid))
            .DeltaDb(frequencyHz);
        for (int step = 1; step <= 2700; step++)
        {
            double diameterMm = 3.0 + step * 0.01;
            double current = MicrophoneAngleModel
                .Estimate(new MicrophoneAngleRequest(90, diameterMm, grid))
                .DeltaDb(frequencyHz);
            Assert.InRange(Math.Abs(current - previous), 0.0, 0.1);
            previous = current;
        }
    }

    [Fact]
    public void BetweenTwoSizes_TheBlendFollowsTheLogarithmOfTheDiameter()
    {
        const double frequency = 12_000;
        const double lower = 6.35;
        const double upper = 12.7;
        double target = Math.Sqrt(lower * upper);

        // Each size is read on the target's own scaled frequency axis (equal ka).
        double expected =
            0.5 * GroupMedianAt90(lower, MicrophoneProtectionGrid.Fitted, frequency * target / lower) +
            0.5 * GroupMedianAt90(upper, MicrophoneProtectionGrid.Fitted, frequency * target / upper);

        double actual = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, target, MicrophoneProtectionGrid.Fitted))
            .DeltaDb(frequency);

        Assert.Equal(expected, actual, precision: 9);
    }

    [Fact]
    public void SonarworksModel_HoldsAboveTheBandItWasFittedOver()
    {
        // The audition FIR samples to Nyquist; the power law would otherwise extrapolate to -13/-18 dB.
        double atTop = MicrophoneAngleModel.SonarworksXref20Delta90Db(20_000);

        Assert.Equal(atTop, MicrophoneAngleModel.SonarworksXref20Delta90Db(48_000));
        Assert.Equal(atTop, MicrophoneAngleModel.SonarworksXref20Delta90Db(96_000));

        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(
                90,
                MicrophoneAngleModel.SonarworksXref20DiameterMm,
                MicrophoneProtectionGrid.Unknown,
                MicrophoneAngleReference.SonarworksXref20));
        Assert.Equal(
            MicrophoneAngleModel.SonarworksXref20HighestFittedHz,
            estimate.HighestSupportedFrequencyHz);
        Assert.Equal(estimate.DeltaDb(20_000), estimate.DeltaDb(96_000), precision: 12);
    }

    [Theory]
    [InlineData(12.7, MicrophoneProtectionGrid.Unknown)]
    [InlineData(9.0, MicrophoneProtectionGrid.Removed)]
    [InlineData(25.4, MicrophoneProtectionGrid.Fitted)]
    [InlineData(60.0, MicrophoneProtectionGrid.Unknown)]
    public void TheCurveHasNoStepsAcrossTheAudioBand(
        double diameterMm,
        MicrophoneProtectionGrid grid)
    {
        AssertNoStepsWiderThan(
            MicrophoneAngleModel.Estimate(
                new MicrophoneAngleRequest(90, diameterMm, grid)),
            0.5);
    }

    private static void AssertNoStepsWiderThan(
        MicrophoneAngleEstimate estimate,
        double maximumStepDb)
    {
        const int points = 500;
        double previous = estimate.DeltaDb(20.0);
        for (int index = 1; index < points; index++)
        {
            double frequency = 20.0 * Math.Pow(1000.0, index / (double)(points - 1));
            double current = estimate.DeltaDb(frequency);
            Assert.InRange(Math.Abs(current - previous), 0.0, maximumStepDb);
            previous = current;
        }
    }

    [Fact]
    public void SonarworksModel_ReproducesItsMeasuredNinetyDegreeFit()
    {
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(
                90,
                MicrophoneAngleModel.SonarworksXref20DiameterMm,
                MicrophoneProtectionGrid.Unknown,
                MicrophoneAngleReference.SonarworksXref20));

        // No uncertainty at 90°: measured on the microphone, not modelled.
        MicrophoneAngleBounds bounds = estimate.Deltas(20_000);
        Assert.Equal(-7.49, bounds.CenterDb, precision: 2);
        Assert.Equal(bounds.CenterDb, bounds.LowerDb, precision: 9);
        Assert.Equal(bounds.CenterDb, bounds.UpperDb, precision: 9);
        Assert.Equal(0.0, estimate.DeltaDb(4_000));
    }

    [Fact]
    public void SonarworksModel_ScalesSmallerAnglesAndKeepsThemInsideTheSpread()
    {
        MicrophoneAngleBounds bounds = MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(
                30,
                MicrophoneAngleModel.SonarworksXref20DiameterMm,
                MicrophoneProtectionGrid.Unknown,
                MicrophoneAngleReference.SonarworksXref20))
            .Deltas(20_000);

        double delta90 = MicrophoneAngleModel.SonarworksXref20Delta90Db(20_000);
        // (1 - cos 30°)^0.85, i.e. 0.181 of the measured 90° difference.
        double factor = Math.Pow(1.0 - Math.Cos(30.0 * Math.PI / 180.0), 0.85);
        Assert.Equal(0.181, factor, precision: 3);
        Assert.Equal(factor * delta90, bounds.CenterDb, precision: 9);
        Assert.InRange(bounds.CenterDb, bounds.LowerDb, bounds.UpperDb);
        Assert.True(bounds.UpperDb - bounds.LowerDb > 0);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(90.1)]
    public void OutsideZeroToNinetyDegrees_TheModelRefuses(double angleDegrees)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MicrophoneAngleModel.Estimate(
                new MicrophoneAngleRequest(angleDegrees, HalfInchMm)));
    }

    [Fact]
    public void EmbeddedTable_CarriesTheReferenceFamiliesTheModelScalesFrom()
    {
        IReadOnlyList<double> diameters = GrasFreeFieldCorrections.Diameters;

        Assert.Equal([3.175, 6.35, 12.7, 25.4], diameters);
        Assert.All(
            GrasFreeFieldCorrections.Curves,
            curve => Assert.True(curve.MinFrequencyHz <= 500));
    }

    private static double GroupMedianAt90(
        double diameterMm,
        MicrophoneProtectionGrid grid,
        double referenceFrequencyHz)
    {
        List<double> deltas = GrasFreeFieldCorrections.Curves
            .Where(curve => curve.DiameterMm == diameterMm && curve.Grid == grid)
            .Select(curve =>
            {
                Assert.True(curve.TryGetAngleDeltas(referenceFrequencyHz, out GrasAngleDeltas d));
                return d.At90;
            })
            .OrderBy(delta => delta)
            .ToList();
        int middle = deltas.Count / 2;
        return deltas.Count % 2 == 1
            ? deltas[middle]
            : (deltas[middle - 1] + deltas[middle]) / 2.0;
    }

    private static GrasReferenceCurve Single(
        double diameterMm,
        MicrophoneProtectionGrid grid) =>
        Assert.Single(
            GrasFreeFieldCorrections.Curves,
            curve => curve.DiameterMm == diameterMm && curve.Grid == grid);
}
