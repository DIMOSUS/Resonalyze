using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A measurement is stored raw, so the calibration it was read through has to
/// travel with it — and then be told apart from the reader's own. The curve
/// decides, never the id: two machines' calibration lists mint their own.
/// </summary>
public sealed class FileCalibrationSelectionTests
{
    private static CalibrationFile Curve(double correctionDb) =>
        CalibrationFile.FromPoints(
            [
                new CalibrationPoint(20.0, correctionDb),
                new CalibrationPoint(1_000.0, correctionDb),
                new CalibrationPoint(20_000.0, correctionDb)
            ],
            "curve");

    private static VirtualCrossoverCalibrationSettings Settings(string name, double correctionDb) =>
        VirtualCrossoverCalibrationSettings.From(Curve(correctionDb), name, name + ".txt");

    private static readonly IReadOnlyList<MicrophoneCalibrationEntry> Entries =
    [
        new(MicrophoneCalibrationIds.ZeroDegrees, "0°", true),
        new("cal-1", "ECM8000 90°", true)
    ];

    // The local list: 0° holds -1 dB, the extra entry holds -3 dB.
    private static CalibrationFile? Resolve(string? id) => id switch
    {
        MicrophoneCalibrationIds.ZeroDegrees => Curve(-1.0),
        "cal-1" => Curve(-3.0),
        _ => null
    };

    [Fact]
    public void AFileWithoutACalibrationLeavesTheSelectionAlone()
    {
        // A measurement written before the format carried one is not a measurement
        // taken without one, so nothing may be concluded from its absence.
        Assert.Null(FileCalibrationSelection.Choose(
            loaded: null,
            selectedId: MicrophoneCalibrationIds.ZeroDegrees,
            Entries,
            Resolve));
    }

    [Fact]
    public void YourOwnFileOnYourOwnMachineChangesNothing()
    {
        Assert.Null(FileCalibrationSelection.Choose(
            Settings("0°", -1.0),
            selectedId: MicrophoneCalibrationIds.ZeroDegrees,
            Entries,
            Resolve));
    }

    [Fact]
    public void ALocalEntryHoldingTheSameCurveIsPreferredToTheFilesCopy()
    {
        // Selected 0°, but the file was measured through the 90° curve this
        // machine also has: point at the local entry rather than at a duplicate.
        string? chosen = FileCalibrationSelection.Choose(
            Settings("Some other name", -3.0),
            selectedId: MicrophoneCalibrationIds.ZeroDegrees,
            Entries,
            Resolve);

        Assert.Equal("cal-1", chosen);
    }

    [Fact]
    public void ACurveThisMachineDoesNotHaveIsOfferedAsTheFilesOwn()
    {
        string? chosen = FileCalibrationSelection.Choose(
            Settings("Foreign mic", -7.5),
            selectedId: MicrophoneCalibrationIds.ZeroDegrees,
            Entries,
            Resolve);

        Assert.Equal(FileCalibrationSelection.FileId, chosen);
    }

    [Fact]
    public void TheOfferedEntryIsNamedAfterTheFilesCalibration()
    {
        IReadOnlyList<MicrophoneCalibrationEntry> extended =
            FileCalibrationSelection.EntriesWith(Entries, Settings("Foreign mic", -7.5), Resolve);

        Assert.Equal(Entries.Count + 1, extended.Count);
        MicrophoneCalibrationEntry offered = extended[^1];
        Assert.Equal(FileCalibrationSelection.FileId, offered.Id);
        Assert.Equal("Foreign mic (from file)", offered.Name);
        Assert.True(offered.Available);
    }

    [Fact]
    public void NoDuplicateEntryWhenTheCurveIsAlreadyInTheList()
    {
        IReadOnlyList<MicrophoneCalibrationEntry> extended =
            FileCalibrationSelection.EntriesWith(Entries, Settings("Renamed", -3.0), Resolve);

        Assert.Equal(Entries.Count, extended.Count);
    }

    [Fact]
    public void AnEntryWhoseFileIsMissingIsSkippedRatherThanAssumedToDiffer()
    {
        IReadOnlyList<MicrophoneCalibrationEntry> entries =
        [
            new(MicrophoneCalibrationIds.ZeroDegrees, "0°", Available: false)
        ];

        // Unavailable means the curve cannot be compared at all, so the file's own
        // copy is offered — the alternative is drawing through a correction nobody
        // can currently read.
        Assert.Equal(
            FileCalibrationSelection.FileId,
            FileCalibrationSelection.Choose(
                Settings("Foreign mic", -7.5),
                MicrophoneCalibrationIds.ZeroDegrees,
                entries,
                _ => null));
    }

    [Fact]
    public void AnUnnamedCalibrationStillReadsAsComingFromAFile()
    {
        VirtualCrossoverCalibrationSettings unnamed =
            VirtualCrossoverCalibrationSettings.From(Curve(-4.0), string.Empty, null);

        Assert.Equal(
            "Measurement calibration (from file)",
            FileCalibrationSelection.DisplayName(unnamed));
    }

    [Fact]
    public void TheFileSelectionIsRecognisedWhateverItsCasing()
    {
        Assert.True(FileCalibrationSelection.IsFile("file-calibration"));
        Assert.True(FileCalibrationSelection.IsFile("FILE-CALIBRATION"));
        Assert.False(FileCalibrationSelection.IsFile(MicrophoneCalibrationIds.ZeroDegrees));
        Assert.False(FileCalibrationSelection.IsFile(null));
    }
}
