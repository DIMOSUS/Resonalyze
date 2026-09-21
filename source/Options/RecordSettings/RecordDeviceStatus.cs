namespace Resonalyze.Options;

internal sealed record RecordStatusLine(string Text, Color Color, bool Emphasized = false);

/// <summary>What the device half of Record Settings shows for the session's route: which controls apply, their
/// captions, and the verdict lines under the Wave/WASAPI and ASIO groups.</summary>
internal sealed record RecordDeviceView(
    bool UseAsio,
    bool UseWasapi,
    bool WaveLoopbackEnabled,
    bool AsioDriverEnabled,
    bool AsioControlPanelEnabled,
    bool AsioInputProbeEnabled,
    bool AsioInputsEnabled,
    bool AsioOutputEnabled,
    string PlaybackDeviceCaption,
    string RecordingDeviceCaption,
    string WaveInputCaption,
    string WaveLoopbackCaption,
    RecordStatusLine LoopbackStatus,
    RecordStatusLine AsioSampleRateStatus,
    string AsioPlaybackLatency);

internal static class RecordDeviceStatus
{
    public static RecordDeviceView Read(RecordSettingsSession session)
    {
        bool useAsio = session.IsAsio;
        bool useWasapi = session.IsWasapi;
        AsioDriverInfo driver = session.AsioDriverInfo;
        bool driverSelected = session.AsioDriver.SelectedItem is AsioDeviceInfo;
        (RecordStatusLine asioStatus, string latency) = AsioStatus(session);
        return new RecordDeviceView(
            useAsio,
            useWasapi,
            WaveLoopbackEnabled: useWasapi || (!useAsio && session.RecordingDeviceSupportsWaveLoopback),
            AsioDriverEnabled: useAsio && session.AsioDriverCount > 0,
            AsioControlPanelEnabled: useAsio && driverSelected,
            AsioInputProbeEnabled: useAsio &&
                driverSelected &&
                driver.InputChannels.Count > 0 &&
                driver.OutputChannels.Count > 0,
            AsioInputsEnabled: useAsio && driver.InputChannels.Count > 0,
            AsioOutputEnabled: useAsio && driver.OutputChannels.Count > 0,
            useWasapi ? "Output endpoint" : "Playback device",
            useWasapi ? "Input endpoint" : "Recording device",
            useWasapi ? "Microphone channel" : "Wave input channel",
            useWasapi ? "Loopback channel" : "Wave loopback channel",
            useWasapi ? WasapiStatus(session) : WaveStatus(session),
            asioStatus,
            latency);
    }

    private static RecordStatusLine WasapiStatus(RecordSettingsSession session)
    {
        AudioEndpointDescriptor? capture = session.SelectedCaptureEndpoint;
        AudioEndpointDescriptor? render = session.SelectedRenderEndpoint;
        if (capture is not { IsAvailable: true } || render is not { IsAvailable: true })
        {
            return new RecordStatusLine(
                "⚠ A saved endpoint is unavailable. Reconnect it or select a replacement.",
                UiPalette.Warning);
        }
        if (session.SelectedWaveLoopbackOffset == null)
        {
            return new RecordStatusLine(
                "⚠ Loopback channel is REQUIRED. Select the physical input carrying the playback reference.",
                UiPalette.Warning,
                Emphasized: true);
        }
        if (session.Backend.SelectedIndex == (int)AudioBackend.WasapiExclusive)
        {
            int selectedRate = session.SelectedSampleRate;
            int bits = (int)session.Bits.Value;
            int captureChannels = session.WaveRecordingChannelCount;
            int renderChannels = session.PlaybackChannelCount;
            if (session.SampleRate.Items.Count == 0)
            {
                // No rate opens at all, so do not name the fallback rate. Exclusive passes the format unchanged;
                // mono (a one-channel format) is the usual reason native-stereo endpoints refuse.
                return new RecordStatusLine(
                    $"⚠ No sample rate opens in Exclusive: {bits}-bit, " +
                    $"{captureChannels}-ch capture, {renderChannels}-ch render. " +
                    (renderChannels < 2
                        ? "Mono asks for a one-channel format most endpoints refuse — try Stereo."
                        : "Try another endpoint pair, or Shared."),
                    UiPalette.Error);
            }

            bool supported = session.IsExclusiveFormatSupported(capture, render, selectedRate);
            return supported
                ? new RecordStatusLine(
                    $"Exclusive: {selectedRate:N0} Hz / {bits}-bit opens directly on both endpoints.",
                    UiPalette.TextSecondary)
                : new RecordStatusLine(
                    $"⚠ Exclusive format {selectedRate:N0} Hz / {bits}-bit is not supported by both endpoints.",
                    UiPalette.Error);
        }

        string compatibility = capture.PreferredFormat.SampleRate == render.PreferredFormat.SampleRate
            ? ""
            : " — sample rates do not match";
        return new RecordStatusLine(
            $"Shared mix format: {capture.PreferredFormat.SampleRate:N0} Hz / " +
            $"{capture.PreferredFormat.BitsPerSample}-bit capture, " +
            $"{render.PreferredFormat.BitsPerSample}-bit render{compatibility}. " +
            "Windows may convert render audio; timing remains loopback-referenced.",
            compatibility.Length == 0 ? UiPalette.TextSecondary : UiPalette.Error);
    }

    // No loopback = no transfer IR, no measurement; the line makes it impossible to overlook.
    private static RecordStatusLine WaveStatus(RecordSettingsSession session)
    {
        bool loopbackSelected = session.WaveLoopback.SelectedItem is InputChannelOption { Offset: not null };
        bool supportsLoopback = session.RecordingDeviceSupportsWaveLoopback;
        if (!loopbackSelected)
        {
            return new RecordStatusLine(
                supportsLoopback
                    ? "⚠ Loopback channel is REQUIRED. Select the channel carrying the " +
                        "loopback reference; measurements cannot run without it."
                    : "⚠ Loopback channel is REQUIRED. Select a stereo recording device, " +
                        "then choose its channel.",
                UiPalette.Warning,
                Emphasized: true);
        }

        return new RecordStatusLine(
            supportsLoopback
                ? "Stereo input available for Wave loopback."
                : "Select a stereo recording device.",
            supportsLoopback ? UiPalette.TextSecondary : UiPalette.Error);
    }

    private static (RecordStatusLine Status, string Latency) AsioStatus(RecordSettingsSession session)
    {
        AsioDriverInfo driver = session.AsioDriverInfo;
        if (!string.IsNullOrWhiteSpace(driver.ErrorMessage))
        {
            return (new RecordStatusLine(driver.ErrorMessage, UiPalette.Error), "-");
        }

        int sampleRate = session.SelectedSampleRate;
        RecordStatusLine status;
        if (session.SampleRateProbeFailed)
        {
            // The last probe said nothing, so do not claim support for an untested rate.
            status = new RecordStatusLine(
                $"{sampleRate} Hz kept — the driver did not report its rates",
                UiPalette.Warning);
        }
        else if (session.SampleRateFellBackFrom is int previous)
        {
            status = new RecordStatusLine(
                $"{previous} Hz is not offered by this driver — changed to {sampleRate} Hz",
                UiPalette.Error);
        }
        else
        {
            status = driver.SupportsSampleRate
                ? new RecordStatusLine($"{sampleRate} Hz supported", UiPalette.Success)
                : new RecordStatusLine($"{sampleRate} Hz not supported", UiPalette.Error);
        }

        return (status, driver.PlaybackLatency > 0 ? $"{driver.PlaybackLatency} samples" : "-");
    }
}
