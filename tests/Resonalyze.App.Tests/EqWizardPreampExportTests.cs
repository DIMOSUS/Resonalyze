using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// Car DSP banks keep gain in a separate control, so an export silently drops the preamp.
public sealed class EqWizardPreampExportTests
{
    private static EqualizationCurve Mixed() => new(
        new[]
        {
            new PeqBand(1_000, 4.0, -6.0),
            new PeqBand(80, 0.7, 4.5, PeqBandType.LowShelf),
            new PeqBand(6_300, 1.1, -3.5, PeqBandType.HighShelf)
        },
        -6.5);

    private static EqWizardExportTarget TargetFor(IEqProfileFormat format) => new(format);

    [Fact]
    public void TheWarningReportsExactlyTheGainThatWouldBeLost()
    {
        Assert.Equal(
            -6.5,
            EqWizardImportExportCoordinator.PreampDroppedBy(
                TargetFor(new AudiotecFischerFormat()), Mixed()),
            9);

        Assert.Equal(
            0,
            EqWizardImportExportCoordinator.PreampDroppedBy(
                TargetFor(new EqualizerApoFormat()), Mixed()),
            9);

        Assert.Equal(
            0,
            EqWizardImportExportCoordinator.PreampDroppedBy(
                TargetFor(new AudiotecFischerFormat()),
                new EqualizationCurve(new[] { new PeqBand(1_000, 1, 3) })),
            9);
    }

    [Fact]
    public void TheTuningSheetCarriesThePreampAndTheShelves()
    {
        EqWizardExportTarget sheet = EqWizardExportTarget.TuningSheet();

        Assert.Equal(0, EqWizardImportExportCoordinator.PreampDroppedBy(sheet, Mixed()), 9);
        Assert.Equal(
            0,
            EqWizardImportExportCoordinator.CountShelvingBandsDroppedBy(sheet, Mixed()));
    }

    [Fact]
    public void ThePreampIsAbsentFromTheExportedBankItself()
    {
        string? written = null;
        var coordinator = new EqWizardImportExportCoordinator(
            EqProfileFormats.Importable,
            EqProfileFormats.Exportable,
            _ => string.Empty,
            (_, text) => written = text,
            _ => { });

        EqWizardFileResult result = coordinator.Export(new EqWizardExportRequest(
            "bank.txt",
            TargetFor(new AudiotecFischerFormat()),
            Mixed(),
            96_000,
            "title",
            20,
            20_000,
            null));

        Assert.True(result.Success);
        Assert.NotNull(written);
        Assert.DoesNotContain("-6.5", written);
        Assert.Contains("1000.0", written);
        Assert.Contains("80.0", written);
        Assert.Contains("6300.0", written);
    }
}
