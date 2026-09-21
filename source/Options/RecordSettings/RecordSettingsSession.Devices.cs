namespace Resonalyze.Options;

// The audio route: backend, devices, channels, rate and the array microphones on them, and the rules that re-read
// the hardware when one of them moves.
internal sealed partial class RecordSettingsSession
{
    private IReadOnlyList<AudioDeviceInfo> playbackDevices = Array.Empty<AudioDeviceInfo>();
    private IReadOnlyList<AudioDeviceInfo> recordingDevices = Array.Empty<AudioDeviceInfo>();
    private IReadOnlyList<AudioEndpointDescriptor> wasapiCaptureEndpoints = Array.Empty<AudioEndpointDescriptor>();
    private IReadOnlyList<AudioEndpointDescriptor> wasapiRenderEndpoints = Array.Empty<AudioEndpointDescriptor>();
    private IReadOnlyList<AsioDeviceInfo> asioDrivers = Array.Empty<AsioDeviceInfo>();
    // Remembered while a mono/missing device forces "None", so a stereo device restores it.
    private int? preferredWaveLoopbackChannelOffset;
    private bool updatingWaveLoopbackSelection;
    private string? preferredWasapiCaptureEndpointId;
    private string? preferredWasapiRenderEndpointId;
    private string? preferredWasapiCaptureEndpointName;
    private string? preferredWasapiRenderEndpointName;
    private int preferredWavePlaybackDeviceNumber = -1;
    private int preferredWaveRecordingDeviceNumber = -1;
    private (ExclusiveFormat Format, bool Supported)? exclusiveVerdict;

    public RecordChoice Backend { get; } = new();
    public RecordChoice PlaybackDevice { get; } = new();
    public RecordChoice RecordingDevice { get; } = new();
    public RecordChoice WaveInput { get; } = new();
    public RecordChoice WaveLoopback { get; } = new();
    public RecordChoice AsioDriver { get; } = new();
    public RecordChoice AsioInput { get; } = new();
    public RecordChoice AsioOutput { get; } = new();
    public RecordChoice AsioLoopback { get; } = new();
    public RecordChoice SampleRate { get; } = new();

    public int AsioDriverCount => asioDrivers.Count;
    public AsioDriverInfo AsioDriverInfo { get; private set; } = AsioDeviceCatalog.EmptyDriverInfo;
    public bool SampleRateProbeFailed { get; private set; }
    public int? SampleRateFellBackFrom { get; private set; }
    public string? PreferredWasapiCaptureEndpointId => preferredWasapiCaptureEndpointId;
    public string? PreferredWasapiRenderEndpointId => preferredWasapiRenderEndpointId;
    public string? PreferredWasapiCaptureEndpointName => preferredWasapiCaptureEndpointName;
    public string? PreferredWasapiRenderEndpointName => preferredWasapiRenderEndpointName;
    public int PreferredWavePlaybackDeviceNumber => preferredWavePlaybackDeviceNumber;
    public int PreferredWaveRecordingDeviceNumber => preferredWaveRecordingDeviceNumber;

    public AudioBackend SelectedBackend =>
        Backend.SelectedIndex >= 0 ? (AudioBackend)Backend.SelectedIndex : AudioBackend.Wave;

    public bool IsAsio => Backend.SelectedIndex == (int)AudioBackend.Asio;

    public bool IsWasapi =>
        Backend.SelectedIndex is (int)AudioBackend.WasapiShared or (int)AudioBackend.WasapiExclusive;

    /// <summary>44.1 kHz while the list is empty; readers that must not describe that fallback check the list.</summary>
    public int SelectedSampleRate => SampleRate.SelectedItem is int rate ? rate : 44_100;

    public AudioEndpointDescriptor? SelectedCaptureEndpoint => RecordingDevice.SelectedItem as AudioEndpointDescriptor;
    public AudioEndpointDescriptor? SelectedRenderEndpoint => PlaybackDevice.SelectedItem as AudioEndpointDescriptor;
    public string? SelectedAsioDriverName => (AsioDriver.SelectedItem as AsioDeviceInfo)?.DriverName;

    public int SelectedPlaybackDeviceNumber =>
        PlaybackDevice.SelectedItem is AudioDeviceInfo device ? device.DeviceNumber : -1;

