using Resonalyze.Dsp;
using Resonalyze.Options;

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

    // A pre-Auto file (v <= 9) has no PhaseGateAutoFit/GroupDelayGateAutoFit
    // fields. These tests deserialize REAL JSON without the fields — the
    // failure mode they guard is exactly a property initializer surviving
    // deserialization (System.Text.Json never assigns a missing property),
    // which a hand-built object with an explicit null cannot catch. A
    // deliberately fitted/typed gate offset must stay manual (Auto would
    // silently re-snap and persist over it); an untouched default offset
    // gets the new Auto.
    [Fact]
    public void PreAutoFileWithACustomGateOffsetStaysManual()
    {
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse(
                """{"PhaseGateOffsetMs": 6.5, "GroupDelayGateOffsetMs": 12.25}""");
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.False(options.PhaseGateAutoFit);
        Assert.Equal(6.5, options.PhaseGateOffsetMs);
        Assert.False(options.GroupDelayGateAutoFit);
        Assert.Equal(12.25, options.GroupDelayGateOffsetMs);
    }

    [Fact]
    public void PreAutoFileWithTheDefaultGateOffsetGetsAuto()
    {
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse("""{"PhasePlateauMs": 4.0}""");
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.True(options.PhaseGateAutoFit);
        Assert.True(options.GroupDelayGateAutoFit);
    }

    [Fact]
    public void StoredAutoFitChoiceIsAppliedAsIs()
    {
        // An explicitly released Auto with the default offset must not be
        // re-enabled by the missing-field heuristic.
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse(
                """
                {"PhaseGateAutoFit": false,
                 "GroupDelayGateAutoFit": true, "GroupDelayGateOffsetMs": 12.25}
                """);
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.False(options.PhaseGateAutoFit);
        Assert.True(options.GroupDelayGateAutoFit);
    }

    private static MeasurementSettingsFile.FrequencyResponseSettings
        DeserializeFrequencyResponse(string json) =>
        System.Text.Json.JsonSerializer
            .Deserialize<MeasurementSettingsFile.FrequencyResponseSettings>(json)!;

    // The migration runs inside LoadOrDefault (which reads the real settings
    // path beside the executable), so the unit exercises it directly.
    private static void Migrate(MeasurementSettingsFile settings) =>
        settings.MigrateLegacyDualDeviceLoopback();
}
