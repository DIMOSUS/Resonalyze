using Resonalyze.Dsp;
using Resonalyze.Options;
using CalibrationOptions =
    System.Collections.Generic.IReadOnlyList<
        Resonalyze.Options.MicrophoneCalibrationOption>;

namespace Resonalyze.App.Tests;

/// <summary>A missing selection stays selectable; landing on Off would be persisted by the next apply.</summary>
public sealed class MicrophoneCalibrationChoicesTests
{
    private static readonly MicrophoneCalibrationEntry[] Configured =
    [
        new(MicrophoneCalibrationIds.ZeroDegrees, "0°", true),
        new("cal1", "45° seat", true)
    ];

    [Fact]
    public void BuildOptions_ListsOffThenEveryConfiguredCalibration()
    {
        CalibrationOptions options = MicrophoneCalibrationChoices.BuildOptions(
            null,
            Configured);

        Assert.Equal(
            [null, MicrophoneCalibrationIds.ZeroDegrees, "cal1"],
            options.Select(option => option.CalibrationId));
        Assert.Equal(0, MicrophoneCalibrationChoices.FindIndex(options, null));
    }

    [Fact]
    public void BuildOptions_MarksAnEntryThatDoesNotResolve()
    {
        CalibrationOptions options = MicrophoneCalibrationChoices.BuildOptions(
            "cal1",
            [new MicrophoneCalibrationEntry("cal1", "45° seat", Available: false)]);

        int index = MicrophoneCalibrationChoices.FindIndex(options, "cal1");
        Assert.Equal(1, index);
        Assert.Equal("45° seat (unavailable)", options[index].DisplayName);
    }

    [Fact]
    public void BuildOptions_KeepsASelectionTheListNoLongerHolds()
    {
        CalibrationOptions options = MicrophoneCalibrationChoices.BuildOptions(
            "deleted",
            Configured);

        int index = MicrophoneCalibrationChoices.FindIndex(options, "deleted");
        Assert.Equal(3, index);
        Assert.Equal("deleted", options[index].CalibrationId);
        Assert.Equal("Deleted calibration (missing)", options[index].DisplayName);
    }

    [Fact]
    public void BuildOptions_DoesNotMarkAvailableCalibrations()
    {
        CalibrationOptions options = MicrophoneCalibrationChoices.BuildOptions(
            "cal1",
            Configured);

        Assert.All(
            options,
            option => Assert.DoesNotContain("(", option.DisplayName));
    }

    [Fact]
    public void ASelectionLeftFromADeletedEntryStaysMissingAfterANewOneIsAdded()
    {
        // A reused counted id would silently re-point every stored selection naming it.
        var definitions = new List<MicrophoneCalibrationDefinition>();
        string deleted = MicrophoneCalibrationDefinition.CreateId(definitions);
        definitions.Add(new MicrophoneCalibrationDefinition { Id = deleted, Name = "Old" });
        definitions.Clear();
        string added = MicrophoneCalibrationDefinition.CreateId(definitions);
        definitions.Add(new MicrophoneCalibrationDefinition { Id = added, Name = "New" });

        Assert.NotEqual(deleted, added);
        CalibrationOptions options = MicrophoneCalibrationChoices.BuildOptions(
            deleted,
            definitions
                .Select(definition => new MicrophoneCalibrationEntry(
                    definition.Id,
                    definition.Name,
                    Available: true))
                .ToList());

        int index = MicrophoneCalibrationChoices.FindIndex(options, deleted);
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
        CalibrationOptions options = MicrophoneCalibrationChoices.BuildOptions(
            null,
            []);

        Assert.Equal(0, MicrophoneCalibrationChoices.FindIndex(options, "cal1"));
    }
}
