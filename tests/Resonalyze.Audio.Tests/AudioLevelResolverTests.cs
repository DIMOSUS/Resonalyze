namespace Resonalyze.Audio.Tests;

/// <summary>Levels keyed by hardware channel come back keyed by requested role, array order preserved.</summary>
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
        // A shorter list would slide readings onto the wrong microphones.
        AudioInputLevels levels = AudioLevelResolver.Resolve(
            Channels(3),
            new AudioCaptureRouting(0, 1) { ArrayChannels = [2, 9] });

        Assert.Equal(2, levels.Array.Count);
        Assert.Equal(-2.0, levels.Array[0].PeakDbFs);
        // A default AudioChannelLevel is 0 dBFS (full scale), not silence.
        Assert.Equal(double.NegativeInfinity, levels.Array[1].PeakDbFs);
        Assert.Equal(double.NegativeInfinity, levels.Array[1].RmsDbFs);
        Assert.False(levels.Array[1].FullScale);
    }
}
