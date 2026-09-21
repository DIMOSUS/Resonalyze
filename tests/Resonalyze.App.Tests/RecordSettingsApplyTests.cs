using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Apply: which routes it refuses and why, and what an accepted one hands the engine and the settings.</summary>
public sealed class RecordSettingsApplyTests
{
    private static MeasurementSettingsFile.SweepMeasurementSettings Settings(AudioBackend backend) =>
        new()
        {
            AudioBackend = backend,
            SampleRate = 48_000,
            OutputDeviceNumber = 0,
            InputDeviceNumber = 1,
            WaveInputChannelOffset = 0,
            WaveLoopbackInputChannelOffset = 1,
            AsioDriverName = "Card",
            AsioInputChannelOffset = 0,
            AsioLoopbackInputChannelOffset = 1,
            WasapiCaptureEndpointId = "{capture}",
            WasapiRenderEndpointId = "{render}",
            PlaybackChannel = PlaybackChannel.Stereo,
            AverageRunCount = 3
        };

    private static (RecordSettingsSession Session, FakeRecordDevices Devices) Load(
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        var devices = new FakeRecordDevices();
        devices.AsioDrivers["Card"] = FakeRecordDevices.Asio("Card", 4, 2, 44_100, 48_000);
        var session = new RecordSettingsSession(devices);
        session.Load(settings);
        return (session, devices);
    }

