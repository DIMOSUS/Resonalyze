using Resonalyze.Audio;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Record Settings' rules on the session alone: what a setting moves when it changes, and what reaches the file.</summary>
public sealed class RecordSettingsSessionTests
{
    private static MeasurementSettingsFile.SweepMeasurementSettings WaveSettings() =>
        new()
        {
            AudioBackend = AudioBackend.Wave,
            SampleRate = 48_000,
            OutputDeviceNumber = 0,
            InputDeviceNumber = 1,
            WaveInputChannelOffset = 0,
            WaveLoopbackInputChannelOffset = 1,
            WaveArrayMicrophones =
            [
                new ArrayMicrophoneDefinition { ChannelOffset = 2, CalibrationId = "cal-1", Note = "left forward" }
            ],
            AsioArrayMicrophones = [new ArrayMicrophoneDefinition { ChannelOffset = 5, Note = "asio side" }]
        };

    private static RecordSettingsSession Load(
        MeasurementSettingsFile.SweepMeasurementSettings settings,
        FakeRecordDevices? devices = null)
    {
        var session = new RecordSettingsSession(devices ?? new FakeRecordDevices());
        session.Load(settings);
        return session;
    }

    private static void Pick(RecordChoice choice, string text)
    {
        int index = choice.Items.ToList().FindIndex(item => item.ToString() == text);
        Assert.True(index >= 0, $"no item '{text}' in [{string.Join(", ", choice.Items)}]");
        choice.SelectedIndex = index;
    }

    [Fact]
    public void TheLiveApplyCarriesTheArrayOfEachBackend()
    {
        RecordSettingsSession session = Load(WaveSettings());

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        session.WriteLiveSettings(applied);

        ArrayMicrophoneDefinition wave = Assert.Single(applied.WaveArrayMicrophones);
        Assert.Equal(2, wave.ChannelOffset);
        Assert.Equal("cal-1", wave.CalibrationId);
        Assert.Equal("left forward", wave.Note);
        // Channel numbers mean different inputs per backend, so the other backend's list stays untouched.
        Assert.Equal(5, Assert.Single(applied.AsioArrayMicrophones).ChannelOffset);
    }

    [Fact]
    public void TheAppliedArrayIsACopy()
    {
        RecordSettingsSession session = Load(WaveSettings());

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        session.WriteLiveSettings(applied);
        applied.WaveArrayMicrophones[0].Note = "edited afterwards";

        var again = new MeasurementSettingsFile.SweepMeasurementSettings();
        session.WriteLiveSettings(again);
        Assert.Equal("left forward", again.WaveArrayMicrophones[0].Note);
    }

    [Theory]
    [InlineData("cal-1")]
    [InlineData("cal-that-went-away")]
    public void TheMeasurementCalibrationSurvivesTheLiveApply_EvenWhenItsEntryIsGone(string calibrationId)
    {
        MeasurementSettingsFile.SweepMeasurementSettings settings = WaveSettings();
        settings.MicrophoneCalibrationId = calibrationId;
        RecordSettingsSession session = Load(settings);

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        session.WriteLiveSettings(applied);

        Assert.Equal(calibrationId, applied.MicrophoneCalibrationId);
        Assert.Equal("Deleted calibration (missing)", session.MicrophoneCalibration.SelectedItem!.ToString());
    }

    [Fact]
    public void AMonoRecordingDeviceForcesNoLoopback_AndAStereoOneRestoresIt()
    {
        RecordSettingsSession session = Load(WaveSettings());
        Assert.Equal(1, session.SelectedWaveLoopbackOffset);

        Pick(session.RecordingDevice, "Mono mic");
        Assert.Null(session.SelectedWaveLoopbackOffset);

        Pick(session.RecordingDevice, "Line in");
        Assert.Equal(1, session.SelectedWaveLoopbackOffset);
    }

    [Fact]
    public void AMissingSavedDeviceStaysSelectedAsMissing()
    {
        MeasurementSettingsFile.SweepMeasurementSettings settings = WaveSettings();
        settings.InputDeviceNumber = 7;

        RecordSettingsSession session = Load(settings);

        Assert.Equal("(missing) Device #7", session.RecordingDevice.SelectedItem!.ToString());
        Assert.Empty(session.SampleRate.Items);
    }

    [Fact]
    public void AGoneWasapiEndpointIsKeptAsUnavailable()
    {
        MeasurementSettingsFile.SweepMeasurementSettings settings = WaveSettings();
        settings.AudioBackend = AudioBackend.WasapiShared;
        settings.WasapiCaptureEndpointId = "{gone}";
        settings.WasapiCaptureEndpointName = "Old interface";

        RecordSettingsSession session = Load(settings);

        Assert.Equal("[Unavailable] Old interface", session.RecordingDevice.SelectedItem!.ToString());
        Assert.Empty(session.SampleRate.Items);
    }

