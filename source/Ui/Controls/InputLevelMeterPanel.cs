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
    private static readonly Color FullScaleColor = UiPalette.MeterFullScale;
    private static readonly Color DimFillColor = UiPalette.MeterDimFill;
    private const long PeakHoldDurationMs = 700;
    private const long TextUpdateIntervalMs = 500;
    private const double MinimumDecibels = -60;
    private const double MaximumDecibels = 0;
    private const double AttackFactor = 0.42;
    private const double ReleaseFactor = 0.12;
    private const double PeakHoldFallDbPerSecond = 24;
    private readonly System.Windows.Forms.Timer animationTimer;
    private InputLevelMeterEntry microphoneTarget = InputLevelMeterEntry.Unavailable;
    private InputLevelMeterEntry loopbackTarget = InputLevelMeterEntry.Unavailable;
    private MeterVisualState microphoneState = MeterVisualState.CreateUnavailable();
    private MeterVisualState loopbackState = MeterVisualState.CreateUnavailable();
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
        Font = new Font("Segoe UI", 8.75f, FontStyle.Bold, GraphicsUnit.Point);

        animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 33
        };
        animationTimer.Tick += (_, _) => Animate();
        animationTimer.Start();
    }

    public void SetLevels(InputLevelMeterSnapshot levels)
    {
        microphoneTarget = levels.Microphone;
        loopbackTarget = levels.Loopback;
    }

    public void ClearLevels()
    {
        microphoneTarget = InputLevelMeterEntry.Unavailable;
        loopbackTarget = InputLevelMeterEntry.Unavailable;
        microphoneState = MeterVisualState.CreateUnavailable();
        loopbackState = MeterVisualState.CreateUnavailable();
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
        MeterVisualState state,
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
            int x = innerRectangle.Left + (int)Math.Round((innerRectangle.Width - 1) * Normalize(db));
            graphics.DrawLine(tickPen, x, innerRectangle.Top, x, innerRectangle.Bottom);
        }
    }

    private static void DrawRmsFill(
        Graphics graphics,
        Rectangle rectangle,
        MeterVisualState state)
    {
        int width = (int)Math.Round(rectangle.Width * Normalize(state.DisplayedRmsDbFs));
        if (width <= 0)
        {
            return;
        }

        Rectangle fillRectangle = new(rectangle.X + 1, rectangle.Y + 1, Math.Min(width, rectangle.Width - 2), rectangle.Height - 2);
        using var brush = new SolidBrush(GetRmsColor(state));
        graphics.FillRectangle(brush, fillRectangle);
    }

    private static void DrawPeakMarker(
        Graphics graphics,
        Rectangle rectangle,
        MeterVisualState state)
    {
        Rectangle innerRectangle = Rectangle.Inflate(rectangle, -1, -1);
        int x = innerRectangle.Left + (int)Math.Round((innerRectangle.Width - 1) * Normalize(state.HoldPeakDbFs));
        using var markerPen = new Pen(GetPeakMarkerColor(state), 2);
        graphics.DrawLine(markerPen, x, rectangle.Top - 1, x, rectangle.Bottom + 1);
    }

    private static Color GetRmsColor(MeterVisualState state)
    {
        if (state.Clipped || state.DisplayedPeakDbFs >= -3)
        {
            return UiPalette.WarningRed;
        }
        if (state.DisplayedPeakDbFs >= -12)
        {
            return UiPalette.WarningOrange;
        }
        if (state.DisplayedPeakDbFs >= -24)
        {
            return UiPalette.SuccessGreenAlt;
        }

        return state.Available
            ? UiPalette.MeterLowAccent
            : DimFillColor;
    }

    private static Color GetPeakMarkerColor(MeterVisualState state)
    {
        if (state.FullScaleReference)
        {
            return PeakHoldColor;
        }

        return state.Clipped || state.HoldPeakDbFs >= -3
            ? UiPalette.ErrorSoftTint
            : PeakHoldColor;
    }

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

        MeterVisualState newMicrophoneState =
            AdvanceState(microphoneState, microphoneTarget, now, dt);
        MeterVisualState newLoopbackState =
            AdvanceState(loopbackState, loopbackTarget, now, dt);
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

    private static MeterVisualState AdvanceState(
        MeterVisualState state,
        InputLevelMeterEntry target,
        long nowMs,
        double dt)
    {
        if (!target.Available)
        {
            // Keep the existing unavailable state so idle ticks compare equal.
            return state.Available ? MeterVisualState.CreateUnavailable() : state;
        }

        if (!state.Available)
        {
            return MeterVisualState.CreateActive(target, nowMs);
        }

        double displayedPeak = Smooth(state.DisplayedPeakDbFs, target.PeakDbFs);
        double displayedRms = Smooth(state.DisplayedRmsDbFs, target.RmsDbFs);

        double holdPeak = state.HoldPeakDbFs;
        long holdTimestamp = state.HoldTimestampMs;
        // Strictly greater: at equality (a fully converged meter) re-stamping
        // the hold would make every tick "change" the state and defeat the
        // idle repaint skip in Animate.
        if (displayedPeak > holdPeak)
        {
            holdPeak = displayedPeak;
            holdTimestamp = nowMs;
        }
        else if (nowMs - holdTimestamp > PeakHoldDurationMs)
        {
            holdPeak = Math.Max(
                displayedPeak,
                holdPeak - PeakHoldFallDbPerSecond * dt);
        }

        double textPeak = state.TextPeakDbFs;
        double textRms = state.TextRmsDbFs;
        long textTimestamp = state.LastTextUpdateMs;
        if (nowMs - state.LastTextUpdateMs >= TextUpdateIntervalMs)
        {
            textPeak = displayedPeak;
            textRms = displayedRms;
            textTimestamp = nowMs;
        }

        return new MeterVisualState(
            true,
            displayedPeak,
            displayedRms,
            holdPeak,
            holdTimestamp,
            textPeak,
            textRms,
            textTimestamp,
            target.Clipped,
            target.FullScaleReference);
    }

    private static double Smooth(double current, double target)
    {
        double factor = target > current ? AttackFactor : ReleaseFactor;
        return current + (target - current) * factor;
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

    private string FormatValueText(MeterVisualState state, int availableWidth)
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

    private readonly record struct MeterVisualState(
        bool Available,
        double DisplayedPeakDbFs,
        double DisplayedRmsDbFs,
        double HoldPeakDbFs,
        long HoldTimestampMs,
        double TextPeakDbFs,
        double TextRmsDbFs,
        long LastTextUpdateMs,
        bool Clipped,
        bool FullScaleReference)
    {
        public static MeterVisualState CreateUnavailable() => new(
            false,
            MinimumDecibels,
            MinimumDecibels,
            MinimumDecibels,
            0,
            MinimumDecibels,
            MinimumDecibels,
            0,
            false,
            false);

        public static MeterVisualState CreateActive(
            InputLevelMeterEntry target,
            long nowMs) => new(
            true,
            target.PeakDbFs,
            target.RmsDbFs,
            target.PeakDbFs,
            nowMs,
            target.PeakDbFs,
            target.RmsDbFs,
            nowMs,
            target.Clipped,
            target.FullScaleReference);
    }
}