    public int SelectedRecordingDeviceNumber =>
        RecordingDevice.SelectedItem is AudioDeviceInfo device ? device.DeviceNumber : -1;

    public int SelectedWaveInputOffset =>
        WaveInput.SelectedItem is InputChannelOption option ? option.Offset ?? 0 : 0;

    public int? SelectedWaveLoopbackOffset =>
        WaveLoopback.SelectedItem is InputChannelOption option ? option.Offset : null;

    public int SelectedAsioInputOffset => AsioInput.SelectedItem is AsioChannelInfo channel ? channel.Offset : 0;
    public int SelectedAsioOutputOffset => AsioOutput.SelectedItem is AsioChannelInfo channel ? channel.Offset : 0;

    public int? SelectedAsioLoopbackOffset =>
        AsioLoopback.SelectedItem is InputChannelOption option ? option.Offset : null;

    public int PlaybackChannelCount => SelectedPlaybackChannel == Audio.PlaybackChannel.Mono ? 1 : 2;

    public bool RecordingDeviceSupportsWaveLoopback =>
        RecordingDevice.SelectedItem is AudioDeviceInfo { Channels: >= 2 } or
            AudioEndpointDescriptor { ChannelCount: >= 2, IsAvailable: true };

    /// <remarks>Uses <see cref="AudioCaptureRouting.RequiredInputChannelCount"/> so the probed width matches the opened width.</remarks>
    public int WaveRecordingChannelCount
    {
        get
        {
            int microphone = SelectedWaveInputOffset;
            int? loopback = SelectedWaveLoopbackOffset;
            var routing = new AudioCaptureRouting(microphone, loopback)
            {
                ArrayChannels = RecordArrayInputs.ReachableChannels(this)
            };
            // Mic on offset 1 needs a 2-channel format even without loopback.
            int loopbackChannels = loopback.HasValue ? 2 : 1;
            return Math.Max(routing.RequiredInputChannelCount, loopbackChannels);
        }
    }

    // A channel number names a different input on each backend (and each device).
    public IReadOnlyList<ArrayMicrophoneDefinition> ArrayMicrophones =>
        SelectedBackend == AudioBackend.Asio ? asioArrayMicrophones : waveArrayMicrophones;

    public string? ArrayDeviceId =>
        SelectedBackend == AudioBackend.Asio ? asioArrayDeviceId : waveArrayDeviceId;

    public string? CaptureDeviceId =>
        SelectedBackend == AudioBackend.Asio
            ? SelectedAsioDriverName
            : SelectedCaptureEndpoint?.Id ?? preferredWasapiCaptureEndpointId;

    public void SetArrayMicrophones(IEnumerable<ArrayMicrophoneDefinition> microphones)
    {
        List<ArrayMicrophoneDefinition> edited = microphones
            .Select(microphone => microphone.Clone())
            .ToList();
        if (SelectedBackend == AudioBackend.Asio)
        {
            asioArrayMicrophones = edited;
            asioArrayDeviceId = CaptureDeviceId;
        }
        else
        {
            waveArrayMicrophones = edited;
            waveArrayDeviceId = CaptureDeviceId;
        }

        // Applies as edited like every other field, so closing the panel keeps it.
        RaiseSweepSettingsChanged();
    }

    /// <summary>Rates the selected route opens, asked of the hardware (the last probe, for ASIO).</summary>
    public IReadOnlyList<int> SupportedSampleRates()
    {
        if (IsAsio)
        {
            // From the last probe: some drivers refuse a second open moments later (empty rate list).
            return AsioDriverInfo.SupportedSampleRates;
        }

        if (IsWasapi)
        {
            AudioEndpointDescriptor? capture = SelectedCaptureEndpoint;
            AudioEndpointDescriptor? render = SelectedRenderEndpoint;
            if (capture is not { IsAvailable: true } || render is not { IsAvailable: true })
            {
                return [];
            }
            if (Backend.SelectedIndex == (int)AudioBackend.WasapiShared)
            {
                return capture.PreferredFormat.SampleRate == render.PreferredFormat.SampleRate
                    ? [capture.PreferredFormat.SampleRate]
                    : [];
            }

            int captureChannels = WaveRecordingChannelCount;
            int renderChannels = PlaybackChannelCount;
            int bits = (int)Bits.Value;
            return SampleRateCatalog.GetCandidateRates()
                .Where(rate => devices.IsExclusiveFormatSupported(
                    capture.Id,
                    render.Id,
                    rate,
                    bits,
                    captureChannels,
                    renderChannels))
                .ToArray();
        }

        return devices.GetSupportedWaveSampleRates(
            SelectedPlaybackDeviceNumber,
            SelectedRecordingDeviceNumber,
            PlaybackChannelCount,
            WaveRecordingChannelCount,
            (int)Bits.Value);
    }

