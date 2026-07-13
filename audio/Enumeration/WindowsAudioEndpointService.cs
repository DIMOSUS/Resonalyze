using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Resonalyze.Audio;

/// <summary>
/// Enumerates WASAPI capture/render endpoints and watches for hot-plug changes,
/// exposing everything through the neutral <see cref="AudioEndpointDescriptor"/>
/// so no NAudio Core Audio type crosses the boundary.
/// </summary>
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

    /// <summary>Raised on any endpoint add/remove/state/default change.</summary>
    public event Action? EndpointsChanged;

    public IReadOnlyList<AudioEndpointDescriptor> GetCaptureEndpoints() =>
        GetEndpoints(DataFlow.Capture, AudioEndpointDirection.Capture);

    public IReadOnlyList<AudioEndpointDescriptor> GetRenderEndpoints() =>
        GetEndpoints(DataFlow.Render, AudioEndpointDirection.Render);

    private IReadOnlyList<AudioEndpointDescriptor> GetEndpoints(
        DataFlow flow,
        AudioEndpointDirection direction)
    {
        string? defaultId = TryGetDefaultEndpointId(flow);
        var endpoints = new List<AudioEndpointDescriptor>();
        foreach (MMDevice endpoint in enumerator.EnumerateAudioEndPoints(
            flow,
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
            bool available = endpoint.State == DeviceState.Active;
            endpoints.Add(new AudioEndpointDescriptor(
                endpoint.ID,
                endpoint.FriendlyName,
                direction,
                AudioFormatConversions.ToAudioFormat(mixFormat),
                available ? mixFormat.Channels : 0,
                available,
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

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
            NotifyChanged();

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
