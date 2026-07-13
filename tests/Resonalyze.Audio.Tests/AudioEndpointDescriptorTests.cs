namespace Resonalyze.Audio.Tests;

public sealed class AudioEndpointDescriptorTests
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
        var endpoint = new AudioEndpointDescriptor(
            "id",
            "USB Audio",
            AudioEndpointDirection.Capture,
            new AudioFormat(48_000, 32, 8, AudioSampleEncoding.IeeeFloat),
            8,
            IsAvailable: true,
            IsDefault: true);

        Assert.Equal("USB Audio (Default)", endpoint.ToString());
        Assert.True(endpoint.IsAvailable);
    }

    [Fact]
    public void DisplayNameMarksUnavailableEndpoint()
    {
        var endpoint = new AudioEndpointDescriptor(
            "missing-id",
            "USB Audio",
            AudioEndpointDirection.Capture,
            new AudioFormat(44_100, 16, 1, AudioSampleEncoding.Pcm),
            0,
            IsAvailable: false,
            IsDefault: false);

        Assert.Equal("[Unavailable] USB Audio", endpoint.ToString());
        Assert.False(endpoint.IsAvailable);
    }
}
