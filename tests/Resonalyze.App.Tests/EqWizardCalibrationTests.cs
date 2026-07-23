using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardCalibrationTests
{
    [Fact]
    public void ToMicrophoneMode_MapsEachEffectiveMode()
    {
        Assert.Equal(
            MicrophoneCalibrationMode.Off,
            EqWizardCalibration.ToMicrophoneMode(EqWizardCalibrationMode.Off));
        Assert.Equal(
            MicrophoneCalibrationMode.Degrees0,
            EqWizardCalibration.ToMicrophoneMode(EqWizardCalibrationMode.Degrees0));
        Assert.Equal(
            MicrophoneCalibrationMode.Degrees90,
            EqWizardCalibration.ToMicrophoneMode(EqWizardCalibrationMode.Degrees90));
        // "Own" applies the correction stored on the imported curve, not a file-backed
        // measurement profile, so at the measurement layer it is Off.
        Assert.Equal(
            MicrophoneCalibrationMode.Off,
            EqWizardCalibration.ToMicrophoneMode(EqWizardCalibrationMode.Own));
    }

    [Fact]
    public void UpdatedIrPreference_KeepsPreferenceWhenACurveForcesOwn()
    {
        // The reported regression: the user's 90° impulse-response preference must survive
        // loading a raw RTA overlay, which forces the effective mode to Own.
        MicrophoneCalibrationMode next = EqWizardCalibration.UpdatedIrPreference(
            current: MicrophoneCalibrationMode.Degrees90,
            loadedKind: EqWizardSourceKind.OverlaySlot,
            chosen: EqWizardCalibrationMode.Own);

        Assert.Equal(MicrophoneCalibrationMode.Degrees90, next);
    }

    [Fact]
    public void UpdatedIrPreference_KeepsPreferenceWhenATextCurveForcesOff()
    {
        // A text curve carries no re-smoothable reference, so it forces Off; that must not
        // erase the impulse-response preference either.
        MicrophoneCalibrationMode next = EqWizardCalibration.UpdatedIrPreference(
            current: MicrophoneCalibrationMode.Degrees0,
            loadedKind: EqWizardSourceKind.TextCurve,
            chosen: EqWizardCalibrationMode.Off);

        Assert.Equal(MicrophoneCalibrationMode.Degrees0, next);
    }

    [Fact]
    public void UpdatedIrPreference_AdoptsAFileBackedChoiceMadeAgainstAnImpulseResponse()
    {
        // Choosing 90° while an impulse response (or nothing) is loaded IS a standing
        // preference and must be remembered.
        Assert.Equal(
            MicrophoneCalibrationMode.Degrees90,
            EqWizardCalibration.UpdatedIrPreference(
                MicrophoneCalibrationMode.Off,
                EqWizardSourceKind.ImpulseResponse,
                EqWizardCalibrationMode.Degrees90));
        Assert.Equal(
            MicrophoneCalibrationMode.Degrees0,
            EqWizardCalibration.UpdatedIrPreference(
                MicrophoneCalibrationMode.Off,
                loadedKind: null,
                EqWizardCalibrationMode.Degrees0));
    }
}
