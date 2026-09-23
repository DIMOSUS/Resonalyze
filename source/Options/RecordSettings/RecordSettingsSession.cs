using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal sealed record RecordCalibrationSelection(
    string? MicrophoneCalibration0DegreesPath,
    IReadOnlyList<MicrophoneCalibrationDefinition> AdditionalMicrophoneCalibrations,
    SplCalibration? SplCalibration);

/// <summary>
/// Record Settings without its window: the audio route, the sweep, the protective high-pass and the calibrations, each
/// held as its field shows it, and the rules that move one field when another does. <see cref="MeasurementOptions"/>
/// writes the fields and presents what the session and its readers answer.
/// </summary>
internal sealed partial class RecordSettingsSession
{
    public static readonly NumericFieldRange BandRange = new(20, 20_000, 0);
    public static readonly NumericFieldRange OctavePaceRange = new(5, 20_000, 0);
    public static readonly NumericFieldRange HighPassFrequencyRange = new(10, 20_000, 0);
    public static readonly NumericFieldRange AverageRunCountRange = new(1, 64, 0);
    public static readonly NumericFieldRange BitsRange = new(8, 32, 0);

    private readonly IRecordDevices devices;
    private bool initializing;
    private bool updatingSweepBand;
    private string? microphoneCalibration0DegreesPath;
    private List<MicrophoneCalibrationDefinition> additionalMicrophoneCalibrations = [];
    // The rig's choice, frozen into the file by the run.
    private string? microphoneCalibrationId = MicrophoneCalibrationIds.ZeroDegrees;
    private List<ArrayMicrophoneDefinition> waveArrayMicrophones = [];
    private List<ArrayMicrophoneDefinition> asioArrayMicrophones = [];
    // Null until the array is next edited; see MeasurementSettingsFile.ArrayMatchesDevice.
    private string? waveArrayDeviceId;
    private string? asioArrayDeviceId;

    public RecordSettingsSession(IRecordDevices devices)
    {
        this.devices = devices ?? throw new ArgumentNullException(nameof(devices));
        WireDeviceRules();
        WireSweepRules();
    }

    /// <summary>Any setting but the audio-backend group (backend, rate, bit depth, devices) changed; it applies as edited.</summary>
    public event Action? SweepSettingsChanged;

    /// <summary>A calibration changed; the host persists it at once rather than on Apply.</summary>
    public event Action<RecordCalibrationSelection>? CalibrationChanged;

    public RecordChoice PlaybackChannel { get; } = new();
    public RecordChoice HighPassKind { get; } = new();
    public RecordChoice HighPassSlope { get; } = new();
    public RecordChoice MicrophoneCalibration { get; } = new();
    public RecordNumber Bits { get; } = new(BitsRange, 8);
    public RecordNumber LowFrequency { get; } = new(BandRange, 20);
    public RecordNumber HighFrequency { get; } = new(BandRange, 20_000);
    public RecordNumber OctavePaceMilliseconds { get; } = new(OctavePaceRange, 200);
    public RecordNumber HighPassFrequency { get; } = new(HighPassFrequencyRange, 2_000);
    public RecordNumber AverageRunCount { get; } = new(AverageRunCountRange, 2);

    public string? MicrophoneCalibration0DegreesPath => NormalizeCalibrationPath(microphoneCalibration0DegreesPath);
    public IReadOnlyList<MicrophoneCalibrationDefinition> AdditionalMicrophoneCalibrations => additionalMicrophoneCalibrations;
    public string? MicrophoneCalibrationId => microphoneCalibrationId;
    public SplCalibration? SplCalibration { get; private set; }

    /// <summary>Why the 0° file cannot be read, as of its last selection; a deleted or unparsable file would otherwise
    /// leave measurements silently uncalibrated.</summary>
    public string? ZeroDegreeCalibrationProblem { get; private set; }

    public PlaybackChannel SelectedPlaybackChannel =>
        PlaybackChannel.SelectedIndex >= 0
            ? (PlaybackChannel)PlaybackChannel.SelectedIndex
            : Audio.PlaybackChannel.Mono;

