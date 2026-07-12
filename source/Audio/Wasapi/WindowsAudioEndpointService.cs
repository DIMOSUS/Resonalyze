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
        var endpoints = new List<AudioEndpointInfo>();
        foreach (MMDevice endpoint in enumerator.EnumerateAudioEndPoints(
            direction,
            DeviceState.Active | DeviceState.Unplugged))
        {
            using (endpoint)
            {
                endpoints.Add(new AudioEndpointInfo(
                endpoint.ID,
                endpoint.FriendlyName,
                direction,
                endpoint.State,
                endpoint.AudioClient.MixFormat,
                endpoint.AudioClient.MixFormat.Channels,
                    endpoint.ID == defaultId));
            }
        }
        return endpoints;
    }

    private string? TryGetDefaultEndpointId(DataFlow direction)
    {
        try
        {
            using MMDevice endpoint = enumerator.GetDefaultAudioEndpoint(
                direction,
                Role.Multimedia);
            return endpoint.ID;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => enumerator.Dispose();
}
