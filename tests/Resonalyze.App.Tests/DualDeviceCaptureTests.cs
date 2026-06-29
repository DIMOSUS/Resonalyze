namespace Resonalyze.App.Tests;

public sealed class DualDeviceCaptureTests
{
    [Fact]
    public void MergeMicrophoneAndLoopback_SelectsRequestedChannelsAsMicThenLoopback()
    {
        float[][] microphone =
        [
            [1f, 2f, 3f],   // channel 0
            [4f, 5f, 6f]    // channel 1
        ];
        float[][] loopback =
        [
            [7f, 8f, 9f],   // channel 0
            [10f, 11f, 12f] // channel 1
        ];

        float[][] merged = DualDeviceCapture.MergeMicrophoneAndLoopback(
            microphone,
            microphoneChannel: 1,
            loopback,
            loopbackChannel: 0);

        Assert.Equal(2, merged.Length);
        Assert.Equal([4f, 5f, 6f], merged[0]);
        Assert.Equal([7f, 8f, 9f], merged[1]);
    }

    [Fact]
    public void MergeMicrophoneAndLoopback_TrimsBothToTheShorterLength()
    {
        float[][] microphone = [[1f, 2f, 3f, 4f]];
        float[][] loopback = [[5f, 6f]];

        float[][] merged = DualDeviceCapture.MergeMicrophoneAndLoopback(
            microphone,
            microphoneChannel: 0,
            loopback,
            loopbackChannel: 0);

        Assert.Equal([1f, 2f], merged[0]);
        Assert.Equal([5f, 6f], merged[1]);
    }

    [Fact]
    public void MergeMicrophoneAndLoopback_MissingChannelYieldsEmptyResult()
    {
        float[][] microphone = [[1f, 2f, 3f]];
        float[][] loopback = [[4f, 5f, 6f]];

        float[][] merged = DualDeviceCapture.MergeMicrophoneAndLoopback(
            microphone,
            microphoneChannel: 5,
            loopback,
            loopbackChannel: 0);

        Assert.Empty(merged[0]);
        Assert.Empty(merged[1]);
    }
}
