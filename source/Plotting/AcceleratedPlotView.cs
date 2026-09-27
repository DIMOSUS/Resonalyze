using OxyPlot;
using OxyPlot.WindowsForms;
using SkiaSharp;

namespace Resonalyze;

/// <summary>A <see cref="PlotView"/> drawn by Skia on the GPU; input, tracker and cursors stay the base's.</summary>
/// <remarks>GDI+ draws without a hardware OpenGL driver, after a failed frame, and for DrawToBitmap. See docs/tech/plot-interaction.md#rendering.</remarks>
internal sealed class AcceleratedPlotView : PlotView
{
    private const int PrintClientMessage = 0x0318;
    private const int StencilBits = 8;
    private const uint Rgba8Format = 0x8058;

    private readonly SkiaPlotRenderContext renderContext = new();
    private WglContext? glContext;
    private GRContext? gpu;
    private GRBackendRenderTarget? renderTarget;
    private SKSurface? surface;
    private bool printing;

    public AcceleratedPlotView()
    {
        // PlotView paints on a GDI+ back buffer, which would cover the GL frame after it is presented.
        SetGdiPainting(true);
    }

    /// <summary>Raised after every frame on either path; the base's Paint event is the GDI+ path's alone.</summary>
    public event EventHandler? FrameRendered;

    public bool IsAccelerated => gpu != null;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= WglContext.OwnDeviceContextClassStyle;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        glContext = WglContext.TryCreate(Handle);
        gpu = glContext == null ? null : GRContext.CreateGl(GRGlInterface.Create());
        if (gpu == null)
        {
            ReleaseGpu();
            return;
        }

        SetGdiPainting(false);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseGpu();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg != PrintClientMessage)
        {
            base.WndProc(ref m);
            return;
        }

        printing = true;
        try
        {
            base.WndProc(ref m);
        }
        finally
        {
            printing = false;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        if (gpu == null || printing)
        {
            base.OnPaintBackground(pevent);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (gpu == null || printing)
        {
            base.OnPaint(e);
        }
        else
        {
            try
            {
                RenderOnGpu();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One failed frame, driver or model, hands this window to GDI+ for good.
                ReleaseGpu();
                SetGdiPainting(true);
                Invalidate();
                return;
            }
        }

        FrameRendered?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseGpu();
            renderContext.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RenderOnGpu()
    {
        if (glContext == null || gpu == null || !glContext.MakeCurrent())
        {
            throw new InvalidOperationException("The OpenGL context could not be made current.");
        }

        int width = Math.Max(1, ClientSize.Width);
        int height = Math.Max(1, ClientSize.Height);
        SKCanvas canvas = SurfaceOf(width, height).Canvas;
        canvas.Clear(ToSkia(BackColor));

        PlotModel? model = Model;
        PlotViewPaintState.ApplyPendingUpdate(this);
        if (model != null)
        {
            if (!model.Background.IsUndefined())
            {
                using var background = new SKPaint { Color = ToSkia(model.Background) };
                canvas.DrawRect(0, 0, width, height, background);
            }

            renderContext.Canvas = canvas;
            renderContext.SetDpi(DeviceDpi);
            ((IPlotModel)model).Render(renderContext, new OxyRect(0, 0, width, height));
        }

        DrawZoomRectangle(canvas);
        canvas.Flush();
        gpu.Flush();
        glContext.SwapBuffers();
    }

    private SKSurface SurfaceOf(int width, int height)
    {
        if (surface != null && renderTarget != null &&
            renderTarget.Width == width && renderTarget.Height == height)
        {
            return surface;
        }

        surface?.Dispose();
        renderTarget?.Dispose();
        renderTarget = new GRBackendRenderTarget(width, height, 0, StencilBits, new GRGlFramebufferInfo(0, Rgba8Format));
        surface = SKSurface.Create(gpu, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("Skia could not wrap the OpenGL frame buffer.");
        return surface;
    }

    private void DrawZoomRectangle(SKCanvas canvas)
    {
        Rectangle zoom = PlotViewPaintState.ZoomRectangle(this);
        if (zoom == Rectangle.Empty)
        {
            return;
        }

        var bounds = new SKRect(zoom.Left, zoom.Top, zoom.Right, zoom.Bottom);
        using var fill = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0x00, 0x40) };
        using var dashes = SKPathEffect.CreateDash([3, 1], 0);
        using var outline = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, PathEffect = dashes };
        canvas.DrawRect(bounds, fill);
        canvas.DrawRect(bounds, outline);
    }

    private void SetGdiPainting(bool gdi)
    {
        SetStyle(ControlStyles.Opaque, !gdi);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        DoubleBuffered = gdi;
    }

    private void ReleaseGpu()
    {
        // Without the context current, the GPU objects can only be dropped, not freed through it.
        if (glContext?.MakeCurrent() != true)
        {
            gpu?.AbandonContext();
        }

        surface?.Dispose();
        renderTarget?.Dispose();
        gpu?.Dispose();
        surface = null;
        renderTarget = null;
        gpu = null;
        glContext?.Dispose();
        glContext = null;
    }

    private static SKColor ToSkia(Color color) => new(color.R, color.G, color.B, color.A);

    private static SKColor ToSkia(OxyColor color) => new(color.R, color.G, color.B, color.A);
}
