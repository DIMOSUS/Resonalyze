namespace Resonalyze.Options;

/// <summary>The hardware behind Record Settings. Watches WASAPI endpoints for as long as it lives.</summary>
internal sealed class SystemRecordDevices : IRecordDevices
{
    private WindowsAudioEndpointService? endpointService;

    public SystemRecordDevices()
    {
        try
        {
            endpointService = new WindowsAudioEndpointService();
            endpointService.EndpointsChanged += OnEndpointsChanged;
        }
        catch
        {
            endpointService = null;
        }
    }

    public event Action? EndpointsChanged;

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => AudioDeviceCatalog.GetPlaybackDevices();

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => AudioDeviceCatalog.GetRecordingDevices();

    public IReadOnlyList<int> GetSupportedWaveSampleRates(
        int playbackDeviceNumber,
        int recordingDeviceNumber,
        int playbackChannelCount,
        int recordingChannelCount,
        int bitsPerSample) =>
        AudioDeviceCatalog.GetSupportedWaveSampleRates(
            playbackDeviceNumber,
            recordingDeviceNumber,
            playbackChannelCount,
            recordingChannelCount,
            bitsPerSample);

    public (IReadOnlyList<AudioEndpointDescriptor> Capture, IReadOnlyList<AudioEndpointDescriptor> Render) GetEndpoints()
    {
        try
        {
            if (endpointService != null)
            {
                return (endpointService.GetCaptureEndpoints(), endpointService.GetRenderEndpoints());
            }

            using var temporaryService = new WindowsAudioEndpointService();
            return (temporaryService.GetCaptureEndpoints(), temporaryService.GetRenderEndpoints());
        }
        catch
        {
            return (Array.Empty<AudioEndpointDescriptor>(), Array.Empty<AudioEndpointDescriptor>());
        }
    }

    public IRecordEndpointReader OpenEndpoints() => new EndpointReader();

    public bool IsExclusiveFormatSupported(
        string captureEndpointId,
        string renderEndpointId,
        int sampleRate,
        int bits,
        int captureChannels,
        int renderChannels)
    {
        try
        {
            return WasapiFormatSupport.CheckExclusive(
                captureEndpointId,
                renderEndpointId,
                sampleRate,
                bits,
                captureChannels,
                renderChannels).Supported;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<AsioDeviceInfo> GetAsioDrivers() => AsioDeviceCatalog.GetDrivers();

    public AsioDriverInfo GetAsioDriverInfo(string? driverName, int sampleRate) =>
        AsioDeviceCatalog.GetDriverInfo(driverName, sampleRate);

    public void ShowAsioControlPanel(string driverName) => AsioDeviceCatalog.ShowControlPanel(driverName);

    public Task<IReadOnlyList<AsioInputProbeChannelResult>> ProbeAsioInputsAsync(
        string driverName,
        int sampleRate,
        int outputChannelOffset,
        CancellationToken cancellationToken) =>
        AsioInputProbe.CaptureAsync(
            driverName,
            sampleRate,
            outputChannelOffset,
            milliseconds: 1000,
            cancellationToken);

    public void Dispose()
    {
        if (endpointService == null)
        {
            return;
        }

        endpointService.EndpointsChanged -= OnEndpointsChanged;
        endpointService.Dispose();
        endpointService = null;
    }

    private void OnEndpointsChanged() => EndpointsChanged?.Invoke();

    private sealed class EndpointReader : IRecordEndpointReader
    {
        private readonly WindowsAudioEndpointService service = new();

        public IReadOnlyList<AudioEndpointDescriptor> GetCaptureEndpoints() => service.GetCaptureEndpoints();

        public IReadOnlyList<AudioEndpointDescriptor> GetRenderEndpoints() => service.GetRenderEndpoints();

        public void Dispose() => service.Dispose();
    }
}
