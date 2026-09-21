namespace Resonalyze.Options;

/// <summary>The audio hardware as Record Settings reads it; <see cref="SystemRecordDevices"/> asks the machine.</summary>
internal interface IRecordDevices : IDisposable
{
    /// <summary>A WASAPI endpoint came or went; raised on whatever thread Core Audio notifies on.</summary>
    event Action? EndpointsChanged;

    IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices();

    IReadOnlyList<AudioDeviceInfo> GetRecordingDevices();

    IReadOnlyList<int> GetSupportedWaveSampleRates(
        int playbackDeviceNumber,
        int recordingDeviceNumber,
        int playbackChannelCount,
        int recordingChannelCount,
        int bitsPerSample);

    (IReadOnlyList<AudioEndpointDescriptor> Capture, IReadOnlyList<AudioEndpointDescriptor> Render) GetEndpoints();

    /// <summary>A fresh enumeration for Apply, which must not trust the monitored snapshot; failures propagate.</summary>
    IRecordEndpointReader OpenEndpoints();

    bool IsExclusiveFormatSupported(
        string captureEndpointId,
        string renderEndpointId,
        int sampleRate,
        int bits,
        int captureChannels,
        int renderChannels);

    IReadOnlyList<AsioDeviceInfo> GetAsioDrivers();

    AsioDriverInfo GetAsioDriverInfo(string? driverName, int sampleRate);

    void ShowAsioControlPanel(string driverName);

    Task<IReadOnlyList<AsioInputProbeChannelResult>> ProbeAsioInputsAsync(
        string driverName,
        int sampleRate,
        int outputChannelOffset,
        CancellationToken cancellationToken);
}

internal interface IRecordEndpointReader : IDisposable
{
    IReadOnlyList<AudioEndpointDescriptor> GetCaptureEndpoints();

    IReadOnlyList<AudioEndpointDescriptor> GetRenderEndpoints();
}
