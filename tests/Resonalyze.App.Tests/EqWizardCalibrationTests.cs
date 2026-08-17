using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardCalibrationTests
{
    [Fact]
    public void MicrophoneCalibrationId_MapsEachEffectiveChoice()
    {
        Assert.Null(EqWizardCalibrationChoice.Off.MicrophoneCalibrationId);
        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            EqWizardCalibrationChoice
                .Microphone(MicrophoneCalibrationIds.ZeroDegrees)
                .MicrophoneCalibrationId);
        // "Own" applies the correction stored on the imported curve, not a configured
        // calibration, so at the measurement layer it is Off.
        Assert.Null(EqWizardCalibrationChoice.OwnCapture.MicrophoneCalibrationId);
        Assert.False(EqWizardCalibrationChoice.OwnCapture.IsOff);
        Assert.True(EqWizardCalibrationChoice.Microphone("   ").IsOff);
    }

    [Fact]
    public void UpdatedIrPreference_KeepsPreferenceWhenACurveForcesOwn()
    {
        // The reported regression: the user's impulse-response preference must survive
        // loading a raw RTA overlay, which forces the effective choice to Own.
        string? next = EqWizardCalibration.UpdatedIrPreference(
            current: "cal1",
            loadedKind: EqWizardSourceKind.OverlaySlot,
            chosen: EqWizardCalibrationChoice.OwnCapture);

        Assert.Equal("cal1", next);
    }

    [Fact]
    public void UpdatedIrPreference_KeepsPreferenceWhenATextCurveForcesOff()
    {
        // A text curve carries no re-smoothable reference, so it forces Off; that must not
        // erase the impulse-response preference either.
        string? next = EqWizardCalibration.UpdatedIrPreference(
            current: MicrophoneCalibrationIds.ZeroDegrees,
            loadedKind: EqWizardSourceKind.TextCurve,
            chosen: EqWizardCalibrationChoice.Off);

        Assert.Equal(MicrophoneCalibrationIds.ZeroDegrees, next);
    }

    [Fact]
    public void UpdatedIrPreference_AdoptsAChoiceMadeAgainstAnImpulseResponse()
    {
        // Choosing a calibration while an impulse response (or nothing) is loaded IS a
        // standing preference and must be remembered.
        Assert.Equal(
            "cal1",
            EqWizardCalibration.UpdatedIrPreference(
                null,
                EqWizardSourceKind.ImpulseResponse,
                EqWizardCalibrationChoice.Microphone("cal1")));
        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            EqWizardCalibration.UpdatedIrPreference(
                null,
                loadedKind: null,
                EqWizardCalibrationChoice.Microphone(MicrophoneCalibrationIds.ZeroDegrees)));
    }
}
