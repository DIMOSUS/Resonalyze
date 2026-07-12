using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze.App.Tests;

public sealed class AudioEndpointInfoTests
{
    [Fact]
    public void EndpointServiceRegistersAndUnregistersNotifications()
    {
        var service = new WindowsAudioEndpointService();

        Assert.NotNull(service.GetCaptureEndpoints());
        Assert.NotNull(service.GetRenderEndpoints());
        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void DisplayNameMarksDefaultActiveEndpoint()
    {
        var endpoint = new AudioEndpointInfo(
            "id",
            "USB Audio",
            DataFlow.Capture,
            DeviceState.Active,
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 8),
            8,
            true);

        Assert.Equal("USB Audio (Default)", endpoint.ToString());
        Assert.True(endpoint.IsAvailable);
    }

    [Fact]
    public void DisplayNameMarksUnavailableEndpoint()
    {
        var endpoint = new AudioEndpointInfo(
            "missing-id",
            "USB Audio",
            DataFlow.Capture,
            DeviceState.NotPresent,
            new WaveFormat(44_100, 16, 1),
            0,
            false);

        Assert.Equal("[Unavailable] USB Audio", endpoint.ToString());
        Assert.False(endpoint.IsAvailable);
    }
}
