using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardProfileFileServiceTests
{
    [Fact]
    public void ExportThenImport_RoundTripsTextProfileWithoutWinForms()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-eq-{Guid.NewGuid():N}.txt");
        try
        {
            var format = new EqualizerApoFormat();
            var expected = new EqualizationCurve(
                [new PeqBand(800, 1.2, -3), new PeqBand(3200, 2.1, 1.5)],
                preampDb: -1);

            EqWizardProfileFileService.Export(path, format, expected, sampleRate: 48000);
            EqualizationCurve actual = EqWizardProfileFileService.Import(path, format);

            Assert.Equal(expected.PreampDb, actual.PreampDb);
            Assert.Equal(expected.Bands, actual.Bands);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Export_RejectsInvalidSampleRate()
    {
        var curve = new EqualizationCurve([]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EqWizardProfileFileService.Export(
                "unused.txt",
                new EqualizerApoFormat(),
                curve,
                sampleRate: 0));
    }

    [Fact]
    public void Export_GraphicEqUsesRequestedDigitalSampleRate()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-graphic-eq-{Guid.NewGuid():N}.txt");
        try
        {
            var curve = new EqualizationCurve([new PeqBand(15000, 5, 6)]);

            EqWizardProfileFileService.Export(
                path,
                new GraphicEqFormat(),
                curve,
                sampleRate: 96000);

            Assert.Equal(
                new GraphicEqFormat(96000).Export(curve),
                File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
