using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class TuningSheetPdfTests
{
    [Fact]
    public void Export_WritesAValidPdfFile()
    {
        var curve = new EqualizationCurve(
            new[]
            {
                new PeqBand(600, 4.0, 6.0),
                new PeqBand(5582, 2.0, 4.9),
                new PeqBand(1577, 1.4, -4.1)
            },
            preampDb: -6.0);

        string path = Path.Combine(Path.GetTempPath(), $"tuning_{Guid.NewGuid():N}.pdf");
        try
        {
            var stats = new EqTuneStats(2.1, 5.4, 3, 3.2, -9.5, -3.2);
            TuningSheetPdf.Export(
                path, "LEFT MID", curve, 20, 20_000, 48_000, stats);

            Assert.True(File.Exists(path));
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

    /// <summary>PdfDocument.Save truncates on open; both exporters share PdfSheet.Save, which must not.</summary>
    [Fact]
    public void Export_OverAnExistingFile_NeverLeavesItTruncated()
    {
        var curve = new EqualizationCurve(
            new[] { new PeqBand(1000, 1.0, 3.0) }, preampDb: -3.0);
        string path = Path.Combine(Path.GetTempPath(), $"tuning_{Guid.NewGuid():N}.pdf");
        try
        {
            TuningSheetPdf.Export(path, "First", curve, 20, 20_000, 48_000, null);
            long firstLength = new FileInfo(path).Length;

            TuningSheetPdf.Export(path, "Second", curve, 20, 20_000, 48_000, null);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
            Assert.True(firstLength > 0);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Export_HandlesEmptyCurve()
    {
        var curve = new EqualizationCurve(Array.Empty<PeqBand>(), preampDb: 0);
        string path = Path.Combine(Path.GetTempPath(), $"tuning_{Guid.NewGuid():N}.pdf");
        try
        {
            TuningSheetPdf.Export(
                path, "Empty", curve, 40, 16_000, 48_000, null);
            Assert.True(new FileInfo(path).Length > 0);
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
