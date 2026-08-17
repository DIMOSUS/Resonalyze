using Resonalyze.Dsp;
using Resonalyze.Options;
using CalibrationOptions =
    System.Collections.Generic.IReadOnlyList<
        Resonalyze.Options.MicrophoneCalibrationComboHelper.MicrophoneCalibrationOption>;

namespace Resonalyze.App.Tests;

/// <summary>
/// The persisted calibration selection must stay selectable when its file is
/// missing — or when the entry itself is gone; dropping the entry used to land
/// the selection on "Off" and the next apply permanently overwrote the stored
/// preference.
/// </summary>
public sealed class MicrophoneCalibrationComboHelperTests
{
    private static readonly MicrophoneCalibrationEntry[] Configured =
    [
        new(MicrophoneCalibrationIds.ZeroDegrees, "0°", true),
        new("cal1", "45° seat", true)
    ];

    [Fact]
    public void BuildOptions_ListsOffThenEveryConfiguredCalibration()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            null,
            Configured);

        Assert.Equal(
            [null, MicrophoneCalibrationIds.ZeroDegrees, "cal1"],
            options.Select(option => option.CalibrationId));
        Assert.Equal(0, MicrophoneCalibrationComboHelper.FindIndex(options, null));
    }

    [Fact]
    public void BuildOptions_MarksAnEntryThatDoesNotResolve()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            "cal1",
            [new MicrophoneCalibrationEntry("cal1", "45° seat", Available: false)]);

        int index = MicrophoneCalibrationComboHelper.FindIndex(options, "cal1");
        Assert.Equal(1, index);
        Assert.Equal("45° seat (unavailable)", options[index].DisplayName);
    }

    [Fact]
    public void BuildOptions_KeepsASelectionTheListNoLongerHolds()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            "deleted",
            Configured);

        int index = MicrophoneCalibrationComboHelper.FindIndex(options, "deleted");
        Assert.Equal(3, index);
        Assert.Equal("deleted", options[index].CalibrationId);
        Assert.Equal("Deleted calibration (missing)", options[index].DisplayName);
    }

    [Fact]
    public void BuildOptions_DoesNotMarkAvailableCalibrations()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            "cal1",
            Configured);

        Assert.All(
            options,
            option => Assert.DoesNotContain("(", option.DisplayName));
    }

    [Fact]
    public void ASelectionLeftFromADeletedEntryStaysMissingAfterANewOneIsAdded()
    {
        // A counted id would be handed out again after a deletion, and every
        // stored selection still naming it — another view, a saved Virtual DSP
        // session, a history entry — would silently start correcting with a
        // calibration nobody pointed it at.
        var definitions = new List<MicrophoneCalibrationDefinition>();
        string deleted = MicrophoneCalibrationDefinition.CreateId(definitions);
        definitions.Add(new MicrophoneCalibrationDefinition { Id = deleted, Name = "Old" });
        definitions.Clear();
        string added = MicrophoneCalibrationDefinition.CreateId(definitions);
        definitions.Add(new MicrophoneCalibrationDefinition { Id = added, Name = "New" });

        Assert.NotEqual(deleted, added);
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            deleted,
            definitions
                .Select(definition => new MicrophoneCalibrationEntry(
                    definition.Id,
                    definition.Name,
                    Available: true))
                .ToList());

        int index = MicrophoneCalibrationComboHelper.FindIndex(options, deleted);
        Assert.Equal(deleted, options[index].CalibrationId);
        Assert.Contains("missing", options[index].DisplayName);
    }

    [Fact]
    public void CreateId_NeverCollidesWithTheReservedIds()
    {
        string created = MicrophoneCalibrationDefinition.CreateId([]);

        Assert.NotEqual(MicrophoneCalibrationIds.ZeroDegrees, created);
        Assert.NotEqual(MicrophoneCalibrationDefinition.LegacyNinetyDegreesId, created);
    }

    [Fact]
    public void FindIndex_FallsBackToOffForAnAbsentSelection()
    {
        CalibrationOptions options = MicrophoneCalibrationComboHelper.BuildOptions(
            null,
            []);

        Assert.Equal(0, MicrophoneCalibrationComboHelper.FindIndex(options, "cal1"));
    }
}
