using Resonalyze.Audio;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class ArrayInputSourcesTests
{
    [Theory]
    [InlineData(AudioBackend.Asio, 10, "ASIO driver inputs")]
    [InlineData(AudioBackend.Wave, 2, "MME is limited to two channels")]
    [InlineData(AudioBackend.WasapiExclusive, 8, "WASAPI endpoint channels")]
    public void TheInputSourceIsNamedByBackend(
        AudioBackend backend, int channels, string expected) =>
        Assert.Equal(expected, ArrayInputSources.Describe(backend, channels));

    [Fact]
    public void AStereoWasapiEndpointIsToldWhereTheFurtherInputsAre() =>
        Assert.Equal(
            "WASAPI endpoint channels; use ASIO to reach an interface's further inputs",
            ArrayInputSources.Describe(AudioBackend.WasapiShared, 2));
}
