using System.Drawing;
using OxyPlot;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

// The GPU plot keeps the GDI+ plot's layout only while text measures the same on both.
public sealed class SkiaPlotRenderContextTests
{
    [Theory]
    [InlineData("Frequency Response - l mid.json", 14, FontWeights.Bold)]
    [InlineData("-60", 12, FontWeights.Normal)]
    [InlineData("Noise floor (11,72 Hz BW)\nCalculated overlay 3", 12, FontWeights.Bold)]
    // U+2501 is the plot labels' swatch; Segoe UI lacks it, and GDI+ substitutes a font.
    [InlineData("\u2501\u2501 HD2", 12, FontWeights.Bold)]
    // Joined only when shaped: a measurement named in Arabic.
    [InlineData("\u0642\u064A\u0627\u0633 \u0627\u0644\u0633\u064A\u0627\u0631\u0629", 14, FontWeights.Bold)]
    public void MeasureText_MatchesTheGdiPlotView(string text, double fontSize, double fontWeight)
    {
        using var bitmap = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(bitmap);
        using var gdi = new GraphicsRenderContext(graphics);
        using var skia = new SkiaPlotRenderContext();
        skia.SetDpi(graphics.DpiY);

        OxySize expected = gdi.MeasureText(text, "Segoe UI", fontSize, fontWeight);
        OxySize actual = skia.MeasureText(text, "Segoe UI", fontSize, fontWeight);

        Assert.InRange(actual.Width, expected.Width * 0.92, expected.Width * 1.08);
        Assert.InRange(actual.Height, expected.Height * 0.92, expected.Height * 1.08);
    }
}
