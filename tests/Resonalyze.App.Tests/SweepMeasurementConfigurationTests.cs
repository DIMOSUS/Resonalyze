namespace Resonalyze.App.Tests;

public sealed class SweepMeasurementConfigurationTests
{
    [Fact]
    public void Init_AppliesGroupedSignalAudioAndAveragingConfiguration()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        var configuration = new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                10,
                96_000,
                24,
                0.25,
                PlaybackChannel.Right),
            new SweepAudioConfiguration(
                Backend: AudioBackend.Asio,
                AsioDriverName: "Test ASIO",
                AsioInputChannelOffset: 4,
                AsioLoopbackInputChannelOffset: 5,
                AsioOutputChannelOffset: 2),
            new SweepAveragingConfiguration(3, ConfirmEachRun: true));

        measurement.Init(configuration);

        Assert.Equal(10, measurement.Octaves);
        Assert.Equal(96_000, measurement.SampleRate);
        Assert.Equal(PlaybackChannel.Right, measurement.PlaybackChannel);
        Assert.Equal(AudioBackend.Asio, measurement.AudioBackend);
        Assert.Equal("Test ASIO", measurement.AsioDriverName);
        Assert.Equal(4, measurement.AsioInputChannelOffset);
        Assert.Equal(5, measurement.AsioLoopbackInputChannelOffset);
        Assert.Equal(2, measurement.AsioOutputChannelOffset);
        Assert.Equal(3, measurement.AverageRunCount);
        Assert.True(measurement.ConfirmEachAverageRun);
    }
}
