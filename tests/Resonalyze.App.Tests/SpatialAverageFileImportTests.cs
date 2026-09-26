using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class SpatialAverageFileImportTests
{
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    // Flat 80 dB from 1 Hz to 24 kHz on a linear grid 1 Hz apart, as REW exports one.
    private static FrequencyResponseTextFile FlatFile(string smoothing = "None")
    {
        string text =
            "* Measurement data measured by REW V5.40\n" +
            "* Measurement: l tw\n" +
            $"* Smoothing: {smoothing}\n" +
            "* Freq(Hz) SPL(dB) Phase(degrees)\n" +
            string.Join("\n", Enumerable.Range(1, 24_000).Select(i => $"{i} 80.0 0.0"));
        Assert.True(FrequencyResponseTextFile.TryParse(text, out FrequencyResponseTextFile? file, out string? problem), problem);
        return file!;
    }

    private static VirtualCrossoverCalibrationSettings FlatCalibration(double db, string name = "umik") =>
        new()
        {
            Name = name,
            Points = [[10.0, db], [30_000.0, db]]
        };

    [Fact]
    public void AnUncalibratedFileWithNoFilter_IsItsOwnLevelsOnTheGrid()
    {
        LiveCaptureDocument document = SpatialAverageFileImport.Build(
            FlatFile(), new SpatialAverageFileSettings(), "l tw.txt");

        Assert.Equal(SpatialAverageMethod.File, document.Method);
        Assert.Equal("l tw", document.Title);
        Assert.Equal(LiveCaptureDocument.CurvePointCount, document.CurveDb.Length);
        Assert.All(document.CurveDb, level => Assert.Equal(80.0, level, 9));
        Assert.Empty(document.CalibrationCorrectionDb);
        Assert.Null(document.Calibration);
    }

    [Fact]
    public void AStatedCalibration_CanBeRemovedAgainByThePanel()
    {
        LiveCaptureDocument document = SpatialAverageFileImport.Build(
            FlatFile(),
            new SpatialAverageFileSettings { Calibration = FlatCalibration(2.0) },
            "l tw.txt");
        double[] probe = [Grid[100], Grid[900]];

        List<SignalPoint>? own = SpatialAverageHybrid.BuildChannelCurve(
            document, DspChannelChain.Identity, 48_000, SpatialAverageCalibration.Own, probe, 0);
        List<SignalPoint>? off = SpatialAverageHybrid.BuildChannelCurve(
            document, DspChannelChain.Identity, 48_000, SpatialAverageCalibration.Off, probe, 0);

        // The pipeline subtracts a correction, so undoing a +2 dB file adds 2 dB back.
        Assert.All(own!, point => Assert.Equal(80.0, point.Y, 6));
        Assert.All(off!, point => Assert.Equal(82.0, point.Y, 6));
    }

    [Fact]
    public void AFileStatedAsAlreadyCorrect_IsDrawnAsStoredWhateverCalibrationIsAsked()
    {
        LiveCaptureDocument document = SpatialAverageFileImport.Build(
            FlatFile(),
            new SpatialAverageFileSettings { CalibratedAsIs = true, Calibration = FlatCalibration(2.0) },
            "l tw.txt");
        double[] probe = [Grid[100], Grid[900]];

        foreach (SpatialAverageCalibration calibration in new[]
        {
            SpatialAverageCalibration.Off,
            SpatialAverageCalibration.Own,
            SpatialAverageCalibration.Specific(FlatCalibration(5.0).ToCalibrationFile())
        })
        {
            List<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
                document, DspChannelChain.Identity, 48_000, calibration, probe, 0);
            Assert.All(curve!, point => Assert.Equal(80.0, point.Y, 6));
        }
    }

    [Fact]
    public void AStatedHighPass_IsDividedOutOfTheFile()
    {
        var answers = new SpatialAverageFileSettings
        {
            HighPassKind = ProtectiveHighPassKind.LinkwitzRiley,
            HighPassFrequencyHz = 2_000,
            HighPassSlopeDbPerOctave = 24,
            HighPassSampleRateHz = 96_000
        };

        LiveCaptureDocument document = SpatialAverageFileImport.Build(FlatFile(), answers, "l tw.txt");

        // LR24 is a squared second-order Butterworth: |H| = x^4 / (1 + x^4) at x = f / fc.
        int band = Enumerable.Range(0, Grid.Count).MinBy(i => Math.Abs(Grid[i] - 1_000.0));
        double x = Grid[band] / 2_000.0;
        double lossDb = -20.0 * Math.Log10(Math.Pow(x, 4) / (1 + Math.Pow(x, 4)));
        Assert.Equal(80.0 + lossDb, document.CurveDb[band], 1);
        Assert.Equal(80.0, document.CurveDb[^1], 1);
        // Where the boost would pass its cap nothing is recovered: a break, not a level.
        Assert.True(double.IsNaN(document.CurveDb[0]));
    }

    [Fact]
    public void FilesFormASetOnTheirOwn_ButNotWithMovingMicrophoneCaptures()
    {
        LiveCaptureDocument first = SpatialAverageFileImport.Build(
            FlatFile(), new SpatialAverageFileSettings(), "a.txt");
        LiveCaptureDocument second = SpatialAverageFileImport.Build(
            FlatFile(), new SpatialAverageFileSettings(), "b.txt");
        LiveCaptureDocument moving = SpatialAverageFileImport.Build(
            FlatFile(), new SpatialAverageFileSettings(), "c.txt");
        moving.Method = SpatialAverageMethod.MovingMic;

        Assert.True(LiveCaptureDocument.JudgeSet([first, second]).Coherent);
        Assert.False(LiveCaptureDocument.JudgeSet([first, moving]).Coherent);
    }

    [Fact]
    public void AFileDocumentWrittenToDisk_IsNotReadBackAsACapture()
    {
        LiveCaptureDocument document = SpatialAverageFileImport.Build(
            FlatFile(), new SpatialAverageFileSettings(), "l tw.txt");
        string path = Path.Combine(Path.GetTempPath(), $"file-capture-{Guid.NewGuid():N}.json");
        try
        {
            document.Save(path);

            Assert.Throws<InvalidDataException>(() => LiveCaptureDocument.TryLoad(path, out _));
            Assert.Throws<InvalidDataException>(() => LiveCaptureDocument.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Warnings_NameASmoothedCoarseOrShortFile()
    {
        Assert.Empty(SpatialAverageFileImport.Warnings(FlatFile()));

        Assert.Single(SpatialAverageFileImport.Warnings(FlatFile("1/6 octave")));
        Assert.True(FrequencyResponseTextFile.TryParse(
            "100 70\n200 71\n400 72\n", out FrequencyResponseTextFile? short3, out _));
        // No REW header, 100-400 Hz only, one point per octave.
        Assert.Equal(3, SpatialAverageFileImport.Warnings(short3!).Count);
    }

    [Fact]
    public void CalibrationChoices_OfferNoneTheMeasurementsFileAndTheList_AndKeepAStatedCurve()
    {
        VirtualCrossoverCalibrationSettings measurement = FlatCalibration(1.0, "ecm8000");
        VirtualCrossoverCalibrationSettings stated = FlatCalibration(3.0, "old umik");
        CalibrationFile listed = FlatCalibration(2.0, "umik").ToCalibrationFile();
        CalibrationFile duplicate = measurement.ToCalibrationFile();

        List<SpatialAverageFileCalibrationChoice> choices = SpatialAverageFileCalibrationChoice.Offer(
            stated, measurement, [("umik", null, listed), ("ecm copy", null, duplicate)]);

        Assert.Equal(5, choices.Count);
        Assert.Null(choices[0].Calibration);
        Assert.True(choices[SpatialAverageFileCalibrationChoice.AsIsIndex].AsIs);
        Assert.Equal(4, SpatialAverageFileCalibrationChoice.IndexOf(choices, stated));
        Assert.Equal(2, SpatialAverageFileCalibrationChoice.IndexOf(choices, measurement));
        Assert.Equal(0, SpatialAverageFileCalibrationChoice.IndexOf(choices, null));
    }
}
