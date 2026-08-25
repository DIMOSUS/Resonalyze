using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

// The persisted schema sections of MeasurementSettingsFile: one nested class per
// mode, each owning its own Capture/ApplyTo and its own clamping. Split out of
// the main file, which handles load/save/backup, so a new mode's settings do not
// have to be threaded past 600 lines of unrelated schema.

namespace Resonalyze;

internal sealed partial class MeasurementSettingsFile
{
    internal sealed class SweepMeasurementSettings
    {
        // The lowest/highest sweep frequency the user may request; the achieved
        // band is rounded outward from here for phase alignment.
        public const double MinSweepFrequencyHz = 20.0;
        public const double MaxSweepFrequencyHz = 20_000.0;

        // Legacy: the sweep used to be defined by an octave count with the top
        // pinned to Nyquist. Kept for migration only; the band is now stored in
        // LowFrequencyHz/HighFrequencyHz (0 = derive from the legacy octave count).
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
        // Legacy field (pre removal of the separate-loopback-device
        // capability), kept ONLY so old files deserialize into the migration
        // (see MigrateLegacyDualDeviceLoopback); always null after loading
        // and never written back.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WaveLoopbackDeviceNumber { get; set; }
        public int AsioInputChannelOffset { get; set; }
        public int? AsioLoopbackInputChannelOffset { get; set; }
        public int AsioOutputChannelOffset { get; set; }
        // Two runs by default: averaging is what the Measurements control offers
        // (its minimum is 2), and a lone sweep gives nothing to average away.
        public int AverageRunCount { get; set; } = 2;
        public bool ConfirmEachAverageRun { get; set; }
        public ProtectiveHighPassKind ProtectiveHighPassKind { get; set; }
        public double ProtectiveHighPassFrequencyHz { get; set; } = 2_000.0;
        public int ProtectiveHighPassSlopeDbPerOctave { get; set; } = 24;
        public string? MicrophoneCalibration0DegreesPath { get; set; }
        // Legacy field (schema <= 10, when 90° was a second fixed slot), kept
        // ONLY so old files deserialize into the migration, which turns the file
        // into a named entry of AdditionalMicrophoneCalibrations; always null
        // after loading and never written back.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MicrophoneCalibration90DegreesPath { get; set; }
        // Every calibration beside the microphone's own 0° file: further files
        // and curves estimated for an angle of incidence, in the order the user
        // arranged them.
        public List<MicrophoneCalibrationDefinition> AdditionalMicrophoneCalibrations
        { get; set; } = [];
        public SplCalibration? SplCalibration { get; set; }

        /// <summary>
        /// Carries the microphone calibrations over from <paramref name="previous"/>.
        /// <see cref="Capture"/> rebuilds this section from the measurement, which
        /// knows the audio configuration and the SPL anchor but nothing about the
        /// calibration files — so without this they would be dropped on every
        /// capture. Kept here rather than at the call site so a calibration added
        /// later cannot be forgotten by one caller.
        /// </summary>
        public void CopyCalibrationFrom(SweepMeasurementSettings previous)
        {
            ArgumentNullException.ThrowIfNull(previous);
            MicrophoneCalibration0DegreesPath = previous.MicrophoneCalibration0DegreesPath;
            AdditionalMicrophoneCalibrations = previous.AdditionalMicrophoneCalibrations
                .Select(definition => definition.Clone())
                .ToList();
        }

        // A loopback reference channel is mandatory: every analysis mode is derived from the
        // transfer IR, which only exists when the loopback is captured alongside the microphone.
        public bool HasLoopbackConfigured =>
            AudioBackend == AudioBackend.Asio
                ? AsioLoopbackInputChannelOffset.HasValue
                : WaveLoopbackInputChannelOffset.HasValue;

