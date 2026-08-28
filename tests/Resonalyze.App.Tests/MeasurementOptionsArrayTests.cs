using Resonalyze.Audio;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// The array survives the Record Settings panel.
/// </summary>
/// <remarks>
/// The panel has two ways out — the live apply that runs on every edit, and the
/// one that reads the whole panel back — and an edit made in a DIALOG reaches the
/// file through neither unless both carry it and the dialog says it changed
/// something. It did not: the button read "1 microphone" while the file kept an
/// empty list, and the next open read that back.
/// </remarks>
public sealed class MeasurementOptionsArrayTests
{
    /// <summary>
    /// The status line's parenthesis is one wording with two readers now — the panel
    /// that has a device, and the screenshot tool that builds the dialog without one —
    /// and a figure stating a line the application never shows is the reason it is
    /// shared rather than written twice.
    /// </summary>
    [Theory]
    [InlineData(AudioBackend.Asio, 10, "ASIO driver inputs")]
    [InlineData(AudioBackend.Wave, 2, "MME is limited to two channels")]
    [InlineData(AudioBackend.WasapiExclusive, 8, "WASAPI endpoint channels")]
    public void TheInputSourceIsNamedByBackend(
        AudioBackend backend, int channels, string expected) =>
        Assert.Equal(expected, ArrayInputSources.Describe(backend, channels));

    /// <summary>
    /// The one branch that is advice rather than a label: an interface presenting its
    /// inputs as separate stereo endpoints reports two, and its further inputs are
    /// reachable — through ASIO. A user left to guess that has nowhere to go.
    /// </summary>
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
        // The path every on-the-fly edit takes, and the one that reopens the device
        // with the new capture routing. It wrote the sweep fields and the protective
        // high-pass and stopped there, so an array edited in the dialog reached the
        // settings through nothing at all. (Its counterpart, SetOptions, validates the
        // selected recording device and so cannot run without one — that path already
        // carried the array.)
        using ExpSweepMeasurement measurement = CreateMeasurement();
        using var panel = new MeasurementOptions();
        panel.Init(measurement, SettingsWithArray());

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        panel.ApplySweepSettings(applied);

        ArrayMicrophoneDefinition wave = Assert.Single(applied.WaveArrayMicrophones);
        Assert.Equal(2, wave.ChannelOffset);
        Assert.Equal("cal-1", wave.CalibrationId);
        Assert.Equal("left forward", wave.Note);

        // The other backend's list rides along untouched: a channel number means a
        // different input on each, so switching backend must not silently point an
        // array microphone at whatever input happens to share its number.
        ArrayMicrophoneDefinition asio = Assert.Single(applied.AsioArrayMicrophones);
        Assert.Equal(5, asio.ChannelOffset);
    }

    /// <summary>
    /// The measurement microphone's calibration is the rig's answer now, so it makes
    /// the same trip the array does: through the panel and out on the live apply.
    /// </summary>
    /// <remarks>
    /// It used to be read off the Frequency Response view at run start, which meant a
    /// calibration chosen after the sweeps labelled none of them and one adopted to
    /// read a foreign file labelled all of them with a stranger's microphone.
    /// </remarks>
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

    /// <summary>
    /// A selection naming a calibration the list no longer holds is KEPT, the way
    /// every other selector in the application keeps one: it is shown as deleted
    /// rather than quietly rewritten to Off, which the next apply would persist as
    /// the user's own choice.
    /// </summary>
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

        // Editing what was applied must not reach back into the panel, or the next
        // apply would carry a change nobody made in it.
        var again = new MeasurementSettingsFile.SweepMeasurementSettings();
        panel.ApplySweepSettings(again);
        Assert.Equal("left forward", again.WaveArrayMicrophones[0].Note);
    }
}
