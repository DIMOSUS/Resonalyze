using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

public sealed class CalibrationFileTests
{
    [Fact]
    public void LoadsWhitespaceSeparatedCalibrationWithComments()
    {
        string path = WriteCalibrationFile(
            "# Frequency dB\n" +
            "20 2.5\n" +
            "1000 2.5 extra-column\n" +
            "20000 2.5\n");

        var calibration = new CalibrationFile(path);

        Assert.Equal(2.5, calibration.GetDecibelCorrection(1000), precision: 6);
    }

    [Fact]
    public void LoadsDecimalCommaCalibration()
    {
        string path = WriteCalibrationFile(
            "20 2,5\n" +
            "1000 2,5\n" +
            "20000 2,5\n");

        var calibration = new CalibrationFile(path);

        Assert.Equal(2.5, calibration.GetDecibelCorrection(1000), precision: 6);
    }

    [Fact]
    public void LoadsCsvCalibrationWithAdditionalColumns()
    {
        string path = WriteCalibrationFile(
            "Hz,dB,phase\n" +
            "20,2.5,0\n" +
            "1000,2.5,10\n" +
            "20000,2.5,0\n");

        var calibration = new CalibrationFile(path);

        Assert.Equal(2.5, calibration.GetDecibelCorrection(1000), precision: 6);
    }

    [Fact]
    public void Delta90Minus0_MatchesHalfInchMicrophoneApproximation()
    {
        double delta = CalibrationFile.Delta90Minus0(10_000);

        Assert.Equal(-3.000769, delta, precision: 6);
    }

    [Fact]
    public void CreateNinetyDegreeApproximation_AddsDeltaToZeroDegreeCalibration()
    {
        string path = WriteCalibrationFile(
            "20 2.5\n" +
            "1000 2.5\n" +
            "20000 2.5\n");
        var zeroDegree = new CalibrationFile(path);
        CalibrationFile ninetyDegree =
            CalibrationFile.CreateNinetyDegreeApproximation(zeroDegree);

        double expected = 2.5 + CalibrationFile.Delta90Minus0(1000);

        Assert.Equal(expected, ninetyDegree.GetDecibelCorrection(1000), precision: 6);
    }

    [Fact]
    public void QueryBelowCalibratedRange_HoldsFirstPointInsteadOfExtrapolating()
    {
        // A steep first segment on a file starting at 100 Hz: unclamped linear
        // extrapolation down to 20 Hz would run ~80 segment widths out and drive
        // the amplitude negative (a -160 dB correction spike).
        string path = WriteCalibrationFile(
            "100 0.0\n" +
            "101 1.0\n" +
            "20000 1.0\n");

        var calibration = new CalibrationFile(path);

        Assert.Equal(0.0, calibration.GetDecibelCorrection(20), precision: 6);
    }

    [Fact]
    public void QueryAboveCalibratedRange_StaysNearLastPoint()
    {
        string path = WriteCalibrationFile(
            "20 1.0\n" +
            "1000 1.0\n" +
            "5000 3.0\n");

        var calibration = new CalibrationFile(path);

        double correction = calibration.GetDecibelCorrection(20_000);

        Assert.InRange(correction, 1.0, 3.5);
    }

    [Fact]
    public void HasData_ReflectsWhetherCalibrationLoaded()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-missing-{Guid.NewGuid():N}.txt");
        string loaded = WriteCalibrationFile(
            "20 2.5\n" +
            "20000 2.5\n");

        Assert.False(new CalibrationFile(missing).HasData);
        Assert.True(new CalibrationFile(loaded).HasData);
        Assert.True(CalibrationFile
            .CreateNinetyDegreeApproximation(new CalibrationFile(loaded))
            .HasData);
    }

    [Fact]
    public void LoadError_ReportsMissingFile()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-missing-{Guid.NewGuid():N}.txt");

        var calibration = new CalibrationFile(missing);

        Assert.False(calibration.HasData);
        Assert.Contains("not found", calibration.LoadError);
    }

    [Fact]
    public void LoadError_ReportsFileWithoutParsablePairs()
    {
        string path = WriteCalibrationFile(
            "# header only\n" +
            "no numbers here\n");

        var calibration = new CalibrationFile(path);

        Assert.False(calibration.HasData);
        Assert.Contains("no frequency/level pairs", calibration.LoadError);
    }

    [Fact]
    public void LoadError_IsNullForValidFile()
    {
        string path = WriteCalibrationFile(
            "20 2.5\n" +
            "20000 2.5\n");

        var calibration = new CalibrationFile(path);

        Assert.True(calibration.HasData);
        Assert.Null(calibration.LoadError);
    }

    [Fact]
    public void LoadError_PropagatesThroughNinetyDegreeApproximation()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-missing-{Guid.NewGuid():N}.txt");

        CalibrationFile approximation =
            CalibrationFile.CreateNinetyDegreeApproximation(new CalibrationFile(missing));

        Assert.False(approximation.HasData);
        Assert.Contains("not found", approximation.LoadError);
    }

    [Fact]
    public void Parse_MatchesFileLoad_ForIdenticalText()
    {
        const string text =
            "# Frequency dB\n" +
            "20 2.5\n" +
            "1000,2.5,10\n" +
            "20000\t2.5\n";
        string path = WriteCalibrationFile(text);

        var parsed = CalibrationFile.Parse(text);
        var loaded = new CalibrationFile(path);

        Assert.Equal(loaded.HasData, parsed.HasData);
        Assert.Equal(
            loaded.GetDecibelCorrection(1000),
            parsed.GetDecibelCorrection(1000),
            precision: 12);
        Assert.Null(parsed.LoadError);
    }

    [Fact]
    public void Parse_HandlesCrlfLineEndings()
    {
        var calibration = CalibrationFile.Parse(
            "20 2.5\r\n1000 2.5\r\n20000 2.5\r\n");

        Assert.True(calibration.HasData);
        Assert.Equal(2.5, calibration.GetDecibelCorrection(1000), precision: 6);
    }

    [Fact]
    public void Parse_WithoutParsablePairs_ReportsContentLoadError()
    {
        var calibration = CalibrationFile.Parse("# header only\nno numbers here\n");

        Assert.False(calibration.HasData);
        Assert.Contains("no frequency/level pairs", calibration.LoadError);
    }

    [Fact]
    public void Parse_WithSourceName_WeavesItIntoTheLoadError()
    {
        var calibration = CalibrationFile.Parse("garbage", sourceName: "mic.cal");

        Assert.Contains("mic.cal", calibration.LoadError);
    }

    [Fact]
    public void Parse_WithoutSourceName_OmitsPathFromLoadError()
    {
        var calibration = CalibrationFile.Parse("garbage");

        Assert.NotNull(calibration.LoadError);
        Assert.DoesNotContain(":", calibration.LoadError);
    }

    [Fact]
    public void Parse_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CalibrationFile.Parse(null!));
    }

    private static string WriteCalibrationFile(string text)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text);
        return path;
    }
}
