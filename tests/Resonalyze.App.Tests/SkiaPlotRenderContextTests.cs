using System.Drawing;
using OxyPlot;
using OxyPlot.WindowsForms;
using SkiaSharp;
using HorizontalAlignment = OxyPlot.HorizontalAlignment;

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

    [Theory]
    [InlineData("Frequency Response - a very long measurement file name from the left door woofer at 90 cm.json")]
    [InlineData("Frequency Response - averyveryveryveryveryveryveryveryverylongnamewithoutspaces.json")]
    public void DrawText_ClipsATitleToItsMaxSizeAsGdiDoes(string title)
    {
        // PlotModel clips its title to 90 % of the title area, and the analyzer's title carries the file name.
        const int Width = 520;
        const double MaxWidth = 300;
        var anchor = new ScreenPoint(Width / 2.0, 10);
        var limit = new OxySize(MaxWidth, double.MaxValue);

        using var gdiBitmap = new Bitmap(Width, 40);
        using (var graphics = Graphics.FromImage(gdiBitmap))
        using (var gdi = new GraphicsRenderContext(graphics))
        {
            graphics.Clear(Color.Black);
            gdi.DrawText(anchor, title, OxyColors.White, "Segoe UI", 14, FontWeights.Bold, 0,
                HorizontalAlignment.Center, VerticalAlignment.Top, limit);
        }

        using var surface = SKSurface.Create(new SKImageInfo(Width, 40));
        using var skia = new SkiaPlotRenderContext { Canvas = surface.Canvas };
        skia.SetDpi(96);
        surface.Canvas.Clear(SKColors.Black);
        skia.DrawText(anchor, title, OxyColors.White, "Segoe UI", 14, FontWeights.Bold, 0,
            HorizontalAlignment.Center, VerticalAlignment.Top, limit);
        using SKImage image = surface.Snapshot();
        using var pixels = SKBitmap.FromImage(image);

        (int Left, int Right) expected = InkColumns(Width, x => Enumerable.Range(0, 40).Any(y => gdiBitmap.GetPixel(x, y).R > 128));
        (int Left, int Right) actual = InkColumns(Width, x => Enumerable.Range(0, 40).Any(y => pixels.GetPixel(x, y).Red > 128));
        Assert.InRange(actual.Left, expected.Left - 3, expected.Left + 3);
        Assert.InRange(actual.Right, expected.Right - 12, expected.Right + 12);
        Assert.True(actual.Right <= Width / 2 + MaxWidth / 2);
    }

    private static (int Left, int Right) InkColumns(int width, Func<int, bool> inked)
    {
        int[] columns = [.. Enumerable.Range(0, width).Where(inked)];
        return (columns[0], columns[^1]);
    }
}
