using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The angular estimate must reproduce the GRAS table exactly where the table
/// applies — same diameter, tabulated angle — and interpolate between those
/// points without inventing anything: no smoothing, no polynomial above the
/// tabulated range, and a spread that states how far the reference
/// constructions disagree.
/// </summary>
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
        // One inch with its grid is the only 25.4 mm variant, so the estimate is
        // that curve alone and must equal the difference of its own columns.
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

        // 45° sits between the 30° and 60° nodes at u = 1 - cos(45°).
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
        // 25.4 mm with the grid removed has no reference of its own: the only
        // no-grid variants are smaller, and the nearest is the half-inch one, so
        // the estimate reads it at twice the frequency (equal ka).
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
        // A 60 mm front runs the smallest reference (140 kHz, 3.175 mm) out of
        // table at 7.4 kHz; beyond that the estimate must HOLD its last value
        // rather than continue a diffraction curve it has no data for.
        MicrophoneAngleEstimate estimate = MicrophoneAngleModel.Estimate(
            new MicrophoneAngleRequest(90, 60.0));

        double top = estimate.HighestSupportedFrequencyHz;
        Assert.InRange(top, 1_000.0, 20_000.0);
        Assert.Equal(estimate.DeltaDb(top), estimate.DeltaDb(20_000), precision: 12);
    }

    [Fact]
    public void WhereAReferenceRunsOut_TheCurveHoldsInsteadOfSwitchingFamilies()
    {
        // The one-inch table ends at 18 kHz. Reading the rest of the band from a
        // quarter-inch reference instead — the same ka, a different construction
        // — stepped the correction by ~9 dB mid-curve and left the estimate
        // naming references the top of the band never used.
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
        // Two sizes are blended by log-diameter rather than pooled, so an
        // infinitesimal change of the stated diameter cannot change the answer
        // by three quarters of a decibel.
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

    [Fact]
    public void BetweenTwoSizes_TheBlendFollowsTheLogarithmOfTheDiameter()
    {
        const double frequency = 12_000;
        const double lower = 6.35;
        const double upper = 12.7;
        // The geometric mean of the two sizes sits halfway along log-diameter.
        double target = Math.Sqrt(lower * upper);

        // Each size is read on the target's OWN scaled frequency axis — that is
        // what keeps ka equal — so the two halves of the blend are taken at
        // different reference frequencies, not at the same one.
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
        // The audition FIR samples the correction up to Nyquist, so the power
        // law would otherwise be continued to -13 dB at 48 kHz and -18 dB at
        // 96 kHz on nothing but arithmetic.
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

    // A diffraction curve moves smoothly; a step between neighbouring points a
    // fiftieth of an octave apart is the model changing its mind about which
    // reference to read, not the microphone doing anything.
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

        // Its own fit, to the accuracy stated for it, and no uncertainty at 90°:
        // that difference is measured on the microphone rather than modelled.
        MicrophoneAngleBounds bounds = estimate.Deltas(20_000);
        Assert.Equal(-7.49, bounds.CenterDb, precision: 2);
        Assert.Equal(bounds.CenterDb, bounds.LowerDb, precision: 9);
        Assert.Equal(bounds.CenterDb, bounds.UpperDb, precision: 9);
        // The two measured units showed no angular change below the fit's knee.
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
