using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>An in-memory machine for Record Settings: device lists, rates and ASIO drivers are plain data a test edits.</summary>
internal sealed class FakeRecordDevices : IRecordDevices
{
    public List<AudioDeviceInfo> Playback { get; } =
    [
        new AudioDeviceInfo(-1, "Default playback device"),
        new AudioDeviceInfo(0, "Speakers", 2),
    ];

    public List<AudioDeviceInfo> Recording { get; } =
    [
        new AudioDeviceInfo(-1, "Default recording device"),
        new AudioDeviceInfo(0, "Mono mic", 1),
        new AudioDeviceInfo(1, "Line in", 2),
    ];

    public List<AudioEndpointDescriptor> Capture { get; } =
    [
        Endpoint("{capture}", "Interface in", AudioEndpointDirection.Capture, 48_000, 8, isDefault: true),
    ];

    public List<AudioEndpointDescriptor> Render { get; } =
    [
        Endpoint("{render}", "Interface out", AudioEndpointDirection.Render, 48_000, 2, isDefault: true),
    ];

    public Dictionary<string, AsioDriverInfo> AsioDrivers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rates every Wave pair and every Exclusive format opens at.</summary>
    public List<int> Rates { get; } = [44_100, 48_000, 96_000];

    public List<string> ControlPanels { get; } = [];

    public int AsioOpenCount { get; private set; }

    public int ExclusiveChecks { get; private set; }

    public bool Disposed { get; private set; }

    public event Action? EndpointsChanged;

    public static AudioEndpointDescriptor Endpoint(
        string id,
        string name,
        AudioEndpointDirection direction,
        int rate,
        int channels,
        bool isDefault = false) =>
        new(id, name, direction, new AudioFormat(rate, 24, channels, AudioSampleEncoding.Pcm), channels, true, isDefault);

    public static AsioDriverInfo Asio(string name, int inputs, int outputs, params int[] rates) =>
        new(
            name,
            Enumerable.Range(0, inputs).Select(index => new AsioChannelInfo(index, $"In {index + 1}")).ToArray(),
            Enumerable.Range(0, outputs).Select(index => new AsioChannelInfo(index, $"Out {index + 1}")).ToArray(),
            128,
            256,
            false,
            rates,
            null);

    public void RaiseEndpointsChanged() => EndpointsChanged?.Invoke();

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => Playback.ToArray();

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => Recording.ToArray();

    public IReadOnlyList<int> GetSupportedWaveSampleRates(
        int playbackDeviceNumber,
        int recordingDeviceNumber,
        int playbackChannelCount,
        int recordingChannelCount,
        int bitsPerSample)
    {
        AudioDeviceInfo? input = Recording.FirstOrDefault(device => device.DeviceNumber == recordingDeviceNumber);
        bool known = Playback.Any(device => device.DeviceNumber == playbackDeviceNumber) && input != null;
        bool wideEnough = input != null && (input.DeviceNumber < 0 || recordingChannelCount <= input.Channels);
        return known && wideEnough ? Rates.ToArray() : [];
    }

    public (IReadOnlyList<AudioEndpointDescriptor> Capture, IReadOnlyList<AudioEndpointDescriptor> Render) GetEndpoints() =>
        (Capture.ToArray(), Render.ToArray());

    public IRecordEndpointReader OpenEndpoints() => new Reader(this);

    public bool IsExclusiveFormatSupported(
        string captureEndpointId,
        string renderEndpointId,
        int sampleRate,
        int bits,
        int captureChannels,
        int renderChannels)
    {
        ExclusiveChecks++;
        return renderChannels == 2 && Rates.Contains(sampleRate);
    }

    public IReadOnlyList<AsioDeviceInfo> GetAsioDrivers() =>
        AsioDrivers.Keys.Select(name => new AsioDeviceInfo(name)).ToArray();

    public AsioDriverInfo GetAsioDriverInfo(string? driverName, int sampleRate)
    {
        AsioOpenCount++;
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return AsioDeviceCatalog.EmptyDriverInfo with { ErrorMessage = "ASIO driver is not selected." };
        }

        return AsioDrivers.TryGetValue(driverName, out AsioDriverInfo? driver)
            ? driver with { SupportsSampleRate = driver.SupportedSampleRates.Contains(sampleRate) }
            : AsioDeviceCatalog.EmptyDriverInfo with { DriverName = driverName, ErrorMessage = "Not installed." };
    }

    public void ShowAsioControlPanel(string driverName) => ControlPanels.Add(driverName);

    public Task<IReadOnlyList<AsioInputProbeChannelResult>> ProbeAsioInputsAsync(
        string driverName,
        int sampleRate,
        int outputChannelOffset,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AsioInputProbeChannelResult>>(
            [new AsioInputProbeChannelResult(0, "In 1", -12, -20, 1)]);

    public void Dispose() => Disposed = true;

    private sealed class Reader(FakeRecordDevices devices) : IRecordEndpointReader
    {
        public IReadOnlyList<AudioEndpointDescriptor> GetCaptureEndpoints() => devices.Capture.ToArray();

        public IReadOnlyList<AudioEndpointDescriptor> GetRenderEndpoints() => devices.Render.ToArray();

        public void Dispose()
        {
        }
    }
}
