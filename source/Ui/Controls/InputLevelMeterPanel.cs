using System.Drawing.Drawing2D;

namespace Resonalyze;

internal sealed class InputLevelMeterPanel : Control
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding |
        TextFormatFlags.EndEllipsis |
        TextFormatFlags.SingleLine |
        TextFormatFlags.VerticalCenter;
    private static readonly Color SurfaceColor = UiPalette.PlotSurfaceDark;
    private static readonly Color BorderColor = UiPalette.PlotBorder;
    private static readonly Color TrackColor = UiPalette.PlotTrack;
    private static readonly Color TextColor = UiPalette.MeterText;
    private static readonly Color MutedTextColor = UiPalette.MeterMutedText;
    private static readonly Color PeakHoldColor = UiPalette.MeterPeakHold;
    private const double MinimumDecibels = InputLevelMeterBallistics.MinimumDecibels;
    private const double MaximumDecibels = InputLevelMeterBallistics.MaximumDecibels;
    // Fill colour steps, read off the held peak.
    private const double HotDecibels = -12;
    private const double LoudDecibels = -24;
    private readonly System.Windows.Forms.Timer animationTimer;
    private InputLevelMeterTarget microphoneTarget = InputLevelMeterTarget.Unavailable;
    private InputLevelMeterTarget loopbackTarget = InputLevelMeterTarget.Unavailable;
    private InputLevelMeterState microphoneState = InputLevelMeterState.CreateUnavailable();
    private InputLevelMeterState loopbackState = InputLevelMeterState.CreateUnavailable();
    // Monotonic clock: a wall-clock (NTP/DST) step must not distort the
    // animation delta or the peak-hold timing.
    private long lastAnimationTickMs = Environment.TickCount64;

    public InputLevelMeterPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        BackColor = SurfaceColor;
        ForeColor = TextColor;

        animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 33
        };
        animationTimer.Tick += (_, _) => Animate();
        animationTimer.Start();
    }

    public void SetLevels(InputLevelMeterSnapshot levels)
    {
        microphoneTarget = microphoneTarget.Fold(levels.Microphone);
        loopbackTarget = loopbackTarget.Fold(levels.Loopback);
    }

    public void ClearLevels()
    {
        microphoneTarget = InputLevelMeterTarget.Unavailable;
        loopbackTarget = InputLevelMeterTarget.Unavailable;
        microphoneState = InputLevelMeterState.CreateUnavailable();
        loopbackState = InputLevelMeterState.CreateUnavailable();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);

        Graphics graphics = args.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        using var borderPen = new Pen(BorderColor);
        graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        (Rectangle micRow, Rectangle loopRow) = GetRowRectangles();
        DrawRow(
            graphics,
            "Mic",
            microphoneState,
            micRow);
        DrawRow(
            graphics,
            "Loop",
            loopbackState,
            loopRow);
    }

    private void DrawRow(
        Graphics graphics,
        string label,
        InputLevelMeterState state,
        Rectangle rowRectangle)
    {
        int padding = ScaleValue(2);
        int labelWidth = Math.Max(ScaleValue(28), TextRenderer.MeasureText(label, Font).Width);
        int textHeight = TextRenderer.MeasureText("0", Font).Height;
        int barHeight = Math.Max(ScaleValue(12), rowRectangle.Height - textHeight - ScaleValue(8));
        int barTop = rowRectangle.Bottom - barHeight - padding;
        Rectangle barRectangle = new(
            rowRectangle.Left,
            barTop,
            rowRectangle.Width,
            barHeight);
        Rectangle labelRectangle = new(
            rowRectangle.Left,
            rowRectangle.Top,
            labelWidth,
            Math.Max(textHeight, barTop - rowRectangle.Top - padding));
        Rectangle valueRectangle = new(
            labelRectangle.Right + ScaleValue(4),
            rowRectangle.Top,
            Math.Max(0, rowRectangle.Right - (labelRectangle.Right + ScaleValue(4))),
            labelRectangle.Height);

        Color textColor = state.Available ? TextColor : MutedTextColor;
        TextRenderer.DrawText(
            graphics,
            label,
            Font,
            labelRectangle,
            textColor,
            TextFlags | TextFormatFlags.Left);

        DrawTrack(graphics, barRectangle, state.Available);
        if (state.Available)
        {
            DrawRmsFill(graphics, barRectangle, state);
            DrawTicks(graphics, barRectangle, UiPalette.MeterGrid);
            DrawPeakMarker(graphics, barRectangle, state);
        }

        string valueText = FormatValueText(state, valueRectangle.Width);
        TextRenderer.DrawText(
            graphics,
            valueText,
            Font,
            valueRectangle,
            textColor,
            TextFlags | TextFormatFlags.Right);
    }

    private static void DrawTrack(Graphics graphics, Rectangle rectangle, bool active)
    {
        using var backgroundBrush = new SolidBrush(active ? TrackColor : UiPalette.MeterTrackInactive);
        graphics.FillRectangle(backgroundBrush, rectangle);
        using var borderPen = new Pen(active ? BorderColor : UiPalette.MeterBorderInactive);
        graphics.DrawRectangle(borderPen, rectangle);
        DrawTicks(graphics, rectangle, UiPalette.MeterBand);
    }

    private static void DrawTicks(
        Graphics graphics,
        Rectangle rectangle,
        Color tickColor)
    {
        Rectangle innerRectangle = Rectangle.Inflate(rectangle, -1, -1);
        using var tickPen = new Pen(tickColor, 1);

        for (int db = (int)MinimumDecibels + 5; db < MaximumDecibels; db += 5)
        {
            int x = GetTrackX(innerRectangle, db);
            graphics.DrawLine(tickPen, x, innerRectangle.Top, x, innerRectangle.Bottom);
        }
    }

    /// <summary>
    /// The one dB→pixel mapping of the track. Fill, ticks and the peak marker
    /// all read the same scale, so the edge of the fill lines up with the tick
    /// it has just reached.
    /// </summary>
    private static int GetTrackX(Rectangle innerRectangle, double valueDbFs) =>
        innerRectangle.Left + (int)Math.Round((innerRectangle.Width - 1) * Normalize(valueDbFs));

    private static void DrawRmsFill(
        Graphics graphics,
        Rectangle rectangle,
        InputLevelMeterState state)
    {
        Rectangle innerRectangle = Rectangle.Inflate(rectangle, -1, -1);
        int width = GetTrackX(innerRectangle, state.DisplayedRmsDbFs) - innerRectangle.Left;
        if (width <= 0)
        {
            return;
        }

        Rectangle fillRectangle = new(
            innerRectangle.Left,
            innerRectangle.Top,
            width,
            innerRectangle.Height);
        using var brush = new SolidBrush(GetRmsColor(state));
        graphics.FillRectangle(brush, fillRectangle);
    }

    private static void DrawPeakMarker(
        Graphics graphics,
        Rectangle rectangle,
        InputLevelMeterState state)
    {
        Rectangle innerRectangle = Rectangle.Inflate(rectangle, -1, -1);
        int x = GetTrackX(innerRectangle, state.HoldPeakDbFs);
        using var markerPen = new Pen(GetPeakMarkerColor(state), 2);
        graphics.DrawLine(markerPen, x, rectangle.Top - 1, x, rectangle.Bottom + 1);
    }

    private static Color GetRmsColor(InputLevelMeterState state)
    {
        if (state.IsAlarming)
        {
            return UiPalette.WarningRed;
        }
        if (state.HoldPeakDbFs >= HotDecibels)
        {
            return UiPalette.WarningOrange;
        }

        return state.HoldPeakDbFs >= LoudDecibels
            ? UiPalette.SuccessGreenAlt
            : UiPalette.MeterLowAccent;
    }

    private static Color GetPeakMarkerColor(InputLevelMeterState state) =>
        state.IsAlarming
            ? UiPalette.ErrorSoftTint
            : PeakHoldColor;

    private static double Normalize(double valueDbFs)
    {
        double clamped = Math.Clamp(valueDbFs, MinimumDecibels, MaximumDecibels);
        return (clamped - MinimumDecibels) / (MaximumDecibels - MinimumDecibels);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        lastAnimationTickMs = Environment.TickCount64;
    }

    protected override Size DefaultSize => new(150, 88);

    private void Animate()
    {
        long now = Environment.TickCount64;
        double dt = Math.Max((now - lastAnimationTickMs) / 1000.0, 0.001);
        lastAnimationTickMs = now;

        InputLevelMeterState newMicrophoneState = InputLevelMeterBallistics.Advance(
            microphoneState, microphoneTarget.Pending, now, dt);
        InputLevelMeterState newLoopbackState = InputLevelMeterBallistics.Advance(
            loopbackState, loopbackTarget.Pending, now, dt);
        // Unconditionally, including on the idle path below: this frame has
        // latched whatever the fold was carrying.
        microphoneTarget = microphoneTarget.Consume();
        loopbackTarget = loopbackTarget.Consume();
        if (newMicrophoneState == microphoneState &&
            newLoopbackState == loopbackState)
        {
            // Idle meters (no measurement running) must not repaint at 30 Hz.
            return;
        }

        microphoneState = newMicrophoneState;
        loopbackState = newLoopbackState;
        Invalidate();
    }

    private (Rectangle Mic, Rectangle Loop) GetRowRectangles()
    {
        int outerPadding = ScaleValue(6);
        int rowGap = ScaleValue(4);
        int availableHeight = Math.Max(0, Height - outerPadding * 2 - rowGap);
        int rowHeight = availableHeight / 2;
        int rowWidth = Math.Max(0, Width - outerPadding * 2);
        Rectangle mic = new(
            outerPadding,
            outerPadding,
            rowWidth,
            rowHeight);
        Rectangle loop = new(
            outerPadding,
            outerPadding + rowHeight + rowGap,
            rowWidth,
            rowHeight);
        return (mic, loop);
    }

    private string FormatValueText(InputLevelMeterState state, int availableWidth)
    {
        if (!state.Available)
        {
            return "--.- / --.- dBFS";
        }

        string fullText = $"{state.TextPeakDbFs,5:0.0} / {state.TextRmsDbFs,5:0.0} dBFS";
        if (TextRenderer.MeasureText(fullText, Font).Width <= availableWidth)
        {
            return fullText;
        }

        string compactText = $"{state.TextPeakDbFs:0.0}/{state.TextRmsDbFs:0.0} dBFS";
        return compactText;
    }

    private int ScaleValue(int value) =>
        (int)Math.Round(value * DeviceDpi / 96.0);
}