    private static ExpSweepMeasurement Engine()
    {
        var engine = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        engine.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(20, 20_000, 48_000, 24, 0.2, PlaybackChannel.Mono),
            new SweepAudioConfiguration(WaveInputChannelOffset: 0, WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration(1)));
        return engine;
    }

    private static string Refusal(RecordSettingsSession session)
    {
        using ExpSweepMeasurement engine = Engine();
        return Assert.Throws<InvalidOperationException>(() =>
            RecordSettingsApply.Apply(session, engine, new MeasurementSettingsFile.SweepMeasurementSettings())).Message;
    }

    [Fact]
    public void AnAcceptedWaveRouteReachesTheEngineAndTheSettings()
    {
        (RecordSettingsSession session, _) = Load(Settings(AudioBackend.Wave));
        using ExpSweepMeasurement engine = Engine();
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings();

        RecordSettingsApply.Apply(session, engine, settings);

        Assert.Equal(AudioBackend.Wave, engine.AudioBackend);
        Assert.Equal(48_000, engine.SampleRate);
        Assert.Equal(1, engine.InputDeviceNumber);
        Assert.Equal(1, engine.WaveLoopbackInputChannelOffset);
        Assert.Equal(PlaybackChannel.Stereo, engine.PlaybackChannel);
        Assert.Equal(3, engine.AverageRunCount);
        Assert.Equal(20.0, settings.LowFrequencyHz);
    }

    [Fact]
    public void TheWaveMicrophoneAndLoopbackMustDiffer()
    {
        (RecordSettingsSession session, _) = Load(Settings(AudioBackend.Wave));
        session.WaveInput.SelectedIndex = 1;

        Assert.Equal("Microphone and loopback inputs must use different Wave channels.", Refusal(session));
    }

    [Fact]
    public void AWaveRouteWithoutALoopbackIsRefused()
    {
        (RecordSettingsSession session, _) = Load(Settings(AudioBackend.Wave));
        session.WaveLoopback.SelectedIndex = 0;

        Assert.Equal("A loopback reference channel is required before measuring.", Refusal(session));
    }

    [Fact]
    public void AWaveRateThePairDoesNotOpenIsRefused()
    {
        (RecordSettingsSession session, FakeRecordDevices devices) = Load(Settings(AudioBackend.Wave));
        devices.Rates.Clear();
        session.PlaybackChannel.SelectedIndex = (int)PlaybackChannel.Mono;

        Assert.StartsWith("Wave devices report no sample rate in common", Refusal(session));
    }

    [Fact]
    public void TheAsioMicrophoneAndLoopbackMustDiffer()
    {
        (RecordSettingsSession session, _) = Load(Settings(AudioBackend.Asio));
        session.AsioLoopback.SelectedIndex = 1;

        Assert.Equal("Microphone and loopback inputs must use different ASIO channels.", Refusal(session));
    }

    [Fact]
    public void AnAsioDriverThatCannotOpenIsRefusedWithItsOwnMessage()
    {
        MeasurementSettingsFile.SweepMeasurementSettings settings = Settings(AudioBackend.Asio);
        settings.AsioDriverName = "Gone";
        (RecordSettingsSession session, _) = Load(settings);

        Assert.Equal("Not installed.", Refusal(session));
    }

    [Fact]
    public void AWasapiEndpointGoneSinceTheListWasReadIsRefused()
    {
        (RecordSettingsSession session, FakeRecordDevices devices) = Load(Settings(AudioBackend.WasapiShared));
        devices.Capture.Clear();

        Assert.StartsWith("The saved WASAPI capture endpoint is unavailable.", Refusal(session));
    }

    [Fact]
    public void SharedWasapiRunsAtTheEndpointsMixRate_AndRemembersTheResolvedEndpoints()
    {
        (RecordSettingsSession session, _) = Load(Settings(AudioBackend.WasapiShared));
        using ExpSweepMeasurement engine = Engine();
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings();

        RecordSettingsApply.Apply(session, engine, settings);

        Assert.Equal(48_000, engine.SampleRate);
        Assert.Equal("{capture}", settings.WasapiCaptureEndpointId);
        Assert.Equal("Interface in", settings.WasapiCaptureEndpointName);
        Assert.Equal("{capture}", session.PreferredWasapiCaptureEndpointId);
    }

    [Theory]
    [InlineData("{other}", new[] { 4, 5 })]
    [InlineData("{capture}", new int[0])]
    public void AnArrayRecordsOnlyOnTheEndpointItWasSetUpOn(string selectedCapture, int[] expected)
    {
        MeasurementSettingsFile.SweepMeasurementSettings loaded = Settings(AudioBackend.WasapiShared);
        loaded.WaveArrayMicrophones = [new ArrayMicrophoneDefinition { ChannelOffset = 4 }, new ArrayMicrophoneDefinition { ChannelOffset = 5 }];
        loaded.WaveArrayDeviceId = "{other}";
        loaded.WasapiCaptureEndpointId = selectedCapture;
        var devices = new FakeRecordDevices();
        devices.Capture.Add(FakeRecordDevices.Endpoint("{other}", "Other in", AudioEndpointDirection.Capture, 48_000, 8));
        var session = new RecordSettingsSession(devices);
        session.Load(loaded);
        using ExpSweepMeasurement engine = Engine();

        RecordSettingsApply.Apply(session, engine, new MeasurementSettingsFile.SweepMeasurementSettings());

        Assert.Equal(expected, engine.WaveArrayInputChannelOffsets);
    }

    [Fact]
    public void ARefusedApplyStillKeepsTheCalibrations()
    {
        MeasurementSettingsFile.SweepMeasurementSettings loaded = Settings(AudioBackend.Wave);
        loaded.MicrophoneCalibration0DegreesPath = "zero.cal";
        (RecordSettingsSession session, _) = Load(loaded);
        session.WaveLoopback.SelectedIndex = 0;
        using ExpSweepMeasurement engine = Engine();
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings();

        Assert.Throws<InvalidOperationException>(() => RecordSettingsApply.Apply(session, engine, settings));

        Assert.Equal("zero.cal", settings.MicrophoneCalibration0DegreesPath);
    }

    [Fact]
    public void TheLoopbackIsRequired_AndNeedsAStereoDevice()
    {
        Assert.Contains(
            "required",
            Assert.Throws<InvalidOperationException>(() =>
                RecordSettingsApply.ValidateRequiredWaveLoopback(false, true)).Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "stereo",
            Assert.Throws<InvalidOperationException>(() =>
                RecordSettingsApply.ValidateRequiredWaveLoopback(true, false)).Message,
            StringComparison.OrdinalIgnoreCase);
    }
}
