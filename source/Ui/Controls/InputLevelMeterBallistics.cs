namespace Resonalyze;

/// <summary>Single (held) peak: with instant attack a smoothed current peak could only read lower than the hold.</summary>
internal readonly record struct InputLevelMeterState(
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
    public static InputLevelMeterState CreateUnavailable() => new(
        false,
        InputLevelMeterBallistics.MinimumDecibels,
        InputLevelMeterBallistics.MinimumDecibels,
        0,
        false,
        false,
        InputLevelMeterBallistics.MinimumDecibels,
        InputLevelMeterBallistics.MinimumDecibels,
        0);

    public static InputLevelMeterState CreateActive(
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

    /// <summary>A reference channel at full scale is expected, so only the microphone's clipping alarms.</summary>
    public bool IsAlarming =>
        HoldClipped ||
        (HoldPeakDbFs >= InputLevelMeterBallistics.WarningDecibels && !HoldFullScaleReference);
}

/// <summary>Current level plus a fold of peaks not yet latched: several drains can land between frames (posted callbacks outrank WM_TIMER),
/// while the hold's decay floor needs the current level, or it would sag and re-latch.</summary>
internal readonly record struct InputLevelMeterTarget(
    InputLevelMeterEntry Level,
    InputLevelMeterEntry Pending)
{
    public static InputLevelMeterTarget Unavailable => new(
        InputLevelMeterEntry.Unavailable,
        InputLevelMeterEntry.Unavailable);

    public InputLevelMeterTarget Fold(InputLevelMeterEntry entry) =>
        new(entry, Pending.Merge(entry));

    /// <summary>A peak left in the fold would re-latch seconds later once the hold decayed past it.</summary>
    public InputLevelMeterTarget Consume() => new(Level, Level);
}

/// <summary>Meter ballistics: the peak is an event to latch and decay, RMS a level to ease towards.</summary>
internal static class InputLevelMeterBallistics
{
    public const double MinimumDecibels = -60;
    public const double MaximumDecibels = 0;
    public const double WarningDecibels = -3;
    public const long PeakHoldDurationMs = 1050;
    public const long TextUpdateIntervalMs = 500;
    public const double PeakHoldFallDbPerSecond = 24;
    // Only the hold fall is rate-limited (a stalled pump would teleport the marker); RMS advances on true elapsed time.
    public const double MaximumHoldFallSeconds = 0.25;
    // Time constants, not per-tick fractions: WM_TIMER coalesces when the UI is busy. Equal to 0.42/0.12 at a 33 ms tick.
    private const double RmsAttackSeconds = 0.060;
    private const double RmsReleaseSeconds = 0.260;

    public static InputLevelMeterState Advance(
        InputLevelMeterState state,
        InputLevelMeterEntry target,
        long nowMs,
        double dt)
    {
        if (!target.Available)
        {
            return state.Available ? InputLevelMeterState.CreateUnavailable() : state;
        }

        if (!state.Available)
        {
            return InputLevelMeterState.CreateActive(target, nowMs);
        }

        double displayedRms = SmoothRms(state.DisplayedRmsDbFs, target.RmsDbFs, dt);

        // Instant latch: PeakDbFs is already the window's true maximum; easing would under-report transients by tens of dB.
        double holdPeak = state.HoldPeakDbFs;
        long holdTimestamp = state.HoldTimestampMs;
        // Flags follow the held peak, not the newest window.
        bool holdClipped = state.HoldClipped || target.Clipped;
        bool holdFullScale = state.HoldFullScaleReference || target.FullScaleReference;
        // Strictly greater, so a pinned floor or full-scale loopback does not re-stamp every frame and defeat the idle repaint skip.
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
            holdClipped = false;
            holdFullScale = false;
        }

        double textPeak = state.TextPeakDbFs;
        double textRms = state.TextRmsDbFs;
        long textTimestamp = state.LastTextUpdateMs;
        // Quotes the hold, which outlives the text interval (1050 > 500 ms), so every latched peak gets sampled.
        if (nowMs - textTimestamp >= TextUpdateIntervalMs &&
            (holdPeak != textPeak || displayedRms != textRms))
        {
            textPeak = holdPeak;
            textRms = displayedRms;
            textTimestamp = nowMs;
        }

        return new InputLevelMeterState(
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
}
