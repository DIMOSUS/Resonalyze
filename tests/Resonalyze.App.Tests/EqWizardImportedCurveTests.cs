using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardImportedCurveTests
{
    private static SignalPoint[] Curve(params (double X, double Y)[] values) =>
        values.Select(value => new SignalPoint(value.X, value.Y)).ToArray();

    [Fact]
    public void Render_WithNothingToDo_ReturnsTheInputUntouched()
    {
        SignalPoint[] points = Curve((100, 80), (1_000, 78), (10_000, 74));

        IReadOnlyList<SignalPoint> result = EqWizardImportedCurve.Render(
            points, Array.Empty<double>(), Array.Empty<double>(), 0);

        Assert.Same(points, result);
    }

    [Fact]
    public void Render_SwappingCalibration_ReplacesTheCapturedCorrectionExactly()
    {
        // Stored level is (measured - captured), so the result must be (measured - chosen).
        double[] captured = { 2, -1, 3 };
        double[] chosen = { -0.5, 4, 1 };
        SignalPoint[] points = Curve((100, 80), (1_000, 78), (10_000, 74));

        IReadOnlyList<SignalPoint> result =
            EqWizardImportedCurve.Render(points, captured, chosen, 0);

        for (int i = 0; i < points.Length; i++)
        {
            double measured = points[i].Y + captured[i];
            Assert.Equal(measured - chosen[i], result[i].Y, 9);
            Assert.Equal(points[i].X, result[i].X);
        }
    }

    [Fact]
    public void Render_OwnCalibration_IsALosslessRoundTrip()
    {
        double[] captured = { 2, -1, 3 };
        SignalPoint[] points = Curve((100, 80), (1_000, 78), (10_000, 74));

        IReadOnlyList<SignalPoint> result =
            EqWizardImportedCurve.Render(points, captured, captured, 0);

        Assert.All(
            result.Select((point, i) => (point, i)),
            entry => Assert.Equal(points[entry.i].Y, entry.point.Y, 12));
    }

    [Fact]
    public void Render_CalibrationOff_LeavesTheUncalibratedLevel()
    {
        double[] captured = { 2, -1, 3 };
        SignalPoint[] points = Curve((100, 80), (1_000, 78), (10_000, 74));

        IReadOnlyList<SignalPoint> result =
            EqWizardImportedCurve.Render(points, captured, Array.Empty<double>(), 0);

        Assert.Equal(82, result[0].Y, 9);
        Assert.Equal(77, result[1].Y, 9);
        Assert.Equal(77, result[2].Y, 9);
    }

    [Fact]
    public void Render_Smoothing_FlattensRippleOnTheCurvesOwnFrequencies()
    {
        // A no-raw curve must keep its frequencies, never be resampled onto the display range.
        var points = new List<SignalPoint>();
        for (int i = 0; i < 200; i++)
        {
            double f = 100 * Math.Pow(100, i / 199.0);
            points.Add(new SignalPoint(f, 80 + (i % 2 == 0 ? 5 : -5)));
        }

        IReadOnlyList<SignalPoint> result = EqWizardImportedCurve.Render(
            points, Array.Empty<double>(), Array.Empty<double>(), 3);

        Assert.Equal(points.Select(point => point.X), result.Select(point => point.X));

        double[] band = result.Skip(20).Take(160).Select(point => point.Y).ToArray();
        double swing = band.Max() - band.Min();
        Assert.True(swing < 1.0, $"Ripple only fell to a {swing:0.0} dB swing.");
        // Power averaging settles above the dB midpoint (a dB mean would land on 80).
        Assert.All(band, value => Assert.InRange(value, 80.0, 85.0));
        Assert.True(
            band.Average() > 81.0,
            $"Levelled at {band.Average():0.0} dB — that is a decibel mean, not a power one.");
    }

    [Fact]
    public void Render_SmoothingIsTheAnalyzersOwnAndNotADecibelMean()
    {
        // A dB mean is geometric and pulls a narrow peak down harder than the analyzer's band-power mean.
        var points = new List<SignalPoint>();
        for (int i = 0; i < 121; i++)
        {
            double f = 100 * Math.Pow(2, i / 20.0);
            points.Add(new SignalPoint(f, i == 60 ? 100 : 80));
        }

        double peak = EqWizardImportedCurve.Render(
            points, Array.Empty<double>(), Array.Empty<double>(), 1)[60].Y;

        // 21 points per octave: dB mean ≈ 81.0 dB, power mean ≈ 87.6 dB.
        Assert.True(
            peak > 86.0,
            $"Peak read {peak:0.0} dB — that is a decibel mean, not the analyzer's power one.");
    }

    [Fact]
    public void Render_KeepsUnmeasuredBandsAsGaps()
    {
        SignalPoint[] points = Curve((100, 80), (1_000, double.NaN), (10_000, 74));
        double[] captured = { 1, 1, 1 };

        IReadOnlyList<SignalPoint> result =
            EqWizardImportedCurve.Render(points, captured, Array.Empty<double>(), 6);

        Assert.True(double.IsNaN(result[1].Y));
        Assert.True(double.IsFinite(result[0].Y));
        Assert.True(double.IsFinite(result[2].Y));
    }

    [Fact]
    public void Render_IgnoresACorrectionThatDoesNotLineUpWithThePoints()
    {
        SignalPoint[] points = Curve((100, 80), (1_000, 78), (10_000, 74));

        IReadOnlyList<SignalPoint> result = EqWizardImportedCurve.Render(
            points, new double[] { 5, 5 }, Array.Empty<double>(), 0);

        Assert.Same(points, result);
    }

    [Fact]
    public void SampleCorrection_WithoutAProfile_MeansNoCorrection()
    {
        Assert.Empty(EqWizardImportedCurve.SampleCorrection(
            null, Curve((100, 80), (1_000, 78))));
    }
}
