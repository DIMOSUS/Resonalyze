using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed class MeasurementSettingsFile
{
    private const int CurrentSchemaVersion = 9;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public SweepMeasurementSettings Measurement { get; set; } = new();
    public FrequencyResponseSettings FrequencyResponse { get; set; } = new();
    public FrequencyResponseSettings PhaseResponse { get; set; } = new();
    public FrequencyResponseSettings GroupDelay { get; set; } = new();
    public ImpulseResponseSettings ImpulseResponse { get; set; } = new();
    public WaterfallSettings Waterfall { get; set; } = new();
    public WaterfallSettings BurstDecay { get; set; } = new();
    public LiveSpectrumSettings LiveSpectrum { get; set; } = new();
    public TimeAlignmentSettings TimeAlignment { get; set; } = new();
    public EqWizardSettings EqWizard { get; set; } = new();
    public string? LastImpulseResponseDirectory { get; set; }

    // True when loading reset a loopback configuration that pointed at the
    // removed separate-loopback-device capability; the shell shows a one-time
    // notice telling the user to pick a loopback channel again.
    [JsonIgnore]
    public bool LegacyDualDeviceLoopbackReset { get; private set; }

    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    [JsonIgnore]
    private string pathOnDisk = ApplicationDataPaths.Current.SettingsFile;

    [JsonIgnore]
    // A load failure is safe to recover from only after the original file has
    // been moved aside. Keep automatic UI saves from overwriting it meanwhile.
    private bool preserveExistingFileBeforeSave;

    public static MeasurementSettingsFile LoadOrDefault(string? pathOnDisk = null)
    {
        string path = pathOnDisk ?? ApplicationDataPaths.Current.SettingsFile;
        try
        {
            if (!File.Exists(path))
            {
                return new MeasurementSettingsFile { pathOnDisk = path };
            }

            using FileStream stream = File.OpenRead(path);
            MeasurementSettingsFile? settings =
                JsonSerializer.Deserialize<MeasurementSettingsFile>(
                    stream,
                    SerializerOptions);
            if (settings == null || settings.SchemaVersion is < 7 or > CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    settings == null
                        ? "The settings file is empty."
                        : $"Settings schema version {settings.SchemaVersion} is not supported.");
            }

            if (settings.SchemaVersion == 7)
            {
                settings.PhaseResponse.PhaseWindowMode =
                    Resonalyze.Dsp.PhaseWindowMode.Fixed;
                settings.PhaseResponse.PhaseDetrendMode =
                    Resonalyze.Dsp.PhaseDetrendMode.Manual;
                settings.PhaseResponse.PhaseFdwCycles =
                    PhaseAnalysisSettings.DefaultFdwCycles;
            }

            // Version 9 added the SPL calibration; 7 and 8 files simply carry none.
            // A structurally broken anchor drops to null rather than failing the
            // whole settings load — the measurement configuration is the value here.
            try
            {
                settings.Measurement.SplCalibration?.Validate();
            }
            catch (InvalidDataException)
            {
                settings.Measurement.SplCalibration = null;
            }

            settings.SchemaVersion = CurrentSchemaVersion;
            settings.MigrateLegacyDualDeviceLoopback();
            settings.pathOnDisk = path;
            return settings;
        }
        catch (Exception exception)
        {
            BackupResult backup = BackupUnusableFile(path);
            string? backupPath = backup.Path;
            string preservation = backupPath == null
                ? "The unusable file could not be backed up. Changes will not be saved " +
                    "until the original file can be preserved; check file permissions."
                : $"The unusable file was preserved as '{backupPath}'.";
            return new MeasurementSettingsFile
            {
                pathOnDisk = path,
                LoadWarning = $"Settings could not be loaded: {exception.Message}\r\n\r\n{preservation}",
                preserveExistingFileBeforeSave = backup.Status == BackupStatus.Failed
            };
        }
    }

    private static BackupResult BackupUnusableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new BackupResult(BackupStatus.NotFound, null);
            }

            string backupPath = GetAvailableBackupPath(path);
            File.Move(path, backupPath);
            return new BackupResult(BackupStatus.Preserved, backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new BackupResult(BackupStatus.Failed, null);
        }
    }

    private static string GetAvailableBackupPath(string path)
    {
        string backupPath = path + ".backup";
        for (int suffix = 1; File.Exists(backupPath); suffix++)
        {
            backupPath = path + $".backup.{suffix}";
        }

        return backupPath;
    }

    // The separate-loopback-device capability was removed: microphone and
    // loopback are always channels of ONE input device now. A file written by
    // an older version with the loopback on a DIFFERENT device carries channel
    // offsets that are meaningless on the shared device (the channels may
    // legitimately be equal, and the microphone device may be mono), so the
    // loopback selection is reset to "unset" — the existing loopback-required
    // flow then walks the user through picking a channel — instead of being
    // silently misread as a shared-device configuration.
    internal void MigrateLegacyDualDeviceLoopback()
    {
        if (Measurement.WaveLoopbackDeviceNumber is int legacyDevice &&
            legacyDevice != Measurement.InputDeviceNumber)
        {
            Measurement.WaveLoopbackInputChannelOffset = null;
            LegacyDualDeviceLoopbackReset = true;
        }

        Measurement.WaveLoopbackDeviceNumber = null;
    }

    public void Save()
    {
        if (preserveExistingFileBeforeSave)
        {
            BackupResult backup = BackupUnusableFile(pathOnDisk);
            if (backup.Status == BackupStatus.Failed)
            {
                return;
            }

            preserveExistingFileBeforeSave = false;
        }

        SchemaVersion = CurrentSchemaVersion;
        // Temp file + move keeps the settings intact if the write is interrupted.
        string directory = Path.GetDirectoryName(pathOnDisk)
            ?? throw new InvalidOperationException("Settings directory cannot be resolved.");
        Directory.CreateDirectory(directory);
        string tempPath = pathOnDisk + ".tmp";
        using (FileStream stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, this, SerializerOptions);
        }

        File.Move(tempPath, pathOnDisk, overwrite: true);
    }

    private enum BackupStatus
    {
        NotFound,
        Preserved,
        Failed
    }

    private readonly record struct BackupResult(BackupStatus Status, string? Path);

    public void ApplyTo(
        ExpSweepMeasurement measurement,
        FrequencyResponseOptions frequencyResponse,
        CurveVisibilityOptions frequencyResponseVisibility,
        FrequencyResponseOptions phaseResponse,
        CurveVisibilityOptions phaseResponseVisibility,
        FrequencyResponseOptions groupDelay,
        CurveVisibilityOptions groupDelayVisibility,
        ImpulseResponseOptions impulseResponse,
        WaterfallGenerateOptions waterfall,
        WaterfallGenerateOptions burstDecay,
        LiveSpectrumOptions liveSpectrum,
        TimeAlignmentOptions timeAlignment)
    {
        Measurement.ApplyTo(measurement);
        FrequencyResponse.ApplyTo(frequencyResponse, frequencyResponseVisibility);
        PhaseResponse.ApplyTo(phaseResponse, phaseResponseVisibility);
        GroupDelay.ApplyTo(groupDelay, groupDelayVisibility);
        ImpulseResponse.ApplyTo(impulseResponse);
        Waterfall.ApplyTo(waterfall, WaterfallMode.Fourier);
        BurstDecay.ApplyTo(burstDecay, WaterfallMode.BurstDecay);
        LiveSpectrum.ApplyTo(liveSpectrum);
        TimeAlignment.ApplyTo(timeAlignment, measurement.SampleRate);
    }

    public void CaptureFrom(
        ExpSweepMeasurement measurement,
        FrequencyResponseOptions frequencyResponse,
        CurveVisibilityOptions frequencyResponseVisibility,
        FrequencyResponseOptions phaseResponse,
        CurveVisibilityOptions phaseResponseVisibility,
        FrequencyResponseOptions groupDelay,
        CurveVisibilityOptions groupDelayVisibility,
        ImpulseResponseOptions impulseResponse,
        WaterfallGenerateOptions waterfall,
        WaterfallGenerateOptions burstDecay,
        LiveSpectrumOptions liveSpectrum,
        TimeAlignmentOptions timeAlignment)
    {
        SchemaVersion = CurrentSchemaVersion;
        Measurement = SweepMeasurementSettings.Capture(measurement);
        FrequencyResponse = FrequencyResponseSettings.Capture(frequencyResponse, frequencyResponseVisibility);
        PhaseResponse = FrequencyResponseSettings.Capture(phaseResponse, phaseResponseVisibility);
        GroupDelay = FrequencyResponseSettings.Capture(groupDelay, groupDelayVisibility);
        ImpulseResponse = ImpulseResponseSettings.Capture(impulseResponse);
        Waterfall = WaterfallSettings.Capture(waterfall);
        BurstDecay = WaterfallSettings.Capture(burstDecay);
        LiveSpectrum = LiveSpectrumSettings.Capture(liveSpectrum);
        TimeAlignment = TimeAlignmentSettings.Capture(timeAlignment);
    }

    internal sealed class SweepMeasurementSettings
    {
        public int Octaves { get; set; } = 12;
        public int SampleRate { get; set; } = 44100;
        public int Bits { get; set; } = 24;
        public double RequestedDurationSeconds { get; set; } = 1.0;
        public PlaybackChannel PlaybackChannel { get; set; } = PlaybackChannel.Mono;
        public AudioBackend AudioBackend { get; set; } = AudioBackend.Wave;
        public int OutputDeviceNumber { get; set; } = -1;
        public int InputDeviceNumber { get; set; } = -1;
        public string? WasapiCaptureEndpointId { get; set; }
        public string? WasapiRenderEndpointId { get; set; }
        public string? WasapiCaptureEndpointName { get; set; }
        public string? WasapiRenderEndpointName { get; set; }
        public int WasapiBufferMilliseconds { get; set; } = 100;
        public string? AsioDriverName { get; set; }
        public int WaveInputChannelOffset { get; set; }
        public int? WaveLoopbackInputChannelOffset { get; set; }
        // Legacy field (pre removal of the separate-loopback-device
        // capability), kept ONLY so old files deserialize into the migration
        // (see MigrateLegacyDualDeviceLoopback); always null after loading
        // and never written back.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WaveLoopbackDeviceNumber { get; set; }
        public int AsioInputChannelOffset { get; set; }
        public int? AsioLoopbackInputChannelOffset { get; set; }
        public int AsioOutputChannelOffset { get; set; }
        public int AverageRunCount { get; set; } = 1;
        public bool ConfirmEachAverageRun { get; set; }
        public string? MicrophoneCalibration0DegreesPath { get; set; }
        public string? MicrophoneCalibration90DegreesPath { get; set; }
        public SplCalibration? SplCalibration { get; set; }

        // A loopback reference channel is mandatory: every analysis mode is derived from the
        // transfer IR, which only exists when the loopback is captured alongside the microphone.
        public bool HasLoopbackConfigured =>
            AudioBackend == AudioBackend.Asio
                ? AsioLoopbackInputChannelOffset.HasValue
                : WaveLoopbackInputChannelOffset.HasValue;

        public static SweepMeasurementSettings Capture(
            ExpSweepMeasurement measurement) =>
            new()
            {
                Octaves = measurement.Octaves,
                SampleRate = measurement.SampleRate,
                Bits = measurement.Bits,
                RequestedDurationSeconds = measurement.Sweep?.RequestedDuration ?? 1.0,
                PlaybackChannel = measurement.PlaybackChannel,
                AudioBackend = measurement.AudioBackend,
                OutputDeviceNumber = measurement.OutputDeviceNumber,
                InputDeviceNumber = measurement.InputDeviceNumber,
                WasapiCaptureEndpointId = measurement.WasapiCaptureEndpointId,
                WasapiRenderEndpointId = measurement.WasapiRenderEndpointId,
                WasapiCaptureEndpointName = measurement.WasapiCaptureEndpointName,
                WasapiRenderEndpointName = measurement.WasapiRenderEndpointName,
                WasapiBufferMilliseconds = measurement.WasapiBufferMilliseconds,
                AsioDriverName = measurement.AsioDriverName,
                WaveInputChannelOffset = measurement.WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset = measurement.WaveLoopbackInputChannelOffset,
                AsioInputChannelOffset = measurement.AsioInputChannelOffset,
                AsioLoopbackInputChannelOffset = measurement.AsioLoopbackInputChannelOffset,
                AsioOutputChannelOffset = measurement.AsioOutputChannelOffset,
                AverageRunCount = measurement.AverageRunCount,
                ConfirmEachAverageRun = measurement.ConfirmEachAverageRun,
                SplCalibration = measurement.SplCalibration
            };

        public void ApplyTo(ExpSweepMeasurement measurement)
        {
            AudioBackend backend = NormalizeAudioBackend(AudioBackend, AsioDriverName);
            string? captureEndpointId = NormalizeWasapiEndpointId(
                WasapiCaptureEndpointId,
                capture: true);
            string? renderEndpointId = NormalizeWasapiEndpointId(
                WasapiRenderEndpointId,
                capture: false);
            int sampleRate = NormalizeWasapiSampleRate(
                backend,
                captureEndpointId,
                renderEndpointId,
                Clamp(SampleRate, 44_100, 384_000));
            measurement.Init(new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(
                    Clamp(Octaves, 1, 32),
                    sampleRate,
                    Bits is 16 or 24 ? Bits : 24,
                    Math.Clamp(RequestedDurationSeconds, 0.001, 100.0),
                    Enum.IsDefined(PlaybackChannel)
                        ? PlaybackChannel
                        : PlaybackChannel.Mono),
                new SweepAudioConfiguration(
                    Backend: backend,
                    OutputDeviceNumber: NormalizeDeviceNumber(
                        AudioDeviceCatalog.GetPlaybackDevices(),
                        OutputDeviceNumber),
                    InputDeviceNumber: NormalizeDeviceNumber(
                        AudioDeviceCatalog.GetRecordingDevices(),
                        InputDeviceNumber),
                    WaveInputChannelOffset: IsWasapiBackend(backend)
                        ? Math.Max(0, WaveInputChannelOffset)
                        : NormalizeWaveChannelOffset(WaveInputChannelOffset),
                    WaveLoopbackInputChannelOffset: IsWasapiBackend(backend)
                        ? NormalizeOptionalWasapiChannelOffset(WaveLoopbackInputChannelOffset)
                        : NormalizeOptionalWaveChannelOffset(WaveLoopbackInputChannelOffset),
                    AsioDriverName: NormalizeAsioDriverName(AsioDriverName),
                    AsioInputChannelOffset: NormalizeAsioChannelOffset(
                        AsioDriverName,
                        sampleRate,
                        AsioInputChannelOffset,
                        input: true),
                    AsioLoopbackInputChannelOffset: NormalizeOptionalAsioChannelOffset(
                        AsioDriverName,
                        sampleRate,
                        AsioLoopbackInputChannelOffset),
                    AsioOutputChannelOffset: NormalizeAsioChannelOffset(
                        AsioDriverName,
                        sampleRate,
                        AsioOutputChannelOffset,
                        input: false),
                    WasapiCaptureEndpointId: captureEndpointId,
                    WasapiRenderEndpointId: renderEndpointId,
                    WasapiCaptureEndpointName: WasapiCaptureEndpointName,
                    WasapiRenderEndpointName: WasapiRenderEndpointName,
                    WasapiBufferMilliseconds: Clamp(WasapiBufferMilliseconds, 10, 100)),
                new SweepAveragingConfiguration(
                    Clamp(AverageRunCount, 1, 64),
                    ConfirmEachAverageRun)));
            // Metadata, applied after Init (which does not touch it).
            measurement.SplCalibration = SplCalibration;
        }
    }

    internal sealed class FrequencyResponseSettings
    {
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 256;
        public int RightTukeyWindow { get; set; } = 256;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        public bool UseCalibration { get; set; } = true;
        public MicrophoneCalibrationMode? CalibrationMode { get; set; }
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;
        public bool ShowCoherence { get; set; } = true;
        public bool ShowMeasuredPhase { get; set; } = true;
        public bool ShowMinimumPhase { get; set; } = true;
        public bool ShowExcessPhase { get; set; } = true;
        public bool ShowPrimary { get; set; } = true;
        public bool ShowHd2 { get; set; } = true;
        public bool ShowHd3 { get; set; } = true;
        public bool ShowHd4 { get; set; } = true;
        public bool ShowThdPlusNoise { get; set; } = true;
        public bool ShowNoiseFloor { get; set; } = true;
        public bool ShowGroupDelay { get; set; } = true;
        // Nullable so a pre-Auto file (v <= 9, field absent) is
        // distinguishable from a stored choice — see ApplyTo.
        public bool? PhaseGateAutoFit { get; set; } = true;
        public double PhaseGateOffsetMs { get; set; } = FrequencyResponseOptions.DefaultPhaseGateOffsetMs;
        public double PhaseLeftMs { get; set; } = FrequencyResponseOptions.DefaultPhaseLeftMs;
        public double PhasePlateauMs { get; set; } = FrequencyResponseOptions.DefaultPhasePlateauMs;
        public double PhaseRightMs { get; set; } = FrequencyResponseOptions.DefaultPhaseRightMs;
        public double PhaseDetrendMs { get; set; } = FrequencyResponseOptions.DefaultPhaseDetrendMs;
        public PhaseWindowMode? PhaseWindowMode { get; set; } =
            Resonalyze.Dsp.PhaseWindowMode.FrequencyDependent;
        public int PhaseFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
        public PhaseDetrendMode? PhaseDetrendMode { get; set; } =
            Resonalyze.Dsp.PhaseDetrendMode.Auto;
        public bool? GroupDelayGateAutoFit { get; set; } = true;
        public double GroupDelayGateOffsetMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayGateOffsetMs;
        public double GroupDelayLeftMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayLeftMs;
        public double GroupDelayPlateauMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayPlateauMs;
        public double GroupDelayRightMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayRightMs;

        public static FrequencyResponseSettings Capture(
            FrequencyResponseOptions options,
            CurveVisibilityOptions visibility) =>
            new()
            {
                Window = options.Window,
                LeftTukeyWindow = options.LeftTukeyWindow,
                RightTukeyWindow = options.RightTukeyWindow,
                SmoothingInverseOctaves = options.SmoothingInverseOctaves,
                Offset = options.Offset,
                Unwrap = options.Unwrap,
                UseCalibration = options.UseCalibration,
                CalibrationMode = options.CalibrationMode,
                MagnitudeScale = options.MagnitudeScale,
                ShowCoherence = visibility.ShowCoherence,
                ShowMeasuredPhase = visibility.ShowMeasuredPhase,
                ShowMinimumPhase = visibility.ShowMinimumPhase,
                ShowExcessPhase = visibility.ShowExcessPhase,
                ShowPrimary = visibility.ShowPrimary,
                ShowHd2 = visibility.ShowHd2,
                ShowHd3 = visibility.ShowHd3,
                ShowHd4 = visibility.ShowHd4,
                ShowThdPlusNoise = visibility.ShowThdPlusNoise,
                ShowNoiseFloor = visibility.ShowNoiseFloor,
                ShowGroupDelay = visibility.ShowGroupDelay,
                PhaseGateAutoFit = options.PhaseGateAutoFit,
                PhaseGateOffsetMs = options.PhaseGateOffsetMs,
                PhaseLeftMs = options.PhaseLeftMs,
                PhasePlateauMs = options.PhasePlateauMs,
                PhaseRightMs = options.PhaseRightMs,
                PhaseDetrendMs = options.PhaseDetrendMs,
                PhaseWindowMode = options.PhaseWindowMode,
                PhaseFdwCycles = options.PhaseFdwCycles,
                PhaseDetrendMode = options.PhaseDetrendMode,
                GroupDelayGateAutoFit = options.GroupDelayGateAutoFit,
                GroupDelayGateOffsetMs = options.GroupDelayGateOffsetMs,
                GroupDelayLeftMs = options.GroupDelayLeftMs,
                GroupDelayPlateauMs = options.GroupDelayPlateauMs,
                GroupDelayRightMs = options.GroupDelayRightMs
            };

        public void ApplyTo(FrequencyResponseOptions options, CurveVisibilityOptions visibility)
        {
            // Lower bound matches the UI (numericWindow.Minimum = 4); clamping to a
            // higher floor would corrupt small windows on a settings/history roundtrip.
            int window = Clamp(Window, 4, 32768);
            options.Window = window;
            (options.LeftTukeyWindow, options.RightTukeyWindow) =
                ClampTukeyWindows(LeftTukeyWindow, RightTukeyWindow, window);
            options.SmoothingInverseOctaves =
                SmoothingPresetOptions.Normalize(SmoothingInverseOctaves);
            options.Offset = Clamp(Offset, -32768, 32768);
            options.Unwrap = Unwrap;
            options.CalibrationMode = NormalizeCalibrationMode(CalibrationMode, UseCalibration);
            options.MagnitudeScale = Enum.IsDefined(MagnitudeScale)
                ? MagnitudeScale
                : MagnitudeScale.Relative;
            visibility.ShowCoherence = ShowCoherence;
            visibility.ShowMeasuredPhase = ShowMeasuredPhase;
            visibility.ShowMinimumPhase = ShowMinimumPhase;
            visibility.ShowExcessPhase = ShowExcessPhase;
            visibility.ShowPrimary = ShowPrimary;
            visibility.ShowHd2 = ShowHd2;
            visibility.ShowHd3 = ShowHd3;
            visibility.ShowHd4 = ShowHd4;
            visibility.ShowThdPlusNoise = ShowThdPlusNoise;
            visibility.ShowNoiseFloor = ShowNoiseFloor;
            visibility.ShowGroupDelay = ShowGroupDelay;
            // Absent in a pre-Auto file: enable Auto only when the stored
            // offset is the untouched default. A deliberately fitted/typed
            // gate must stay manual — the Auto re-snap would silently
            // overwrite the user's placement and persist over it.
            options.PhaseGateAutoFit = PhaseGateAutoFit ??
                PhaseGateOffsetMs == FrequencyResponseOptions.DefaultPhaseGateOffsetMs;
            options.PhaseGateOffsetMs = ClampMilliseconds(PhaseGateOffsetMs, 0.0, 2000.0);
            options.PhaseLeftMs = ClampMilliseconds(PhaseLeftMs, 0.0, 1000.0);
            options.PhasePlateauMs = ClampMilliseconds(PhasePlateauMs, 0.0, 1000.0);
            options.PhaseRightMs = ClampMilliseconds(PhaseRightMs, 0.0, 1000.0);
            options.PhaseDetrendMs = ClampMilliseconds(PhaseDetrendMs, -2000.0, 2000.0);
            // Missing fields identify the pre-FDW format: retain its Fixed/manual
            // representation rather than silently changing existing projects.
            options.PhaseWindowMode = PhaseWindowMode is { } windowMode &&
                Enum.IsDefined(windowMode)
                    ? windowMode
                    : Resonalyze.Dsp.PhaseWindowMode.Fixed;
            options.PhaseFdwCycles = PhaseFdwCycles is 4 or 6 or 8
                ? PhaseFdwCycles
                : PhaseAnalysisSettings.DefaultFdwCycles;
            options.PhaseDetrendMode = PhaseDetrendMode is { } detrendMode &&
                Enum.IsDefined(detrendMode)
                    ? detrendMode
                    : Resonalyze.Dsp.PhaseDetrendMode.Manual;
            options.GroupDelayGateAutoFit = GroupDelayGateAutoFit ??
                GroupDelayGateOffsetMs == FrequencyResponseOptions.DefaultGroupDelayGateOffsetMs;
            options.GroupDelayGateOffsetMs = ClampMilliseconds(GroupDelayGateOffsetMs, 0.0, 2000.0);
            options.GroupDelayLeftMs = ClampMilliseconds(GroupDelayLeftMs, 0.0, 1000.0);
            options.GroupDelayPlateauMs = ClampMilliseconds(GroupDelayPlateauMs, 0.0, 1000.0);
            options.GroupDelayRightMs = ClampMilliseconds(GroupDelayRightMs, 0.0, 1000.0);
        }

        private static double ClampMilliseconds(double value, double min, double max) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : 0.0;
    }

    // Self-contained EQ Wizard state (the mode no longer derives anything from
    // overlays or the current measurement): the isolated target curve, the gain
    // range and band count of the fader bank, source smoothing and the microphone
    // calibration applied to the loaded IR. The loaded IR itself is not persisted.
    internal sealed class EqWizardSettings
    {
        public TargetPreset Preset { get; set; } = TargetPreset.Flat;
        public double TiltDbPerOctave { get; set; }
        public double BassShelfGainDb { get; set; }
        public double BassShelfFrequencyHz { get; set; } = 100;
        public double BassShelfWidthOctaves { get; set; } = 1.5;
        public double TrebleShelfGainDb { get; set; }
        public double TrebleShelfFrequencyHz { get; set; } = 5000;
        public double TrebleShelfWidthOctaves { get; set; } = 1.5;
        public double PresenceGainDb { get; set; }
        public double PresenceFrequencyHz { get; set; } = 3000;
        public double PresenceWidthOctaves { get; set; } = 1.0;
        public double ToleranceDb { get; set; } = 3;
        public TargetDeviationMode DeviationMode { get; set; } = TargetDeviationMode.Deviation;
        public int TargetColorArgb { get; set; } = unchecked((int)0xFF37C8A0);
        public double TargetStrokeThickness { get; set; } = 2;
        public OverlayLineStyle TargetLineStyle { get; set; } = OverlayLineStyle.Dash;
        public int TargetSmoothingInverseOctaves { get; set; }
        public double TargetOffsetDb { get; set; }
        public double GainMinDb { get; set; } = -15;
        public double GainMaxDb { get; set; } = 6;
        public int BandCount { get; set; } = 1;
        public int SourceSmoothingInverseOctaves { get; set; }
        public MicrophoneCalibrationMode CalibrationMode { get; set; } =
            MicrophoneCalibrationMode.Off;

        // Auto Tune only cuts, never boosts. The safe default for a car tune; see
        // EqAutoTuner.Options.CutsOnlyMode.
        public bool CutsOnly { get; set; } = true;
    }

    internal sealed class ImpulseResponseSettings
    {
        public int Length { get; set; } = 4096;
        public bool Logarithmic { get; set; }
        public bool ShowImpulse { get; set; } = true;
        public bool ShowAutocorrelation { get; set; } = true;

        public static ImpulseResponseSettings Capture(
            ImpulseResponseOptions options) =>
            new()
            {
                Length = options.Length,
                Logarithmic = options.Logarithmic,
                ShowImpulse = options.ShowImpulse,
                ShowAutocorrelation = options.ShowAutocorrelation
            };

        public void ApplyTo(ImpulseResponseOptions options)
        {
            options.Length = Clamp(Length, 1, 262144);
            options.Logarithmic = Logarithmic;
            options.ShowImpulse = ShowImpulse;
            options.ShowAutocorrelation = ShowAutocorrelation;
        }
    }

    internal sealed class WaterfallSettings
    {
        public int SliceCount { get; set; } = 64;
        public int Step { get; set; } = 4;
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 8;
        public int RightTukeyWindow { get; set; } = 512;
        public int DbRange { get; set; } = -60;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public WaterfallMode WaterfallMode { get; set; } = WaterfallMode.Fourier;
        public double Periods { get; set; } = 30;

        public static WaterfallSettings Capture(
            WaterfallGenerateOptions options) =>
            new()
            {
                SliceCount = options.SliceCount,
                Step = options.Step,
                Window = options.Window,
                LeftTukeyWindow = options.LeftTukeyWindow,
                RightTukeyWindow = options.RightTukeyWindow,
                DbRange = options.DbRange,
                SmoothingInverseOctaves = options.SmoothingInverseOctaves,
                Offset = options.Offset,
                WaterfallMode = options.WaterfallMode,
                Periods = options.Periods
            };

        public void ApplyTo(
            WaterfallGenerateOptions options,
            WaterfallMode requiredMode)
        {
            int window = Clamp(Window, 32, 32768);
            options.SliceCount = Clamp(SliceCount, 1, 1024);
            options.Step = Step == 0 ? 1 : Clamp(Step, -32768, 32768);
            options.Window = window;
            (options.LeftTukeyWindow, options.RightTukeyWindow) =
                ClampTukeyWindows(LeftTukeyWindow, RightTukeyWindow, window);
            options.DbRange = Clamp(DbRange, -140, -10);
            options.SmoothingInverseOctaves =
                SmoothingPresetOptions.Normalize(SmoothingInverseOctaves);
            options.Offset = Clamp(Offset, -32768, 32768);
            options.WaterfallMode = requiredMode;
            options.Periods = Math.Clamp(Periods, 1.0, 60.0);
        }
    }

    internal sealed class LiveSpectrumSettings
    {
        public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;
        public bool UseCalibration { get; set; } = true;
        public MicrophoneCalibrationMode? CalibrationMode { get; set; }
        public int SequenceLength { get; set; } = 2048;
        public int OverlapPercent { get; set; } = 50;
        public int SmoothingInverseOctaves { get; set; } = 6;
        public WindowType WindowType { get; set; } = WindowType.Hann;
        public AveragingSpeed AveragingSpeed { get; set; } = AveragingSpeed.Medium;
        public bool ShowMainCurve { get; set; } = true;
        public bool ShowInputMagnitude { get; set; }
        public bool PeakHold { get; set; }
        public bool ShowCoherence { get; set; } = true;
        public int CoherenceThresholdPercent { get; set; } = 25;
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;

        public static LiveSpectrumSettings Capture(
            LiveSpectrumOptions options) =>
            new()
            {
                NoiseColor = Enum.IsDefined(options.NoiseColor)
                    ? options.NoiseColor
                    : NoiseColor.PinkPeriodic,
                UseCalibration = options.UseCalibration,
                CalibrationMode = options.CalibrationMode,
                SequenceLength = NormalizeSequenceLength(options.SequenceLength),
                OverlapPercent = NormalizeOverlapPercent(options.OverlapPercent),
                SmoothingInverseOctaves =
                    SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves),
                WindowType = Enum.IsDefined(options.WindowType)
                    ? options.WindowType
                    : WindowType.Hann,
                AveragingSpeed = Enum.IsDefined(options.AveragingSpeed)
                    ? options.AveragingSpeed
                    : AveragingSpeed.Medium,
                ShowMainCurve = options.ShowMainCurve,
                ShowInputMagnitude = options.ShowInputMagnitude,
                PeakHold = options.PeakHold,
                ShowCoherence = options.ShowCoherence,
                CoherenceThresholdPercent =
                    NormalizeCoherenceThreshold(options.CoherenceThresholdPercent),
                MagnitudeScale = options.MagnitudeScale
            };

        public void ApplyTo(LiveSpectrumOptions options)
        {
            options.NoiseColor = Enum.IsDefined(NoiseColor)
                ? NoiseColor
                : NoiseColor.PinkPeriodic;
            options.CalibrationMode = NormalizeCalibrationMode(CalibrationMode, UseCalibration);
            options.SequenceLength = NormalizeSequenceLength(SequenceLength);
            options.OverlapPercent = NormalizeOverlapPercent(OverlapPercent);
            options.SmoothingInverseOctaves =
                SmoothingPresetOptions.Normalize(SmoothingInverseOctaves);
            options.WindowType = Enum.IsDefined(WindowType)
                ? WindowType
                : WindowType.Hann;
            options.AveragingSpeed = Enum.IsDefined(AveragingSpeed)
                ? AveragingSpeed
                : AveragingSpeed.Medium;
            options.ShowMainCurve = ShowMainCurve;
            options.ShowInputMagnitude = ShowInputMagnitude;
            options.PeakHold = PeakHold;
            options.ShowCoherence = ShowCoherence;
            options.CoherenceThresholdPercent =
                NormalizeCoherenceThreshold(CoherenceThresholdPercent);
            options.MagnitudeScale = Enum.IsDefined(MagnitudeScale)
                ? MagnitudeScale
                : MagnitudeScale.Relative;
        }

        private static int NormalizeCoherenceThreshold(int thresholdPercent) =>
            Math.Clamp(thresholdPercent, 0, 95);

        private static int NormalizeOverlapPercent(int overlapPercent)
        {
            int[] supported = [0, 50, 75];
            int normalized = supported[0];
            foreach (int candidate in supported)
            {
                if (overlapPercent >= candidate)
                {
                    normalized = candidate;
                }
            }

            return normalized;
        }

        private static int NormalizeSequenceLength(int sequenceLength)
        {
            int[] supported = [256, 512, 1024, 2048, 4096, 8192, 16384];
            int normalized = supported[0];
            foreach (int candidate in supported)
            {
                if (sequenceLength >= candidate)
                {
                    normalized = candidate;
                }
            }

            return normalized;
        }
    }

    internal sealed class TimeAlignmentSettings
    {
        public string? AsioDriverName { get; set; }
        public int MicrophoneInputChannelOffset { get; set; }
        public int LoopbackInputChannelOffset { get; set; }
        public int AsioOutputChannelOffset { get; set; }
        // Pre-band-mode files carry only this bool; BandMode is null there
        // and the migration below keeps an explicit manual window, otherwise
        // adopts the new AutoBand default.
        public bool UseBandpassWindow { get; set; }
        public string? BandMode { get; set; }
        public double BandpassCenterHz { get; set; } = 1000;
        public double BandpassPassOctaves { get; set; } = 1;
        public double BandpassFadeOctaves { get; set; } = 0.5;
        public double FirstPeakThresholdBelowMaxDb { get; set; } = 25;
        public double FirstPeakMinimumSnrDb { get; set; } = 12;
        public double PeakSearchWindowMilliseconds { get; set; } = 80;

        public static TimeAlignmentSettings Capture(
            TimeAlignmentOptions options) =>
            new()
            {
                AsioDriverName = options.AsioDriverName,
                MicrophoneInputChannelOffset = options.MicrophoneInputChannelOffset,
                LoopbackInputChannelOffset = options.LoopbackInputChannelOffset,
                AsioOutputChannelOffset = options.AsioOutputChannelOffset,
                UseBandpassWindow = options.BandMode == TimeAlignmentBandMode.ManualBand,
                BandMode = options.BandMode.ToString(),
                BandpassCenterHz = options.BandpassCenterHz,
                BandpassPassOctaves = options.BandpassPassOctaves,
                BandpassFadeOctaves = options.BandpassFadeOctaves,
                FirstPeakThresholdBelowMaxDb = options.FirstPeakThresholdBelowMaxDb,
                FirstPeakMinimumSnrDb = options.FirstPeakMinimumSnrDb,
                PeakSearchWindowMilliseconds = options.PeakSearchWindowMilliseconds
            };

        public void ApplyTo(TimeAlignmentOptions options, int sampleRate)
        {
            options.AsioDriverName = NormalizeAsioDriverName(AsioDriverName);
            options.MicrophoneInputChannelOffset =
                NormalizeAsioChannelOffset(
                    AsioDriverName,
                    sampleRate,
                    MicrophoneInputChannelOffset,
                    input: true);
            options.LoopbackInputChannelOffset =
                NormalizeAsioChannelOffset(
                    AsioDriverName,
                    sampleRate,
                    LoopbackInputChannelOffset,
                    input: true);
            options.AsioOutputChannelOffset =
                NormalizeAsioChannelOffset(
                    AsioDriverName,
                    sampleRate,
                    AsioOutputChannelOffset,
                    input: false);
            options.BandMode = Enum.TryParse(BandMode, out TimeAlignmentBandMode mode)
                ? mode
                : UseBandpassWindow
                    ? TimeAlignmentBandMode.ManualBand
                    : TimeAlignmentBandMode.AutoBand;
            options.BandpassCenterHz = Math.Clamp(BandpassCenterHz, 20.0, 20_000.0);
            options.BandpassPassOctaves = Math.Clamp(BandpassPassOctaves, 0.0, 8.0);
            options.BandpassFadeOctaves = Math.Clamp(BandpassFadeOctaves, 0.0, 8.0);
            options.FirstPeakThresholdBelowMaxDb =
                Math.Clamp(FirstPeakThresholdBelowMaxDb, 1.0, 80.0);
            options.FirstPeakMinimumSnrDb =
                Math.Clamp(FirstPeakMinimumSnrDb, 0.0, 80.0);
            options.PeakSearchWindowMilliseconds =
                Math.Clamp(PeakSearchWindowMilliseconds, 1.0, 1000.0);
        }
    }

    private static int NormalizeDeviceNumber(
        IReadOnlyList<AudioDeviceInfo> devices,
        int deviceNumber) =>
        devices.Any(device => device.DeviceNumber == deviceNumber)
            ? deviceNumber
            : -1;

    private static AudioBackend NormalizeAudioBackend(
        AudioBackend backend,
        string? asioDriverName)
    {
        if (!Enum.IsDefined(backend))
        {
            return AudioBackend.Wave;
        }
        if (backend == AudioBackend.Asio &&
            string.IsNullOrWhiteSpace(NormalizeAsioDriverName(asioDriverName)))
        {
            return AudioBackend.Wave;
        }

        return backend;
    }

    private static string? NormalizeWasapiEndpointId(string? endpointId, bool capture)
    {
        try
        {
            using var service = new WindowsAudioEndpointService();
            IReadOnlyList<AudioEndpointDescriptor> endpoints = capture
                ? service.GetCaptureEndpoints()
                : service.GetRenderEndpoints();
            AudioEndpointDescriptor? exact = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, endpointId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(endpointId))
            {
                return exact?.Id ?? endpointId;
            }
            return endpoints.FirstOrDefault(endpoint => endpoint.IsDefault)?.Id;
        }
        catch
        {
            return endpointId;
        }
    }

    private static int NormalizeWasapiSampleRate(
        AudioBackend backend,
        string? captureEndpointId,
        string? renderEndpointId,
        int fallback)
    {
        if (backend != AudioBackend.WasapiShared ||
            captureEndpointId == null ||
            renderEndpointId == null)
        {
            return fallback;
        }
        try
        {
            using var service = new WindowsAudioEndpointService();
            AudioEndpointDescriptor? capture = service.GetCaptureEndpoints()
                .FirstOrDefault(endpoint => endpoint.Id == captureEndpointId);
            AudioEndpointDescriptor? render = service.GetRenderEndpoints()
                .FirstOrDefault(endpoint => endpoint.Id == renderEndpointId);
            return capture != null && render != null &&
                capture.PreferredFormat.SampleRate == render.PreferredFormat.SampleRate
                    ? capture.PreferredFormat.SampleRate
                    : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string? NormalizeAsioDriverName(string? asioDriverName)
    {
        IReadOnlyList<AsioDeviceInfo> drivers = AsioDeviceCatalog.GetDrivers();
        if (drivers.Count == 0)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(asioDriverName))
        {
            return drivers[0].DriverName;
        }

        return drivers.Any(driver =>
            string.Equals(
                driver.DriverName,
                asioDriverName,
                StringComparison.OrdinalIgnoreCase))
            ? asioDriverName
            : drivers[0].DriverName;
    }

    private static int NormalizeAsioChannelOffset(
        string? asioDriverName,
        int sampleRate,
        int offset,
        bool input)
    {
        AsioDriverInfo driverInfo = AsioDeviceCatalog.GetDriverInfo(
            NormalizeAsioDriverName(asioDriverName),
            sampleRate);
        IReadOnlyList<AsioChannelInfo> channels = input
            ? driverInfo.InputChannels
            : driverInfo.OutputChannels;

        return channels.Any(channel => channel.Offset == offset)
            ? offset
            : 0;
    }

    private static int NormalizeWaveChannelOffset(int offset) =>
        Math.Clamp(offset, 0, 1);

    private static int? NormalizeOptionalWaveChannelOffset(int? offset) =>
        offset.HasValue ? NormalizeWaveChannelOffset(offset.Value) : null;

    private static int? NormalizeOptionalWasapiChannelOffset(int? offset) =>
        offset.HasValue ? Math.Max(0, offset.Value) : null;

    private static bool IsWasapiBackend(AudioBackend backend) =>
        backend is AudioBackend.WasapiShared or AudioBackend.WasapiExclusive;

    private static int? NormalizeOptionalAsioChannelOffset(
        string? asioDriverName,
        int sampleRate,
        int? offset) =>
        offset.HasValue
            ? NormalizeAsioChannelOffset(asioDriverName, sampleRate, offset.Value, input: true)
            : null;

    private static MicrophoneCalibrationMode NormalizeCalibrationMode(
        MicrophoneCalibrationMode? mode,
        bool legacyUseCalibration)
    {
        if (mode.HasValue && Enum.IsDefined(mode.Value))
        {
            return mode.Value;
        }

        return legacyUseCalibration
            ? MicrophoneCalibrationMode.Degrees0
            : MicrophoneCalibrationMode.Off;
    }

    // Mirrors the UI invariant (see TukeyWindowControlHelper): each fade is in
    // [0, window] and their sum must not exceed the window length. Clamping each
    // to window/2 instead would corrupt valid asymmetric windows (e.g. 256 + 16).
    private static (int Left, int Right) ClampTukeyWindows(
        int left,
        int right,
        int window)
    {
        int clampedLeft = Clamp(left, 0, window);
        int clampedRight = Clamp(right, 0, window);
        if (clampedLeft + clampedRight > window)
        {
            clampedRight = Math.Max(0, window - clampedLeft);
        }

        return (clampedLeft, clampedRight);
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);
}
