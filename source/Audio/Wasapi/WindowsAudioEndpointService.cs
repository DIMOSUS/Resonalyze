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
            // Do not explicitly dispose MMDevice wrappers here. Core Audio can
            // return the same COM identity from a later GetDevice call; forcing
            // ReleaseComObject through MMDevice.Dispose can leave the cached RCW
            // disconnected and WasapiOut then fails to query IMMDevice with
            // E_NOINTERFACE. The short-lived wrappers are released by the CLR.
            NAudio.Wave.WaveFormat mixFormat;
            try
            {
                mixFormat = endpoint.AudioClient.MixFormat;
            }
            catch
            {
                mixFormat = new NAudio.Wave.WaveFormat(44_100, 16, 1);
            }
            endpoints.Add(new AudioEndpointInfo(
                endpoint.ID,
                endpoint.FriendlyName,
                direction,
                endpoint.State,
                mixFormat,
                endpoint.State == DeviceState.Active ? mixFormat.Channels : 0,
                endpoint.ID == defaultId));
        }
        return endpoints;
    }

    private string? TryGetDefaultEndpointId(DataFlow direction)
    {
        try
        {
            MMDevice endpoint = enumerator.GetDefaultAudioEndpoint(
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