    /// <summary>For the status line: asked again only when the format or a device changes, since each ask opens both
    /// endpoints and an edit elsewhere in the panel is not a reason to.</summary>
    public bool IsExclusiveFormatSupported(AudioEndpointDescriptor capture, AudioEndpointDescriptor render, int sampleRate)
    {
        var format = new ExclusiveFormat(
            capture.Id,
            render.Id,
            sampleRate,
            (int)Bits.Value,
            WaveRecordingChannelCount,
            PlaybackChannelCount);
        if (exclusiveVerdict is not { } verdict || verdict.Format != format)
        {
            verdict = (format, devices.IsExclusiveFormatSupported(
                format.CaptureId,
                format.RenderId,
                format.SampleRate,
                format.Bits,
                format.CaptureChannels,
                format.RenderChannels));
            exclusiveVerdict = verdict;
        }

        return verdict.Supported;
    }

    /// <summary>A WASAPI endpoint came or went: re-read them and, on WASAPI, rebuild the route around the same picks.</summary>
    public void RefreshEndpoints()
    {
        int inputOffset = SelectedWaveInputOffset;
        int? loopbackOffset = SelectedWaveLoopbackOffset;
        LoadWasapiEndpoints();
        exclusiveVerdict = null;
        if (IsWasapi)
        {
            PopulateDeviceChoices(inputOffset, loopbackOffset);
            RefreshSampleRateOptions(SelectedSampleRate);
            SettleWaveLoopback();
        }
    }

    /// <summary>Re-reads the device after Apply reconfigured it under the open panel (otherwise the status stays stale).</summary>
    public void RefreshAudioDevice()
    {
        if (initializing)
        {
            return;
        }

        int preferredSampleRate = SelectedSampleRate;
        exclusiveVerdict = null;
        if (IsAsio)
        {
            RefreshAsioDriverInfo(
                preferredSampleRate,
                SelectedAsioInputOffset,
                SelectedAsioOutputOffset,
                SelectedAsioLoopbackOffset);
        }

        RefreshSampleRateOptions(preferredSampleRate);
        SettleWaveLoopback();
    }

    /// <summary>Opens the driver's own panel, then re-reads what it may have changed (buffer size, reported rates).</summary>
    public void ShowAsioControlPanel()
    {
        if (AsioDriver.SelectedItem is not AsioDeviceInfo asioDriver)
        {
            return;
        }

        int preferredSampleRate = SelectedSampleRate;
        int preferredInputOffset = SelectedAsioInputOffset;
        int preferredOutputOffset = SelectedAsioOutputOffset;
        int? preferredLoopbackOffset = SelectedAsioLoopbackOffset;
        devices.ShowAsioControlPanel(asioDriver.DriverName);
        RefreshAsioDriverInfo(
            preferredSampleRate,
            preferredInputOffset,
            preferredOutputOffset,
            preferredLoopbackOffset);
        RefreshSampleRateOptions(preferredSampleRate);
        SettleWaveLoopback();
    }

    /// <summary>Records one second of every input of the selected driver; null when no driver is selected.</summary>
    public Task<IReadOnlyList<AsioInputProbeChannelResult>>? ProbeAsioInputsAsync() =>
        AsioDriver.SelectedItem is AsioDeviceInfo driver
            ? devices.ProbeAsioInputsAsync(
                driver.DriverName,
                SelectedSampleRate,
                SelectedAsioOutputOffset,
                CancellationToken.None)
            : null;

    /// <summary>Settles the route after something outside the session touched the device (the input probe).</summary>
    public void SettleRoute() => SettleWaveLoopback();

