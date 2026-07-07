using Resonalyze.Dsp;
using Resonalyze.Options;
using CalibrationOptions =
    System.Collections.Generic.IReadOnlyList<
        Resonalyze.Options.MicrophoneCalibrationComboHelper.MicrophoneCalibrationOption>;

namespace Resonalyze.App.Tests;

/// <summary>
/// The persisted calibration mode must stay selectable when its file is
/// missing; dropping the entry used to land the selection on "Off" and the
/// next apply permanently overwrote the stored preference.
/// </summary>
public sealed class MicrophoneCalibrationComboHelperTests
{
    [Fact]
    public void BuildOptions_ListsAvailableProfiles()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            MicrophoneCalibrationMode.Off,
            hasZeroDegreeCalibration: true,
            hasNinetyDegreeCalibration: true);

        Assert.Equal(
            [
                MicrophoneCalibrationMode.Off,
                MicrophoneCalibrationMode.Degrees0,
                MicrophoneCalibrationMode.Degrees90
            ],
            options.Select(option => option.Mode));
        Assert.Equal(0, MicrophoneCalibrationComboHelper.FindIndex(
            options,
            MicrophoneCalibrationMode.Off));
    }

    [Fact]
    public void BuildOptions_OmitsUnselectedProfilesWithoutFiles()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            MicrophoneCalibrationMode.Off,
            hasZeroDegreeCalibration: false,
            hasNinetyDegreeCalibration: false);

        Assert.Equal(
            [MicrophoneCalibrationMode.Off],
            options.Select(option => option.Mode));
    }

    [Theory]
    [InlineData(MicrophoneCalibrationMode.Degrees0, "0 degrees (file missing)")]
    [InlineData(MicrophoneCalibrationMode.Degrees90, "90 degrees (file missing)")]
    public void BuildOptions_KeepsSelectedModeWhenItsFileIsMissing(
        MicrophoneCalibrationMode selectedMode,
        string expectedDisplayName)
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            selectedMode,
            hasZeroDegreeCalibration: false,
            hasNinetyDegreeCalibration: false);

        int index = MicrophoneCalibrationComboHelper.FindIndex(options, selectedMode);
        Assert.True(index > 0);
        Assert.Equal(selectedMode, options[index].Mode);
        Assert.Equal(expectedDisplayName, options[index].DisplayName);
    }

    [Fact]
    public void BuildOptions_DoesNotMarkAvailableProfiles()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            MicrophoneCalibrationMode.Degrees90,
            hasZeroDegreeCalibration: true,
            hasNinetyDegreeCalibration: true);

        Assert.All(
            options,
            option => Assert.DoesNotContain("missing", option.DisplayName));
    }

    [Fact]
    public void FindIndex_FallsBackToOffForAnAbsentMode()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            MicrophoneCalibrationMode.Off,
            hasZeroDegreeCalibration: false,
            hasNinetyDegreeCalibration: false);

        Assert.Equal(0, MicrophoneCalibrationComboHelper.FindIndex(
            options,
            MicrophoneCalibrationMode.Degrees90));
    }
}
