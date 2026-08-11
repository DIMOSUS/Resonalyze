namespace Resonalyze.App.Tests;

/// <summary>
/// The input meter's ballistics. The peak is an event — latched whole, held,
/// then decayed — and most of what is pinned here is some way of losing one.
/// </summary>
public sealed class InputLevelMeterBallisticsTests
{
    private const double Tick = 0.033;
    private const long TickMs = 33;

    private static InputLevelMeterEntry Entry(
        double peakDbFs,
        double rmsDbFs,
        bool clipped = false,
        bool fullScaleReference = false) =>
        new(true, peakDbFs, rmsDbFs, clipped, fullScaleReference);

    [Fact]
    public void Advance_LatchesAPeakOnTheVeryNextFrame()
    {
        long now = 1000;
        InputLevelMeterState state = InputLevelMeterState.CreateActive(Entry(-60, -60), now);

        state = InputLevelMeterBallistics.Advance(state, Entry(-6, -30), now + TickMs, Tick);

        // Smoothing the way up would show about -37 dBFS here, and would need
        // some 300 ms to arrive.
        Assert.Equal(-6, state.HoldPeakDbFs);
    }

    [Fact]
    public void Advance_CapturesATransientThatLivedInOneWindow()
    {
        long now = 1000;
        InputLevelMeterState state = InputLevelMeterState.CreateActive(Entry(-40, -46), now);

        now += TickMs;
        state = InputLevelMeterBallistics.Advance(state, Entry(-1, -44), now, Tick);
        Assert.Equal(-1, state.HoldPeakDbFs);

        // The window it lived in is long gone; the marker still reports it.
        for (int i = 0; i < 20; i++)
        {
            now += TickMs;
            state = InputLevelMeterBallistics.Advance(state, Entry(-40, -46), now, Tick);
            Assert.Equal(-1, state.HoldPeakDbFs);
        }
    }

    [Fact]
    public void Advance_HoldsForTheFullDurationThenFallsAtTheStatedRate()
    {
        long now = 1000;
        InputLevelMeterState state = InputLevelMeterState.CreateActive(Entry(-40, -46), now);
        now += TickMs;
        state = InputLevelMeterBallistics.Advance(state, Entry(-6, -30), now, Tick);
        long peakAt = now;

        // Every frame this loop runs falls inside the plateau.
        while (now + TickMs - peakAt <= InputLevelMeterBallistics.PeakHoldDurationMs)
        {
            now += TickMs;
            state = InputLevelMeterBallistics.Advance(state, Entry(-40, -46), now, Tick);
        }

        double atHoldEnd = state.HoldPeakDbFs;
        Assert.Equal(-6, atHoldEnd);

        long fallStart = now;
        while (now - fallStart < 330)
        {
            now += TickMs;
            state = InputLevelMeterBallistics.Advance(state, Entry(-40, -46), now, Tick);
        }

        double fallen = atHoldEnd - state.HoldPeakDbFs;
        double expected = InputLevelMeterBallistics.PeakHoldFallDbPerSecond * ((now - fallStart) / 1000.0);
        Assert.Equal(expected, fallen, 1);
    }

    [Fact]
    public void Advance_NeverFallsBelowTheCurrentLevel()
    {
        long now = 1000;
        InputLevelMeterState state = InputLevelMeterState.CreateActive(Entry(-6, -12), now);

        for (int i = 0; i < 200; i++)
        {
            now += TickMs;
            state = InputLevelMeterBallistics.Advance(state, Entry(-20, -26), now, Tick);
        }

        Assert.Equal(-20, state.HoldPeakDbFs);
    }

    [Theory]
    // A settled meter, digital silence pinned to the dB floor, and a loopback
    // pinned to full scale: each holds a peak exactly equal to the incoming
    // one, which a non-strict hold comparison would re-stamp every frame.
    [InlineData(-20, -26, false)]
    [InlineData(-160, -160, false)]
    [InlineData(0, -8, true)]
    public void Advance_LeavesASettledStateUntouched(
        double peakDbFs,
        double rmsDbFs,
        bool fullScaleReference)
    {
        long now = 1000;
        InputLevelMeterEntry target = Entry(peakDbFs, rmsDbFs, fullScaleReference: fullScaleReference);
        InputLevelMeterState state = InputLevelMeterState.CreateActive(target, now);
        for (int i = 0; i < 120; i++)
        {
            now += TickMs;
            state = InputLevelMeterBallistics.Advance(state, target, now, Tick);
        }

        // Equality is what lets the panel skip repainting an idle meter.
        InputLevelMeterState next = InputLevelMeterBallistics.Advance(state, target, now + TickMs, Tick);

        Assert.Equal(state, next);
    }