    /// <summary>What Apply resolved the endpoints to becomes the pick the next rebuild keeps.</summary>
    public void AdoptAppliedEndpoints(string? captureId, string? renderId, string? captureName, string? renderName)
    {
        preferredWasapiCaptureEndpointId = captureId;
        preferredWasapiRenderEndpointId = renderId;
        preferredWasapiCaptureEndpointName = captureName;
        preferredWasapiRenderEndpointName = renderName;
    }

    internal IRecordEndpointReader OpenEndpoints() => devices.OpenEndpoints();

    internal static AudioEndpointDescriptor CreateUnavailableEndpoint(
        string endpointId,
        string? friendlyName,
        AudioEndpointDirection direction) =>
        new(
            endpointId,
            string.IsNullOrWhiteSpace(friendlyName) ? endpointId : friendlyName,
            direction,
            new AudioFormat(44_100, 16, 1, AudioSampleEncoding.Pcm),
            0,
            IsAvailable: false,
            IsDefault: false);

    private void LoadDevices(MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        Backend.Clear();
        foreach (AudioBackend backend in Enum.GetValues<AudioBackend>())
        {
            Backend.Add(backend switch
            {
                AudioBackend.Wave => "MME Compatibility",
                AudioBackend.WasapiShared => "WASAPI Shared",
                AudioBackend.WasapiExclusive => "WASAPI Exclusive",
                _ => backend.ToString()
            });
        }
        Backend.SelectedIndex = Enum.IsDefined(settings.AudioBackend)
            ? (int)settings.AudioBackend
            : (int)AudioBackend.Wave;

        playbackDevices = devices.GetPlaybackDevices();
        PlaybackDevice.Clear();
        PlaybackDevice.AddRange(playbackDevices);
        SelectDeviceOrShowMissing(PlaybackDevice, playbackDevices, settings.OutputDeviceNumber);

        recordingDevices = devices.GetRecordingDevices();
        LoadWasapiEndpoints();
        PopulateDeviceChoices(settings.WaveInputChannelOffset, settings.WaveLoopbackInputChannelOffset);

        asioDrivers = devices.GetAsioDrivers();
        AsioDriver.Clear();
        AsioDriver.AddRange(asioDrivers);
        int asioDriverIndex = AsioDeviceCatalog.FindDriverIndex(asioDrivers, settings.AsioDriverName);
        if (asioDriverIndex < 0 && !string.IsNullOrWhiteSpace(settings.AsioDriverName))
        {
            // Keep an absent saved driver selectable so Apply re-persists the same name.
            AsioDriver.Add(new AsioDeviceInfo(settings.AsioDriverName, Missing: true));
            asioDriverIndex = AsioDriver.Items.Count - 1;
        }
        if (asioDriverIndex >= 0)
        {
            AsioDriver.SelectedIndex = asioDriverIndex;
        }
    }

