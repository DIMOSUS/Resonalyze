using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class FrequencyResponseSettingsSessionTests
{
    [Fact]
    public void TheSplToolTipNamesTheRecordSettingsPanel()
    {
        string tip = FrequencyResponseSplChoice.ToolTip(new ModeSettingsMeasurement(null, 48_000));

        Assert.Contains("SPL calibration configured in Record Settings", tip, StringComparison.Ordinal);
        Assert.DoesNotContain("Measurement Options", tip, StringComparison.Ordinal);
    }

    [Fact]
    public void DbSplIsAmberOnlyWhenAMeasurementOnScreenHasNoAnchor()
    {
        var empty = new ModeSettingsMeasurement(null, 48_000);
        var bare = new ModeSettingsMeasurement(ModeSettingsWiringTests.Transfer(48_000, peak: 480), 48_000);

        Assert.False(FrequencyResponseSplChoice.ViewOnlyConflict(empty));
        Assert.True(FrequencyResponseSplChoice.ViewOnlyConflict(bare));
        Assert.StartsWith("Absolute dB SPL", FrequencyResponseSplChoice.ToolTip(bare), StringComparison.Ordinal);
        Assert.Contains("View-only", FrequencyResponseSplChoice.ToolTip(bare), StringComparison.Ordinal);
        Assert.Contains("No measurement yet", FrequencyResponseSplChoice.ToolTip(empty), StringComparison.Ordinal);
    }

    [Fact]
    public void WhatLoadsIsWhatWrites_WithTheFieldsNormalizations()
    {
        var session = new FrequencyResponseSettingsSession();
        var options = new FrequencyResponseOptions
        {
            Window = 99_999,
            LeftTukeyWindow = 40_000,
            RightTukeyWindow = 10,
            MagnitudeWindowMode = PhaseWindowMode.FrequencyDependent,
            MagnitudeFdwCycles = 5,
            SmoothingInverseOctaves = 5,
            CalibrationId = "gone",
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        var visibility = new CurveVisibilityOptions { ShowHd3 = false, ShowArraySpread = true, ShowMeasuredPhase = false };
        session.Load(options, visibility, [new MicrophoneCalibrationEntry(MicrophoneCalibrationIds.ZeroDegrees, "0°", true)]);

        var written = new FrequencyResponseOptions();
        var shown = new CurveVisibilityOptions();
        session.WriteTo(written, shown);

        Assert.Equal(
            (32768, 32768, 0, PhaseWindowMode.FrequencyDependent, PhaseAnalysisSettings.DefaultFdwCycles, 6.0, "gone",
                MagnitudeScale.SoundPressureLevel),
            (written.Window, written.LeftTukeyWindow, written.RightTukeyWindow, written.MagnitudeWindowMode,
                written.MagnitudeFdwCycles, written.SmoothingInverseOctaves, written.CalibrationId, written.MagnitudeScale));
        Assert.Equal(["Off", "0°", "Deleted calibration (missing)"], session.Calibrations.Select(option => option.DisplayName));
        Assert.Equal((false, true, true), (shown.ShowHd3, shown.ShowArraySpread, shown.ShowMeasuredPhase));
    }
}
