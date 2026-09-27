using OxyPlot;
using OxyPlot.SkiaSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Microsoft.Win32;
using HorizontalAlignment = OxyPlot.HorizontalAlignment;

namespace Resonalyze;

/// <summary>OxyPlot's Skia context with text sized and font-substituted as the GDI+ <c>PlotView</c> does it.</summary>
/// <remarks>GDI+ reads a font size as <c>0.8 × size</c> points at the window's DPI; OxyPlot's Skia text reads pixels and has no fallback.</remarks>
internal sealed class SkiaPlotRenderContext : IRenderContext, IDisposable
{
    private const double GdiFontSizeFactor = 0.8;
    private const double PointsPerInch = 72;
    private const string FontLinkKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink";

    private readonly SkiaRenderContext inner = new() { RenderTarget = RenderTarget.Screen };
    private readonly SKPaint textPaint = new()
    {
        IsAntialias = true,
        HintingLevel = SKPaintHinting.Full,
        SubpixelText = true
    };

    private readonly Dictionary<(string Family, int Weight), SKTypeface> typefaces = [];
    private readonly Dictionary<(string Family, int Weight, int Codepoint), SKTypeface?> fallbacks = [];
    private readonly Dictionary<SKTypeface, SKShaper> shapers = [];
    private double textScale = 1;

    public SKCanvas Canvas
    {
        get => inner.SkCanvas;
        set => inner.SkCanvas = value;
    }

    public bool RendersToScreen => inner.RendersToScreen;

    public int ClipCount => inner.ClipCount;

    public void SetDpi(double dpi) => textScale = GdiFontSizeFactor * dpi / PointsPerInch;

    public void DrawText(
        ScreenPoint p,
        string text,
        OxyColor fill,
        string? fontFamily = null,
        double fontSize = 10,
        double fontWeight = FontWeights.Normal,
        double rotation = 0,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top,
        OxySize? maxSize = null)
    {
        if (text == null || !fill.IsVisible())
        {
            return;
        }

        string[] lines = SplitLines(text);
        SKTypeface primary = TypefaceOf(fontFamily, fontWeight);
        SKPaint paint = TextPaint(primary, fontSize);
        paint.Color = new SKColor(fill.R, fill.G, fill.B, fill.A);
        float lineHeight = paint.GetFontMetrics(out SKFontMetrics metrics);
        float y = verticalAlignment switch
        {
            VerticalAlignment.Top => -metrics.Ascent,
            VerticalAlignment.Middle => -(metrics.Ascent + metrics.Descent + lineHeight * (lines.Length - 1)) / 2,
            _ => -metrics.Descent - lineHeight * (lines.Length - 1)
        };

        using var restore = new SKAutoCanvasRestore(Canvas);
        Canvas.Translate((float)p.X, (float)p.Y);
        Canvas.RotateDegrees((float)rotation);
        // GDI+ aligns the block by its widest line and starts every line at the block's left edge.
        List<(string Text, SKTypeface Typeface)>[] lineRuns = [.. lines.Select(line => RunsOf(line, primary, fontWeight))];
        float blockWidth = lineRuns.Max(runs => Width(runs, paint));
        float left = horizontalAlignment switch
        {
            HorizontalAlignment.Left => 0,
            HorizontalAlignment.Center => -blockWidth / 2,
            _ => -blockWidth
        };
        foreach (List<(string Text, SKTypeface Typeface)> runs in lineRuns)
        {
            float x = left;
            foreach ((string run, SKTypeface typeface) in runs)
            {
                paint.Typeface = typeface;
                if (NeedsShaping(run))
                {
                    SKShaper shaper = ShaperOf(typeface);
                    Canvas.DrawShapedText(shaper, run, x, y, paint);
                    x += shaper.Shape(run, paint).Width;
                }
                else
                {
                    Canvas.DrawText(run, x, y, paint);
                    x += paint.MeasureText(run);
                }
            }

            paint.Typeface = primary;
            y += lineHeight;
        }
    }

    public OxySize MeasureText(string text, string? fontFamily = null, double fontSize = 10, double fontWeight = 500)
    {
        if (text == null)
        {
            return new OxySize(0, 0);
        }

        string[] lines = SplitLines(text);
        SKTypeface primary = TypefaceOf(fontFamily, fontWeight);
        SKPaint paint = TextPaint(primary, fontSize);
        float height = paint.GetFontMetrics(out _) * lines.Length;
        float width = lines.Max(line => Width(RunsOf(line, primary, fontWeight), paint));
        paint.Typeface = primary;
        return new OxySize(width, height);
    }

    public void DrawEllipse(OxyRect extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) =>
        inner.DrawEllipse(extents, fill, stroke, thickness, edgeRenderingMode);

    public void DrawEllipses(IList<OxyRect> extents, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) =>
        inner.DrawEllipses(extents, fill, stroke, thickness, edgeRenderingMode);

