using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed class MeasurementSettingsFile
{
    private const int CurrentSchemaVersion = 7;
    private const string FileName = "measurement-settings.json";

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
    public string? LastImpulseResponseDirectory { get; set; }

    // True when loading reset a loopback configuration that pointed at the
    // removed separate-loopback-device capability; the shell shows a one-time
    // notice telling the user to pick a loopback channel again.
    [JsonIgnore]
    public bool LegacyDualDeviceLoopbackReset { get; private set; }

    private static string PathOnDisk =>
        Path.Combine(AppContext.BaseDirectory, FileName);

    public static MeasurementSettingsFile LoadOrDefault()
    {
        try
        {
            if (!File.Exists(PathOnDisk))
            {
                return new MeasurementSettingsFile();
            }

            using FileStream stream = File.OpenRead(PathOnDisk);
            MeasurementSettingsFile? settings =
                JsonSerializer.Deserialize<MeasurementSettingsFile>(
                    stream,
                    SerializerOptions);
            if (settings?.SchemaVersion != CurrentSchemaVersion)
            {
                return new MeasurementSettingsFile();
            }

            settings.MigrateLegacyDualDeviceLoopback();
            return settings;
        }
        catch
        {
            return new MeasurementSettingsFile();
        }
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
        SchemaVersion = CurrentSchemaVersion;
        // Temp file + move keeps the settings intact if the write is interrupted;
        // a corrupted file silently loads as defaults.
        string tempPath = PathOnDisk + ".tmp";
        using (FileStream stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, this, SerializerOptions);
        }

        File.Move(tempPath, PathOnDisk, overwrite: true);
    }

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
                ConfirmEachAverageRun = measurement.ConfirmEachAverageRun
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
            measurement.Init(
                Clamp(Octaves, 1, 32),
                sampleRate,
                Bits is 16 or 24 ? Bits : 24,
                Math.Clamp(RequestedDurationSeconds, 0.001, 100.0),
                Enum.IsDefined(PlaybackChannel)
                    ? PlaybackChannel
                    : PlaybackChannel.Mono,
                NormalizeDeviceNumber(
                    AudioDeviceCatalog.GetPlaybackDevices(),
                    OutputDeviceNumber),
                NormalizeDeviceNumber(
                    AudioDeviceCatalog.GetRecordingDevices(),
                    InputDeviceNumber),
                backend,
                NormalizeAsioDriverName(AsioDriverName),
                NormalizeAsioChannelOffset(
                    AsioDriverName,
                    sampleRate,
                    AsioInputChannelOffset,
                    input: true),
                NormalizeAsioChannelOffset(
                    AsioDriverName,
                    sampleRate,
                    AsioOutputChannelOffset,
                    input: false),
                backend == AudioBackend.WasapiShared
                    ? Math.Max(0, WaveInputChannelOffset)
                    : NormalizeWaveChannelOffset(WaveInputChannelOffset),
                backend == AudioBackend.WasapiShared
                    ? NormalizeOptionalWasapiChannelOffset(WaveLoopbackInputChannelOffset)
                    : NormalizeOptionalWaveChannelOffset(WaveLoopbackInputChannelOffset),
                NormalizeOptionalAsioChannelOffset(
                    AsioDriverName,
                    sampleRate,
                    AsioLoopbackInputChannelOffset),
                Clamp(AverageRunCount, 1, 64),
                ConfirmEachAverageRun,
                captureEndpointId,
                renderEndpointId,
                Clamp(WasapiBufferMilliseconds, 10, 100),
                WasapiCaptureEndpointName,
                WasapiRenderEndpointName);
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
        public bool ShowCoherence { get; set; } = true;
        public bool ShowMeasuredPhase { get; set; } = true;
        public bool ShowMinimumPhase { get; set; } = true;
        public bool ShowExcessPhase { get; set; } = true;
        public bool ShowPrimary { get; set; } = true;
        public bool ShowHd2 { get; set; } = true;
        public bool ShowHd3 { get; set; } = true;
        public bool ShowHd4 { get; set; } = true;
        public bool ShowThdPlusNoise { get; set; } = true;
        public bool ShowGroupDelay { get; set; } = true;
        public double PhaseGateOffsetMs { get; set; } = FrequencyResponseOptions.DefaultPhaseGateOffsetMs;
        public double PhaseLeftMs { get; set; } = FrequencyResponseOptions.DefaultPhaseLeftMs;
        public double PhasePlateauMs { get; set; } = FrequencyResponseOptions.DefaultPhasePlateauMs;
        public double PhaseRightMs { get; set; } = FrequencyResponseOptions.DefaultPhaseRightMs;
        public double PhaseDetrendMs { get; set; } = FrequencyResponseOptions.DefaultPhaseDetrendMs;
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
                ShowCoherence = visibility.ShowCoherence,
                ShowMeasuredPhase = visibility.ShowMeasuredPhase,
                ShowMinimumPhase = visibility.ShowMinimumPhase,
                ShowExcessPhase = visibility.ShowExcessPhase,
                ShowPrimary = visibility.ShowPrimary,
                ShowHd2 = visibility.ShowHd2,
                ShowHd3 = visibility.ShowHd3,
                ShowHd4 = visibility.ShowHd4,
                ShowThdPlusNoise = visibility.ShowThdPlusNoise,
                ShowGroupDelay = visibility.ShowGroupDelay,
                PhaseGateOffsetMs = options.PhaseGateOffsetMs,
                PhaseLeftMs = options.PhaseLeftMs,
                PhasePlateauMs = options.PhasePlateauMs,
                PhaseRightMs = options.PhaseRightMs,
                PhaseDetrendMs = options.PhaseDetrendMs,
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
            visibility.ShowCoherence = ShowCoherence;
            visibility.ShowMeasuredPhase = ShowMeasuredPhase;
            visibility.ShowMinimumPhase = ShowMinimumPhase;
            visibility.ShowExcessPhase = ShowExcessPhase;
            visibility.ShowPrimary = ShowPrimary;
            visibility.ShowHd2 = ShowHd2;
            visibility.ShowHd3 = ShowHd3;
            visibility.ShowHd4 = ShowHd4;
            visibility.ShowThdPlusNoise = ShowThdPlusNoise;
            visibility.ShowGroupDelay = ShowGroupDelay;
            options.PhaseGateOffsetMs = ClampMilliseconds(PhaseGateOffsetMs, 0.0, 2000.0);
            options.PhaseLeftMs = ClampMilliseconds(PhaseLeftMs, 0.0, 1000.0);
            options.PhasePlateauMs = ClampMilliseconds(PhasePlateauMs, 0.0, 1000.0);
            options.PhaseRightMs = ClampMilliseconds(PhaseRightMs, 0.0, 1000.0);
            options.PhaseDetrendMs = ClampMilliseconds(PhaseDetrendMs, -2000.0, 2000.0);
            options.GroupDelayGateOffsetMs = ClampMilliseconds(GroupDelayGateOffsetMs, 0.0, 2000.0);
            options.GroupDelayLeftMs = ClampMilliseconds(GroupDelayLeftMs, 0.0, 1000.0);
            options.GroupDelayPlateauMs = ClampMilliseconds(GroupDelayPlateauMs, 0.0, 1000.0);
            options.GroupDelayRightMs = ClampMilliseconds(GroupDelayRightMs, 0.0, 1000.0);
        }

        private static double ClampMilliseconds(double value, double min, double max) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : 0.0;
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
                    NormalizeCoherenceThreshold(options.CoherenceThresholdPercent)
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
        public bool UseBandpassWindow { get; set; }
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
                UseBandpassWindow = options.UseBandpassWindow,
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
            options.UseBandpassWindow = UseBandpassWindow;
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
            IReadOnlyList<AudioEndpointInfo> endpoints = capture
                ? service.GetCaptureEndpoints()
                : service.GetRenderEndpoints();
            AudioEndpointInfo? exact = endpoints.FirstOrDefault(endpoint =>
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
            AudioEndpointInfo? capture = service.GetCaptureEndpoints()
                .FirstOrDefault(endpoint => endpoint.Id == captureEndpointId);
            AudioEndpointInfo? render = service.GetRenderEndpoints()
                .FirstOrDefault(endpoint => endpoint.Id == renderEndpointId);
            return capture != null && render != null &&
                capture.MixFormat.SampleRate == render.MixFormat.SampleRate
                    ? capture.MixFormat.SampleRate
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
