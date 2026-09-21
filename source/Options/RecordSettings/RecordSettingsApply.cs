namespace Resonalyze.Options;

/// <summary>Apply: checks the selected route against the hardware as it is now, then hands the whole configuration to
/// the sweep engine and the settings. A refused route throws <see cref="InvalidOperationException"/> with the reason.</summary>
internal static class RecordSettingsApply
{
    public static void Apply(
        RecordSettingsSession session,
        ExpSweepMeasurement expSweepMeasurement,
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        session.WriteCalibrations(settings);

        int sampleRate = session.SelectedSampleRate;
        // The field is the source of truth (read-only today, equal to expSweepMeasurement.Bits).
        int bits = (int)session.Bits.Value;
        PlaybackChannel playbackChannel = session.SelectedPlaybackChannel;
        double lowFrequencyHz = (double)session.LowFrequency.Value;
        double highFrequencyHz = (double)session.HighFrequency.Value;
        double requestedDuration = session.RequestedDurationSeconds(sampleRate);
        var audioBackend = (AudioBackend)session.Backend.SelectedIndex;
        int outputDeviceNumber = session.PlaybackDevice.SelectedItem is AudioDeviceInfo playbackDevice
            ? playbackDevice.DeviceNumber
            : session.PreferredWavePlaybackDeviceNumber;
        int inputDeviceNumber = session.RecordingDevice.SelectedItem is AudioDeviceInfo recordingDevice
            ? recordingDevice.DeviceNumber
            : session.PreferredWaveRecordingDeviceNumber;
        string? asioDriverName = session.SelectedAsioDriverName;
        if (audioBackend == AudioBackend.Asio && string.IsNullOrWhiteSpace(asioDriverName))
        {
            throw new InvalidOperationException("Select an ASIO driver before starting measurement.");
        }
        if (audioBackend == AudioBackend.Asio)
        {
            ValidateAsioDriver(session.AsioDriverInfo, sampleRate);
        }
        if (audioBackend != AudioBackend.Asio)
        {
            ValidateRequiredWaveLoopback(
                session.WaveLoopback.SelectedItem is InputChannelOption { Offset: not null },
                session.RecordingDeviceSupportsWaveLoopback);
        }
        if (audioBackend == AudioBackend.Wave)
        {
            // An empty Wave rate list means none in common; skipping this let an unopenable rate through.
            SampleRateOptions.ValidateSelectedRate(session.SupportedSampleRates(), sampleRate, "Wave devices");
        }
        int asioInputChannelOffset = session.SelectedAsioInputOffset;
        int? asioLoopbackInputChannelOffset = session.SelectedAsioLoopbackOffset;
        if (audioBackend == AudioBackend.Asio &&
            asioLoopbackInputChannelOffset.HasValue &&
            asioLoopbackInputChannelOffset.Value == asioInputChannelOffset)
        {
            throw new InvalidOperationException("Microphone and loopback inputs must use different ASIO channels.");
        }
        int asioOutputChannelOffset = session.SelectedAsioOutputOffset;
        int waveInputChannelOffset = session.SelectedWaveInputOffset;
        int? waveLoopbackInputChannelOffset = session.SelectedWaveLoopbackOffset;
        int averageRunCount = (int)session.AverageRunCount.Value;
        string? wasapiCaptureEndpointId = session.SelectedCaptureEndpoint?.Id ?? session.PreferredWasapiCaptureEndpointId;
        string? wasapiRenderEndpointId = session.SelectedRenderEndpoint?.Id ?? session.PreferredWasapiRenderEndpointId;
        if (audioBackend.IsWasapi())
        {
            using IRecordEndpointReader endpoints = session.OpenEndpoints();
            AudioEndpointDescriptor captureEndpoint = SelectWasapiEndpoint(
                endpoints.GetCaptureEndpoints(),
                wasapiCaptureEndpointId,
                "capture");
            AudioEndpointDescriptor renderEndpoint = SelectWasapiEndpoint(
                endpoints.GetRenderEndpoints(),
                wasapiRenderEndpointId,
                "render");
            if (!captureEndpoint.IsAvailable || !renderEndpoint.IsAvailable)
            {
                throw new InvalidOperationException(
                    "A selected WASAPI endpoint is unavailable. Reconnect it or select a replacement.");
            }
            if (audioBackend == AudioBackend.WasapiShared &&
                captureEndpoint.PreferredFormat.SampleRate != renderEndpoint.PreferredFormat.SampleRate)
            {
                throw new InvalidOperationException(
                    "The default WASAPI capture and render endpoints use different mix rates. " +
                    "Choose endpoints with the same Windows audio format.");
            }
            wasapiCaptureEndpointId = captureEndpoint.Id;
            wasapiRenderEndpointId = renderEndpoint.Id;
            if (audioBackend == AudioBackend.WasapiShared)
            {
                sampleRate = captureEndpoint.PreferredFormat.SampleRate;
            }
            else if (audioBackend == AudioBackend.WasapiExclusive)
            {
                // An empty list (no common rate) reads as 44.1 kHz; persisting that would persist a refused format.
                // Checked after availability so a gone endpoint keeps its message.
                SampleRateOptions.ValidateSelectedRate(
                    session.SupportedSampleRates(),
                    sampleRate,
                    "The WASAPI Exclusive endpoints");
            }
        }
        if (audioBackend != AudioBackend.Asio &&
            waveLoopbackInputChannelOffset.HasValue &&
            waveLoopbackInputChannelOffset.Value == waveInputChannelOffset)
        {
            throw new InvalidOperationException("Microphone and loopback inputs must use different Wave channels.");
        }

        string? wasapiCaptureEndpointName =
            session.SelectedCaptureEndpoint?.DisplayName ?? session.PreferredWasapiCaptureEndpointName;
        string? wasapiRenderEndpointName =
            session.SelectedRenderEndpoint?.DisplayName ?? session.PreferredWasapiRenderEndpointName;
        IReadOnlyList<int> arrayChannels = RecordArrayInputs.ReachableChannels(session);
        expSweepMeasurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                lowFrequencyHz,
                highFrequencyHz,
                sampleRate,
                bits,
                requestedDuration,
                playbackChannel),
            new SweepAudioConfiguration(
                Backend: audioBackend,
                OutputDeviceNumber: outputDeviceNumber,
                InputDeviceNumber: inputDeviceNumber,
                WaveInputChannelOffset: waveInputChannelOffset,
                WaveLoopbackInputChannelOffset: waveLoopbackInputChannelOffset,
                AsioDriverName: asioDriverName,
                AsioInputChannelOffset: asioInputChannelOffset,
                AsioLoopbackInputChannelOffset: asioLoopbackInputChannelOffset,
                AsioOutputChannelOffset: asioOutputChannelOffset,
                WasapiCaptureEndpointId: wasapiCaptureEndpointId,
                WasapiRenderEndpointId: wasapiRenderEndpointId,
                WasapiCaptureEndpointName: wasapiCaptureEndpointName,
                WasapiRenderEndpointName: wasapiRenderEndpointName,
                WasapiBufferMilliseconds: settings.WasapiBufferMilliseconds,
                // The array too, or the applied configuration differs from what the settings build for the next run.
                WaveArrayInputChannelOffsets: audioBackend == AudioBackend.Asio ? [] : arrayChannels,
                AsioArrayInputChannelOffsets: audioBackend == AudioBackend.Asio ? arrayChannels : []),
            new SweepAveragingConfiguration(averageRunCount),
            RecordHighPass.Read(session)));

        settings.LowFrequencyHz = lowFrequencyHz;
        settings.HighFrequencyHz = highFrequencyHz;
        settings.WasapiCaptureEndpointId = wasapiCaptureEndpointId;
        settings.WasapiRenderEndpointId = wasapiRenderEndpointId;
        settings.WasapiCaptureEndpointName = wasapiCaptureEndpointName;
        settings.WasapiRenderEndpointName = wasapiRenderEndpointName;
        session.AdoptAppliedEndpoints(
            wasapiCaptureEndpointId,
            wasapiRenderEndpointId,
            wasapiCaptureEndpointName,
            wasapiRenderEndpointName);

        expSweepMeasurement.SplCalibration = session.SplCalibration;
    }

    public static void ValidateRequiredWaveLoopback(bool loopbackSelected, bool recordingDeviceSupportsLoopback)
    {
        if (!loopbackSelected)
        {
            throw new InvalidOperationException("A loopback reference channel is required before measuring.");
        }
        if (!recordingDeviceSupportsLoopback)
        {
            throw new InvalidOperationException("Wave loopback requires a selected stereo recording device.");
        }
    }

    private static void ValidateAsioDriver(AsioDriverInfo driver, int sampleRate)
    {
        if (!string.IsNullOrWhiteSpace(driver.ErrorMessage))
        {
            throw new InvalidOperationException(driver.ErrorMessage);
        }
        if (!driver.SupportsSampleRate)
        {
            throw new InvalidOperationException(
                $"ASIO driver '{driver.DriverName}' does not support {sampleRate} Hz.");
        }
        if (driver.InputChannels.Count == 0)
        {
            throw new InvalidOperationException($"ASIO driver '{driver.DriverName}' has no input channels.");
        }
        if (driver.OutputChannels.Count == 0)
        {
            throw new InvalidOperationException(
                $"ASIO driver '{driver.DriverName}' needs at least two output channels.");
        }
    }

    private static AudioEndpointDescriptor SelectWasapiEndpoint(
        IReadOnlyList<AudioEndpointDescriptor> endpoints,
        string? preferredId,
        string direction)
    {
        AudioEndpointDescriptor? endpoint = endpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, preferredId, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(preferredId) && endpoint == null)
        {
            throw new InvalidOperationException(
                $"The saved WASAPI {direction} endpoint is unavailable. " +
                "Reconnect it or choose a replacement before applying settings.");
        }
        endpoint ??= endpoints.FirstOrDefault(candidate => candidate.IsDefault);
        return endpoint ?? throw new InvalidOperationException(
            $"No active WASAPI {direction} endpoint is available.");
    }
}
