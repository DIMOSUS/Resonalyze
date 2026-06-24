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

            return settings;
        }
        catch
        {
            return new MeasurementSettingsFile();
        }
    }

    public void Save()
    {
        SchemaVersion = CurrentSchemaVersion;
        using FileStream stream = File.Create(PathOnDisk);
        JsonSerializer.Serialize(stream, this, SerializerOptions);
    }

    public void ApplyTo(
        ExpSweepMeasurement measurement,
        FrequencyResponseOptions frequencyResponse,
        FrequencyResponseOptions phaseResponse,
        FrequencyResponseOptions groupDelay,
        ImpulseResponseOptions impulseResponse,
        WaterfallGenerateOptions waterfall,
        WaterfallGenerateOptions burstDecay,
        LiveSpectrumOptions liveSpectrum,
        TimeAlignmentOptions timeAlignment)
    {
        Measurement.ApplyTo(measurement);
        FrequencyResponse.ApplyTo(frequencyResponse);
        PhaseResponse.ApplyTo(phaseResponse);
        GroupDelay.ApplyTo(groupDelay);
        ImpulseResponse.ApplyTo(impulseResponse);
        Waterfall.ApplyTo(waterfall, WaterfallMode.Fourier);
        BurstDecay.ApplyTo(burstDecay, WaterfallMode.BurstDecay);
        LiveSpectrum.ApplyTo(liveSpectrum);
        TimeAlignment.ApplyTo(timeAlignment, measurement.SampleRate);
    }

    public void CaptureFrom(
        ExpSweepMeasurement measurement,
        FrequencyResponseOptions frequencyResponse,
        FrequencyResponseOptions phaseResponse,
        FrequencyResponseOptions groupDelay,
        ImpulseResponseOptions impulseResponse,
        WaterfallGenerateOptions waterfall,
        WaterfallGenerateOptions burstDecay,
        LiveSpectrumOptions liveSpectrum,
        TimeAlignmentOptions timeAlignment)
    {
        SchemaVersion = CurrentSchemaVersion;
        Measurement = SweepMeasurementSettings.Capture(measurement);
        FrequencyResponse = FrequencyResponseSettings.Capture(frequencyResponse);
        PhaseResponse = FrequencyResponseSettings.Capture(phaseResponse);
        GroupDelay = FrequencyResponseSettings.Capture(groupDelay);
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
        public string? AsioDriverName { get; set; }
        public int WaveInputChannelOffset { get; set; }
        public int? WaveLoopbackInputChannelOffset { get; set; }
        public int AsioInputChannelOffset { get; set; }
        public int? AsioLoopbackInputChannelOffset { get; set; }
        public int AsioOutputChannelOffset { get; set; }

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
                AsioDriverName = measurement.AsioDriverName,
                WaveInputChannelOffset = measurement.WaveInputChannelOffset,
                WaveLoopbackInputChannelOffset = measurement.WaveLoopbackInputChannelOffset,
                AsioInputChannelOffset = measurement.AsioInputChannelOffset,
                AsioLoopbackInputChannelOffset = measurement.AsioLoopbackInputChannelOffset,
                AsioOutputChannelOffset = measurement.AsioOutputChannelOffset
            };

        public void ApplyTo(ExpSweepMeasurement measurement)
        {
            measurement.Init(
                Clamp(Octaves, 1, 32),
                Clamp(SampleRate, 44_100, 384_000),
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
                NormalizeAudioBackend(AudioBackend, AsioDriverName),
                NormalizeAsioDriverName(AsioDriverName),
                NormalizeAsioChannelOffset(
                    AsioDriverName,
                    Clamp(SampleRate, 44_100, 384_000),
                    AsioInputChannelOffset,
                    input: true),
                NormalizeAsioChannelOffset(
                    AsioDriverName,
                    Clamp(SampleRate, 44_100, 384_000),
                    AsioOutputChannelOffset,
                    input: false),
                NormalizeWaveChannelOffset(WaveInputChannelOffset),
                NormalizeOptionalWaveChannelOffset(WaveLoopbackInputChannelOffset),
                NormalizeOptionalAsioChannelOffset(
                    AsioDriverName,
                    Clamp(SampleRate, 44_100, 384_000),
                    AsioLoopbackInputChannelOffset));
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

        public static FrequencyResponseSettings Capture(
            FrequencyResponseOptions options) =>
            new()
            {
                Window = options.Window,
                LeftTukeyWindow = options.LeftTukeyWindow,
                RightTukeyWindow = options.RightTukeyWindow,
                SmoothingInverseOctaves = options.SmoothingInverseOctaves,
                Offset = options.Offset,
                Unwrap = options.Unwrap,
                UseCalibration = options.UseCalibration
            };

        public void ApplyTo(FrequencyResponseOptions options)
        {
            int window = Clamp(Window, 32, 32768);
            options.Window = window;
            options.LeftTukeyWindow = Clamp(LeftTukeyWindow, 0, window / 2);
            options.RightTukeyWindow = Clamp(RightTukeyWindow, 0, window / 2);
            options.SmoothingInverseOctaves =
                Math.Clamp(SmoothingInverseOctaves, 1.0, 96.0);
            options.Offset = Clamp(Offset, -32768, 32768);
            options.Unwrap = Unwrap;
            options.UseCalibration = UseCalibration;
        }
    }

    internal sealed class ImpulseResponseSettings
    {
        public int Length { get; set; } = 4096;
        public bool Logarithmic { get; set; }

        public static ImpulseResponseSettings Capture(
            ImpulseResponseOptions options) =>
            new()
            {
                Length = options.Length,
                Logarithmic = options.Logarithmic
            };

        public void ApplyTo(ImpulseResponseOptions options)
        {
            options.Length = Clamp(Length, 1, 262144);
            options.Logarithmic = Logarithmic;
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
            options.LeftTukeyWindow = Clamp(LeftTukeyWindow, 0, window / 2);
            options.RightTukeyWindow = Clamp(RightTukeyWindow, 0, window / 2);
            options.DbRange = Clamp(DbRange, -140, -10);
            options.SmoothingInverseOctaves =
                Math.Clamp(SmoothingInverseOctaves, 1.0, 96.0);
            options.Offset = Clamp(Offset, -32768, 32768);
            options.WaterfallMode = requiredMode;
            options.Periods = Math.Clamp(Periods, 1.0, 60.0);
        }
    }

    internal sealed class LiveSpectrumSettings
    {
        public LiveSpectrumMode Mode { get; set; } = LiveSpectrumMode.TransferFunction;
        public bool UseCalibration { get; set; } = true;
        public int SequenceLength { get; set; } = 2048;

        public static LiveSpectrumSettings Capture(
            LiveSpectrumOptions options) =>
            new()
            {
                Mode = Enum.IsDefined(options.Mode)
                    ? options.Mode
                    : LiveSpectrumMode.TransferFunction,
                UseCalibration = options.UseCalibration,
                SequenceLength = NormalizeSequenceLength(options.SequenceLength)
            };

        public void ApplyTo(LiveSpectrumOptions options)
        {
            options.Mode = Enum.IsDefined(Mode)
                ? Mode
                : LiveSpectrumMode.TransferFunction;
            options.UseCalibration = UseCalibration;
            options.SequenceLength = NormalizeSequenceLength(SequenceLength);
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

    private static int? NormalizeOptionalAsioChannelOffset(
        string? asioDriverName,
        int sampleRate,
        int? offset) =>
        offset.HasValue
            ? NormalizeAsioChannelOffset(asioDriverName, sampleRate, offset.Value, input: true)
            : null;

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);
}
