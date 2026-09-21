using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>The verdict lines and captions of the device half, read from a session.</summary>
public sealed class RecordDeviceStatusTests
{
    private static RecordSettingsSession Load(AudioBackend backend, FakeRecordDevices devices, Action<MeasurementSettingsFile.SweepMeasurementSettings>? edit = null)
    {
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            AudioBackend = backend,
            SampleRate = 48_000,
            OutputDeviceNumber = 0,
            InputDeviceNumber = 1,
            WaveInputChannelOffset = 0,
            WaveLoopbackInputChannelOffset = 1,
            PlaybackChannel = PlaybackChannel.Stereo
        };
        edit?.Invoke(settings);
        var session = new RecordSettingsSession(devices);
        session.Load(settings);
        return session;
    }

    [Fact]
    public void AStereoWaveRouteWithALoopbackIsReady()
    {
        RecordDeviceView view = RecordDeviceStatus.Read(Load(AudioBackend.Wave, new FakeRecordDevices()));

        Assert.False(view.UseAsio);
        Assert.Equal("Recording device", view.RecordingDeviceCaption);
        Assert.Equal("Stereo input available for Wave loopback.", view.LoopbackStatus.Text);
        Assert.False(view.LoopbackStatus.Emphasized);
        Assert.True(view.WaveLoopbackEnabled);
    }

    [Fact]
    public void AMonoRecordingDeviceDemandsTheLoopbackInBold()
    {
        RecordDeviceView view = RecordDeviceStatus.Read(
            Load(AudioBackend.Wave, new FakeRecordDevices(), settings => settings.InputDeviceNumber = 0));

        Assert.StartsWith("⚠ Loopback channel is REQUIRED. Select a stereo recording device", view.LoopbackStatus.Text);
        Assert.True(view.LoopbackStatus.Emphasized);
        Assert.False(view.WaveLoopbackEnabled);
    }

    [Fact]
    public void SharedWasapiNamesTheMixFormat_AndExclusiveThatNoRateOpensInMono()
    {
        var devices = new FakeRecordDevices();
        RecordDeviceView shared = RecordDeviceStatus.Read(Load(AudioBackend.WasapiShared, devices));
        Assert.Equal("Input endpoint", shared.RecordingDeviceCaption);
        Assert.StartsWith("Shared mix format: ", shared.LoopbackStatus.Text);
        Assert.Contains("24-bit capture, 24-bit render. ", shared.LoopbackStatus.Text, StringComparison.Ordinal);

        RecordDeviceView mono = RecordDeviceStatus.Read(Load(
            AudioBackend.WasapiExclusive,
            devices,
            settings => settings.PlaybackChannel = PlaybackChannel.Mono));
        Assert.StartsWith("⚠ No sample rate opens in Exclusive", mono.LoopbackStatus.Text);
        Assert.EndsWith("try Stereo.", mono.LoopbackStatus.Text);
    }

    [Fact]
    public void AnAsioDriverThatCannotOpenSaysWhy()
    {
        var devices = new FakeRecordDevices();
        devices.AsioDrivers["Card"] = FakeRecordDevices.Asio("Card", 4, 2, 48_000);
        RecordSettingsSession session = Load(
            AudioBackend.Asio,
            devices,
            settings => settings.AsioDriverName = "Gone");

        RecordDeviceView view = RecordDeviceStatus.Read(session);

        Assert.Equal("Not installed.", view.AsioSampleRateStatus.Text);
        Assert.Equal("-", view.AsioPlaybackLatency);
        Assert.False(view.AsioInputProbeEnabled);
        Assert.True(view.AsioControlPanelEnabled);
    }

    [Fact]
    public void AnAsioDriverWithTheRateIsSupported()
    {
        var devices = new FakeRecordDevices();
        devices.AsioDrivers["Card"] = FakeRecordDevices.Asio("Card", 4, 2, 48_000);
        RecordSettingsSession session = Load(AudioBackend.Asio, devices, settings => settings.AsioDriverName = "Card");

        RecordDeviceView view = RecordDeviceStatus.Read(session);

        Assert.Equal("48000 Hz supported", view.AsioSampleRateStatus.Text);
        Assert.Equal("256 samples", view.AsioPlaybackLatency);
        Assert.True(view.AsioInputProbeEnabled);
    }
}
