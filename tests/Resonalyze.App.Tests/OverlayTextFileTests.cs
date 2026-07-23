using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlayTextFileTests
{
    [Fact]
    public void ExportThenImport_RoundTripsPoints()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        OverlayPoint[] original =
        [
            new OverlayPoint(20, -10.5),
            new OverlayPoint(1_000, -2.25),
            new OverlayPoint(20_000, -30.0)
        ];

        try
        {
            OverlayTextFile.Export(path, original);
            OverlayPoint[] loaded = OverlayTextFile.Import(path);

            Assert.Equal(original, loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildCurveMetadata_LabelsACapturedResponseAndKeepsItsKind()
    {
        OverlayTextMetadata metadata = OverlayTextFile.BuildCurveMetadata(
            OverlayKind.Captured,
            AnalysisCurveKind.Primary,
            MagnitudeScale.SoundPressureLevel,
            48_000,
            "Overlay 1");

        Assert.Equal(OverlayCurveRole.Response, metadata.Role);
        Assert.Equal(AnalysisCurveKind.Primary, metadata.CurveKind);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, metadata.Scale);
        Assert.Equal(48_000, metadata.SampleRateHz);
    }

    [Theory]
    [InlineData(OverlayKind.Target, OverlayCurveRole.Target)]
    [InlineData(OverlayKind.Operation, OverlayCurveRole.Calculated)]
    public void BuildCurveMetadata_LabelsDerivedSlotsAndDropsAStaleResponseKind(
        OverlayKind kind,
        OverlayCurveRole expectedRole)
    {
        // A slot converted from a captured Primary can still carry that kind; the export
        // must label the role from the slot's real nature and NOT ship the stale kind, or
        // the EQ Wizard would read a target/operation as a measured Primary response.
        OverlayTextMetadata metadata = OverlayTextFile.BuildCurveMetadata(
            kind,
            AnalysisCurveKind.Primary,
            MagnitudeScale.Relative,
            48_000,
            "Derived");

        Assert.Equal(expectedRole, metadata.Role);
        Assert.Null(metadata.CurveKind);
        // And the wizard refuses what it exports.
        Assert.False(
            EqWizardSourceResolver.IsEqualizableResponse(metadata.Role, metadata.CurveKind));
    }

    [Fact]
    public void Import_IgnoresCommentsAndBlankLinesAndExtraColumns()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        File.WriteAllText(
            path,
            "# comment\n; another\n\n100   -3.0   ignored\n200\t-4.5\n");

        try
        {
            OverlayPoint[] loaded = OverlayTextFile.Import(path);

            Assert.Equal(
                [new OverlayPoint(100, -3.0), new OverlayPoint(200, -4.5)],
                loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_AcceptsCommaSemicolonAndTabSeparatorsAndSkipsGarbage()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        File.WriteAllText(
            path,
            "freq,gain\n100,-3.0\n200; -4.5\n300\t-6.0\nnot a pair\n400 abc\n");

        try
        {
            OverlayPoint[] loaded = OverlayTextFile.Import(path);

            Assert.Equal(
                [
                    new OverlayPoint(100, -3.0),
                    new OverlayPoint(200, -4.5),
                    new OverlayPoint(300, -6.0)
                ],
                loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_RejectsFileWithFewerThanTwoPoints()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overlay-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "# only one\n1000 -3.0\n");

        try
        {
            Assert.Throws<InvalidDataException>(() => OverlayTextFile.Import(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExportThenImportCurve_RoundTripsTheHeader()
    {
        string path = CreateTemporaryPath();
        var metadata = new OverlayTextMetadata(
            OverlayCurveRole.Response,
            AnalysisCurveKind.InputSpectrum,
            MagnitudeScale.SoundPressureLevel,
            96_000,
            "Overlay 3: Input Spectrum (RTA)");

        try
        {
            OverlayTextFile.Export(path, SamplePoints, metadata);
            OverlayTextCurve loaded = OverlayTextFile.ImportCurve(path);

            Assert.Equal(metadata, loaded.Metadata);
            Assert.Equal(SamplePoints, loaded.Points);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Export_WritesNoHeaderWhenNothingIsDeclared()
    {
        string path = CreateTemporaryPath();
        try
        {
            OverlayTextFile.Export(path, SamplePoints, OverlayTextMetadata.Empty);

            Assert.DoesNotContain(
                File.ReadAllLines(path),
                line => line.StartsWith('#'));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportCurve_LeavesTheHeaderEmptyForAForeignFile()
    {
        string path = CreateTemporaryPath();
        // What another measurement tool exports: a comment banner and bare pairs.
        File.WriteAllText(path, "* Exported by SomeTool\n100 -3.0\n200 -4.5\n");

        try
        {
            OverlayTextCurve loaded = OverlayTextFile.ImportCurve(path);

            Assert.True(loaded.Metadata.IsEmpty);
            Assert.Equal(2, loaded.Points.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportCurve_KeepsHeaderLinesOutOfThePoints()
    {
        string path = CreateTemporaryPath();
        // A header key whose value parses as a number must not be read as a data pair.
        File.WriteAllText(
            path,
            "# resonalyze-curve v1\n# sample-rate: 48000\n100 -3.0\n200 -4.5\n");

        try
        {
            OverlayTextCurve loaded = OverlayTextFile.ImportCurve(path);

            Assert.Equal(48_000, loaded.Metadata.SampleRateHz);
            Assert.Equal(
                [new OverlayPoint(100, -3.0), new OverlayPoint(200, -4.5)],
                loaded.Points);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportCurve_IgnoresUnknownKeysAndUnparsableValues()
    {
        string path = CreateTemporaryPath();
        File.WriteAllText(
            path,
            "# resonalyze-curve v9\n" +
            "# future-key: whatever\n" +
            "# kind: NotACurveKind\n" +
            "# sample-rate: nonsense\n" +
            "# scale: SoundPressureLevel\n" +
            "100 -3.0\n200 -4.5\n");

        try
        {
            OverlayTextCurve loaded = OverlayTextFile.ImportCurve(path);

            // The one key this build understands survives; the rest are simply not stated.
            Assert.Equal(MagnitudeScale.SoundPressureLevel, loaded.Metadata.Scale);
            Assert.Null(loaded.Metadata.CurveKind);
            Assert.Null(loaded.Metadata.SampleRateHz);
            Assert.Equal(2, loaded.Points.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Export_KeepsAMultiLineTitleFromForgingHeaderLines()
    {
        string path = CreateTemporaryPath();
        var metadata = new OverlayTextMetadata(
            Title: "Overlay 1\n# scale: SoundPressureLevel");

        try
        {
            OverlayTextFile.Export(path, SamplePoints, metadata);
            OverlayTextCurve loaded = OverlayTextFile.ImportCurve(path);

            Assert.Null(loaded.Metadata.Scale);
            Assert.Equal("Overlay 1 # scale: SoundPressureLevel", loaded.Metadata.Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static readonly OverlayPoint[] SamplePoints =
    [
        new OverlayPoint(20, -10.5),
        new OverlayPoint(1_000, -2.25),
        new OverlayPoint(20_000, -30.0)
    ];

    private static string CreateTemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"overlay-{Guid.NewGuid():N}.txt");
}
