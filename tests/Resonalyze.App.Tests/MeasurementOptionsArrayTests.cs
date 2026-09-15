using Resonalyze.Audio;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>A dialog edit must reach the file through both the live apply and the full read-back.</summary>
public sealed class MeasurementOptionsArrayTests
{
    [Theory]
    [InlineData(AudioBackend.Asio, 10, "ASIO driver inputs")]
    [InlineData(AudioBackend.Wave, 2, "MME is limited to two channels")]
    [InlineData(AudioBackend.WasapiExclusive, 8, "WASAPI endpoint channels")]
    public void TheInputSourceIsNamedByBackend(
        AudioBackend backend, int channels, string expected) =>
        Assert.Equal(expected, ArrayInputSources.Describe(backend, channels));

    [Fact]
    public void AStereoWasapiEndpointIsToldWhereTheFurtherInputsAre() =>
        Assert.Equal(
            "WASAPI endpoint channels; use ASIO to reach an interface's further inputs",
            ArrayInputSources.Describe(AudioBackend.WasapiShared, 2));

    private static MeasurementSettingsFile.SweepMeasurementSettings SettingsWithArray() =>
        new()
        {
            AudioBackend = AudioBackend.Wave,
            WaveArrayMicrophones =
            [
                new ArrayMicrophoneDefinition
                {
                    ChannelOffset = 2,
                    CalibrationId = "cal-1",
                    Note = "left forward"
                }
            ],
            AsioArrayMicrophones =
            [
                new ArrayMicrophoneDefinition { ChannelOffset = 5, Note = "asio side" }
            ]
        };

    private static ExpSweepMeasurement CreateMeasurement()
    {
        var factory = new FakeAudioSessionFactory();
        var measurement = new ExpSweepMeasurement(factory);
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(20, 20_000, 48_000, 24, 0.2, PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration(1)));
        return measurement;
    }

    [Fact]
    public void TheLiveApplyCarriesTheArray()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        using var panel = new MeasurementOptions();
        panel.Init(measurement, SettingsWithArray());

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        panel.ApplySweepSettings(applied);

        ArrayMicrophoneDefinition wave = Assert.Single(applied.WaveArrayMicrophones);
        Assert.Equal(2, wave.ChannelOffset);
        Assert.Equal("cal-1", wave.CalibrationId);
        Assert.Equal("left forward", wave.Note);

        // Channel numbers mean different inputs per backend, so the other backend's list stays untouched.
        ArrayMicrophoneDefinition asio = Assert.Single(applied.AsioArrayMicrophones);
        Assert.Equal(5, asio.ChannelOffset);
    }

    [Fact]
    public void TheLiveApplyCarriesTheMeasurementCalibration()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        using var panel = new MeasurementOptions();
        MeasurementSettingsFile.SweepMeasurementSettings settings = SettingsWithArray();
        settings.MicrophoneCalibrationId = "cal-1";
        panel.Init(measurement, settings);

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        panel.ApplySweepSettings(applied);

        Assert.Equal("cal-1", applied.MicrophoneCalibrationId);
    }

    /// <summary>A missing calibration is shown as deleted, not rewritten to Off (which the next apply would persist).</summary>
    [Fact]
    public void ADeletedCalibrationKeepsItsPlaceRatherThanBecomingOff()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        using var panel = new MeasurementOptions();
        MeasurementSettingsFile.SweepMeasurementSettings settings = SettingsWithArray();
        settings.MicrophoneCalibrationId = "cal-that-went-away";
        panel.Init(measurement, settings);

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        panel.ApplySweepSettings(applied);

        Assert.Equal("cal-that-went-away", applied.MicrophoneCalibrationId);
    }

    [Fact]
    public void TheAppliedArrayIsACopy()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        using var panel = new MeasurementOptions();
        MeasurementSettingsFile.SweepMeasurementSettings source = SettingsWithArray();
        panel.Init(measurement, source);

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        panel.ApplySweepSettings(applied);
        applied.WaveArrayMicrophones[0].Note = "edited afterwards";

        var again = new MeasurementSettingsFile.SweepMeasurementSettings();
        panel.ApplySweepSettings(again);
        Assert.Equal("left forward", again.WaveArrayMicrophones[0].Note);
    }
}
