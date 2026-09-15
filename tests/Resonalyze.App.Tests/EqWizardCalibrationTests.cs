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
        // "Own" applies the correction stored on the imported curve, so at the measurement layer it is Off.
        Assert.Null(EqWizardCalibrationChoice.OwnCapture.MicrophoneCalibrationId);
        Assert.False(EqWizardCalibrationChoice.OwnCapture.IsOff);
        Assert.True(EqWizardCalibrationChoice.Microphone("   ").IsOff);
        // Pinned applies the source's own curve, which may be absent from the list.
        Assert.Null(EqWizardCalibrationChoice.PinnedToSource.MicrophoneCalibrationId);
        Assert.False(EqWizardCalibrationChoice.PinnedToSource.IsOff);
        Assert.True(EqWizardCalibrationChoice.PinnedToSource.Pinned);
        Assert.NotEqual(EqWizardCalibrationChoice.PinnedToSource, EqWizardCalibrationChoice.OwnCapture);
        Assert.NotEqual(EqWizardCalibrationChoice.PinnedToSource, EqWizardCalibrationChoice.Off);
    }

    [Fact]
    public void UpdatedIrPreference_KeepsPreferenceWhenAVirtualDspChannelIsPinned()
    {
        string? next = EqWizardCalibration.UpdatedIrPreference(
            current: "cal1",
            loadedKind: EqWizardSourceKind.VirtualDspChannel,
            chosen: EqWizardCalibrationChoice.PinnedToSource);

        Assert.Equal("cal1", next);
    }

    [Fact]
    public void UpdatedIrPreference_KeepsPreferenceWhenACurveForcesOwn()
    {
        string? next = EqWizardCalibration.UpdatedIrPreference(
            current: "cal1",
            loadedKind: EqWizardSourceKind.OverlaySlot,
            chosen: EqWizardCalibrationChoice.OwnCapture);

        Assert.Equal("cal1", next);
    }

    [Fact]
    public void UpdatedIrPreference_KeepsPreferenceWhenATextCurveForcesOff()
    {
        string? next = EqWizardCalibration.UpdatedIrPreference(
            current: MicrophoneCalibrationIds.ZeroDegrees,
            loadedKind: EqWizardSourceKind.TextCurve,
            chosen: EqWizardCalibrationChoice.Off);

        Assert.Equal(MicrophoneCalibrationIds.ZeroDegrees, next);
    }

    [Fact]
    public void UpdatedIrPreference_SurvivesAVirtualDspHandoff()
    {
        // A handoff pin is the DSP project's choice, not the user's standing IR preference.
        string? next = EqWizardCalibration.UpdatedIrPreference(
            current: "cal1",
            loadedKind: EqWizardSourceKind.VirtualDspChannel,
            chosen: EqWizardCalibrationChoice.Microphone("dsp-cal"));

        Assert.Equal("cal1", next);
    }

    [Fact]
    public void UpdatedIrPreference_AdoptsAChoiceMadeAgainstAnImpulseResponse()
    {
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