    private void WireDeviceRules()
    {
        Backend.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            int preferredSampleRate = SelectedSampleRate;
            PopulateDeviceChoices(SelectedWaveInputOffset, SelectedWaveLoopbackOffset);
            if (IsAsio)
            {
                // Opening ASIO is a slow synchronous COM call; skip it for Wave changes.
                RefreshAsioDriverInfo(
                    preferredSampleRate,
                    SelectedAsioInputOffset,
                    SelectedAsioOutputOffset,
                    SelectedAsioLoopbackOffset);
            }
            RefreshSampleRateOptions(preferredSampleRate);
            SettleWaveLoopback();
        };
        PlaybackDevice.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            if (PlaybackDevice.SelectedItem is AudioEndpointDescriptor endpoint)
            {
                preferredWasapiRenderEndpointId = endpoint.Id;
            }
            else if (PlaybackDevice.SelectedItem is AudioDeviceInfo device)
            {
                preferredWavePlaybackDeviceNumber = device.DeviceNumber;
            }
            RefreshSampleRateOptions(SelectedSampleRate);
        };
        RecordingDevice.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            if (RecordingDevice.SelectedItem is AudioEndpointDescriptor endpoint)
            {
                preferredWasapiCaptureEndpointId = endpoint.Id;
                FillWasapiChannelChoices(SelectedWaveInputOffset, SelectedWaveLoopbackOffset);
            }
            else if (RecordingDevice.SelectedItem is AudioDeviceInfo device)
            {
                preferredWaveRecordingDeviceNumber = device.DeviceNumber;
            }
            SettleWaveLoopback();
            RefreshSampleRateOptions(SelectedSampleRate);
        };
        // Channel count changes which sample rates the device supports.
        WaveInput.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            RefreshSampleRateOptions(SelectedSampleRate);
        };
        WaveLoopback.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            if (!updatingWaveLoopbackSelection)
            {
                preferredWaveLoopbackChannelOffset = SelectedWaveLoopbackOffset;
            }
            SettleWaveLoopback();
            RefreshSampleRateOptions(SelectedSampleRate);
        };
        AsioDriver.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            // With ASIO the rate list comes from the driver probe.
            RefreshAsioDriverInfo(
                SelectedSampleRate,
                SelectedAsioInputOffset,
                SelectedAsioOutputOffset,
                SelectedAsioLoopbackOffset);
            RefreshSampleRateOptions(SelectedSampleRate);
            SettleWaveLoopback();
        };
        SampleRate.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            // A user pick ends any earlier automatic-fallback marker; the probe verdict is re-taken below.
            SampleRateFellBackFrom = null;
            if (!IsAsio)
            {
                return;
            }

            // Buffer size and latency are rate-dependent.
            RefreshAsioDriverInfo(
                SelectedSampleRate,
                SelectedAsioInputOffset,
                SelectedAsioOutputOffset,
                SelectedAsioLoopbackOffset);
            SettleWaveLoopback();
        };
    }

    private void LoadWasapiEndpoints() =>
        (wasapiCaptureEndpoints, wasapiRenderEndpoints) = devices.GetEndpoints();

    private void PopulateDeviceChoices(int preferredInputOffset, int? preferredLoopbackOffset)
    {
        bool wasInitializing = initializing;
        initializing = true;
        try
        {
            if (IsWasapi)
            {
                PopulateWasapiEndpointChoice(
                    PlaybackDevice,
                    wasapiRenderEndpoints,
                    preferredWasapiRenderEndpointId,
                    preferredWasapiRenderEndpointName,
                    AudioEndpointDirection.Render);
                PopulateWasapiEndpointChoice(
                    RecordingDevice,
                    wasapiCaptureEndpoints,
                    preferredWasapiCaptureEndpointId,
                    preferredWasapiCaptureEndpointName,
                    AudioEndpointDirection.Capture);
                FillWasapiChannelChoices(preferredInputOffset, preferredLoopbackOffset);
            }
            else
            {
                PlaybackDevice.Clear();
                PlaybackDevice.AddRange(playbackDevices);
                SelectDeviceOrShowMissing(PlaybackDevice, playbackDevices, preferredWavePlaybackDeviceNumber);
                RecordingDevice.Clear();
                RecordingDevice.AddRange(recordingDevices);
                SelectDeviceOrShowMissing(RecordingDevice, recordingDevices, preferredWaveRecordingDeviceNumber);
                FillWaveChannelChoices(preferredInputOffset, preferredLoopbackOffset);
            }
        }
        finally
        {
            initializing = wasInitializing;
        }
    }

    private static void PopulateWasapiEndpointChoice(
        RecordChoice choice,
        IReadOnlyList<AudioEndpointDescriptor> endpoints,
        string? preferredId,
        string? preferredName,
        AudioEndpointDirection direction)
    {
        choice.Clear();
        choice.AddRange(endpoints);
        int index = endpoints.ToList().FindIndex(endpoint =>
            string.Equals(endpoint.Id, preferredId, StringComparison.Ordinal));
        if (index < 0 && !string.IsNullOrWhiteSpace(preferredId))
        {
            choice.Add(CreateUnavailableEndpoint(preferredId, preferredName, direction));
            index = choice.Items.Count - 1;
        }
        if (index < 0)
        {
            index = endpoints.ToList().FindIndex(endpoint => endpoint.IsDefault);
        }
        if (index < 0 && choice.Items.Count > 0)
        {
            index = 0;
        }
        choice.SelectedIndex = index;
    }

    private void FillWasapiChannelChoices(int preferredInputOffset, int? preferredLoopbackOffset)
    {
        int channelCount = SelectedCaptureEndpoint?.ChannelCount ?? 0;
        int preservedChannelCount = Math.Max(
            preferredInputOffset + 1,
            preferredLoopbackOffset.GetValueOrDefault(-1) + 1);
        channelCount = Math.Max(channelCount, preservedChannelCount);
        InputChannelOption[] channels = Enumerable.Range(0, channelCount)
            .Select(index => new InputChannelOption(index, $"Input {index + 1}"))
            .ToArray();

        WaveInput.Clear();
        WaveInput.AddRange(channels);
        WaveInput.SelectedIndex = channelCount > 0
            ? Math.Clamp(preferredInputOffset, 0, channelCount - 1)
            : -1;
        WaveLoopback.Clear();
        WaveLoopback.Add(new InputChannelOption(null, "None"));
        WaveLoopback.AddRange(channels);
        WaveLoopback.SelectedIndex = preferredLoopbackOffset is int offset && offset >= 0 && offset < channelCount
            ? offset + 1
            : 0;
        preferredWaveLoopbackChannelOffset = preferredLoopbackOffset;
    }

    private void FillWaveChannelChoices(int preferredInputOffset, int? preferredLoopbackOffset)
    {
        InputChannelOption[] requiredChannels =
        [
            new InputChannelOption(0, "Left"),
            new InputChannelOption(1, "Right")
        ];
        WaveInput.Clear();
        WaveInput.AddRange(requiredChannels);
        WaveInput.SelectedIndex = preferredInputOffset == 1 ? 1 : 0;

        WaveLoopback.Clear();
        WaveLoopback.Add(new InputChannelOption(null, "None"));
        WaveLoopback.AddRange(requiredChannels);
        WaveLoopback.SelectedIndex = preferredLoopbackOffset.HasValue
            ? preferredLoopbackOffset.Value == 1 ? 2 : 1
            : 0;
        preferredWaveLoopbackChannelOffset = preferredLoopbackOffset;
        SettleWaveLoopback();
    }

    // MME only: a device that cannot carry a loopback forces "None"; a stereo one restores the remembered channel.
    private void SettleWaveLoopback()
    {
        if (IsWasapi)
        {
            return;
        }

        bool loopbackSelected = WaveLoopback.SelectedItem is InputChannelOption { Offset: not null };
        bool supportsLoopback = RecordingDeviceSupportsWaveLoopback;
        if (!supportsLoopback && WaveLoopback.Items.Count > 0)
        {
            SetWaveLoopbackSelection(0);
        }
        else if (supportsLoopback &&
            !loopbackSelected &&
            preferredWaveLoopbackChannelOffset is int rememberedOffset)
        {
            int rememberedIndex = FindInputChannelOptionIndex(WaveLoopback, rememberedOffset);
            if (rememberedIndex >= 0)
            {
                SetWaveLoopbackSelection(rememberedIndex);
            }
        }
    }

    private void SetWaveLoopbackSelection(int index)
    {
        if (WaveLoopback.SelectedIndex == index)
        {
            return;
        }

        bool wasUpdating = updatingWaveLoopbackSelection;
        updatingWaveLoopbackSelection = true;
        try
        {
            WaveLoopback.SelectedIndex = index;
        }
        finally
        {
            updatingWaveLoopbackSelection = wasUpdating;
        }
    }

    // Opens the driver once and keeps what it said, including the rates SupportedSampleRates serves.
    private void RefreshAsioDriverInfo(
        int sampleRate,
        int preferredInputOffset,
        int preferredOutputOffset,
        int? preferredLoopbackOffset)
    {
        AsioDriverInfo = devices.GetAsioDriverInfo(SelectedAsioDriverName, sampleRate);
        // Settled here: not every caller rebuilds the rate list, and the flag must describe this probe.
        SampleRateProbeFailed = IsAsioSampleRateProbeFailure();

        AsioInput.Clear();
        AsioLoopback.Clear();
        AsioOutput.Clear();
        AsioInput.AddRange(AsioDriverInfo.InputChannels);
        AsioLoopback.Add(new InputChannelOption(null, "None"));
        AsioLoopback.AddRange(AsioDriverInfo.InputChannels
            .Select(channel => new InputChannelOption(channel.Offset, channel.ToString())));
        AsioOutput.AddRange(AsioDriverInfo.OutputChannels);
        // A named but unopenable driver must not collapse the saved routing to channel 1 / None on the next apply.
        bool preserveOffsets = !string.IsNullOrWhiteSpace(AsioDriverInfo.DriverName);
        AsioInput.SelectedIndex = SelectAsioChannelIndex(
            AsioInput,
            AsioDriverInfo.InputChannels,
            preferredInputOffset,
            preserveOffsets);
        int loopbackIndex = FindInputChannelOptionIndex(AsioLoopback, preferredLoopbackOffset);
        if (loopbackIndex < 0 && preserveOffsets && preferredLoopbackOffset is int missingLoopbackOffset)
        {
            AsioLoopback.Add(new InputChannelOption(
                missingLoopbackOffset,
                $"{missingLoopbackOffset + 1}: (missing)"));
            loopbackIndex = AsioLoopback.Items.Count - 1;
        }
        AsioLoopback.SelectedIndex = Math.Max(0, loopbackIndex);
        AsioOutput.SelectedIndex = SelectAsioChannelIndex(
            AsioOutput,
            AsioDriverInfo.OutputChannels,
            preferredOutputOffset,
            preserveOffsets);
    }

    private void RefreshSampleRateOptions(int preferredSampleRate)
    {
        SampleRateResolution resolution = SampleRateOptions.Resolve(
            SupportedSampleRates(),
            preferredSampleRate,
            SampleRate.Items.Count > 0,
            IsAsioSampleRateProbeFailure());
        SampleRateProbeFailed = resolution.ProbeFailed;
        SampleRateFellBackFrom = resolution.FellBackFrom;
        if (resolution.Rates is null)
        {
            // No answer: keep the list and selection; rebuilding would replace a working rate with the fallback.
            return;
        }

        // Empty list is a real outcome: no rate works, Apply refuses. Do not fill in the configured rate.
        int[] availableRates = resolution.Rates;
        bool wasInitializing = initializing;
        initializing = true;
        try
        {
            SampleRate.Clear();
            SampleRate.AddRange(availableRates.Select(rate => (object)rate));
            // -1 on an empty list, where index 0 would throw mid-rebuild.
            SampleRate.SelectedIndex = SampleRateOptions.FindRateIndex(availableRates, resolution.Selected);
        }
        finally
        {
            initializing = wasInitializing;
        }
    }

    private bool IsAsioSampleRateProbeFailure() =>
        SampleRateOptions.IsProbeFailure(
            IsAsio,
            AsioDriverInfo.DriverName,
            AsioDriverInfo.SupportedSampleRates.Count);

    private static int FindInputChannelOptionIndex(RecordChoice choice, int? offset)
    {
        for (int i = 0; i < choice.Items.Count; i++)
        {
            if (choice.Items[i] is InputChannelOption option && option.Offset == offset)
            {
                return i;
            }
        }

        return -1;
    }

    // A missing persisted device stays as "(missing)" so Apply cannot silently re-target another device.
    private static void SelectDeviceOrShowMissing(
        RecordChoice choice,
        IReadOnlyList<AudioDeviceInfo> devices,
        int deviceNumber)
    {
        int index = AudioDeviceCatalog.FindDeviceIndex(devices, deviceNumber);
        if (index >= 0)
        {
            choice.SelectedIndex = index;
            return;
        }

        choice.Add(AudioDeviceCatalog.CreateMissingDevice(deviceNumber));
        choice.SelectedIndex = choice.Items.Count - 1;
    }

    // An offset the driver does not report now (fewer channels, busy driver) must survive the round-trip.
    private static int SelectAsioChannelIndex(
        RecordChoice choice,
        IReadOnlyList<AsioChannelInfo> channels,
        int preferredOffset,
        bool preserveMissingOffset)
    {
        int index = AsioDeviceCatalog.FindChannelIndex(channels, preferredOffset);
        if (index >= 0 || !preserveMissingOffset)
        {
            return index;
        }

        choice.Add(new AsioChannelInfo(preferredOffset, "(missing)"));
        return choice.Items.Count - 1;
    }

    private readonly record struct ExclusiveFormat(
        string CaptureId,
        string RenderId,
        int SampleRate,
        int Bits,
        int CaptureChannels,
        int RenderChannels);
}