    public void Load(MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        initializing = true;
        Bits.Value = settings.Bits is 16 or 24 ? settings.Bits : 24;
        preferredWasapiCaptureEndpointId = settings.WasapiCaptureEndpointId;
        preferredWasapiRenderEndpointId = settings.WasapiRenderEndpointId;
        preferredWasapiCaptureEndpointName = settings.WasapiCaptureEndpointName;
        preferredWasapiRenderEndpointName = settings.WasapiRenderEndpointName;
        preferredWavePlaybackDeviceNumber = settings.OutputDeviceNumber;
        preferredWaveRecordingDeviceNumber = settings.InputDeviceNumber;

        PlaybackChannel.Clear();
        foreach (PlaybackChannel channel in Enum.GetValues<PlaybackChannel>())
        {
            PlaybackChannel.Add(channel.ToString());
        }
        PlaybackChannel.SelectedIndex = Enum.IsDefined(settings.PlaybackChannel)
            ? (int)settings.PlaybackChannel
            : (int)Audio.PlaybackChannel.Mono;

        LoadDevices(settings);

        // Clamped and rounded: the file is not normalized to control ranges, and (int) truncation loses a millisecond.
        (double lowFrequencyHz, double highFrequencyHz) = settings.ResolveBand(settings.SampleRate);
        LowFrequency.Value = BandRange.Clamp(Math.Round(lowFrequencyHz));
        HighFrequency.Value = BandRange.Clamp(
            Math.Max((double)LowFrequency.Value + 1.0, Math.Round(highFrequencyHz)));
        double perOctaveMs = ExponentialSineSweep.OctavePaceForTotalDuration(
            lowFrequencyHz,
            highFrequencyHz,
            settings.RequestedDurationSeconds,
            settings.SampleRate) * 1000.0;
        OctavePaceMilliseconds.Value = OctavePaceRange.Clamp(perOctaveMs > 0 ? Math.Round(perOctaveMs) : 100.0);
        ProtectiveHighPassConfiguration protectiveHighPass =
            ProtectiveHighPassConfiguration.Normalize(
                new ProtectiveHighPassConfiguration(
                    settings.ProtectiveHighPassKind,
                    settings.ProtectiveHighPassFrequencyHz,
                    settings.ProtectiveHighPassSlopeDbPerOctave));
        HighPassKind.Select(protectiveHighPass.Kind);
        HighPassFrequency.Value = HighPassFrequencyRange.Clamp(protectiveHighPass.FrequencyHz);
        PopulateHighPassSlopes(protectiveHighPass.SlopeDbPerOctave);
        AverageRunCount.Value = Math.Clamp(settings.AverageRunCount, 1, 64);
        microphoneCalibration0DegreesPath = settings.MicrophoneCalibration0DegreesPath;
        additionalMicrophoneCalibrations = settings.AdditionalMicrophoneCalibrations
            .Select(definition => definition.Clone())
            .ToList();
        microphoneCalibrationId = settings.MicrophoneCalibrationId;
        RefreshMicrophoneCalibrationChoice();
        waveArrayMicrophones = settings.WaveArrayMicrophones
            .Select(definition => definition.Clone())
            .ToList();
        asioArrayMicrophones = settings.AsioArrayMicrophones
            .Select(definition => definition.Clone())
            .ToList();
        waveArrayDeviceId = settings.WaveArrayDeviceId;
        asioArrayDeviceId = settings.AsioArrayDeviceId;
        SplCalibration = settings.SplCalibration;
        RefreshCalibrationFiles();
        // With ASIO the driver probe supplies the rate list, so it must run before the rate control is filled.
        RefreshAsioDriverInfo(
            settings.SampleRate,
            settings.AsioInputChannelOffset,
            settings.AsioOutputChannelOffset,
            settings.AsioLoopbackInputChannelOffset);
        RefreshSampleRateOptions(settings.SampleRate);
        initializing = false;
        SettleWaveLoopback();
    }

