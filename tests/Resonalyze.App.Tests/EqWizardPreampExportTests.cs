using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// The other thing a device format can quietly fail to carry: the preamp. A car
// DSP's per-channel bank keeps the gain in a control of its own, so an export
// leaves the whole curve that many dB off the tune on screen — with nothing in the
// file to hint at it. The panel must be able to say exactly what is being left.
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

        // Nothing to warn about when the format carries it...
        Assert.Equal(
            0,
            EqWizardImportExportCoordinator.PreampDroppedBy(
                TargetFor(new EqualizerApoFormat()), Mixed()),
            9);

        // ...nor when the curve has no preamp to lose.
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
        // It prints them in tables of its own, so neither warning fires for it.
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
        // The bands themselves are all there — the loss is the gain, and only that.
        Assert.Contains("1000.0", written);
        Assert.Contains("80.0", written);
        Assert.Contains("6300.0", written);
    }
}
