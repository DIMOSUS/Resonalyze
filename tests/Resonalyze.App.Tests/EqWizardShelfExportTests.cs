using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardShelfExportTests
{
    private static EqualizationCurve Mixed() => new(
        new[]
        {
            new PeqBand(1_000, 4.0, -6.0),
            new PeqBand(80, 0.7, 4.5, PeqBandType.LowShelf),
            new PeqBand(2_500, 3.0, -2.0),
            new PeqBand(6_300, 1.1, -3.5, PeqBandType.HighShelf)
        },
        -6.5);

    private static EqWizardExportTarget TargetFor(IEqProfileFormat format) => new(format);

    [Fact]
    public void AFormatThatCannotStateAShelfNeverReceivesOne()
    {
        string? written = null;
        var coordinator = new EqWizardImportExportCoordinator(
            EqProfileFormats.Importable,
            EqProfileFormats.Exportable,
            _ => string.Empty,
            (_, text) => written = text,
            _ => { });

        EqWizardFileResult result = coordinator.Export(new EqWizardExportRequest(
            "profile.json",
            TargetFor(new EasyEffectsFormat()),
            Mixed(),
            48_000,
            "title",
            20,
            20_000,
            null));

        Assert.True(result.Success);
        Assert.NotNull(written);
        Assert.Contains("1000", written);
        Assert.Contains("2500", written);
        Assert.DoesNotContain("80.0", written);
        Assert.DoesNotContain("6300", written);
    }

    [Fact]
    public void TheWarningCountsExactlyWhatWouldBeLost()
    {
        Assert.Equal(
            2,
            EqWizardImportExportCoordinator.CountShelvingBandsDroppedBy(
                TargetFor(new EasyEffectsFormat()), Mixed()));

        Assert.Equal(
            0,
            EqWizardImportExportCoordinator.CountShelvingBandsDroppedBy(
                TargetFor(new EqualizerApoFormat()), Mixed()));

        Assert.Equal(
            0,
            EqWizardImportExportCoordinator.CountShelvingBandsDroppedBy(
                TargetFor(new EasyEffectsFormat()),
                new EqualizationCurve(new[] { new PeqBand(1_000, 1, 3) })));
    }

    [Fact]
    public void WithoutShelvingBands_KeepsTheBellsInOrderAndThePreamp()
    {
        EqualizationCurve stripped =
            EqWizardImportExportCoordinator.WithoutShelvingBands(Mixed());

        Assert.Equal(2, stripped.Bands.Count);
        Assert.Equal(1_000, stripped.Bands[0].FrequencyHz);
        Assert.Equal(2_500, stripped.Bands[1].FrequencyHz);
        Assert.Equal(-6.5, stripped.PreampDb);
    }

    [Fact]
    public void TheSheetSplitsTheTablesButKeepsTheBankNumbering()
    {
        (IReadOnlyList<PdfSheet.NumberedBand> peaking,
            IReadOnlyList<PdfSheet.NumberedBand> shelving,
            IReadOnlyList<PdfSheet.NumberedBand> allPass) =
            PdfSheet.SplitByShape(Mixed().Bands);

        // Each filter keeps the panel's number rather than being renumbered inside its table.
        Assert.Equal(new[] { 1, 3 }, peaking.Select(entry => entry.Number));
        Assert.Equal(new[] { 2, 4 }, shelving.Select(entry => entry.Number));
        Assert.Empty(allPass);
        Assert.Equal(PeqBandType.LowShelf, shelving[0].Band.Type);
        Assert.Equal(PeqBandType.HighShelf, shelving[1].Band.Type);
    }

    // An out-of-range stored shape must be a bell to the realizer, the exporter and the sheet alike.
    [Fact]
    public void AnUnknownShapeIsABellToEveryConsumerAlike()
    {
        var unknown = new PeqBand(1_000, 2, 3, (PeqBandType)99);
        var curve = new EqualizationCurve(new[] { unknown });

        Assert.False(unknown.Type.IsShelving());
        Assert.Equal(
            PeakingBiquad.Compute(unknown, 48_000),
            PeqBiquad.Compute(unknown, 48_000));
        Assert.Equal(
            0,
            EqWizardImportExportCoordinator.CountShelvingBandsDroppedBy(
                TargetFor(new EasyEffectsFormat()), curve));
        Assert.Single(EqWizardImportExportCoordinator.WithoutShelvingBands(curve).Bands);
        (IReadOnlyList<PdfSheet.NumberedBand> peaking,
            IReadOnlyList<PdfSheet.NumberedBand> shelving,
            IReadOnlyList<PdfSheet.NumberedBand> allPass) = PdfSheet.SplitByShape(curve.Bands);
        Assert.Single(peaking);
        Assert.Empty(shelving);
        Assert.Empty(allPass);
    }

    [Fact]
    public void ASheetWithBothKindsOfFilterStillWrites()
    {
        string path = Path.Combine(Path.GetTempPath(), $"shelf_{Guid.NewGuid():N}.pdf");
        try
        {
            TuningSheetPdf.Export(path, "LEFT MID", Mixed(), 20, 20_000, 48_000, null);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