    /// <summary>Writes the half of the settings that applies as edited; the backend group waits for Apply.</summary>
    public void WriteLiveSettings(MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        settings.LowFrequencyHz = (double)LowFrequency.Value;
        settings.HighFrequencyHz = (double)HighFrequency.Value;
        // Paced against the applied rate: an uncommitted rate must not leak into the sweep.
        settings.RequestedDurationSeconds = RequestedDurationSeconds(settings.SampleRate);
        settings.PlaybackChannel = SelectedPlaybackChannel;
        settings.AverageRunCount = (int)AverageRunCount.Value;
        ProtectiveHighPassConfiguration protectiveHighPass = RecordHighPass.Read(this);
        settings.ProtectiveHighPassKind = protectiveHighPass.Kind;
        settings.ProtectiveHighPassFrequencyHz = protectiveHighPass.FrequencyHz;
        settings.ProtectiveHighPassSlopeDbPerOctave = protectiveHighPass.SlopeDbPerOctave;
        // Array is capture routing, so it reaches the settings on the same apply that reopens the device.
        WriteArrays(settings);
        settings.MicrophoneCalibrationId = microphoneCalibrationId;
    }

    /// <summary>The calibrations and arrays Apply writes before it checks the route, so a refused Apply keeps them.</summary>
    public void WriteCalibrations(MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        settings.MicrophoneCalibration0DegreesPath = MicrophoneCalibration0DegreesPath;
        settings.MicrophoneCalibrationId = microphoneCalibrationId;
        settings.AdditionalMicrophoneCalibrations = additionalMicrophoneCalibrations
            .Select(definition => definition.Clone())
            .ToList();
        WriteArrays(settings);
        settings.SplCalibration = SplCalibration;
    }

    private void WriteArrays(MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        settings.WaveArrayMicrophones = waveArrayMicrophones
            .Select(definition => definition.Clone())
            .ToList();
        settings.AsioArrayMicrophones = asioArrayMicrophones
            .Select(definition => definition.Clone())
            .ToList();
        settings.WaveArrayDeviceId = waveArrayDeviceId;
        settings.AsioArrayDeviceId = asioArrayDeviceId;
    }

    /// <summary>The whole sweep in seconds at <paramref name="sampleRate"/>, from the per-octave pace the field holds.</summary>
    public double RequestedDurationSeconds(int sampleRate)
    {
        double perOctaveSeconds = (double)OctavePaceMilliseconds.Value * 0.001;
        double requestedDuration = ExponentialSineSweep.TotalDurationForOctavePace(
            (double)LowFrequency.Value,
            (double)HighFrequency.Value,
            perOctaveSeconds,
            sampleRate);
        return requestedDuration > 0 ? requestedDuration : perOctaveSeconds;
    }

    /// <summary>A path chosen for the 0° calibration, or null to clear it; announced even when unchanged.</summary>
    public void SetZeroDegreeCalibration(string? path)
    {
        microphoneCalibration0DegreesPath = path;
        RefreshCalibrationFiles();
        RaiseCalibrationChanged();
    }

    public void SetAdditionalCalibrations(IEnumerable<MicrophoneCalibrationDefinition> definitions)
    {
        additionalMicrophoneCalibrations = definitions.ToList();
        RefreshCalibrationFiles();
        RaiseCalibrationChanged();
    }

    /// <summary>Adopts a list the shell changed behind the panel, so the next Apply does not write back the stale one.</summary>
    public void AdoptAdditionalCalibrations(IReadOnlyList<MicrophoneCalibrationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        additionalMicrophoneCalibrations = definitions
            .Select(definition => definition.Clone())
            .ToList();
        RefreshCalibrationFiles();
    }

    /// <summary>A completed physical calibration, or null to clear it; persisted now, not on an Apply that may never come.</summary>
    public void SetSplCalibration(SplCalibration? calibration)
    {
        SplCalibration = calibration;
        RaiseCalibrationChanged();
    }

    /// <summary>From the working copy, so a just-added calibration is assignable before Apply.</summary>
    public IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntries()
    {
        string? zeroDegreePath = MicrophoneCalibration0DegreesPath;
        var entries = new List<MicrophoneCalibrationEntry>(additionalMicrophoneCalibrations.Count + 1)
        {
            new(MicrophoneCalibrationIds.ZeroDegrees, "0°", !string.IsNullOrWhiteSpace(zeroDegreePath))
        };
        foreach (MicrophoneCalibrationDefinition definition in additionalMicrophoneCalibrations)
        {
            entries.Add(new MicrophoneCalibrationEntry(definition.Id, definition.Name, true));
        }

        return entries;
    }

    private void RefreshCalibrationFiles()
    {
        string? path = MicrophoneCalibration0DegreesPath;
        ZeroDegreeCalibrationProblem = path == null ? null : new CalibrationFile(path).LoadError;
        RefreshMicrophoneCalibrationChoice();
    }

