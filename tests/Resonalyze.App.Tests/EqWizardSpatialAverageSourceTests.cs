using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A stored spatial average offered to the EQ Wizard as a source. It rides the
/// IMPORTED-curve path, not the impulse-response one: it is a finished set of band
/// levels with a recipe, and what the wizard is then allowed to do to it — re-smooth
/// it, re-calibrate it, fit a bank to it — is decided entirely from the fields mapped
/// here.
/// </summary>
public sealed class EqWizardSpatialAverageSourceTests
{
    [Fact]
    public void TheCaptureArrivesAsAnImportedCurveOnItsOwnGrid()
    {
        LiveCaptureDocument document = Capture();

        EqWizardCurveSource source =
            EqWizardSourceResolver.CreateFromSpatialAverage(document, "described");

        Assert.Equal(EqWizardSourceKind.SpatialAverage, source.Kind);
        Assert.Equal("sub mmm", source.DisplayName);
        Assert.Equal(document.CurveDb.Length, source.Points.Count);
        Assert.Equal(document.GridStartHz, source.Points[0].X, 6);
        Assert.Equal(document.GridStopHz, source.Points[^1].X, 6);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, source.Scale);
        Assert.Equal(48_000, source.SampleRateHz);
        // No impulse response behind it, so nothing here is gated or previewed
        // through a window: the curve IS the measurement.
        Assert.Null(source.Measurement);
        Assert.False(source.IsGated);
        // And no coherence: one microphone, no reference. Auto Tune gates its boosts
        // on what remains rather than on a fabricated one.
        Assert.Null(source.Coherence);
    }

    /// <summary>
    /// The wizard may re-smooth it, because the capture was taken unsmoothed and is an
    /// analyzer input spectrum — so the re-smoothing is that analyzer's own second pass
    /// over its own band levels, not a near-enough substitute. That distinction matters
    /// because the result feeds Auto Tune.
    /// </summary>
    [Fact]
    public void TheCaptureIsRESmoothableBecauseItWasTakenUnsmoothed()
    {
        LiveCaptureDocument document = Capture();

        EqWizardCurveSource source =
            EqWizardSourceResolver.CreateFromSpatialAverage(document, "described");

        Assert.Equal(0, source.CapturedSmoothingCode);
        Assert.Equal(AnalysisCurveKind.InputSpectrum, source.CurveKind);
        Assert.True(source.SupportsSmoothing);
    }

    /// <summary>
    /// The correction travels frozen on the drawn points, which is what lets the
    /// calibration selector switch exactly: these corrections are additive per
    /// frequency, so removing one and applying another loses nothing.
    /// </summary>
    [Fact]
    public void ACapturedCalibrationTravelsWithThePointsAndCanBeSwitched()
    {
        LiveCaptureDocument document = Capture();
        document.CalibrationCorrectionDb =
            document.CurveDb.Select((_, i) => 0.01 * i).ToArray();

        EqWizardCurveSource source =
            EqWizardSourceResolver.CreateFromSpatialAverage(document, "described");

        Assert.True(source.HasOwnCalibration);
        Assert.True(source.SupportsCalibration);
        Assert.Equal(source.Points.Count, source.PointsCalibrationCorrectionDb.Count);

        // Undoing it is exact: rendering with the captured correction removed and none
        // applied gives back the uncalibrated level the analyzer measured.
        IReadOnlyList<SignalPoint> uncalibrated = EqWizardImportedCurve.Render(
            source.Points,
            source.PointsCalibrationCorrectionDb,
            targetCorrectionDb: [],
            smoothingCode: 0);
        for (int i = 0; i < uncalibrated.Count; i++)
        {
            Assert.Equal(source.Points[i].Y + 0.01 * i, uncalibrated[i].Y, 9);
        }
    }

    /// <summary>
    /// A capture taken without calibration cannot be re-calibrated, because the numbers
    /// carry no correction to undo — the selector says so rather than doubling one.
    /// </summary>
    [Fact]
    public void ACaptureWithoutACalibrationDoesNotOfferTheSelector()
    {
        EqWizardCurveSource source =
            EqWizardSourceResolver.CreateFromSpatialAverage(Capture(), "described");

        Assert.Empty(source.PointsCalibrationCorrectionDb);
        Assert.False(source.HasOwnCalibration);
        Assert.False(source.SupportsCalibration);
    }

    /// <summary>
    /// Where the protective high-pass took the signal below what could be recovered the
    /// capture says NaN, and that has to reach the fitter as "do not equalize here"
    /// rather than as a level. Nothing on the way may fill or bridge it.
    /// </summary>
    [Fact]
    public void TheGapUnderAProtectiveHighPassSurvivesIntoTheFitAndBlocksBandsThere()
    {
        LiveCaptureDocument document = Capture();
        // A tweeter's real shape: nothing below the corner, and above it a peak worth
        // equalizing — a flat curve would need no bands and prove nothing.
        for (int i = 0; i < document.CurveDb.Length; i++)
        {
            double hz = document.FrequencyAt(i);
            document.CurveDb[i] = hz < 500
                ? double.NaN
                : -30 + 9 * Math.Exp(-Math.Pow(Math.Log2(hz / 4_000) / 0.4, 2));
        }

        EqWizardCurveSource source =
            EqWizardSourceResolver.CreateFromSpatialAverage(document, "described");

        Assert.Contains(source.Points, point => double.IsNaN(point.Y));
        Assert.All(
            source.Points.Where(point => point.X < 400),
            point => Assert.True(double.IsNaN(point.Y)));

        // And the fitter honours it: aiming at a flat target far above the curve, every
        // band it places sits above the corner.
        var target = new List<SignalPoint>();
        for (int i = 0; i < 200; i++)
        {
            target.Add(new SignalPoint(20 * Math.Pow(10, 3.0 * i / 199), 0));
        }

        EqualizationCurve bank = EqAutoTuner.Tune(
            source.Points,
            target,
            new EqAutoTuner.Options { SampleRateHz = 48_000, MaxBands = 6 });

        Assert.NotEmpty(bank.Bands);
        Assert.All(bank.Bands, band => Assert.True(
            band.FrequencyHz >= 400,
            $"a band landed at {band.FrequencyHz:0} Hz, where the capture has no data"));
    }

    private static LiveCaptureDocument Capture()
    {
        var curve = new double[1_024];
        for (int i = 0; i < curve.Length; i++)
        {
            curve[i] = -30 + 3 * Math.Sin(i / 40.0);
        }

        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "sub mmm",
            Method = SpatialAverageMethod.MovingMic,
            CurveDb = curve,
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                SampleRateHz = 48_000,
                MagnitudeScale = MagnitudeScale.SoundPressureLevel,
                SmoothingCode = 0,
                IntegratedSeconds = 42
            }
        };
    }
}