    [Fact]
    public void AnEndpointChangeRebuildsTheRouteAroundTheSamePicks()
    {
        var devices = new FakeRecordDevices();
        MeasurementSettingsFile.SweepMeasurementSettings settings = WaveSettings();
        settings.AudioBackend = AudioBackend.WasapiShared;
        settings.WasapiCaptureEndpointId = "{capture}";
        settings.WaveInputChannelOffset = 2;
        settings.WaveLoopbackInputChannelOffset = 5;
        RecordSettingsSession session = Load(settings, devices);

        devices.Capture.Clear();
        session.RefreshEndpoints();

        Assert.False(((AudioEndpointDescriptor)session.RecordingDevice.SelectedItem!).IsAvailable);
        Assert.Equal(2, session.SelectedWaveInputOffset);
        Assert.Equal(5, session.SelectedWaveLoopbackOffset);
    }

    [Fact]
    public void ARateTheNewDeviceLacksFallsBackAndSaysFromWhat()
    {
        var devices = new FakeRecordDevices();
        RecordSettingsSession session = Load(WaveSettings(), devices);
        Pick(session.SampleRate, "96000");

        devices.Rates.Remove(96_000);
        Pick(session.PlaybackDevice, "Default playback device");

        Assert.Equal(44_100, session.SelectedSampleRate);
        Assert.Equal(96_000, session.SampleRateFellBackFrom);
    }

    [Fact]
    public void AnAsioDriverThatIsGoneKeepsItsNameAndChannels()
    {
        var devices = new FakeRecordDevices();
        devices.AsioDrivers["Other"] = FakeRecordDevices.Asio("Other", 2, 2, 48_000);
        MeasurementSettingsFile.SweepMeasurementSettings settings = WaveSettings();
        settings.AudioBackend = AudioBackend.Asio;
        settings.AsioDriverName = "Gone";
        settings.AsioInputChannelOffset = 3;
        settings.AsioLoopbackInputChannelOffset = 4;

        RecordSettingsSession session = Load(settings, devices);

        Assert.Equal("(missing) Gone", session.AsioDriver.SelectedItem!.ToString());
        Assert.Equal(3, session.SelectedAsioInputOffset);
        Assert.Equal(4, session.SelectedAsioLoopbackOffset);
    }

    [Fact]
    public void TheBandNeverInverts_TheOtherEdgeFollows()
    {
        RecordSettingsSession session = Load(WaveSettings());
        session.LowFrequency.Value = 100;
        int changes = 0;
        session.SweepSettingsChanged += () => changes++;

        session.HighFrequency.Value = 50;
        Assert.Equal(49m, session.LowFrequency.Value);
        Assert.Equal(1, changes);

        session.LowFrequency.Value = 20_000;
        Assert.Equal(20_000m, session.HighFrequency.Value);
        Assert.Equal(19_999m, session.LowFrequency.Value);
    }

    [Fact]
    public void TheHighPassKindChoosesItsSlopes_KeepingTheSlopeWhereItCan()
    {
        RecordSettingsSession session = Load(WaveSettings());

        Pick(session.HighPassKind, ProtectiveHighPassKind.Butterworth.ToString());
        Pick(session.HighPassSlope, "12");
        Pick(session.HighPassKind, ProtectiveHighPassKind.LinkwitzRiley.ToString());

        Assert.Equal(
            ProtectiveHighPassConfiguration.SupportedSlopes(ProtectiveHighPassKind.LinkwitzRiley),
            session.HighPassSlope.Items.Cast<int>());
        Assert.True(RecordHighPass.IsEditable(session));
        Assert.Equal(ProtectiveHighPassKind.LinkwitzRiley, RecordHighPass.Read(session).Kind);
    }

    [Fact]
    public void ACalibrationEditIsAnnouncedWithTheWholeSelection()
    {
        RecordSettingsSession session = Load(WaveSettings());
        RecordCalibrationSelection? announced = null;
        session.CalibrationChanged += selection => announced = selection;

        session.SetAdditionalCalibrations(
            [new MicrophoneCalibrationDefinition { Id = "cal-2", Name = "second", Kind = MicrophoneCalibrationKind.File, Path = "b.cal" }]);

        Assert.NotNull(announced);
        Assert.Equal("cal-2", Assert.Single(announced.AdditionalMicrophoneCalibrations).Id);
        Assert.Contains(session.MicrophoneCalibration.Items, item => item.ToString() == "second");
    }

    [Fact]
    public void AnArrayEditIsStampedWithTheDeviceItWasMadeOn()
    {
        var devices = new FakeRecordDevices();
        MeasurementSettingsFile.SweepMeasurementSettings settings = WaveSettings();
        settings.AudioBackend = AudioBackend.WasapiShared;
        settings.WaveInputChannelOffset = 0;
        settings.WaveLoopbackInputChannelOffset = 1;
        RecordSettingsSession session = Load(settings, devices);

        session.SetArrayMicrophones([new ArrayMicrophoneDefinition { ChannelOffset = 4 }, new ArrayMicrophoneDefinition { ChannelOffset = 1 }]);

        var applied = new MeasurementSettingsFile.SweepMeasurementSettings();
        session.WriteLiveSettings(applied);
        Assert.Equal("{capture}", applied.WaveArrayDeviceId);
        // The loopback's own input is not recordable as an array microphone.
        Assert.Equal([4], RecordArrayInputs.ReachableChannels(session));
        Assert.Equal("2 microphones (1 unusable)...", RecordArrayInputs.ButtonText(session));
    }
}
