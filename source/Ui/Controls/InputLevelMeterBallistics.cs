namespace Resonalyze;

/// <summary>
/// What one meter row draws. There is a single peak here — the held one —
/// because with an instant attack a separately smoothed "current" peak could
/// only ever read lower than the hold it is derived from.
/// </summary>
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

    /// <summary>
    /// Whether the held peak is the kind of hot the user has to act on. A
    /// reference channel sitting at full scale is the expected condition, not a
    /// fault, so it alarms on nothing but the microphone's own clipping.
    /// </summary>
    public bool IsAlarming =>
        HoldClipped ||
        (HoldPeakDbFs >= InputLevelMeterBallistics.WarningDecibels && !HoldFullScaleReference);
}

/// <summary>
/// The two entries one row's animation reads: the newest snapshot as the
/// current level, and that level folded with the peaks and full-scale flags no
/// frame has latched yet.
/// </summary>
/// <remarks>
/// The two have to stay apart. Several dispatcher drains can land between two
/// animation frames — they arrive as posted callbacks, which outrank the
/// low-priority WM_TIMER the animation runs on — so a frame must see the
/// loudest window among them, not just the last. But the hold's decay floor
/// wants the current level: on a frame with no snapshot behind it, flooring on
/// a consumed fold would let the hold sag and re-latch on the next one.
/// </remarks>
internal readonly record struct InputLevelMeterTarget(
    InputLevelMeterEntry Level,
    InputLevelMeterEntry Pending)
{
    public static InputLevelMeterTarget Unavailable => new(
        InputLevelMeterEntry.Unavailable,
        InputLevelMeterEntry.Unavailable);

    /// <summary>Takes a newly arrived snapshot, folding it into the pending events.</summary>
    public InputLevelMeterTarget Fold(InputLevelMeterEntry entry) =>
        new(entry, Pending.Merge(entry));

    /// <summary>
    /// Drops what a frame has latched. A peak left in the fold would latch
    /// again on some later frame, once the hold had decayed past it, and report
    /// an event that is seconds old.
    /// </summary>
    public InputLevelMeterTarget Consume() => new(Level, Level);
}

/// <summary>
/// The meter's ballistics: how one row's displayed state advances from the
/// levels the audio layer publishes. Peak and RMS are treated as different
/// kinds of quantity — the peak is an event to latch and then let decay, the
/// RMS a level to ease towards — which is the whole reason this reads the way
/// it does.
/// </summary>
internal static class InputLevelMeterBallistics
{
    /// <summary>Bottom of the meter's scale, and where an idle row rests.</summary>
    public const double MinimumDecibels = -60;
    public const double MaximumDecibels = 0;
    /// <summary>At or above this, a held peak is worth alarming about.</summary>
    public const double WarningDecibels = -3;
    public const long PeakHoldDurationMs = 1050;
    public const long TextUpdateIntervalMs = 500;
    public const double PeakHoldFallDbPerSecond = 24;
    // The hold's fall is a display rate, not a physical one, so it alone is
    // rate-limited: after a stalled message pump, settling seconds of decay in
    // a single frame would teleport the marker across the track. Everything
    // else advances on true elapsed time — RMS is a level, and the level the
    // input is at now is the honest thing to show once the pump recovers.
    public const double MaximumHoldFallSeconds = 0.25;
    // RMS ballistics as time constants rather than per-tick fractions: a fixed
    // fraction makes the meter's speed depend on how often the timer actually
    // fires, and WM_TIMER coalesces whenever the UI thread is busy — which it
    // is during a measurement. These match the factors this replaced at a
    // nominal 33 ms tick (0.42 attack, 0.12 release).
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
            // Keep the existing unavailable state so idle frames compare equal.
            return state.Available ? InputLevelMeterState.CreateUnavailable() : state;
        }

        if (!state.Available)
        {
            return InputLevelMeterState.CreateActive(target, nowMs);
        }

        double displayedRms = SmoothRms(state.DisplayedRmsDbFs, target.RmsDbFs, dt);

        // The peak latches instantly. target.PeakDbFs is already the true
        // maximum of one 30 Hz meter window, so easing towards it would report a
        // transient tens of dB below the sample that caused it — a single loud
        // window would never be shown at all.
        double holdPeak = state.HoldPeakDbFs;
        long holdTimestamp = state.HoldTimestampMs;
        // The full-scale flags describe the peak being held, not the newest
        // window: re-reading them every frame turns a reference channel red the
        // moment it steps off full scale, while its own peak is still on screen.
        bool holdClipped = state.HoldClipped || target.Clipped;
        bool holdFullScale = state.HoldFullScaleReference || target.FullScaleReference;
        // Strictly greater: at equality — digital silence pinned to the dB
        // floor, or a loopback pinned to full scale — re-stamping the hold would
        // make every frame "change" the state and defeat the caller's idle
        // repaint skip.
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
        // real change keeps a settled meter comparing equal.
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
