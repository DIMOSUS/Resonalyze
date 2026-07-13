using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Resonalyze;

public sealed class WindowsAudioEndpointService : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly EndpointNotificationClient notificationClient;
    private bool disposed;

    public WindowsAudioEndpointService()
    {
        notificationClient = new EndpointNotificationClient(this);
        enumerator.RegisterEndpointNotificationCallback(notificationClient);
    }

    public event Action? EndpointsChanged;
    public event Action<DataFlow, Role, string?>? DefaultEndpointChanged;

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

    private void NotifyEndpointsChanged() => EndpointsChanged?.Invoke();

    private void NotifyDefaultEndpointChanged(DataFlow flow, Role role, string? endpointId)
    {
        DefaultEndpointChanged?.Invoke(flow, role, endpointId);
        EndpointsChanged?.Invoke();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        enumerator.UnregisterEndpointNotificationCallback(notificationClient);
        enumerator.Dispose();
    }

    [ComVisible(true)]
    private sealed class EndpointNotificationClient : IMMNotificationClient
    {
        private readonly WeakReference<WindowsAudioEndpointService> owner;

        public EndpointNotificationClient(WindowsAudioEndpointService owner)
        {
            this.owner = new WeakReference<WindowsAudioEndpointService>(owner);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            NotifyChanged();

        public void OnDeviceAdded(string pwstrDeviceId) => NotifyChanged();

        public void OnDeviceRemoved(string deviceId) => NotifyChanged();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (owner.TryGetTarget(out WindowsAudioEndpointService? service))
            {
                service.NotifyDefaultEndpointChanged(flow, role, defaultDeviceId);
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) =>
            NotifyChanged();

        private void NotifyChanged()
        {
            if (owner.TryGetTarget(out WindowsAudioEndpointService? service))
            {
                service.NotifyEndpointsChanged();
            }
        }
    }
}