    public void DrawLine(
        IList<ScreenPoint> points, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode,
        double[]? dashArray = null, LineJoin lineJoin = LineJoin.Miter) =>
        inner.DrawLine(points, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawLineSegments(
        IList<ScreenPoint> points, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode,
        double[]? dashArray = null, LineJoin lineJoin = LineJoin.Miter) =>
        inner.DrawLineSegments(points, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawPolygon(
        IList<ScreenPoint> points, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode,
        double[]? dashArray = null, LineJoin lineJoin = LineJoin.Miter) =>
        inner.DrawPolygon(points, fill, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawPolygons(
        IList<IList<ScreenPoint>> polygons, OxyColor fill, OxyColor stroke, double thickness,
        EdgeRenderingMode edgeRenderingMode, double[]? dashArray = null, LineJoin lineJoin = LineJoin.Miter) =>
        inner.DrawPolygons(polygons, fill, stroke, thickness, edgeRenderingMode, dashArray, lineJoin);

    public void DrawRectangle(OxyRect rectangle, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) =>
        inner.DrawRectangle(rectangle, fill, stroke, thickness, edgeRenderingMode);

    public void DrawRectangles(IList<OxyRect> rectangles, OxyColor fill, OxyColor stroke, double thickness, EdgeRenderingMode edgeRenderingMode) =>
        inner.DrawRectangles(rectangles, fill, stroke, thickness, edgeRenderingMode);

    public void DrawImage(
        OxyImage source, double srcX, double srcY, double srcWidth, double srcHeight,
        double destX, double destY, double destWidth, double destHeight, double opacity, bool interpolate) =>
        inner.DrawImage(source, srcX, srcY, srcWidth, srcHeight, destX, destY, destWidth, destHeight, opacity, interpolate);

    public void SetToolTip(string text) => inner.SetToolTip(text);

    public void CleanUp() => inner.CleanUp();

    public void PushClip(OxyRect clippingRectangle) => inner.PushClip(clippingRectangle);

    public void PopClip() => inner.PopClip();

    public void Dispose()
    {
        inner.Dispose();
        textPaint.Dispose();
        foreach (SKTypeface typeface in typefaces.Values)
        {
            typeface.Dispose();
        }

        foreach (SKShaper shaper in shapers.Values)
        {
            shaper.Dispose();
        }

        foreach (SKTypeface? typeface in fallbacks.Values)
        {
            typeface?.Dispose();
        }
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private float Width(List<(string Text, SKTypeface Typeface)> runs, SKPaint paint)
    {
        float width = 0;
        foreach ((string run, SKTypeface typeface) in runs)
        {
            paint.Typeface = typeface;
            width += NeedsShaping(run) ? ShaperOf(typeface).Shape(run, paint).Width : paint.MeasureText(run);
        }

        return width;
    }

    // Latin, Greek, Cyrillic and symbols draw glyph by glyph, as GDI+ draws them; shaping them cost 0.6 ms a frame.
    private static bool NeedsShaping(string run)
    {
        foreach (char c in run)
        {
            if (c is (>= '\u0300' and < '\u0370') or (>= '\u0590' and < '\u2000') or >= '\u2C00')
            {
                return true;
            }
        }

        return false;
    }

    // Joins Arabic, forms Indic clusters, applies combining marks, as GDI+ does.
    private SKShaper ShaperOf(SKTypeface typeface)
    {
        if (!shapers.TryGetValue(typeface, out SKShaper? shaper))
        {
            shaper = new SKShaper(typeface);
            shapers.Add(typeface, shaper);
        }

        return shaper;
    }

    private SKPaint TextPaint(SKTypeface typeface, double fontSize)
    {
        textPaint.Typeface = typeface;
        textPaint.TextSize = (float)(fontSize * textScale);
        return textPaint;
    }

    private SKTypeface TypefaceOf(string? fontFamily, double fontWeight)
    {
        var key = (fontFamily ?? string.Empty, (int)fontWeight);
        if (!typefaces.TryGetValue(key, out SKTypeface? typeface))
        {
            typeface = SKTypeface.FromFamilyName(fontFamily, StyleOf(fontWeight));
            typefaces.Add(key, typeface);
        }

        return typeface;
    }

    // Splits a line where its font lacks a glyph, each piece in the font Windows would substitute, as GDI+ does.
    private List<(string Text, SKTypeface Typeface)> RunsOf(string line, SKTypeface primary, double fontWeight)
    {
        var runs = new List<(string Text, SKTypeface Typeface)>();
        var current = new System.Text.StringBuilder();
        SKTypeface currentTypeface = primary;
        foreach (System.Text.Rune rune in line.EnumerateRunes())
        {
            SKTypeface typeface = primary.GetGlyph(rune.Value) != 0
                ? primary
                : FallbackOf(primary, fontWeight, rune.Value) ?? primary;
            if (typeface != currentTypeface && current.Length > 0)
            {
                runs.Add((current.ToString(), currentTypeface));
                current.Clear();
            }

            currentTypeface = typeface;
            current.Append(rune.ToString());
        }

        if (current.Length > 0 || runs.Count == 0)
        {
            runs.Add((current.ToString(), currentTypeface));
        }

        return runs;
    }

    private SKTypeface? FallbackOf(SKTypeface primary, double fontWeight, int codepoint)
    {
        var key = (primary.FamilyName, (int)fontWeight, codepoint);
        if (!fallbacks.TryGetValue(key, out SKTypeface? typeface))
        {
            typeface = LinkedFallbackOf(primary.FamilyName, fontWeight, codepoint)
                ?? SKFontManager.Default.MatchCharacter(primary.FamilyName, StyleOf(fontWeight), null, codepoint);
            fallbacks.Add(key, typeface);
        }

        return typeface;
    }

    // Windows' font link chain is the order GDI+ tries; Skia's own match picks a font whose glyphs differ in width.
    private static SKTypeface? LinkedFallbackOf(string family, double fontWeight, int codepoint)
    {
        using RegistryKey? links = Registry.LocalMachine.OpenSubKey(FontLinkKey);
        if (links?.GetValue(family) is not string[] entries)
        {
            return null;
        }

        foreach (string entry in entries)
        {
            string[] fields = entry.Split(',');
            if (fields.Length < 2)
            {
                continue;
            }

            SKTypeface linked = SKTypeface.FromFamilyName(fields[1], StyleOf(fontWeight));
            if (linked.FamilyName == fields[1] && linked.GetGlyph(codepoint) != 0)
            {
                return linked;
            }

            linked.Dispose();
        }

        return null;
    }

    private static SKFontStyle StyleOf(double fontWeight) =>
        new((int)fontWeight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
}
