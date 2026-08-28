using System.Numerics;
using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What a Virtual DSP handoff is corrected through once it reaches the wizard. Two
/// corrections travel with a channel and only one of them is a curve: the impulse
/// response's, and the MODE the panel read the spatial average through. The wizard
/// has to reproduce both — a channel it draws through a different correction than the
/// panel did is the one thing the handoff exists to prevent.
/// </summary>
public sealed class EqWizardHandoffCalibrationTests
{
    private const int SampleRate = 48_000;

    /// <summary>
    /// The field case: an impulse response measured before calibrations were stamped
    /// into files, with a moving-microphone capture beside it taken through the
    /// microphone's own 90° curve, and the panel on "Own (as measured)". The measured
    /// correction reaches 2.4 dB across the tweeter's band, so reading the capture
    /// uncalibrated is a different curve and a different tune.
    /// </summary>
    [Fact]
    public void ACaptureKeepsItsOwnCorrectionWhenTheMeasurementNamesNoFile()
    {
        using var panel = new EqWizardPanel();
        EqWizardCurveSource source = Handoff(
            pinnedCalibration: null,
            SpatialAverageCalibration.Own);

        ApplySource(panel, source);

        Assert.Equal(
            SpatialAverageCalibration.Own,
            ResolvedSpatialAverageCalibration(panel, source));
        // And the disabled selector says so, in the panel's own words: the correction
        // is whatever that capture was recorded through, which no entry in the
        // wizard's list need name.
        Assert.Contains("Own (as measured)", CalibrationOptionNames(panel));
    }

    [Fact]
    public void ThePinnedCurveStillWinsWhenTheMeasurementCarriesOne()
    {
        using var panel = new EqWizardPanel();
        CalibrationFile curve = CalibrationFile.Parse("20 0\n20000 -1.5\n");
        EqWizardCurveSource source = Handoff(curve, SpatialAverageCalibration.Specific(curve));

        ApplySource(panel, source);

        Assert.Equal(
            SpatialAverageCalibrationMode.Specific,
            ResolvedSpatialAverageCalibration(panel, source).Mode);
        Assert.Contains("mic 90", CalibrationOptionNames(panel));
    }

    [Fact]
    public void APanelReadingTheCaptureUncalibratedIsReproducedAsOff()
    {
        using var panel = new EqWizardPanel();
        EqWizardCurveSource source = Handoff(
            pinnedCalibration: null,
            SpatialAverageCalibration.Off);

        ApplySource(panel, source);

        // Nothing is invented in the other direction either: the panel applied no
        // correction, and Off is what reproduces that exactly.
        Assert.Equal(
            SpatialAverageCalibration.Off,
            ResolvedSpatialAverageCalibration(panel, source));
    }

    private static EqWizardCurveSource Handoff(
        CalibrationFile? pinnedCalibration,
        SpatialAverageCalibration spatialAverageCalibration)
    {
        var response = new Complex[16_384];
        for (int i = 0; i < 96; i++)
        {
            response[600 + i] = Math.Exp(-i / 20.0) * Math.Cos(2 * Math.PI * i / 24.0);
        }

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "Ch D · R (MMM)",
            Description = "Spatial average, as measured with the DSP bypassed.",
            Measurement = new ImpulseMeasurementView(response, 600, SampleRate),
            PreviewImpulseResponse = response,
            PreviewChain = DspChannelChain.Identity,
            PinnedCalibration = pinnedCalibration,
            PinnedCalibrationName = pinnedCalibration == null ? null : "mic 90",
            SpatialAverage = Capture(),
            SpatialAverageCalibration = spatialAverageCalibration,
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };
    }

    private static LiveCaptureDocument Capture()
    {
        var curve = new double[1_024];
        for (int i = 0; i < curve.Length; i++)
        {
            curve[i] = -40 + 2 * Math.Sin(i / 50.0);
        }

        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "r tw mmm",
            Method = SpatialAverageMethod.MovingMic,
            CurveDb = curve,
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                SampleRateHz = SampleRate,
                MagnitudeScale = MagnitudeScale.SoundPressureLevel,
                SmoothingCode = 0,
                IntegratedSeconds = 42
            }
        };
    }

    private static void ApplySource(EqWizardPanel panel, EqWizardCurveSource source) =>
        typeof(EqWizardPanel)
            .GetMethod("ApplySource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [source]);

    private static SpatialAverageCalibration ResolvedSpatialAverageCalibration(
        EqWizardPanel panel,
        EqWizardCurveSource source) =>
        (SpatialAverageCalibration)typeof(EqWizardPanel)
            .GetMethod(
                "ResolveSpatialAverageCalibration",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [source])!;

    private static IReadOnlyList<string> CalibrationOptionNames(EqWizardPanel panel) =>
        ((System.Collections.IEnumerable)typeof(EqWizardPanel)
            .GetMethod(
                "BuildCalibrationOptions",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [])!)
        .Cast<object>()
        // The option renders itself into the combo through ToString.
        .Select(option => option.ToString()!)
        .ToList();
}
