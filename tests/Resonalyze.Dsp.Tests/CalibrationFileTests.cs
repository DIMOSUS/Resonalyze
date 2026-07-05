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

    private static string WriteCalibrationFile(string text)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, text);
        return path;
    }
}
