namespace Resonalyze.App.Tests;

public sealed class MeasurementChannelRoutingTests
{
    [Fact]
    public void SweepRejectsSameMmeMicrophoneAndLoopbackChannel()
    {
        using var measurement = new ExpSweepMeasurement();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.Init(
                8,
                48_000,
                24,
                0.25,
                PlaybackChannel.Right,
                audioBackend: AudioBackend.Wave,
                waveInputChannelOffset: 0,
                waveLoopbackInputChannelOffset: 0));

        Assert.Contains("different channels", exception.Message);
    }

    [Fact]
    public void LiveNoiseRejectsSameMmeMicrophoneAndLoopbackChannel()
    {
        using var measurement = new NoiseMeasurement();

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
