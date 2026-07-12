using NAudio.CoreAudioApi;

namespace Resonalyze;

public sealed class WindowsAudioEndpointService : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();

    public IReadOnlyList<AudioEndpointInfo> GetCaptureEndpoints() => GetEndpoints(DataFlow.Capture);

    public IReadOnlyList<AudioEndpointInfo> GetRenderEndpoints() => GetEndpoints(DataFlow.Render);

    private IReadOnlyList<AudioEndpointInfo> GetEndpoints(DataFlow direction)
    {
        string? defaultId = TryGetDefaultEndpointId(direction);
        return enumerator
            .EnumerateAudioEndPoints(direction, DeviceState.Active | DeviceState.Unplugged)
            .Select(endpoint => new AudioEndpointInfo(
                endpoint.ID,
                endpoint.FriendlyName,
                direction,
                endpoint.State,
                endpoint.AudioClient.MixFormat,
                endpoint.AudioClient.MixFormat.Channels,
                endpoint.ID == defaultId))
            .ToArray();
    }

    private string? TryGetDefaultEndpointId(DataFlow direction)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(direction, Role.Multimedia).ID;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => enumerator.Dispose();
}
