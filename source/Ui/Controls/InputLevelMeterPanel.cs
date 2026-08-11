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
    private const long PeakHoldDurationMs = 1050;
    private const long TextUpdateIntervalMs = 500;
    private const double MinimumDecibels = -60;
    private const double MaximumDecibels = 0;
    // Fill colour steps, read off the held peak.
    private const double WarningDecibels = -3;
    private const double HotDecibels = -12;
    private const double LoudDecibels = -24;
    // RMS ballistics as time constants rather than per-tick fractions: a fixed
    // fraction makes the meter's speed depend on how often the timer actually
    // fires, and WM_TIMER coalesces whenever the UI thread is busy — which it is
    // during a measurement. These match the previous factors at a nominal
    // 33 ms tick (0.42 attack, 0.12 release), so the meter still looks the same.
    private const double RmsAttackSeconds = 0.060;
    private const double RmsReleaseSeconds = 0.260;
    private const double PeakHoldFallDbPerSecond = 24;
    // The hold's fall is a display rate, not a physical one, so it alone is
    // rate-limited: after a stalled message pump, settling seconds of decay in
    // a single frame would teleport the marker across the track. Everything
    // else advances on true elapsed time — RMS is a level, and the level the
    // input is at now is the honest thing to show once the pump recovers.
    private const double MaximumHoldFallSeconds = 0.25;
    private readonly System.Windows.Forms.Timer animationTimer;
    // The newest snapshot as received: the current level, which stands until the
    // next one replaces it.
    private InputLevelMeterEntry microphoneLevel = InputLevelMeterEntry.Unavailable;
    private InputLevelMeterEntry loopbackLevel = InputLevelMeterEntry.Unavailable;
    // The same, folded with the peaks and full-scale flags no frame has latched
    // yet. Animate reads these, then drops them back to the level above.
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

        animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 33
        };
        animationTimer.Tick += (_, _) => Animate();
        animationTimer.Start();
    }

    public void SetLevels(InputLevelMeterSnapshot levels)
    {
        // Fold rather than overwrite. Several dispatcher drains can land between
        // two animation ticks — they arrive as posted callbacks, which outrank
        // the low-priority WM_TIMER the animation runs on — and the peak of a
        // window the timer never got to look at is exactly the one worth
        // keeping. Animate consumes the fold once it has latched it.
        microphoneLevel = levels.Microphone;
        loopbackLevel = levels.Loopback;
        microphoneTarget = microphoneTarget.Merge(levels.Microphone);
        loopbackTarget = loopbackTarget.Merge(levels.Loopback);
    }

    public void ClearLevels()
    {
        microphoneLevel = InputLevelMeterEntry.Unavailable;
        loopbackLevel = InputLevelMeterEntry.Unavailable;
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
        MeterVisualState state)
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
        MeterVisualState state)
    {
        Rectangle innerRectangle = Rectangle.Inflate(rectangle, -1, -1);
        int x = GetTrackX(innerRectangle, state.HoldPeakDbFs);
        using var markerPen = new Pen(GetPeakMarkerColor(state), 2);
        graphics.DrawLine(markerPen, x, rectangle.Top - 1, x, rectangle.Bottom + 1);
    }

    /// <summary>
    /// Whether the held peak is the kind of hot the user has to act on. A
    /// reference channel sitting at full scale is the expected condition, not a
    /// fault, so it alarms on nothing but the microphone's own clipping.
    /// </summary>
    private static bool IsAlarming(MeterVisualState state) =>
        state.HoldClipped ||
        (state.HoldPeakDbFs >= WarningDecibels && !state.HoldFullScaleReference);

    private static Color GetRmsColor(MeterVisualState state)
    {
        if (IsAlarming(state))
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

    private static Color GetPeakMarkerColor(MeterVisualState state) =>
        IsAlarming(state)
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

        MeterVisualState newMicrophoneState =
            AdvanceState(microphoneState, microphoneTarget, now, dt);
        MeterVisualState newLoopbackState =
            AdvanceState(loopbackState, loopbackTarget, now, dt);
        // Drop back to the plain level, unconditionally — including on the idle
        // path below. A peak left in the fold would latch again on some later
        // tick, once the hold had decayed past it, and report an event that is
        // seconds old.
        microphoneTarget = microphoneLevel;
        loopbackTarget = loopbackLevel;
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

        double displayedRms = SmoothRms(state.DisplayedRmsDbFs, target.RmsDbFs, dt);

        // The peak latches instantly. target.PeakDbFs is already the true
        // maximum of one 30 Hz meter window, so easing towards it would report a
        // transient tens of dB below the sample that caused it — a single loud
        // window would never be shown at all.
        double holdPeak = state.HoldPeakDbFs;
        long holdTimestamp = state.HoldTimestampMs;
        // The full-scale flags describe the peak being held, not the newest
        // window: re-reading them every tick turns a reference channel red the
        // moment it steps off full scale, while its own peak is still on screen.
        bool holdClipped = state.HoldClipped || target.Clipped;
        bool holdFullScale = state.HoldFullScaleReference || target.FullScaleReference;
        // Strictly greater: at equality — digital silence pinned to the dB
        // floor, or a loopback pinned to full scale — re-stamping the hold would
        // make every tick "change" the state and defeat the idle repaint skip in
        // Animate.
        if (target.PeakDbFs > holdPeak)
        {
            holdPeak = target.PeakDbFs;
            holdTimestamp = nowMs;
        }
        else if (nowMs - holdTimestamp > PeakHoldDurationMs)
        {
            holdPeak = Math.Max(
                target.PeakDbFs,
                holdPeak - PeakHoldFallDbPerSecond * Math.Min(dt, MaximumHoldFallSeconds));
        }

        if (holdPeak < WarningDecibels)
        {
            // The peak that earned the flags has decayed out of the warning
            // zone; they expire with it.
            holdClipped = false;
            holdFullScale = false;
        }

        double textPeak = state.TextPeakDbFs;
        double textRms = state.TextRmsDbFs;
        long textTimestamp = state.LastTextUpdateMs;
        // The readout quotes the hold, not the live window: the hold outlives
        // the text interval by design (1050 > 500 ms), so every latched peak is
        // still standing when the next update samples it. Re-stamping only on a
        // real change keeps a settled meter comparing equal — see Animate.
        if (nowMs - textTimestamp >= TextUpdateIntervalMs &&
            (holdPeak != textPeak || displayedRms != textRms))
        {
            textPeak = holdPeak;
            textRms = displayedRms;
            textTimestamp = nowMs;
        }

        return new MeterVisualState(
            true,
            displayedRms,
            holdPeak,
            holdTimestamp,
            holdClipped,
            holdFullScale,
            textPeak,
            textRms,
            textTimestamp);
    }

    private static double SmoothRms(double current, double target, double dt)
    {
        double seconds = target > current ? RmsAttackSeconds : RmsReleaseSeconds;
        double alpha = 1.0 - Math.Exp(-dt / seconds);
        return current + (target - current) * alpha;
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

    /// <summary>
    /// What one row draws. There is a single peak here — the held one — because
    /// with an instant attack a separately smoothed "current" peak would only
    /// ever read lower than the hold it is derived from.
    /// </summary>
    private readonly record struct MeterVisualState(
        bool Available,
        double DisplayedRmsDbFs,
        double HoldPeakDbFs,
        long HoldTimestampMs,
        bool HoldClipped,
        bool HoldFullScaleReference,
        double TextPeakDbFs,
        double TextRmsDbFs,
        long LastTextUpdateMs)
    {
        public static MeterVisualState CreateUnavailable() => new(
            false,
            MinimumDecibels,
            MinimumDecibels,
            0,
            false,
            false,
            MinimumDecibels,
            MinimumDecibels,
            0);

        public static MeterVisualState CreateActive(
            InputLevelMeterEntry target,
            long nowMs) => new(
            true,
            target.RmsDbFs,
            target.PeakDbFs,
            nowMs,
            target.Clipped,
            target.FullScaleReference,
            target.PeakDbFs,
            target.RmsDbFs,
            nowMs);
    }
}
