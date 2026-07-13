using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Resonalyze.App.Tests;

public sealed class WasapiStreamConfigurationTests
{
    [Fact]
    public void SharedEventDrivenInitializationUsesZeroDurations()
    {
        (long bufferDuration, long periodicity) =
            WasapiStreamConfiguration.GetDurations(AudioClientShareMode.Shared, 100);

        Assert.Equal(0, bufferDuration);
        Assert.Equal(0, periodicity);
    }

    [Fact]
    public void ExclusiveEventDrivenInitializationUsesEqualDurations()
    {
        (long bufferDuration, long periodicity) =
            WasapiStreamConfiguration.GetDurations(AudioClientShareMode.Exclusive, 40);

        Assert.Equal(400_000, bufferDuration);
        Assert.Equal(bufferDuration, periodicity);
    }

    [Fact]
    public void ExclusiveRenderAlwaysRequestsFullEndpointBuffer()
    {
        int frames = WasapiStreamConfiguration.GetRenderFrames(
            AudioClientShareMode.Exclusive,
            bufferFrames: 480,
            currentPaddingFrames: 120);

        Assert.Equal(480, frames);
    }

    [Fact]
    public void SharedRenderRequestsOnlyAvailableFrames()
    {
        int frames = WasapiStreamConfiguration.GetRenderFrames(
            AudioClientShareMode.Shared,
            bufferFrames: 480,
            currentPaddingFrames: 120);

        Assert.Equal(360, frames);
    }

    [Theory]
    [InlineData(512, 48_000, 106_667)]
    [InlineData(256, 48_000, 53_333)]
    [InlineData(64, 44_100, 14_512)]
    public void AlignedExclusiveDurationRoundsToNearestReferenceTimeUnit(
        int bufferFrames,
        int sampleRate,
        long expectedDuration)
    {
        long duration = WasapiStreamConfiguration.GetAlignedExclusiveDuration(
            bufferFrames,
            sampleRate);

        Assert.Equal(expectedDuration, duration);
    }

    [Fact]
    public void EventTimeoutUsesActualAlignedBufferSize()
    {
        int timeout = WasapiStreamConfiguration.GetEventTimeoutMilliseconds(
            bufferFrames: 1_920,
            sampleRate: 48_000);

        Assert.Equal(120, timeout);
    }

    [Fact]
    public void BufferAlignmentErrorIsRecognizedByHResult()
    {
        var exception = new COMException(
            "not aligned",
            WasapiStreamConfiguration.BufferSizeNotAlignedHResult);

        Assert.True(WasapiStreamConfiguration.IsBufferSizeNotAligned(exception));
    }
}
