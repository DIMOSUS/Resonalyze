using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class MeasurementSettingsFile
{
    private const int CurrentSchemaVersion = 3;
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
        WaterfallGenerateOptions burstDecay)
    {
        Measurement.ApplyTo(measurement);
        FrequencyResponse.ApplyTo(frequencyResponse);
        PhaseResponse.ApplyTo(phaseResponse);
        GroupDelay.ApplyTo(groupDelay);
        ImpulseResponse.ApplyTo(impulseResponse);
        Waterfall.ApplyTo(waterfall, WaterfallMode.Fourier);
        BurstDecay.ApplyTo(burstDecay, WaterfallMode.BurstDecay);
    }

    public void CaptureFrom(
        ExpSweepMeasurement measurement,
        FrequencyResponseOptions frequencyResponse,
        FrequencyResponseOptions phaseResponse,
        FrequencyResponseOptions groupDelay,
        ImpulseResponseOptions impulseResponse,
        WaterfallGenerateOptions waterfall,
        WaterfallGenerateOptions burstDecay)
    {
        SchemaVersion = CurrentSchemaVersion;
        Measurement = SweepMeasurementSettings.Capture(measurement);
        FrequencyResponse = FrequencyResponseSettings.Capture(frequencyResponse);
        PhaseResponse = FrequencyResponseSettings.Capture(phaseResponse);
        GroupDelay = FrequencyResponseSettings.Capture(groupDelay);
        ImpulseResponse = ImpulseResponseSettings.Capture(impulseResponse);
        Waterfall = WaterfallSettings.Capture(waterfall);
        BurstDecay = WaterfallSettings.Capture(burstDecay);
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
        public int AsioInputChannelOffset { get; set; }
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
                AsioInputChannelOffset = measurement.AsioInputChannelOffset,
                AsioOutputChannelOffset = measurement.AsioOutputChannelOffset
            };

        public void ApplyTo(ExpSweepMeasurement measurement)
        {
            measurement.Init(
                Clamp(Octaves, 1, 32),
                Clamp(SampleRate, 8000, 384000),
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
                NormalizeAsioChannelOffset(AsioDriverName, AsioInputChannelOffset, input: true),
                NormalizeAsioChannelOffset(AsioDriverName, AsioOutputChannelOffset, input: false));
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
        int offset,
        bool input)
    {
        AsioDriverInfo driverInfo = AsioDeviceCatalog.GetDriverInfo(
            NormalizeAsioDriverName(asioDriverName),
            sampleRate: 44100);
        IReadOnlyList<AsioChannelInfo> channels = input
            ? driverInfo.InputChannels
            : driverInfo.OutputChannels;

        return channels.Any(channel => channel.Offset == offset)
            ? offset
            : 0;
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);
}
