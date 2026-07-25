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
        // The capture was taken through a profile that read +2/-1/+3 dB at these
        // frequencies; the user picks a different profile. The stored level is
        // (measured - captured), so the result must be (measured - chosen) — the first
        // profile fully undone, not stacked with the second.
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
        // Reproducing the captured calibration must return the stored curve bit for bit:
        // the correction is removed and the very same one applied again.
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
        // A jagged curve on a log grid: smoothing must reduce the swing while keeping the
        // exact same frequencies — a no-raw curve must never be resampled onto the display
        // range, which would invent bands the analyzer never resolved.
        var points = new List<SignalPoint>();
        for (int i = 0; i < 200; i++)
        {
            double f = 100 * Math.Pow(100, i / 199.0); // 100 Hz .. 10 kHz
            points.Add(new SignalPoint(f, 80 + (i % 2 == 0 ? 5 : -5)));
        }

        IReadOnlyList<SignalPoint> result = EqWizardImportedCurve.Render(
            points, Array.Empty<double>(), Array.Empty<double>(), 6);

        Assert.Equal(points.Select(point => point.X), result.Select(point => point.X));
        double worst = result
            .Skip(20)
            .Take(160)
            .Max(point => Math.Abs(point.Y - 80));
        Assert.True(worst < 2.0, $"Ripple only fell to {worst:0.0} dB.");
    }

    [Fact]
    public void Render_KeepsUnmeasuredBandsAsGaps()
    {
        // A NaN marks a band the analyzer could not measure. It must survive every step:
        // filling it would invent data for the fitter to correct.
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
        // A correction of a different length is not aligned to these frequencies, so
        // applying it would shift the curve against its own calibration.
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
