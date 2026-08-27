namespace Resonalyze.Audio.Tests;

/// <summary>
/// The meter path for an array: levels arrive keyed by hardware channel and
/// have to come back keyed by the ROLE the caller asked for, array microphones
/// included and in the order they were requested.
/// </summary>
public sealed class AudioLevelResolverTests
{
    private static AudioChannelLevel[] Channels(int count) =>
        Enumerable
            .Range(0, count)
            .Select(channel => new AudioChannelLevel(-channel, -channel - 20.0, channel == 0))
            .ToArray();

    [Fact]
    public void ResolvesMicrophoneAndLoopbackWithoutAnArray()
    {
        AudioInputLevels levels = AudioLevelResolver.Resolve(
            Channels(2),
            new AudioCaptureRouting(0, 1));

        Assert.Equal(0.0, levels.Microphone.PeakDbFs);
        Assert.Equal(-1.0, Assert.NotNull(levels.Loopback).PeakDbFs);
        Assert.Empty(levels.Array);
    }

    [Fact]
    public void ResolvesTheArrayInItsRequestedOrder()
    {
        AudioInputLevels levels = AudioLevelResolver.Resolve(
            Channels(6),
            new AudioCaptureRouting(0, 1) { ArrayChannels = [4, 2, 5] });

        Assert.Equal(3, levels.Array.Count);
        Assert.Equal(-4.0, levels.Array[0].PeakDbFs);
        Assert.Equal(-2.0, levels.Array[1].PeakDbFs);
        Assert.Equal(-5.0, levels.Array[2].PeakDbFs);
    }

    [Fact]
    public void AChannelOutsideTheCaptureMetersAsSilenceRatherThanShorteningTheList()
    {
        // The caller pairs these with its configured microphones by position. A
        // shorter list would slide every later reading onto the wrong microphone
        // and show the user a clipping warning for a microphone that is fine.
        AudioInputLevels levels = AudioLevelResolver.Resolve(
            Channels(3),
            new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 9] });

        Assert.Equal(2, levels.Array.Count);
        Assert.Equal(-2.0, levels.Array[0].PeakDbFs);
        Assert.Equal(default, levels.Array[1]);
    }
}
