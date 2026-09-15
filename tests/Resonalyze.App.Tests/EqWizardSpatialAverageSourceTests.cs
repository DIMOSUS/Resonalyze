using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>A stored spatial average rides the imported-curve path: finished band levels with a recipe.</summary>
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
        Assert.Null(source.Measurement);
        Assert.False(source.IsGated);
        // No reference, so no coherence: Auto Tune gates boosts on what remains.
        Assert.Null(source.Coherence);
    }

    /// <summary>Re-smoothing is the analyzer's own second pass over unsmoothed band levels, so it is exact.</summary>
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

    /// <summary>Corrections are additive per frequency, so the frozen one can be switched exactly.</summary>
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

    [Fact]
    public void ACaptureWithoutACalibrationDoesNotOfferTheSelector()
    {
        EqWizardCurveSource source =
            EqWizardSourceResolver.CreateFromSpatialAverage(Capture(), "described");

        Assert.Empty(source.PointsCalibrationCorrectionDb);
        Assert.False(source.HasOwnCalibration);
        Assert.False(source.SupportsCalibration);
    }

    /// <summary>NaN under a protective high-pass must reach the fitter as "do not equalize here".</summary>
    [Fact]
    public void TheGapUnderAProtectiveHighPassSurvivesIntoTheFitAndBlocksBandsThere()
    {
        LiveCaptureDocument document = Capture();
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
