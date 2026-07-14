using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardImportExportCoordinatorTests
{
    [Fact]
    public void TextProfileExportAndImport_RoundTripsThroughResolvedTargets()
    {
        string? written = null;
        var coordinator = CreateCoordinator(
            readAllText: _ => written!,
            writeAllText: (_, text) => written = text);
        var expected = new EqualizationCurve(
            [new PeqBand(800, 1.2, -3), new PeqBand(3200, 2.1, 1.5)],
            preampDb: -1);

        EqWizardFileResult export = coordinator.Export(CreateExportRequest(
            coordinator.ResolveExportTarget(1),
            expected));
        EqWizardFileResult<EqualizationCurve> import = coordinator.Import(
            new EqWizardImportRequest(
                "profile.txt",
                coordinator.ResolveImportTarget(1)));

        Assert.True(export.Success);
        Assert.True(import.Success);
        Assert.Equal(expected.PreampDb, import.Value!.PreampDb);
        Assert.Equal(expected.Bands, import.Value.Bands);
    }

    [Fact]
    public void GraphicEqExport_UsesRequestSampleRate()
    {
        string? written = null;
        var coordinator = CreateCoordinator(
            writeAllText: (_, text) => written = text);
        int graphicIndex = EqProfileFormats.Exportable
            .Select((format, index) => (format, index))
            .Single(item => item.format is GraphicEqFormat)
            .index + 1;
        var curve = new EqualizationCurve([new PeqBand(15000, 5, 6)]);

        EqWizardFileResult result = coordinator.Export(
            CreateExportRequest(
                coordinator.ResolveExportTarget(graphicIndex),
                curve,
                sampleRate: 96_000));

        Assert.True(result.Success);
        Assert.Equal(new GraphicEqFormat(96_000).Export(curve), written);
    }

    [Fact]
    public void TuningSheetTarget_DispatchesCompleteTypedRequest()
    {
        EqWizardTuningSheetRequest? captured = null;
        var coordinator = CreateCoordinator(
            exportTuningSheet: request => captured = request);
        EqWizardExportTarget target = coordinator.ResolveExportTarget(
            EqProfileFormats.Exportable.Count + 1);
        var curve = new EqualizationCurve([new PeqBand(1000, 1, -2)]);

        EqWizardFileResult result = coordinator.Export(new EqWizardExportRequest(
            "sheet.pdf",
            target,
            curve,
            48_000,
            "Front stage",
            80,
            16_000,
            new EqTuneStats(1, 2, 1, 0, -2, -1)));

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("sheet.pdf", captured.Path);
        Assert.Equal("Front stage", captured.Title);
        Assert.Same(curve, captured.Curve);
        Assert.Equal(80, captured.MinHz);
        Assert.Equal(16_000, captured.MaxHz);
        Assert.NotNull(captured.Stats);
    }

    [Fact]
    public void ImportAccessFailure_ReturnsTypedFailure()
    {
        var expected = new IOException("denied");
        var coordinator = CreateCoordinator(readAllText: _ => throw expected);

        EqWizardFileResult<EqualizationCurve> result = coordinator.Import(
            new EqWizardImportRequest(
                "profile.txt",
                coordinator.ResolveImportTarget(1)));

        Assert.False(result.Success);
        Assert.Same(expected, result.Exception);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ExportValidationFailure_ReturnsTypedFailureWithoutWriting()
    {
        bool wrote = false;
        var coordinator = CreateCoordinator(
            writeAllText: (_, _) => wrote = true);

        EqWizardFileResult result = coordinator.Export(
            CreateExportRequest(
                coordinator.ResolveExportTarget(1),
                new EqualizationCurve([]),
                sampleRate: 0));

        Assert.False(result.Success);
        Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
        Assert.False(wrote);
    }

    [Fact]
    public void FiltersAndResolutionKeepTuningSheetAsTrailingExportTarget()
    {
        var coordinator = CreateCoordinator();

        EqWizardExportTarget target = coordinator.ResolveExportTarget(
            EqProfileFormats.Exportable.Count + 1);

        Assert.True(target.IsTuningSheet);
        Assert.EndsWith("Tuning sheet (PDF) (*.pdf)|*.pdf", coordinator.ExportFilter);
        Assert.DoesNotContain("Tuning sheet", coordinator.ImportFilter);
    }

    private static EqWizardImportExportCoordinator CreateCoordinator(
        Func<string, string>? readAllText = null,
        Action<string, string>? writeAllText = null,
        Action<EqWizardTuningSheetRequest>? exportTuningSheet = null) =>
        new(
            EqProfileFormats.Importable,
            EqProfileFormats.Exportable,
            readAllText ?? (_ => string.Empty),
            writeAllText ?? ((_, _) => { }),
            exportTuningSheet ?? (_ => { }));

    private static EqWizardExportRequest CreateExportRequest(
        EqWizardExportTarget target,
        EqualizationCurve curve,
        double sampleRate = 48_000) =>
        new(
            "profile.txt",
            target,
            curve,
            sampleRate,
            "EQ",
            20,
            20_000,
            null);
}
