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

    [Fact]
    public void AlignedExclusiveDurationRoundsUpToReferenceTimeUnit()
    {
        long duration = WasapiStreamConfiguration.GetAlignedExclusiveDuration(
            bufferFrames: 512,
            sampleRate: 48_000);

        Assert.Equal(106_667, duration);
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
