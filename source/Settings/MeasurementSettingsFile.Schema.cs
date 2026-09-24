using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed partial class MeasurementSettingsFile
{
    internal sealed class SweepMeasurementSettings
    {
        public const double MinSweepFrequencyHz = 20.0;
        public const double MaxSweepFrequencyHz = 20_000.0;

        // Legacy (top pinned to Nyquist), migration only; 0 in LowFrequencyHz/HighFrequencyHz = derive from this.
        public int Octaves { get; set; } = 12;
        public double LowFrequencyHz { get; set; }
        public double HighFrequencyHz { get; set; }
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
        // Legacy, deserialized only for MigrateLegacyDualDeviceLoopback; null after load, never written.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WaveLoopbackDeviceNumber { get; set; }
        public int AsioInputChannelOffset { get; set; }
        public int? AsioLoopbackInputChannelOffset { get; set; }
        public int AsioOutputChannelOffset { get; set; }
        public int AverageRunCount { get; set; } = 2;
        public ProtectiveHighPassKind ProtectiveHighPassKind { get; set; }
        public double ProtectiveHighPassFrequencyHz { get; set; } = 2_000.0;
        public int ProtectiveHighPassSlopeDbPerOctave { get; set; } = 24;
        public string? MicrophoneCalibration0DegreesPath { get; set; }
        // Belongs to the rig: a run freezes it into its file. Null = uncalibrated.
        public string? MicrophoneCalibrationId { get; set; } =
            MicrophoneCalibrationIds.ZeroDegrees;
        // Legacy (schema <= 10), migrated into AdditionalMicrophoneCalibrations; never written.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MicrophoneCalibration90DegreesPath { get; set; }
        public List<MicrophoneCalibrationDefinition> AdditionalMicrophoneCalibrations
        { get; set; } = [];
        // Per backend: a channel number names a different input on each.
        public List<ArrayMicrophoneDefinition> WaveArrayMicrophones { get; set; } = [];
        public List<ArrayMicrophoneDefinition> AsioArrayMicrophones { get; set; } = [];
        // Also per device: two 8-input interfaces share channel numbers, so a swap silently re-points every mic.
        // Null = configured before stamping (not a mismatch). WASAPI stamps the endpoint id; ASIO only the driver name,
        // so wrappers (ASIO4ALL, FlexASIO) can pass. Channel count was rejected: some drivers report it per sample rate.
        public string? WaveArrayDeviceId { get; set; }
        public string? AsioArrayDeviceId { get; set; }
        public SplCalibration? SplCalibration { get; set; }

        // Not serialized: a third, ignored copy of one of the lists above.
        [JsonIgnore]
        public List<ArrayMicrophoneDefinition> ArrayMicrophones =>
            AudioBackend == AudioBackend.Asio ? AsioArrayMicrophones : WaveArrayMicrophones;

        /// <summary>Carries calibrations over: <see cref="Capture"/> rebuilds from the measurement, which knows none.</summary>
        public void CopyCalibrationFrom(SweepMeasurementSettings previous)
        {
            ArgumentNullException.ThrowIfNull(previous);
            MicrophoneCalibration0DegreesPath = previous.MicrophoneCalibration0DegreesPath;
            MicrophoneCalibrationId = previous.MicrophoneCalibrationId;
            AdditionalMicrophoneCalibrations = previous.AdditionalMicrophoneCalibrations
                .Select(definition => definition.Clone())
                .ToList();
            WaveArrayMicrophones = previous.WaveArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            AsioArrayMicrophones = previous.AsioArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            // With the device stamp, or a stale array would look fresh.
            WaveArrayDeviceId = previous.WaveArrayDeviceId;
            AsioArrayDeviceId = previous.AsioArrayDeviceId;
        }

        // Every analysis mode derives from the transfer IR, which needs the loopback.
        public bool HasLoopbackConfigured =>
            AudioBackend == AudioBackend.Asio
                ? AsioLoopbackInputChannelOffset.HasValue
                : WaveLoopbackInputChannelOffset.HasValue;

        /// <summary>Clamps the requested band, migrating pre-band settings (octave count 12 lands at 20 Hz–20 kHz).</summary>
        public (double LowHz, double HighHz) ResolveBand(int sampleRate)
        {
            double low;
            double high;
            if (LowFrequencyHz > 0 && HighFrequencyHz > LowFrequencyHz)
            {
                low = LowFrequencyHz;
                high = HighFrequencyHz;
            }
            else
            {
                double nyquist = sampleRate / 2.0;
                double span = Octaves > 0 ? Octaves : 12;
                low = nyquist / Math.Pow(2.0, span);
                high = nyquist;
            }

            high = Math.Clamp(high, MinSweepFrequencyHz + 1.0, MaxSweepFrequencyHz);
            low = Math.Clamp(low, MinSweepFrequencyHz, high - 1.0);
            return (low, high);
        }

        public static SweepMeasurementSettings Capture(
            ExpSweepMeasurement measurement) =>
            new()
            {
                LowFrequencyHz = measurement.LowFrequencyHz,
                HighFrequencyHz = measurement.HighFrequencyHz,
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
                ProtectiveHighPassKind = measurement.ProtectiveHighPass.Kind,
                ProtectiveHighPassFrequencyHz = measurement.ProtectiveHighPass.FrequencyHz,
                ProtectiveHighPassSlopeDbPerOctave =
                    measurement.ProtectiveHighPass.SlopeDbPerOctave,
                SplCalibration = measurement.SplCalibration
            };

        public void ApplyTo(ExpSweepMeasurement measurement)
        {
            measurement.Init(BuildConfiguration());
            measurement.SplCalibration = SplCalibration;
        }

        /// <summary>An unstamped array (configured before stamping) is accepted; otherwise an exact identity match.</summary>
        internal static bool ArrayMatchesDevice(string? configuredOn, string? deviceId) =>
            string.IsNullOrWhiteSpace(configuredOn) ||
            string.Equals(configuredOn, deviceId, StringComparison.Ordinal);

        private static IReadOnlyList<int> ResolveArrayChannels(
            List<ArrayMicrophoneDefinition> microphones,
            int microphoneChannel,
            int? loopbackChannel,
            Func<int, bool> reachable,
            bool deviceMatches)
        {
            // Filtered, not refused: the app must start on its own saved settings. A gone device means numbers name unchosen inputs.
            // Stored per backend, not per interface, so offsets beyond a smaller card's inputs are dropped as unreachable.
            return deviceMatches
                ? ArrayChannelRules.Recorded(microphones, microphoneChannel, loopbackChannel, reachable)
                : [];
        }

        /// <remarks>Permissive when the device cannot be asked (unplugged); the session refuses on open instead.</remarks>
        private static Func<int, bool> ReachableInput(
            AudioBackend backend,
            string? asioDriverName,
            int sampleRate,
            string? captureEndpointId)
        {
            if (backend == AudioBackend.Asio)
            {
                IReadOnlyList<AsioChannelInfo> inputs = AsioDeviceCatalog
                    .GetDriverInfo(NormalizeAsioDriverName(asioDriverName), sampleRate)
                    .InputChannels;
                return inputs.Count == 0
                    ? _ => true
                    : offset => inputs.Any(channel => channel.Offset == offset);
            }

            if (backend.IsWasapi())
            {
                int count = WasapiCaptureChannelCount(captureEndpointId);
                return count <= 0 ? _ => true : offset => offset < count;
            }

            return offset => offset < 2;
        }

        public SweepMeasurementConfiguration BuildConfiguration()
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
            (double lowFrequencyHz, double highFrequencyHz) = ResolveBand(sampleRate);
            return new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(
                    lowFrequencyHz,
                    highFrequencyHz,
                    sampleRate,
                    Bits is 16 or 24 ? Bits : 24,
                    Math.Clamp(
                        RequestedDurationSeconds,
                        0.001,
                        ExponentialSineSweep.MaxDurationSeconds),
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
                    WaveInputChannelOffset: backend.IsWasapi()
                        ? Math.Max(0, WaveInputChannelOffset)
                        : NormalizeWaveChannelOffset(WaveInputChannelOffset),
                    WaveLoopbackInputChannelOffset: backend.IsWasapi()
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
                    WasapiBufferMilliseconds: Clamp(WasapiBufferMilliseconds, 10, 100),
                    WaveArrayInputChannelOffsets: ResolveArrayChannels(
                        WaveArrayMicrophones,
                        backend.IsWasapi()
                            ? Math.Max(0, WaveInputChannelOffset)
                            : NormalizeWaveChannelOffset(WaveInputChannelOffset),
                        backend.IsWasapi()
                            ? NormalizeOptionalWasapiChannelOffset(WaveLoopbackInputChannelOffset)
                            : NormalizeOptionalWaveChannelOffset(WaveLoopbackInputChannelOffset),
                        ReachableInput(backend, AsioDriverName, sampleRate, captureEndpointId),
                        // Raw selection, not resolved: must answer the same whether the device is plugged in now.
                        ArrayMatchesDevice(WaveArrayDeviceId, WasapiCaptureEndpointId)),
                    AsioArrayInputChannelOffsets: ResolveArrayChannels(
                        AsioArrayMicrophones,
                        NormalizeAsioChannelOffset(
                            AsioDriverName,
                            sampleRate,
                            AsioInputChannelOffset,
                            input: true),
                        NormalizeOptionalAsioChannelOffset(
                            AsioDriverName,
                            sampleRate,
                            AsioLoopbackInputChannelOffset),
                        ReachableInput(
                            AudioBackend.Asio, AsioDriverName, sampleRate, captureEndpointId),
                        ArrayMatchesDevice(AsioArrayDeviceId, AsioDriverName))),
                new SweepAveragingConfiguration(Clamp(AverageRunCount, 1, 64)),
                ToProtectiveHighPass());
        }

        /// <remarks>Separate accessor: the live analyzer divides this hardware filter out without building a sweep configuration.</remarks>
        public ProtectiveHighPassConfiguration ToProtectiveHighPass() =>
            ProtectiveHighPassConfiguration.Normalize(
                new ProtectiveHighPassConfiguration(
                    ProtectiveHighPassKind,
                    ProtectiveHighPassFrequencyHz,
                    ProtectiveHighPassSlopeDbPerOctave));
    }

    internal sealed class FrequencyResponseSettings
    {
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 256;
        public int RightTukeyWindow { get; set; } = 256;
        public PhaseWindowMode? MagnitudeWindowMode { get; set; } =
            Resonalyze.Dsp.PhaseWindowMode.Fixed;
        public int MagnitudeFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalibrationId { get; set; }
        // Legacy (schema <= 10), migration only; nullable without initializer so absent is distinguishable.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseCalibration { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LegacyMicrophoneCalibrationMode? CalibrationMode { get; set; }
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;
        public bool ShowCoherence { get; set; } = true;
        public bool ShowArrayAverage { get; set; }
        public bool ShowArrayMicrophones { get; set; }
        public bool ShowArraySpread { get; set; }
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
        public bool ShowMinimumPhaseGroupDelay { get; set; } = true;
        public bool ShowExcessGroupDelay { get; set; } = true;
        // No initializer: System.Text.Json never assigns a missing property, so a pre-Auto file (v <= 9) must read as null.
        public bool? PhaseGateAutoFit { get; set; }
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
        public bool? GroupDelayGateAutoFit { get; set; }
        public double GroupDelayGateOffsetMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayGateOffsetMs;
        public double GroupDelayLeftMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayLeftMs;
        public double GroupDelayPlateauMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayPlateauMs;
        public double GroupDelayRightMs { get; set; } = FrequencyResponseOptions.DefaultGroupDelayRightMs;
        // Initializer is the fresh-install default; pre-v13 files are put on Fixed in MeasurementSettingsFile.LoadOrDefault.
        public PhaseWindowMode? GroupDelayWindowMode { get; set; } =
            Resonalyze.Dsp.PhaseWindowMode.FrequencyDependent;
        public int GroupDelayFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;

        public static FrequencyResponseSettings Capture(
            FrequencyResponseOptions options,
            CurveVisibilityOptions visibility) =>
            new()
            {
                Window = options.Window,
                LeftTukeyWindow = options.LeftTukeyWindow,
                RightTukeyWindow = options.RightTukeyWindow,
                MagnitudeWindowMode = options.MagnitudeWindowMode,
                MagnitudeFdwCycles = options.MagnitudeFdwCycles,
                SmoothingInverseOctaves = options.SmoothingInverseOctaves,
                Offset = options.Offset,
                Unwrap = options.Unwrap,
                CalibrationId = options.CalibrationId,
                MagnitudeScale = options.MagnitudeScale,
                ShowCoherence = visibility.ShowCoherence,
                ShowArrayAverage = visibility.ShowArrayAverage,
                ShowArrayMicrophones = visibility.ShowArrayMicrophones,
                ShowArraySpread = visibility.ShowArraySpread,
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
                ShowMinimumPhaseGroupDelay = visibility.ShowMinimumPhaseGroupDelay,
                ShowExcessGroupDelay = visibility.ShowExcessGroupDelay,
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
                GroupDelayRightMs = options.GroupDelayRightMs,
                GroupDelayWindowMode = options.GroupDelayWindowMode,
                GroupDelayFdwCycles = options.GroupDelayFdwCycles
            };

        public void ApplyTo(FrequencyResponseOptions options, CurveVisibilityOptions visibility)
        {
            // Floor matches the UI minimum (4); a higher floor would corrupt small windows on roundtrip.
            int window = Clamp(Window, 4, 32768);
            options.Window = window;
            (options.LeftTukeyWindow, options.RightTukeyWindow) =
                TukeyFades.Contain(LeftTukeyWindow, RightTukeyWindow, window);
            options.MagnitudeWindowMode = MagnitudeWindowMode is { } magnitudeWindowMode &&
                Enum.IsDefined(magnitudeWindowMode)
                    ? magnitudeWindowMode
                    : Resonalyze.Dsp.PhaseWindowMode.Fixed;
            options.MagnitudeFdwCycles = WindowModeChoice.ValidCycles(MagnitudeFdwCycles);
            options.SmoothingInverseOctaves =
                SmoothingPresetOptions.Normalize(SmoothingInverseOctaves);
            options.Offset = Clamp(Offset, -32768, 32768);
            options.Unwrap = Unwrap;
            options.CalibrationId = ResolveCalibrationId(
                CalibrationId,
                CalibrationMode,
                UseCalibration);
            options.MagnitudeScale = Enum.IsDefined(MagnitudeScale)
                ? MagnitudeScale
                : MagnitudeScale.Relative;
            visibility.ShowCoherence = ShowCoherence;
            visibility.ShowArrayAverage = ShowArrayAverage;
            visibility.ShowArrayMicrophones = ShowArrayMicrophones;
            visibility.ShowArraySpread = ShowArraySpread;
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
            visibility.ShowMinimumPhaseGroupDelay = ShowMinimumPhaseGroupDelay;
            visibility.ShowExcessGroupDelay = ShowExcessGroupDelay;
            // Pre-Auto file: Auto only if the offset is the untouched default, so a placed gate is not re-snapped.
            options.PhaseGateAutoFit = PhaseGateAutoFit ??
                PhaseGateOffsetMs == FrequencyResponseOptions.DefaultPhaseGateOffsetMs;
            options.PhaseGateOffsetMs = ClampMilliseconds(PhaseGateOffsetMs, 0.0, 2000.0);
            options.PhaseLeftMs = ClampMilliseconds(PhaseLeftMs, 0.0, 1000.0);
            options.PhasePlateauMs = ClampMilliseconds(PhasePlateauMs, 0.0, 1000.0);
            options.PhaseRightMs = ClampMilliseconds(PhaseRightMs, 0.0, 1000.0);
            options.PhaseDetrendMs = ClampMilliseconds(PhaseDetrendMs, -2000.0, 2000.0);
            options.PhaseWindowMode = PhaseWindowMode is { } windowMode &&
                Enum.IsDefined(windowMode)
                    ? windowMode
                    : Resonalyze.Dsp.PhaseWindowMode.Fixed;
            options.PhaseFdwCycles = WindowModeChoice.ValidCycles(PhaseFdwCycles);
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
            options.GroupDelayWindowMode = GroupDelayWindowMode is { } groupDelayWindowMode &&
                Enum.IsDefined(groupDelayWindowMode)
                    ? groupDelayWindowMode
                    : Resonalyze.Dsp.PhaseWindowMode.Fixed;
            options.GroupDelayFdwCycles = WindowModeChoice.ValidCycles(GroupDelayFdwCycles);
        }

        private static double ClampMilliseconds(double value, double min, double max) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : 0.0;
    }

    // Own type rather than Dsp PeqBand: a file format needs defaults and tolerance for missing fields.
    internal sealed class PeqBandSettings
    {
        public double FrequencyHz { get; set; } = 1000;
        public double Q { get; set; } = 1;
        public double GainDb { get; set; }

        public PeqBandType Type { get; set; } = PeqBandType.Peaking;

        public bool Locked { get; set; }
    }

    // Self-contained: derives nothing from overlays or the current measurement. The loaded IR is not persisted.
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
        // Imported target replaces the parametric terms while present; stored by value (flat frequency, level list)
        // since a path may no longer exist.
        public string? TargetImportedName { get; set; }
        public double[]? TargetImportedCurve { get; set; }
        public double ToleranceDb { get; set; } = 3;
        public TargetDeviationMode DeviationMode { get; set; } = TargetDeviationMode.Deviation;
        public int TargetColorArgb { get; set; } = unchecked((int)0xFF37C8A0);
        public double TargetStrokeThickness { get; set; } = 2;
        public OverlayLineStyle TargetLineStyle { get; set; } = OverlayLineStyle.Dash;
        public int TargetSmoothingInverseOctaves { get; set; }
        public double TargetOffsetDb { get; set; }
        public double GainMinDb { get; set; } = -15;
        public double GainMaxDb { get; set; } = 6;
        // Slot order is the export numbering. Empty = user cleared. Null only in schema <= 9 files:
        // rebuilt as BandCount ISO-spread filters.
        public List<PeqBandSettings>? Bands { get; set; }

        public double PreampDb { get; set; }

        public int BandCount { get; set; }
        public int SourceSmoothingInverseOctaves { get; set; }

        // Unlike the measurement views, defaults to no correction.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalibrationId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LegacyMicrophoneCalibrationMode? CalibrationMode { get; set; }

        public string? ResolveCalibrationId() =>
            MeasurementSettingsFile.ResolveCalibrationId(
                CalibrationId,
                CalibrationMode,
                legacyUseCalibration: false);

        // Used when the source states no rate; a source rate overrides without changing this.
        public int ManualSampleRateHz { get; set; } = 48_000;

        /// <summary>Legacy flag, read only when <see cref="AutoTuneBoosts"/> is absent; still written so older builds can load.</summary>
        public bool CutsOnly { get; set; } = true;

        public EqAutoTuneBoosts? AutoTuneBoosts { get; set; }

        // A file from before the choice existed ticked Cuts only to keep the curve from being lifted, which refilling
        // the bank's own cuts keeps too; unticked was plain boosts.
        public EqAutoTuneBoosts ResolveAutoTuneBoosts() =>
            AutoTuneBoosts is { } boosts && Enum.IsDefined(boosts)
                ? boosts
                : CutsOnly ? EqAutoTuneBoosts.RefillOwnCuts : EqAutoTuneBoosts.Allowed;

        public bool AllowShelves { get; set; }

        /// <summary>A handed-over channel's crossover shapes the target; true for a file written before it existed.</summary>
        public bool CrossoverInTarget { get; set; } = true;

        // Well below the manual limit of 20: the fit reads one mic position, and a sharp notch fits that position alone.
        public double AutoTuneMaxQ { get; set; } = 6.0;

        public bool ShowEqCurve { get; set; } = true;
    }

    internal sealed class ImpulseResponseSettings
    {
        public int Length { get; set; } = 4096;

        /// <summary>Legacy flag, read only when <see cref="AmplitudeScale"/> is absent; still written so older builds can load.</summary>
        public bool? Logarithmic { get; set; }

        public ImpulseAmplitudeScale? AmplitudeScale { get; set; }
        public ImpulseTimeUnit TimeUnit { get; set; } = ImpulseTimeUnit.Milliseconds;
        public ImpulseTimeOrigin TimeOrigin { get; set; } = ImpulseTimeOrigin.RecordStart;
        public double EnvelopeSmoothingMs { get; set; }
        public bool Invert { get; set; }
        public bool NormalizeStepToImpulsePeak { get; set; } = true;
        public double BandFilterOctaves { get; set; }
        public double BandCenterHz { get; set; } = 1000.0;
        public bool ShowImpulse { get; set; } = true;
        public bool ShowEnvelope { get; set; }
        public bool ShowStep { get; set; }
        public bool ShowAutocorrelation { get; set; } = true;

        public static ImpulseResponseSettings Capture(
            ImpulseResponseOptions options) =>
            new()
            {
                Length = options.Length,
                Logarithmic = options.AmplitudeScale == ImpulseAmplitudeScale.Decibels,
                AmplitudeScale = options.AmplitudeScale,
                TimeUnit = options.TimeUnit,
                TimeOrigin = options.TimeOrigin,
                EnvelopeSmoothingMs = options.EnvelopeSmoothingMs,
                Invert = options.Invert,
                NormalizeStepToImpulsePeak = options.NormalizeStepToImpulsePeak,
                BandFilterOctaves = options.BandFilterOctaves,
                BandCenterHz = options.BandCenterHz,
                ShowImpulse = options.ShowImpulse,
                ShowEnvelope = options.ShowEnvelope,
                ShowStep = options.ShowStep,
                ShowAutocorrelation = options.ShowAutocorrelation
            };

        public void ApplyTo(ImpulseResponseOptions options)
        {
            options.Length = Clamp(Length, 1, 262144);
            options.AmplitudeScale = AmplitudeScale
                ?? (Logarithmic == true
                    ? ImpulseAmplitudeScale.Decibels
                    : ImpulseAmplitudeScale.Linear);
            options.TimeUnit = TimeUnit;
            options.TimeOrigin = TimeOrigin;
            options.EnvelopeSmoothingMs = Math.Clamp(EnvelopeSmoothingMs, 0.0, 100.0);
            options.Invert = Invert;
            options.NormalizeStepToImpulsePeak = NormalizeStepToImpulsePeak;
            // A hand-edited width outside the offered ones reads as no band filter.
            options.BandFilterOctaves =
                BandFilterOctaves > 0.0 && BandFilterOctaves <= 1.0
                    ? BandFilterOctaves
                    : 0.0;
            options.BandCenterHz = BandCenterHz > 0.0 ? BandCenterHz : 1000.0;
            options.ShowImpulse = ShowImpulse;
            options.ShowEnvelope = ShowEnvelope;
            options.ShowStep = ShowStep;
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
                TukeyFades.Contain(LeftTukeyWindow, RightTukeyWindow, window);
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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LiveAnalysisMode? AnalysisMode { get; set; }
        public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;
        public bool CompensateNoiseTilt { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalibrationId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseCalibration { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LegacyMicrophoneCalibrationMode? CalibrationMode { get; set; }
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
                AnalysisMode = Enum.IsDefined(options.AnalysisMode)
                    ? options.AnalysisMode
                    : LiveAnalysisMode.TransferFunction,
                NoiseColor = Enum.IsDefined(options.NoiseColor)
                    ? options.NoiseColor
                    : NoiseColor.PinkPeriodic,
                CompensateNoiseTilt = options.CompensateNoiseTilt,
                CalibrationId = options.CalibrationId,
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
            // Pre-mode files: SPL scale or Silent signal were RTA-exclusive, so either marks RTA.
            options.AnalysisMode = AnalysisMode is { } mode && Enum.IsDefined(mode)
                ? mode
                : MagnitudeScale == MagnitudeScale.SoundPressureLevel ||
                    options.NoiseColor == NoiseColor.Silent
                    ? LiveAnalysisMode.Rta
                    : LiveAnalysisMode.TransferFunction;
            // Invariant: Silent only in RTA (nothing to correlate). Repair a hand-edited file's signal, keep the mode.
            if (options.AnalysisMode == LiveAnalysisMode.TransferFunction &&
                options.NoiseColor == NoiseColor.Silent)
            {
                options.NoiseColor = NoiseColor.PinkPeriodic;
            }

            options.CompensateNoiseTilt = CompensateNoiseTilt;
            options.CalibrationId = ResolveCalibrationId(
                CalibrationId,
                CalibrationMode,
                UseCalibration);
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

        private static int NormalizeSequenceLength(int sequenceLength) =>
            LiveSequenceLengths.Normalize(sequenceLength);
    }

    internal sealed class TimeAlignmentSettings
    {
        public string? AsioDriverName { get; set; }
        public int MicrophoneInputChannelOffset { get; set; }
        public int LoopbackInputChannelOffset { get; set; }
        public int AsioOutputChannelOffset { get; set; }
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
}
