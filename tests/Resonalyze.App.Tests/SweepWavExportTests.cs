using System.Globalization;

namespace Resonalyze.App.Tests;

public sealed class SweepWavExportTests
{
    private const int SampleRate = 48_000;
    private static readonly float[] Sweep = [0.25f, -0.5f, 0.75f];
    private static readonly int Silence = (int)(SweepWavExport.SilenceSeconds * SampleRate);

    private static void AssertCarriesTheSweep(float[] channel)
    {
        Assert.Equal(Silence + Sweep.Length + Silence, channel.Length);
        Assert.All(channel[..Silence], sample => Assert.Equal(0.0f, sample));
        Assert.Equal(Sweep, channel[Silence..(Silence + Sweep.Length)]);
        Assert.All(channel[(Silence + Sweep.Length)..], sample => Assert.Equal(0.0f, sample));
    }

    [Fact]
    public void MonoWritesOneChannel()
    {
        AudioFileContent content = SweepWavExport.BuildContent(
            Sweep, SampleRate, PlaybackChannel.Mono);

        Assert.Equal(1, content.ChannelCount);
        Assert.Equal(SampleRate, content.SampleRate);
        AssertCarriesTheSweep(content.Channels[0]);
    }

    [Theory]
    [InlineData(PlaybackChannel.Left, 0)]
    [InlineData(PlaybackChannel.Right, 1)]
    public void SingleSideLeavesTheOtherChannelSilent(PlaybackChannel channel, int excitedIndex)
    {
        AudioFileContent content = SweepWavExport.BuildContent(Sweep, SampleRate, channel);

        Assert.Equal(2, content.ChannelCount);
        AssertCarriesTheSweep(content.Channels[excitedIndex]);
        Assert.All(content.Channels[1 - excitedIndex], sample => Assert.Equal(0.0f, sample));
    }

    [Fact]
    public void StereoWritesTheSweepOnBothChannels()
    {
        AudioFileContent content = SweepWavExport.BuildContent(
            Sweep, SampleRate, PlaybackChannel.Stereo);

        Assert.Equal(2, content.ChannelCount);
        AssertCarriesTheSweep(content.Channels[0]);
        AssertCarriesTheSweep(content.Channels[1]);
    }

    // The silence is measured in seconds, so it has to follow the rate the file
    // is written at rather than being a fixed sample count.
    [Fact]
    public void TheSilenceFollowsTheSampleRate()
    {
        AudioFileContent content = SweepWavExport.BuildContent(
            Sweep, 96_000, PlaybackChannel.Mono);

        Assert.Equal(
            (int)(SweepWavExport.SilenceSeconds * 96_000) * 2 + Sweep.Length,
            content.FrameCount);
    }

    [Fact]
    public void AnEmptySweepIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            SweepWavExport.BuildContent([], SampleRate, PlaybackChannel.Mono));
    }

    // The name carries the settings, and a locale that writes 44,1 must not
    // leak a decimal comma into it.
    [Fact]
    public void SuggestedNameIsInvariantAndCarriesTheSettings()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            string name = SweepWavExport.SuggestFileName(20, 20_000, 2.06, 44_100);

            Assert.Equal("sweep_20-20000Hz_44.1kHz_2.1s.wav", name);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