        /// <summary>
        /// Resolves the requested sweep band into the allowed range, migrating
        /// pre-band settings (only an octave count, with the top pinned to
        /// Nyquist) — for the historical read-only octave count of 12 this lands
        /// at the 20 Hz–20 kHz default. <paramref name="sampleRate"/> is the
        /// already-normalized rate the sweep will run at.
        /// </summary>
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
                ConfirmEachAverageRun = measurement.ConfirmEachAverageRun,
                ProtectiveHighPassKind = measurement.ProtectiveHighPass.Kind,
                ProtectiveHighPassFrequencyHz = measurement.ProtectiveHighPass.FrequencyHz,
                ProtectiveHighPassSlopeDbPerOctave =
                    measurement.ProtectiveHighPass.SlopeDbPerOctave,
                SplCalibration = measurement.SplCalibration
            };

        public void ApplyTo(ExpSweepMeasurement measurement)
        {
            measurement.Init(BuildConfiguration());
            // Metadata, applied after Init (which does not touch it).
            measurement.SplCalibration = SplCalibration;
        }

        /// <summary>
        /// These settings as the measurement configuration they describe, with
        /// every value normalized against the devices actually present. Separate
        /// from <see cref="ApplyTo"/> for the callers that need the configuration
        /// without committing it — importing a recording resolves the sweep from it
        /// first and only applies it once the analysis has succeeded.
        /// </summary>
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
                    WasapiBufferMilliseconds: Clamp(WasapiBufferMilliseconds, 10, 100)),
                new SweepAveragingConfiguration(
                    Clamp(AverageRunCount, 1, 64),
                    ConfirmEachAverageRun),
                ProtectiveHighPassConfiguration.Normalize(
                    new ProtectiveHighPassConfiguration(
                        ProtectiveHighPassKind,
                        ProtectiveHighPassFrequencyHz,
                        ProtectiveHighPassSlopeDbPerOctave)));
        }
    }

    internal sealed class FrequencyResponseSettings
    {
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 256;
        public int RightTukeyWindow { get; set; } = 256;
        // Magnitude FDW ships with a Fixed default, so a file without these
        // fields keeps its meaning without a migration case.
        public PhaseWindowMode? MagnitudeWindowMode { get; set; } =
            Resonalyze.Dsp.PhaseWindowMode.Fixed;
        public int MagnitudeFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        // Which calibration this view corrects with, by id; null means none.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalibrationId { get; set; }
        // Legacy selections (schema <= 10): first a bare on/off flag, then the
        // three fixed modes. Read for migration only — deliberately nullable and
        // without an initializer, so "absent" stays distinguishable from a
        // stored value — and never written back.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseCalibration { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LegacyMicrophoneCalibrationMode? CalibrationMode { get; set; }
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
        public bool ShowMinimumPhaseGroupDelay { get; set; } = true;
        public bool ShowExcessGroupDelay { get; set; } = true;
        // Nullable and deliberately WITHOUT an initializer: System.Text.Json
        // never assigns a missing property, so an initializer value would
        // survive deserialization and a pre-Auto file (v <= 9, field absent)
        // would be indistinguishable from a stored true — see ApplyTo.
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
            options.MagnitudeWindowMode = MagnitudeWindowMode is { } magnitudeWindowMode &&
                Enum.IsDefined(magnitudeWindowMode)
                    ? magnitudeWindowMode
                    : Resonalyze.Dsp.PhaseWindowMode.Fixed;
            options.MagnitudeFdwCycles = MagnitudeFdwCycles is 4 or 6 or 8
                ? MagnitudeFdwCycles
                : PhaseAnalysisSettings.DefaultFdwCycles;
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

    // One PEQ filter of the persisted bank. Deliberately its own three numbers
    // rather than a Dsp PeqBand: a settings file is a format with defaults and
    // tolerance for missing fields, and PeqBand is the analysis type.
    internal sealed class PeqBandSettings
    {
        public double FrequencyHz { get; set; } = 1000;
        public double Q { get; set; } = 1;
        public double GainDb { get; set; }

        // Absent in a file written before shelves existed, which read back as the
        // bells they were.
        public PeqBandType Type { get; set; } = PeqBandType.Peaking;
    }

    // Self-contained EQ Wizard state — the mode derives nothing from overlays or
    // the current measurement: the isolated target curve, the filter
    // bank and its gain range, source smoothing and the microphone calibration
    // applied to the loaded IR. The loaded IR itself is not persisted.
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
        // The filter bank in slot order. The order is not decoration — it is what
        // an exported profile numbers its filters by — so it is stored as written
        // rather than re-derived on load. An empty list is a real state (a bank
        // the user cleared) and restores as one.
        //
        // Null only in a file written before the bank was persisted (schema 9 and
        // earlier). Such a file carries BandCount alone, and the bank is rebuilt
        // as that many ISO-spread filters — exactly what those versions showed.
        public List<PeqBandSettings>? Bands { get; set; }

        // The EQ preamp, part of the bank rather than of the target.
        public double PreampDb { get; set; }

        // How many filters the bank holds. Kept in step with Bands and read only
        // when Bands is absent (see above).
        public int BandCount { get; set; }
        public int SourceSmoothingInverseOctaves { get; set; }

        // The wizard's standing preference for impulse responses. Unlike the
        // measurement views it defaults to no correction, so a file that carries
        // neither the id nor the legacy mode restores as Off.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CalibrationId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LegacyMicrophoneCalibrationMode? CalibrationMode { get; set; }

        public string? ResolveCalibrationId() =>
            MeasurementSettingsFile.ResolveCalibrationId(
                CalibrationId,
                CalibrationMode,
                legacyUseCalibration: false);

        // The rate the fitted biquads are realized at when the source does not state one
        // (a foreign text curve). A source that knows its own rate overrides this without
        // changing it, so the manual choice survives loading such a source.
        public int ManualSampleRateHz { get; set; } = 48_000;

        // Auto Tune only cuts, never boosts. The safe default for a car tune; see
        // EqAutoTuner.Options.CutsOnlyMode.
        public bool CutsOnly { get; set; } = true;
    }

    internal sealed class ImpulseResponseSettings
    {
        public int Length { get; set; } = 4096;

        /// <summary>
        /// The pre-scale-selector setting: a plain "logarithmic amplitude" flag. It
        /// is read only when <see cref="AmplitudeScale"/> is absent, so a file
        /// written by an older build still opens the view the way its user left it,
        /// and it is still WRITTEN (mirroring the scale) so a file written here
        /// stays loadable by an older build, which cannot parse a null into its
        /// non-nullable flag.
        /// </summary>
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
            // Anything outside the offered widths reads as "no band filter" rather than
            // as an arbitrary one: a hand-edited width is not a band anyone chose.
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
        // Null when the file predates the explicit analysis mode; ApplyTo infers it
        // from what used to imply an RTA session (see there).
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LiveAnalysisMode? AnalysisMode { get; set; }
        public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;
        public bool CompensateNoiseTilt { get; set; }
        // See FrequencyResponseSettings for the id and the two legacy fields.
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
            // Settings written before the explicit mode existed: the SPL scale and
            // the Silent signal were both RTA-exclusive back then, so either marks
            // an RTA session; everything else was the transfer analyzer.
            options.AnalysisMode = AnalysisMode is { } mode && Enum.IsDefined(mode)
                ? mode
                : MagnitudeScale == MagnitudeScale.SoundPressureLevel ||
                    options.NoiseColor == NoiseColor.Silent
                    ? LiveAnalysisMode.Rta
                    : LiveAnalysisMode.TransferFunction;
            // The invariant the panel and the controller both rely on: Silent exists
            // only in RTA mode (a transfer function has nothing to correlate against
            // without an excitation). Only a hand-edited file can break it — repair
            // the signal, keeping the explicitly stated mode.
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
}
