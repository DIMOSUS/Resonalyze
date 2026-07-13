namespace Resonalyze.Audio.Tests;

public sealed class CaptureChannelLayoutTests
{
    [Theory]
    [InlineData(0, null, 1)]
    [InlineData(1, null, 2)]
    [InlineData(0, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(3, 1, 4)]
    public void RequiredWaveInputChannelCount_CoversBothChannels(
        int microphone,
        int? loopback,
        int expected)
    {
        Assert.Equal(
            expected,
            CaptureChannelLayout.RequiredWaveInputChannelCount(microphone, loopback));
    }

    [Theory]
    [InlineData(2, null, 2, 1)]
    [InlineData(2, 5, 2, 4)]
    [InlineData(5, 2, 2, 4)]
    [InlineData(3, 3, 3, 1)]
    public void AsioWindow_SpansMicrophoneAndLoopback(
        int microphone,
        int? loopback,
        int expectedFirst,
        int expectedCount)
    {
        Assert.Equal(
            expectedFirst,
            CaptureChannelLayout.AsioFirstInputOffset(microphone, loopback));
        Assert.Equal(
            expectedCount,
            CaptureChannelLayout.AsioInputChannelCount(microphone, loopback));
    }
}
