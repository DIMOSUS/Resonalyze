using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// What the export path does with all-pass bands a format cannot state: counts
// them per order (support genuinely splits there — Equalizer APO's AP is
// second-order only), warns in words that name what is actually lost, and drops
// them from the written file regardless of the warning — the guarantee.
public sealed class EqWizardAllPassExportTests
{
    private static EqualizationCurve Mixed() => new(
        new[]
        {
            new PeqBand(1_000, 4.0, -6.0),
            new PeqBand(120, 1.5, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(2_000, 1.0, 0, PeqBandType.AllPassFirstOrder)
        },
        -6.5);

    private static EqWizardExportTarget TargetFor(IEqProfileFormat format) => new(format);

    [Fact]
    public void TheCountFollowsPerOrderSupport()
    {
        // EasyEffects can state neither order; APO only the second; CamillaDSP both.
        Assert.Equal(2, EqWizardImportExportCoordinator.CountAllPassBandsDroppedBy(
            TargetFor(new EasyEffectsFormat()), Mixed()));
        Assert.Equal(1, EqWizardImportExportCoordinator.CountAllPassBandsDroppedBy(
            TargetFor(new EqualizerApoFormat()), Mixed()));
        Assert.Equal(0, EqWizardImportExportCoordinator.CountAllPassBandsDroppedBy(
            TargetFor(new CamillaDspYamlFormat()), Mixed()));
    }

    [Fact]
    public void TheWarningNamesTheFirstOrderWhenThatIsAllThatIsLost()
    {
        // Next to AP2 rows that do export, "cannot carry an all-pass" would be a
        // lie; the wording narrows to the order actually dropped.
        string? apo = EqExportWarnings.AllPassBandsDropped(
            TargetFor(new EqualizerApoFormat()), Mixed());
        Assert.NotNull(apo);
        Assert.Contains("first-order all-pass", apo);

        string? easyEffects = EqExportWarnings.AllPassBandsDropped(
            TargetFor(new EasyEffectsFormat()), Mixed());
        Assert.NotNull(easyEffects);
        Assert.DoesNotContain("first-order", easyEffects);

        // Nothing to warn about when the format carries them, or the bank has none.
        Assert.Null(EqExportWarnings.AllPassBandsDropped(
            TargetFor(new CamillaDspYamlFormat()), Mixed()));
        Assert.Null(EqExportWarnings.AllPassBandsDropped(
            TargetFor(new EasyEffectsFormat()),
            new EqualizationCurve(new[] { new PeqBand(1_000, 4.0, -6.0) })));
    }

    [Fact]
    public void AFormatThatCannotStateAnAllPassNeverReceivesOne()
    {
        string? written = null;
        var coordinator = new EqWizardImportExportCoordinator(
            EqProfileFormats.Importable,
            EqProfileFormats.Exportable,
            _ => string.Empty,
            (_, text) => written = text,
            _ => { });

        EqWizardFileResult result = coordinator.Export(new EqWizardExportRequest(
            "profile.txt",
            TargetFor(new EqualizerApoFormat()),
            Mixed(),
            48_000,
            "title",
            20,
            20_000,
            null));

        Assert.True(result.Success);
        Assert.NotNull(written);
        // The bell and the second-order all-pass survive; the first-order band is
        // not written as anything at all.
        Assert.Contains("ON PK Fc 1000 Hz", written);
        Assert.Contains("ON AP Fc 120 Hz Q 1.5", written);
        Assert.DoesNotContain("2000", written);
    }
}
