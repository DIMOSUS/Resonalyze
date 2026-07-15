namespace Resonalyze.App.Tests;

// The separate-loopback-device capability was removed; a settings file
// written by an older version must not be misread as a shared-device
// configuration (its channel offsets could be equal, or the microphone
// device mono). The migration resets the loopback selection instead.
public sealed class MeasurementSettingsMigrationTests
{
    [Fact]
    public void LegacySeparateLoopbackDeviceResetsTheLoopbackChannel()
    {
        var settings = new MeasurementSettingsFile();
        settings.Measurement.InputDeviceNumber = 1;
        settings.Measurement.WaveInputChannelOffset = 0;
        settings.Measurement.WaveLoopbackInputChannelOffset = 0;
        settings.Measurement.WaveLoopbackDeviceNumber = 3;

        Migrate(settings);

        Assert.Null(settings.Measurement.WaveLoopbackInputChannelOffset);
        Assert.Null(settings.Measurement.WaveLoopbackDeviceNumber);
        Assert.True(settings.LegacyDualDeviceLoopbackReset);
        Assert.False(settings.Measurement.HasLoopbackConfigured);
    }

    [Fact]
    public void LegacyLoopbackOnTheMicrophoneDeviceIsKept()
    {
        var settings = new MeasurementSettingsFile();
        settings.Measurement.InputDeviceNumber = 1;
        settings.Measurement.WaveInputChannelOffset = 0;
        settings.Measurement.WaveLoopbackInputChannelOffset = 1;
        settings.Measurement.WaveLoopbackDeviceNumber = 1;

        Migrate(settings);

        Assert.Equal(1, settings.Measurement.WaveLoopbackInputChannelOffset);
        Assert.Null(settings.Measurement.WaveLoopbackDeviceNumber);
        Assert.False(settings.LegacyDualDeviceLoopbackReset);
    }

    [Fact]
    public void ModernFileWithoutTheLegacyFieldIsUntouched()
    {
        var settings = new MeasurementSettingsFile();
        settings.Measurement.WaveLoopbackInputChannelOffset = 1;

        Migrate(settings);

        Assert.Equal(1, settings.Measurement.WaveLoopbackInputChannelOffset);
        Assert.False(settings.LegacyDualDeviceLoopbackReset);
    }

    [Fact]
    public void WasapiEndpointIdsAndBufferAreCapturedWithoutOpeningHardware()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                12,
                48_000,
                24,
                1.0,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                Backend: AudioBackend.WasapiShared,
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1,
                WasapiCaptureEndpointId: "capture-id",
                WasapiRenderEndpointId: "render-id",
                WasapiBufferMilliseconds: 40,
                WasapiCaptureEndpointName: "USB Input",
                WasapiRenderEndpointName: "USB Output"),
            new SweepAveragingConfiguration()));

        MeasurementSettingsFile.SweepMeasurementSettings captured =
            MeasurementSettingsFile.SweepMeasurementSettings.Capture(measurement);

        Assert.Equal(AudioBackend.WasapiShared, captured.AudioBackend);
        Assert.Equal("capture-id", captured.WasapiCaptureEndpointId);
        Assert.Equal("render-id", captured.WasapiRenderEndpointId);
        Assert.Equal(40, captured.WasapiBufferMilliseconds);
        Assert.Equal("USB Input", captured.WasapiCaptureEndpointName);
        Assert.Equal("USB Output", captured.WasapiRenderEndpointName);
    }

    [Fact]
    public void RestoreImpulseResponse_PreservesCurrentWasapiConfiguration()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                12,
                48_000,
                24,
                1.0,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                Backend: AudioBackend.WasapiShared,
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1,
                WasapiCaptureEndpointId: "capture-id",
                WasapiRenderEndpointId: "render-id",
                WasapiBufferMilliseconds: 40,
                WasapiCaptureEndpointName: "USB Input",
                WasapiRenderEndpointName: "USB Output"),
            new SweepAveragingConfiguration()));

        measurement.RestoreImpulseResponse(
            12,
            48_000,
            24,
            1.0,
            PlaybackChannel.Mono,
            [System.Numerics.Complex.Zero, System.Numerics.Complex.One],
            1);

        Assert.Equal(AudioBackend.WasapiShared, measurement.AudioBackend);
        Assert.Equal("capture-id", measurement.WasapiCaptureEndpointId);
        Assert.Equal("render-id", measurement.WasapiRenderEndpointId);
        Assert.Equal(40, measurement.WasapiBufferMilliseconds);
        Assert.Equal("USB Input", measurement.WasapiCaptureEndpointName);
        Assert.Equal("USB Output", measurement.WasapiRenderEndpointName);
    }

    [Fact]
    public void WasapiChannelOffsetsAreNotLimitedToStereo()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                12,
                48_000,
                24,
                1.0,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                Backend: AudioBackend.WasapiShared,
                WaveInputChannelOffset: 5,
                WaveLoopbackInputChannelOffset: 7,
                WasapiCaptureEndpointId: "capture-id",
                WasapiRenderEndpointId: "render-id"),
            new SweepAveragingConfiguration()));

        Assert.Equal(5, measurement.WaveInputChannelOffset);
        Assert.Equal(7, measurement.WaveLoopbackInputChannelOffset);
    }

    // The migration runs inside LoadOrDefault (which reads the real settings
    // path beside the executable), so the unit exercises it directly.
    private static void Migrate(MeasurementSettingsFile settings) =>
        settings.MigrateLegacyDualDeviceLoopback();
}
