namespace Resonalyze.Audio.Tests;

public sealed class WasapiRenderTimingTests
{
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(40);

    // Stopping on the event after the final buffer cut that buffer: the device had only begun to play it.
    [Fact]
    public void AnExclusiveStreamPlaysOneSilentPeriodAfterTheFinalBufferBeforeItStops()
    {
        Assert.Equal(WasapiExclusiveRenderStep.Fill, WasapiRenderTiming.NextExclusiveStep(false, false));
        Assert.Equal(WasapiExclusiveRenderStep.Silence, WasapiRenderTiming.NextExclusiveStep(true, false));
        Assert.Equal(WasapiExclusiveRenderStep.Finish, WasapiRenderTiming.NextExclusiveStep(true, true));
    }

    [Fact]
    public void EmptyPaddingAtNormalExclusiveCallbackIsNotUnderrun()
    {
        bool underrun = WasapiRenderTiming.IsUnderrun(
            currentPaddingFrames: 0,
            sourceEnded: false,
            elapsedSinceFill: TimeSpan.FromMilliseconds(40),
            BufferDuration);

        Assert.False(underrun);
    }

    [Fact]
    public void EmptyPaddingAfterMissedDeadlineIsUnderrun()
    {
        bool underrun = WasapiRenderTiming.IsUnderrun(
            currentPaddingFrames: 0,
            sourceEnded: false,
            elapsedSinceFill: TimeSpan.FromMilliseconds(61),
            BufferDuration);

        Assert.True(underrun);
    }

    [Fact]
    public void BufferedAudioIsNotUnderrunAfterDelayedCallback()
    {
        bool underrun = WasapiRenderTiming.IsUnderrun(
            currentPaddingFrames: 1,
            sourceEnded: false,
            elapsedSinceFill: TimeSpan.FromMilliseconds(80),
            BufferDuration);

        Assert.False(underrun);
    }

    [Fact]
    public void DrainedCompletedSourceIsNotUnderrun()
    {
        bool underrun = WasapiRenderTiming.IsUnderrun(
            currentPaddingFrames: 0,
            sourceEnded: true,
            elapsedSinceFill: TimeSpan.FromMilliseconds(80),
            BufferDuration);

        Assert.False(underrun);
    }
}
