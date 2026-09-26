using System.Globalization;
using System.Text;
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
    public void CreateAngled_AddsTheAngularDifferenceToTheSourceCalibration()
    {
        string path = WriteCalibrationFile(
            "20 2.5\n" +
            "1000 2.5\n" +
            "20000 2.5\n");
        var zeroDegree = new CalibrationFile(path);

        CalibrationFile angled = CalibrationFile.CreateAngled(
            zeroDegree,
            frequency => -frequency / 10_000.0);

        Assert.Equal(2.4, angled.GetDecibelCorrection(1000), precision: 9);
        Assert.Equal(0.5, angled.GetDecibelCorrection(20_000), precision: 9);
    }

    [Fact]
    public void QueryBelowCalibratedRange_HoldsFirstPointInsteadOfExtrapolating()
    {
        // Unclamped linear extrapolation to 20 Hz would drive the amplitude negative (a -160 dB spike).
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

        // Above the range the correction holds the last point, not the slope.
        Assert.Equal(3.0, correction, precision: 6);
    }

    [Fact]
    public void ManyPointCurve_BinarySearchLandsOnTheCorrectSegment()
    {
        // 200 points force the interior binary-search descent that 3-point files never reach.
        var text = new StringBuilder();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 200))
        {
            double db = frequency < 1_500 ? 0.0 : 6.0;
            text.Append(frequency.ToString("R", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(db.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        CalibrationFile calibration = CalibrationFile.Parse(text.ToString());

        Assert.Equal(0.0, calibration.GetDecibelCorrection(200), precision: 5);   // low plateau
        Assert.Equal(6.0, calibration.GetDecibelCorrection(8_000), precision: 5); // high plateau
    }

    [Fact]
    public void Correction_ReproducesTheFilePointsExactly()
    {
        // A calibration must reproduce its own points (the Lanczos lookup averaged a 12 dB spike to ~5.6).
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 200);
        double spikeHz = grid.OrderBy(f => Math.Abs(f - 1_000)).First();
        var text = new StringBuilder();
        foreach (double frequency in grid)
        {
            double db = frequency == spikeHz ? 12.0 : 0.0;
            text.Append(frequency.ToString("R", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(db.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        CalibrationFile calibration = CalibrationFile.Parse(text.ToString());

        Assert.Equal(12.0, calibration.GetDecibelCorrection(spikeHz), precision: 6);
    }

    [Fact]
    public void Correction_InterpolatesLinearlyInLogFrequencyAndDecibels()
    {
        CalibrationFile calibration = CalibrationFile.Parse("1000 0\n2000 6\n");

        Assert.Equal(
            3.0,
            calibration.GetDecibelCorrection(1000 * Math.Sqrt(2.0)),
            precision: 6);
    }

    [Fact]
    public void Correction_DuplicateFrequenciesAreMergedNotNaN()
    {
        // Duplicate frequencies once made a zero-width segment and NaN.
        CalibrationFile calibration = CalibrationFile.Parse("1000 0\n1000 6\n2000 6\n");

        double correction = calibration.GetDecibelCorrection(1_500);

        Assert.True(double.IsFinite(correction));
    }

    [Fact]
    public void MissingFile_YieldsNoCorrection()
    {
        string missing = Path.Combine(
            Path.GetTempPath(), $"resonalyze-missing-{Guid.NewGuid():N}.txt");

        var calibration = new CalibrationFile(missing);

        Assert.Equal(0.0, calibration.GetDecibelCorrection(1_000));
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
            .CreateAngled(new CalibrationFile(loaded), _ => -1.0)
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
    public void LoadError_PropagatesThroughAnAngledCalibration()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-missing-{Guid.NewGuid():N}.txt");

        CalibrationFile angled =
            CalibrationFile.CreateAngled(new CalibrationFile(missing), _ => -1.0);

        Assert.False(angled.HasData);
        Assert.Contains("not found", angled.LoadError);
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
    public void FileLoad_HandlesCrlfFile()
    {
        // Exercises File.ReadAllText with CRLF through disk.
        string crlfPath = WriteCalibrationFile("20 2.5\r\n1000 2.5\r\n20000 2.5\r\n");

        var calibration = new CalibrationFile(crlfPath);

        Assert.True(calibration.HasData);
        Assert.Null(calibration.LoadError);
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

    [Fact]
    public void Points_StateTheFileInOrder_AndFromPointsReadsThemBack()
    {
        // A session stores the curve itself; it must correct exactly as the file did.
        var parsed = CalibrationFile.Parse("1000 6\n20 -1.25\n1000 0\n20000 3.5\n");

        IReadOnlyList<CalibrationPoint> points = parsed.Points;

        Assert.Equal(new[] { 20.0, 1000.0, 20000.0 }, points.Select(point => point.FrequencyHz));
        Assert.Equal(-1.25, points[0].Decibels, precision: 9);
        Assert.Equal(3.5, points[2].Decibels, precision: 9);

        CalibrationFile rebuilt = CalibrationFile.FromPoints(points, "session");
        Assert.True(rebuilt.HasData);
        Assert.Null(rebuilt.LoadError);
        foreach (double frequency in new[] { 10.0, 20.0, 150.0, 1000.0, 7000.0, 30000.0 })
        {
            Assert.Equal(
                parsed.GetDecibelCorrection(frequency),
                rebuilt.GetDecibelCorrection(frequency),
                precision: 9);
        }
    }

    [Fact]
    public void FromPoints_DropsWhatAFileCouldNotState_AndReportsTooFew()
    {
        CalibrationFile calibration = CalibrationFile.FromPoints(
            new[]
            {
                new CalibrationPoint(0, 1),
                new CalibrationPoint(double.NaN, 1),
                new CalibrationPoint(1000, double.PositiveInfinity),
                new CalibrationPoint(1000, 2)
            },
            "session");

        Assert.False(calibration.HasData);
        Assert.Contains("session", calibration.LoadError);
        Assert.Throws<ArgumentNullException>(() => CalibrationFile.FromPoints(null!));
    }

    [Fact]
    public void Points_OfAnAngledEstimate_ReproduceTheEstimate()
    {
        // An estimate's points are dense enough that a file written from them reads back the same correction.
        CalibrationFile zero = CalibrationFile.Parse("20 0\n2000 1\n20000 -2\n");
        CalibrationFile angled = CalibrationFile.CreateAngled(
            zero, frequency => -3.0 * Math.Pow(Math.Log10(frequency / 20.0) / 3.0, 2));

        IReadOnlyList<CalibrationPoint> points = angled.Points;
        CalibrationFile sampled = CalibrationFile.FromPoints(points);

        Assert.Equal(1.0, points[0].FrequencyHz);
        Assert.Equal(192_000.0, points[^1].FrequencyHz);
        Assert.Contains(points, point => point.FrequencyHz == 2000.0);
        Assert.InRange(points.Count, 400, 500);
        foreach (double frequency in new[] { 20.0, 33.0, 500.0, 2000.0, 12345.0, 20000.0 })
        {
            Assert.Equal(
                angled.GetDecibelCorrection(frequency),
                sampled.GetDecibelCorrection(frequency),
                precision: 2);
        }

        Assert.True(CalibrationFile.SameCurve(angled, sampled));
    }

    [Fact]
    public void Points_OfAnAngledEstimate_KeepMovingOutsideTheBaseFile()
    {
        // The audition FIR reads to Nyquist, so the carried curve must cover beyond the base file's edges.
        CalibrationFile narrowBase = CalibrationFile.Parse("100 0\n1000 1\n10000 -2\n");
        static double Delta(double frequency) => -6.0 * Math.Log10(frequency / 100.0);
        CalibrationFile angled = CalibrationFile.CreateAngled(narrowBase, Delta);
        CalibrationFile sampled = CalibrationFile.FromPoints(angled.Points);

        Assert.Equal(-2.0 + Delta(20_000.0), angled.GetDecibelCorrection(20_000.0), precision: 9);
        Assert.Equal(0.0 + Delta(20.0), angled.GetDecibelCorrection(20.0), precision: 9);

        foreach (double frequency in new[] { 5.0, 20.0, 50.0, 100.0, 5_000.0, 10_000.0, 20_000.0, 48_000.0, 96_000.0, 192_000.0 })
        {
            Assert.Equal(
                angled.GetDecibelCorrection(frequency),
                sampled.GetDecibelCorrection(frequency),
                precision: 2);
        }

        Assert.True(CalibrationFile.SameCurve(angled, sampled));
    }

    [Fact]
    public void SameCurve_ComparesContent_NotIdentityOrSpelling()
    {
        CalibrationFile a = CalibrationFile.Parse("20 0\n1000 1.5\n20000 -3\n");
        CalibrationFile sameText = CalibrationFile.Parse("20,0\n1000,1.5\n20000,-3\n");
        CalibrationFile differentLevel = CalibrationFile.Parse("20 0\n1000 1.6\n20000 -3\n");
        CalibrationFile extraPoint = CalibrationFile.Parse("20 0\n500 0.75\n1000 1.5\n20000 -3\n");

        Assert.True(CalibrationFile.SameCurve(a, a));
        Assert.True(CalibrationFile.SameCurve(a, sameText));
        Assert.True(CalibrationFile.SameCurve(a, CalibrationFile.FromPoints(a.Points)));
        Assert.False(CalibrationFile.SameCurve(a, differentLevel));
        Assert.False(CalibrationFile.SameCurve(a, extraPoint));
        Assert.True(CalibrationFile.SameCurve(null, null));
        Assert.False(CalibrationFile.SameCurve(a, null));
        Assert.False(CalibrationFile.SameCurve(null, a));
    }

    [Fact]
    public void ToText_RoundTripsThroughParse()
    {
        CalibrationFile original = CalibrationFile.Parse(
            "16 0.125\n1000 -0.3333333333333333\n20000 12.5\n");

        string text = original.ToText();
        CalibrationFile reread = CalibrationFile.Parse(text);

        Assert.Equal(3, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True(CalibrationFile.SameCurve(original, reread));
        Assert.Equal(
            original.GetDecibelCorrection(1000),
            reread.GetDecibelCorrection(1000),
            precision: 12);
    }

    [Fact]
    public void AscendingCorrections_ReadWhatGetDecibelCorrectionReads_BitForBit()
    {
        CalibrationFile plain = CalibrationFile.Parse("20 -1.5\n100 0.25\n1000 0\n5000 2\n20000 -3.75\n");
        CalibrationFile angled = CalibrationFile.CreateAngled(plain, frequency => Math.Log10(frequency) * 0.5);
        double[] frequencies =
        [
            5, 20, 20.5, 99.9, 100, 100, 101, 999, 1000, 4_000, 5_000, 19_999, 20_000, 30_000,
            // Out of order, which a reader must still answer.
            150, 30, 12_000
        ];

        foreach (CalibrationFile calibration in new[] { plain, angled })
        {
            Func<double, double> corrections = calibration.AscendingCorrections();
            foreach (double frequency in frequencies)
            {
                Assert.Equal(calibration.GetDecibelCorrection(frequency), corrections(frequency));
            }
        }
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
