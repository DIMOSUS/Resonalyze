using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

public sealed class UnavailableEndpointTests
{
    [Fact]
    public void MissingEndpointPlaceholderPreservesIdAndDisplayName()
    {
        AudioEndpointDescriptor endpoint = Options.MeasurementOptions.CreateUnavailableEndpoint(
            "endpoint-id",
            "Focusrite USB",
            AudioEndpointDirection.Capture);

        Assert.Equal("endpoint-id", endpoint.Id);
        Assert.Equal("Focusrite USB", endpoint.DisplayName);
        Assert.Equal("[Unavailable] Focusrite USB", endpoint.ToString());
    }
}
