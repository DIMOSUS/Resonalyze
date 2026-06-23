using System.Drawing.Drawing2D;

namespace Resonalyze;

internal sealed class InputLevelMeterPanel : Control
{
    private const TextFormatFlags TextFlags =
        TextFormatFlags.NoPadding |
        TextFormatFlags.EndEllipsis |
        TextFormatFlags.SingleLine |
        TextFormatFlags.VerticalCenter;
    private static readonly Color SurfaceColor = Color.FromArgb(38, 42, 52);
    private static readonly Color BorderColor = Color.FromArgb(78, 84, 98);
    private static readonly Color TrackColor = Color.FromArgb(24, 28, 36);
    private static readonly Color TextColor = Color.FromArgb(225, 230, 240);
    private static readonly Color MutedTextColor = Color.FromArgb(128, 135, 150);
    private static readonly Color PeakHoldColor = Color.FromArgb(248, 248, 252);
    private static readonly Color FullScaleColor = Color.FromArgb(95, 200, 255);
    private static readonly Color DimFillColor = Color.FromArgb(80, 86, 100);
    private static readonly TimeSpan PeakHoldDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan TextUpdateInterval = TimeSpan.FromMilliseconds(500);
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
    private DateTime lastAnimationTickUtc = DateTime.UtcNow;

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
            DrawOverlayTicks(graphics, barRectangle);
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
        using var backgroundBrush = new SolidBrush(active ? TrackColor : Color.FromArgb(30, 34, 42));
        graphics.FillRectangle(backgroundBrush, rectangle);
        using var borderPen = new Pen(active ? BorderColor : Color.FromArgb(56, 60, 70));
        graphics.DrawRectangle(borderPen, rectangle);
        DrawScaleTicks(graphics, rectangle, active);
    }

    private static void DrawScaleTicks(
        Graphics graphics,
        Rectangle rectangle,
        bool active)
    {
        Rectangle innerRectangle = Rectangle.Inflate(rectangle, -1, -1);
        Color tickColor = active
            ? Color.FromArgb(127, 12, 14, 18)
            : Color.FromArgb(127, 12, 14, 18);
        using var tickPen = new Pen(tickColor, 1);

        for (int db = (int)MinimumDecibels + 5; db < MaximumDecibels; db += 5)
        {
            int x = innerRectangle.Left + (int)Math.Round((innerRectangle.Width - 1) * Normalize(db));
            graphics.DrawLine(tickPen, x, innerRectangle.Top, x, innerRectangle.Bottom);
        }
    }

    private static void DrawOverlayTicks(
        Graphics graphics,
        Rectangle rectangle)
    {
        Rectangle innerRectangle = Rectangle.Inflate(rectangle, -1, -1);
        using var tickPen = new Pen(Color.FromArgb(90, 18, 20, 26), 1);

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
            return Color.FromArgb(255, 96, 96);
        }
        if (state.DisplayedPeakDbFs >= -12)
        {
            return Color.FromArgb(255, 196, 76);
        }
        if (state.DisplayedPeakDbFs >= -24)
        {
            return Color.FromArgb(136, 224, 112);
        }

        return state.Available
            ? Color.FromArgb(88, 182, 255)
            : DimFillColor;
    }

    private static Color GetPeakMarkerColor(MeterVisualState state)
    {
        if (state.FullScaleReference)
        {
            return PeakHoldColor;
        }

        return state.Clipped || state.HoldPeakDbFs >= -3
            ? Color.FromArgb(255, 210, 210)
            : PeakHoldColor;
    }

    private static double Normalize(double valueDbFs)
    {
        double clamped = Math.Clamp(valueDbFs, MinimumDecibels, MaximumDecibels);
        return (clamped - MinimumDecibels) / (MaximumDecibels - MinimumDecibels);
    }

    private static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        int r = (int)Math.Round(a.R + (b.R - a.R) * amount);
        int g = (int)Math.Round(a.G + (b.G - a.G) * amount);
        int bl = (int)Math.Round(a.B + (b.B - a.B) * amount);
        return Color.FromArgb(r, g, bl);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        lastAnimationTickUtc = DateTime.UtcNow;
    }

    protected override Size DefaultSize => new(150, 88);

    private void Animate()
    {
        DateTime now = DateTime.UtcNow;
        double dt = Math.Max((now - lastAnimationTickUtc).TotalSeconds, 0.001);
        lastAnimationTickUtc = now;

        microphoneState = AdvanceState(microphoneState, microphoneTarget, now, dt);
        loopbackState = AdvanceState(loopbackState, loopbackTarget, now, dt);
        Invalidate();
    }

    private static MeterVisualState AdvanceState(
        MeterVisualState state,
        InputLevelMeterEntry target,
        DateTime now,
        double dt)
    {
        if (!target.Available)
        {
            return MeterVisualState.CreateUnavailable();
        }

        if (!state.Available)
        {
            return MeterVisualState.CreateActive(target, now);
        }

        double displayedPeak = Smooth(state.DisplayedPeakDbFs, target.PeakDbFs);
        double displayedRms = Smooth(state.DisplayedRmsDbFs, target.RmsDbFs);

        double holdPeak = state.HoldPeakDbFs;
        DateTime holdTimestamp = state.HoldTimestampUtc;
        if (displayedPeak >= holdPeak)
        {
            holdPeak = displayedPeak;
            holdTimestamp = now;
        }
        else if (now - holdTimestamp > PeakHoldDuration)
        {
            holdPeak = Math.Max(
                displayedPeak,
                holdPeak - PeakHoldFallDbPerSecond * dt);
        }

        double textPeak = state.TextPeakDbFs;
        double textRms = state.TextRmsDbFs;
        DateTime textTimestamp = state.LastTextUpdateUtc;
        if (now - state.LastTextUpdateUtc >= TextUpdateInterval)
        {
            textPeak = displayedPeak;
            textRms = displayedRms;
            textTimestamp = now;
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
        DateTime HoldTimestampUtc,
        double TextPeakDbFs,
        double TextRmsDbFs,
        DateTime LastTextUpdateUtc,
        bool Clipped,
        bool FullScaleReference)
    {
        public static MeterVisualState CreateUnavailable() => new(
            false,
            MinimumDecibels,
            MinimumDecibels,
            MinimumDecibels,
            DateTime.UtcNow,
            MinimumDecibels,
            MinimumDecibels,
            DateTime.UtcNow,
            false,
            false);

        public static MeterVisualState CreateActive(
            InputLevelMeterEntry target,
            DateTime now) => new(
            true,
            target.PeakDbFs,
            target.RmsDbFs,
            target.PeakDbFs,
            now,
            target.PeakDbFs,
            target.RmsDbFs,
            now,
            target.Clipped,
            target.FullScaleReference);
    }
}
