using OxyPlot;
using OxyPlot.WindowsForms;
using SkiaSharp;

namespace Resonalyze;

/// <summary>A <see cref="PlotView"/> drawn by Skia on the GPU; input, tracker and cursors stay the base's.</summary>
/// <remarks>GDI+ draws without a hardware OpenGL driver, after any GPU failure, and for DrawToBitmap. See docs/tech/plot-interaction.md#rendering.</remarks>
internal sealed class AcceleratedPlotView : PlotView
{
    private const int PrintClientMessage = 0x0318;
    private const int StencilBits = 8;
    private const uint Rgba8Format = 0x8058;

    private SkiaPlotRenderContext? renderContext;
    private WglContext? glContext;
    private GRContext? gpu;
    private GRBackendRenderTarget? renderTarget;
    private SKSurface? surface;
    private bool printing;
    private bool gpuRefused;
    private bool awaitingFreshWindow;
    private bool gpuAllowed = true;

    public AcceleratedPlotView()
    {
        // PlotView paints on a GDI+ back buffer, which would cover the GL frame after it is presented.
        SetGdiPainting(true);
    }

    /// <summary>Raised after every frame on either path; the base's Paint event is the GDI+ path's alone.</summary>
    public event EventHandler? FrameRendered;

    public bool IsAccelerated => gpu != null;

    /// <summary>The user's choice; a change takes effect at once, on a fresh window, and gives a failed GPU another try.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool GpuAllowed
    {
        get => gpuAllowed;
        set
        {
            if (gpuAllowed == value)
            {
                return;
            }

            gpuAllowed = value;
            gpuRefused = false;
            if (IsHandleCreated)
            {
                ReleaseGpu();
                SetGdiPainting(true);
                RecreateHandle();
            }
        }
    }

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
        awaitingFreshWindow = false;
        if (gpuRefused || !gpuAllowed)
        {
            return;
        }

        // Skia's native library loads here, not in the constructor: a machine it fails on still opens the window.
        bool pixelFormatSet = false;
        try
        {
            glContext = WglContext.TryCreate(Handle, out pixelFormatSet);
            GRGlInterface? glInterface = glContext == null ? null : GRGlInterface.Create();
            gpu = glInterface == null ? null : GRContext.CreateGl(glInterface);
            if (gpu != null)
            {
                renderContext ??= new SkiaPlotRenderContext();
                SetGdiPainting(false);
                return;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        RefuseGpu(pixelFormatSet);
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
        if ((gpu == null && !awaitingFreshWindow) || printing)
        {
            base.OnPaintBackground(pevent);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // An Update() delivers WM_PAINT before the queued RecreateHandle; the old window's GL format takes no GDI+.
        if (awaitingFreshWindow && !printing)
        {
            return;
        }

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
                RefuseGpu(windowHasGlFormat: true);
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
            renderContext?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RenderOnGpu()
    {
        if (glContext == null || gpu == null || renderContext == null || !glContext.MakeCurrent())
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

    // A window keeps its OpenGL pixel format until destroyed, so GDI+ gets a fresh one rather than drawing over GL's.
    private void RefuseGpu(bool windowHasGlFormat)
    {
        ReleaseGpu();
        gpuRefused = true;
        SetGdiPainting(true);
        if (windowHasGlFormat && IsHandleCreated)
        {
            awaitingFreshWindow = true;
            BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    RecreateHandle();
                }
            });
        }
        else
        {
            Invalidate();
        }
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
