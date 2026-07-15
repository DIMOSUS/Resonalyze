namespace Resonalyze.App.Tests;

public sealed class MeasurementChannelRoutingTests
{
    [Fact]
    public void SweepRejectsSameMmeMicrophoneAndLoopbackChannel()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.Init(new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(
                    8,
                    48_000,
                    24,
                    0.25,
                    PlaybackChannel.Right),
                new SweepAudioConfiguration(
                    Backend: AudioBackend.Wave,
                    WaveInputChannelOffset: 0,
                    WaveLoopbackInputChannelOffset: 0),
                new SweepAveragingConfiguration())));

        Assert.Contains("different channels", exception.Message);
    }

    [Fact]
    public void LiveNoiseRejectsSameMmeMicrophoneAndLoopbackChannel()
    {
        using var measurement = new NoiseMeasurement(new FakeAudioSessionFactory());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.Init(
                48_000,
                24,
                1.0,
                PlaybackChannel.Right,
                audioBackend: AudioBackend.Wave,
                waveInputChannelOffset: 1,
                waveLoopbackInputChannelOffset: 1));

        Assert.Contains("different channels", exception.Message);
    }

    [Fact]
    public void OptionsRejectMissingWasapiLoopback()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            Options.MeasurementOptions.ValidateRequiredWaveLoopback(
                loopbackSelected: false,
                recordingDeviceSupportsLoopback: true));

        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
