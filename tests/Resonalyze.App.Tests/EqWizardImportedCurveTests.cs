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

        // A third of an octave spans plenty of these steps, so the alternation averages
        // out rather than leaving a residue that depends on the window landing odd or even.
        IReadOnlyList<SignalPoint> result = EqWizardImportedCurve.Render(
            points, Array.Empty<double>(), Array.Empty<double>(), 3);

        Assert.Equal(points.Select(point => point.X), result.Select(point => point.X));

        // The jagged 10 dB peak-to-peak collapses to a nearly flat line...
        double[] band = result.Skip(20).Take(160).Select(point => point.Y).ToArray();
        double swing = band.Max() - band.Min();
        Assert.True(swing < 1.0, $"Ripple only fell to a {swing:0.0} dB swing.");
        // ...which settles ABOVE the arithmetic dB midpoint, because the average is taken
        // over linear power like the analyzer's: the loud half of the ripple carries far
        // more energy than the quiet half. A dB mean would land on 80.
        Assert.All(band, value => Assert.InRange(value, 80.0, 85.0));
        Assert.True(
            band.Average() > 81.0,
            $"Levelled at {band.Average():0.0} dB — that is a decibel mean, not a power one.");
    }

    [Fact]
    public void Render_SmoothingIsTheAnalyzersOwnAndNotADecibelMean()
    {
        // Averaging dB values is a GEOMETRIC mean: it pulls a narrow peak down much harder
        // than the analyzer, which averages linear band POWER. A curve smoothed that way
        // feeds Auto Tune a peak the measurement never had at this width.
        //
        // One 20 dB spike on an otherwise flat 80 dB curve. The power mean must keep
        // clearly more of it than the dB mean of the same window would.
        var points = new List<SignalPoint>();
        for (int i = 0; i < 121; i++)
        {
            double f = 100 * Math.Pow(2, i / 20.0); // 100 Hz .. 6.4 kHz, 1/20 oct steps
            points.Add(new SignalPoint(f, i == 60 ? 100 : 80));
        }

        double peak = EqWizardImportedCurve.Render(
            points, Array.Empty<double>(), Array.Empty<double>(), 1)[60].Y;

        // A one-octave window here spans 21 points, so a dB mean would read
        // (20 * 80 + 100) / 21 ≈ 81.0 dB, while the power mean keeps ~10*log10((20 + 100)/21)
        // above the floor ≈ 87.6 dB.
        Assert.True(
            peak > 86.0,
            $"Peak read {peak:0.0} dB — that is a decibel mean, not the analyzer's power one.");
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
