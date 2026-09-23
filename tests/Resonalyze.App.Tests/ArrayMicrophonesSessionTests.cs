using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class ArrayMicrophonesSessionTests
{
    private static readonly IReadOnlyList<MicrophoneCalibrationEntry> Calibrations =
    [
        new(MicrophoneCalibrationIds.ZeroDegrees, "0°", true),
        new("cal-1", "ECM8000 90°", true)
    ];

    private static ArrayMicrophonesSession Session(
        IReadOnlyList<ArrayMicrophoneDefinition> microphones,
        IReadOnlyList<int> availableChannels,
        int? loopbackChannel = 1) =>
        new(microphones, Calibrations, availableChannels, microphoneChannel: 0, loopbackChannel, "test inputs");

    private static ArrayMicrophoneDefinition On(int channel, string? note = null, string? calibration = null) =>
        new() { ChannelOffset = channel, Note = note, CalibrationId = calibration };

    [Fact]
    public void OnlyFreeInputsAreOffered()
    {
        ArrayMicrophonesSession session = Session([On(2)], [0, 1, 2, 3]);

        Assert.Equal([3], session.EditorChannels);
        Assert.Equal(3, session.EditorChannel);
        Assert.True(session.CanAdd);
    }

    [Fact]
    public void WithoutALoopbackItsInputIsFreeAgain()
    {
        Assert.Equal([1, 2], Session([], [0, 1, 2], loopbackChannel: null).EditorChannels);
    }

    [Fact]
    public void SelectingAMicrophoneLoadsItIntoTheEditor()
    {
        ArrayMicrophonesSession session = Session([On(2, "left", "cal-1"), On(3)], [0, 1, 2, 3, 4]);
        int loads = session.EditorVersion;

        session.Select(0);

        Assert.Equal(0, session.Selected);
        Assert.Equal([2, 4], session.EditorChannels);
        Assert.Equal(2, session.EditorChannel);
        Assert.Equal("ECM8000 90°", session.CalibrationOptions[session.CalibrationIndex].DisplayName);
        Assert.Equal("left", session.Note);
        Assert.Equal(loads + 1, session.EditorVersion);
        Assert.False(session.CanAdd);
    }

    [Fact]
    public void LeavingTheRowKeepsTheEditorsCalibrationAndNote()
    {
        ArrayMicrophonesSession session = Session([On(2, "left", "cal-1")], [0, 1, 2, 3, 4]);
        session.Select(0);
        int loads = session.EditorVersion;

        session.Select(null);

        Assert.Null(session.Selected);
        Assert.Equal([3, 4], session.EditorChannels);
        Assert.Equal(3, session.EditorChannel);
        Assert.Equal("left", session.Note);
        Assert.Equal("cal-1", session.CalibrationOptions[session.CalibrationIndex].CalibrationId);
        Assert.Equal(loads, session.EditorVersion);
    }

    [Fact]
    public void AGoneCalibrationKeepsItsOwnEntryInTheEditor()
    {
        ArrayMicrophonesSession session = Session([On(2, calibration: "cal-gone")], [0, 1, 2, 3]);

        session.Select(0);

        Assert.Equal(
            ["Off", "0°", "ECM8000 90°", "Deleted calibration (missing)"],
            session.CalibrationOptions.Select(option => option.DisplayName));
        Assert.Equal(3, session.CalibrationIndex);
    }

    [Fact]
    public void AddingTakesTheEditorsInputCalibrationAndNote()
    {
        ArrayMicrophonesSession session = Session([], [0, 1, 2, 3]);
        int list = session.ListVersion;
        session.SetEditorChannel(3);
        session.SetCalibrationIndex(2);
        session.SetNote("  left ear  ");

        Assert.True(session.Add());

        ArrayMicrophoneDefinition added = Assert.Single(session.Microphones);
        Assert.Equal(3, added.ChannelOffset);
        Assert.Equal("cal-1", added.CalibrationId);
        Assert.Equal("left ear", added.Note);
        Assert.Equal(0, session.Selected);
        Assert.Equal(list + 1, session.ListVersion);
    }

    [Fact]
    public void AddingTwiceInARowCannotDuplicateTheFirst()
    {
        ArrayMicrophonesSession session = Session([], [0, 1, 2, 3]);

        Assert.True(session.Add());
        Assert.False(session.CanAdd);
        Assert.False(session.Add());

        Assert.Equal([2], session.Microphones.Select(microphone => microphone.ChannelOffset));
        session.Select(null);
        Assert.True(session.Add());
        session.Select(null);
        Assert.Equal([2, 3], session.Microphones.Select(microphone => microphone.ChannelOffset));
        Assert.Empty(session.EditorChannels);
        Assert.Null(session.EditorChannel);
        Assert.False(session.Add());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ABlankNoteIsNoNote(string? note)
    {
        ArrayMicrophonesSession session = Session([], [0, 1, 2]);
        session.SetNote(note);

        session.Add();

        Assert.Null(Assert.Single(session.Microphones).Note);
    }

    [Fact]
    public void UpdatingChangesTheSelectedMicrophoneOnly()
    {
        ArrayMicrophonesSession session = Session([On(2, "first"), On(3, "second")], [0, 1, 2, 3, 4]);
        session.Select(1);
        session.SetEditorChannel(4);
        session.SetCalibrationIndex(1);
        session.SetNote("renamed");

        Assert.True(session.Update());

        Assert.Equal("first", session.Microphones[0].Note);
        Assert.Equal(4, session.Microphones[1].ChannelOffset);
        Assert.Equal(MicrophoneCalibrationIds.ZeroDegrees, session.Microphones[1].CalibrationId);
        Assert.Equal("renamed", session.Microphones[1].Note);
        Assert.Equal(1, session.Selected);
    }

    [Fact]
    public void UpdatingNeedsARowAndAnInput()
    {
        ArrayMicrophonesSession session = Session([On(2)], [0, 1, 2]);

        Assert.False(session.Update());
        session.Select(0);
        session.SetEditorChannel(null);
        Assert.False(session.Update());
        Assert.Equal(2, session.Microphones[0].ChannelOffset);
    }

    [Fact]
    public void RemovingSelectsTheNextRowAndFreesTheInput()
    {
        ArrayMicrophonesSession session = Session([On(2), On(3), On(4)], [0, 1, 2, 3, 4]);
        Assert.False(session.Remove());
        session.Select(2);

        Assert.True(session.Remove());
        Assert.Equal(1, session.Selected);
        session.Select(0);
        Assert.True(session.Remove());
        Assert.Equal(0, session.Selected);
        Assert.Equal([3], session.Microphones.Select(microphone => microphone.ChannelOffset));
        Assert.True(session.Remove());

        Assert.Null(session.Selected);
        Assert.Equal([2, 3, 4], session.EditorChannels);
    }

    [Fact]
    public void AnInputTheMeasurementTookIsNamedRatherThanDroppedInSilence()
    {
        using var culture = new InvariantCultureScope();
        ArrayMicrophonesSession session = Session([On(0), On(1, "b", "cal-1"), On(2, calibration: "cal-gone")], [0, 1, 2, 3]);

        Assert.Equal(
            [
                new ArrayMicrophoneRow("Input 1 (the measurement microphone)", "Off", ""),
                new ArrayMicrophoneRow("Input 2 (the loopback)", "ECM8000 90°", "b"),
                new ArrayMicrophoneRow("Input 3", "cal-gone (missing)", "")
            ],
            ArrayMicrophoneRows.Read(session));
        Assert.Equal(
            "3 configured, 1 further input(s) free (test inputs). 2 of them cannot be recorded — see the list.",
            ArrayMicrophoneRows.Status(session));
    }

    [Fact]
    public void TheStatusCountsOneConflictInTheSingular()
    {
        Assert.Equal(
            "1 configured; every input is in use (test inputs). 1 of them cannot be recorded — see the list.",
            ArrayMicrophoneRows.Status(Session([On(0)], [0, 1])));
    }

    [Fact]
    public void TheStatusSaysWhenThereIsNothingToRecordFrom()
    {
        Assert.Equal("No inputs to record an array from (test inputs).", ArrayMicrophoneRows.Status(Session([], [])));
        Assert.Equal("0 configured; every input is in use (test inputs).", ArrayMicrophoneRows.Status(Session([], [0, 1])));
    }

    [Fact]
    public void TheSessionEditsACopy()
    {
        var original = new List<ArrayMicrophoneDefinition> { On(2, "original") };
        ArrayMicrophonesSession session = Session(original, [0, 1, 2, 3]);
        session.Select(0);
        session.SetNote("edited");

        session.Update();

        Assert.Equal("original", original[0].Note);
        Assert.Equal("edited", session.Microphones[0].Note);
    }

    [Fact]
    public void TheRecordedChannelsSkipTheMeasurementsOwnDuplicatesAndTheUnreachable()
    {
        IReadOnlyList<int> recorded = ArrayChannelRules.Recorded(
            [On(5), On(-1), On(0), On(1), On(3), On(5), On(7), On(2)],
            microphoneChannel: 0,
            loopbackChannel: 1,
            reachable: channel => channel < 6);

        Assert.Equal([5, 3, 2], recorded);
        Assert.Equal([1, 3], ArrayChannelRules.Recorded([On(1), On(3)], 0, null, _ => true));
        Assert.True(ArrayChannelRules.IsMeasurementInput(4, 4, null));
        Assert.False(ArrayChannelRules.IsMeasurementInput(4, 0, null));
        Assert.True(ArrayChannelRules.IsMeasurementInput(4, 0, 4));
    }
}