    [Fact]
    public void Advance_RateLimitsTheHoldButNotTheLevelAfterAStall()
    {
        long now = 1000;
        InputLevelMeterState state = InputLevelMeterState.CreateActive(Entry(-3, -20), now);

        // Two seconds of stalled message pump, then one frame.
        state = InputLevelMeterBallistics.Advance(state, Entry(-45, -50), now + 2000, 2.0);

        double maximumFall =
            InputLevelMeterBallistics.PeakHoldFallDbPerSecond *
            InputLevelMeterBallistics.MaximumHoldFallSeconds;
        Assert.Equal(-3 - maximumFall, state.HoldPeakDbFs, 3);
        // The level, unlike the hold, is not a display rate: it lands on what
        // the input is actually doing now.
        Assert.Equal(-50, state.DisplayedRmsDbFs, 1);
    }

    [Fact]
    public void Advance_PutsTheHeldPeakInTheReadout()
    {
        long now = 1000;
        InputLevelMeterState state = InputLevelMeterState.CreateActive(Entry(-40, -46), now);
        now += TickMs;
        state = InputLevelMeterBallistics.Advance(state, Entry(-2, -44), now, Tick);

        // The hold outlives the text interval, so the next update cannot miss it.
        long lastUpdate = state.LastTextUpdateMs;
        while (state.LastTextUpdateMs == lastUpdate)
        {
            now += TickMs;
            state = InputLevelMeterBallistics.Advance(state, Entry(-40, -46), now, Tick);
        }

        Assert.True(
            now - lastUpdate < InputLevelMeterBallistics.PeakHoldDurationMs,
            "the readout must refresh while the peak is still held");
        Assert.Equal(-2, state.TextPeakDbFs);
    }

    [Fact]
    public void IsAlarming_SeparatesAMicrophoneClipFromAReferenceAtFullScale()
    {
        long now = 1000;

        Assert.True(InputLevelMeterState
            .CreateActive(Entry(0, -8, clipped: true), now)
            .IsAlarming);
        Assert.False(InputLevelMeterState
            .CreateActive(Entry(0, -8, fullScaleReference: true), now)
            .IsAlarming);
    }

    [Fact]
    public void Advance_KeepsTheFullScaleFlagsUntilTheirPeakHasDecayed()
    {
        long now = 1000;
        InputLevelMeterState state = InputLevelMeterState.CreateActive(Entry(-30, -36), now);
        now += TickMs;
        state = InputLevelMeterBallistics.Advance(
            state, Entry(0, -8, fullScaleReference: true), now, Tick);

        // The reference has stepped off full scale, but its peak is still up:
        // re-reading the flag from this window would turn that peak red.
        now += TickMs;
        state = InputLevelMeterBallistics.Advance(state, Entry(-32, -38), now, Tick);
        Assert.True(state.HoldFullScaleReference);
        Assert.False(state.IsAlarming);

        for (int i = 0; i < 100; i++)
        {
            now += TickMs;
            state = InputLevelMeterBallistics.Advance(state, Entry(-32, -38), now, Tick);
        }

        Assert.False(state.HoldFullScaleReference);
    }

    [Fact]
    public void Advance_HoldsAnUnavailableStateSteady()
    {
        InputLevelMeterState state = InputLevelMeterState.CreateUnavailable();

        InputLevelMeterState next = InputLevelMeterBallistics.Advance(
            state, InputLevelMeterEntry.Unavailable, 1000, Tick);

        Assert.Equal(state, next);
        Assert.False(next.Available);
    }

    [Fact]
    public void Target_KeepsTheLoudestOfSeveralDrainsInOneFrame()
    {
        InputLevelMeterTarget target = InputLevelMeterTarget.Unavailable
            .Fold(Entry(-40, -46));

        // Posted callbacks outrank WM_TIMER, so both of these can be applied
        // before the animation gets a frame.
        target = target.Fold(Entry(-2, -44));
        target = target.Fold(Entry(-38, -45));

        Assert.Equal(-2, target.Pending.PeakDbFs);
        // The level, which floors the hold's decay, is the newest one.
        Assert.Equal(-38, target.Level.PeakDbFs);
    }

    [Fact]
    public void Target_DropsAPeakTheFrameHasAlreadyLatched()
    {
        InputLevelMeterTarget target = InputLevelMeterTarget.Unavailable
            .Fold(Entry(-40, -46))
            .Fold(Entry(-2, -44))
            .Fold(Entry(-38, -45))
            .Consume();

        // Left in the fold, that -2 dBFS would latch again once the hold had
        // decayed past it and report an event seconds old. What remains is the
        // newest window, which is a level, not an event.
        Assert.Equal(-38, target.Pending.PeakDbFs);
        Assert.Equal(target.Level, target.Pending);
    }

    [Fact]
    public void Target_SurvivesAFrameWithNoSnapshotBehindIt()
    {
        InputLevelMeterTarget target = InputLevelMeterTarget.Unavailable
            .Fold(Entry(-20, -26))
            .Consume()
            .Consume();

        // Consuming must not erase the level: it is what stops the hold sagging
        // below the signal on a frame that received nothing.
        Assert.Equal(-20, target.Pending.PeakDbFs);
    }
}