    private void RefreshMicrophoneCalibrationChoice()
    {
        bool wasInitializing = initializing;
        initializing = true;
        IReadOnlyList<MicrophoneCalibrationOption> options =
            MicrophoneCalibrationChoices.BuildOptions(microphoneCalibrationId, CalibrationEntries());
        MicrophoneCalibration.Clear();
        MicrophoneCalibration.AddRange(options);
        MicrophoneCalibration.SelectedIndex = MicrophoneCalibrationChoices.FindIndex(options, microphoneCalibrationId);
        microphoneCalibrationId = SelectedMicrophoneCalibrationId();
        initializing = wasInitializing;
    }

    private string? SelectedMicrophoneCalibrationId() =>
        MicrophoneCalibration.SelectedItem is MicrophoneCalibrationOption option
            ? option.CalibrationId
            : null;

    private void RaiseCalibrationChanged() =>
        CalibrationChanged?.Invoke(new RecordCalibrationSelection(
            MicrophoneCalibration0DegreesPath,
            additionalMicrophoneCalibrations
                .Select(definition => definition.Clone())
                .ToList(),
            SplCalibration));

    private void RaiseSweepSettingsChanged()
    {
        if (initializing)
        {
            return;
        }

        SweepSettingsChanged?.Invoke();
    }

    private void WireSweepRules()
    {
        HighPassKind.AddRange(
        [
            ProtectiveHighPassKind.Off,
            ProtectiveHighPassKind.Butterworth,
            ProtectiveHighPassKind.LinkwitzRiley
        ]);
        HighPassKind.Changed += () =>
        {
            PopulateHighPassSlopes();
            RaiseSweepSettingsChanged();
        };
        HighPassFrequency.Changed += RaiseSweepSettingsChanged;
        HighPassSlope.Changed += RaiseSweepSettingsChanged;
        HighPassKind.Select(ProtectiveHighPassKind.Off);
        PopulateHighPassSlopes(preferredSlope: 24);

        PlaybackChannel.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            RefreshSampleRateOptions(SelectedSampleRate);
            RaiseSweepSettingsChanged();
        };
        LowFrequency.Changed += () => OnBandChanged(highMoved: false);
        HighFrequency.Changed += () => OnBandChanged(highMoved: true);
        OctavePaceMilliseconds.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            RaiseSweepSettingsChanged();
        };
        AverageRunCount.Changed += RaiseSweepSettingsChanged;
        MicrophoneCalibration.Changed += () =>
        {
            if (initializing)
            {
                return;
            }

            microphoneCalibrationId = SelectedMicrophoneCalibrationId();
            RaiseSweepSettingsChanged();
        };
    }

    private void PopulateHighPassSlopes(int? preferredSlope = null)
    {
        int? previousSlope = preferredSlope ?? (HighPassSlope.SelectedItem is int slope ? slope : null);
        HighPassSlope.Clear();
        foreach (int supportedSlope in ProtectiveHighPassConfiguration.SupportedSlopes(RecordHighPass.SelectedKind(this)))
        {
            HighPassSlope.Add(supportedSlope);
        }

        int index = previousSlope.HasValue ? HighPassSlope.IndexOf(previousSlope.Value) : -1;
        HighPassSlope.SelectedIndex = index >= 0 ? index : HighPassSlope.IndexOf(24);
    }

    // The other edge follows so the band never inverts; the follower's own move is not a second edit.
    private void OnBandChanged(bool highMoved)
    {
        if (initializing || updatingSweepBand)
        {
            return;
        }

        updatingSweepBand = true;
        try
        {
            if (LowFrequency.Value >= HighFrequency.Value)
            {
                if (highMoved)
                {
                    LowFrequency.Value = Math.Max(BandRange.Minimum, HighFrequency.Value - 1);
                }
                else
                {
                    HighFrequency.Value = Math.Min(BandRange.Maximum, LowFrequency.Value + 1);
                    if (LowFrequency.Value >= HighFrequency.Value)
                    {
                        LowFrequency.Value = HighFrequency.Value - 1;
                    }
                }
            }
        }
        finally
        {
            updatingSweepBand = false;
        }

        RaiseSweepSettingsChanged();
    }

    private static string? NormalizeCalibrationPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path;
}
