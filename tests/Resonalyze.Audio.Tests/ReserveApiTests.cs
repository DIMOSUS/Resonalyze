namespace Resonalyze.Audio.Tests;

/// <summary>
/// Members kept as a deliberate reserve (see AGENTS.md): nothing in the app
/// calls them yet, so these tests are their only consumer. Without a consumer a
/// reserve member compiles forever without anyone noticing that its behaviour
/// drifted or that its signature no longer fits the callers it was kept for.
/// </summary>
public sealed class ReserveApiTests
{
    [Theory]
    [InlineData(AudioBackendCapabilities.MultiChannelInput, true)]
    [InlineData(AudioBackendCapabilities.StableEndpointIds, true)]
    [InlineData(
        AudioBackendCapabilities.MultiChannelInput | AudioBackendCapabilities.StableEndpointIds,
        true)]
    [InlineData(AudioBackendCapabilities.ExclusiveAccess, false)]
    [InlineData(
        AudioBackendCapabilities.MultiChannelInput | AudioBackendCapabilities.ExclusiveAccess,
        false)]
    [InlineData(AudioBackendCapabilities.None, true)]
    public void Supports_RequiresEveryRequestedFlag(
        AudioBackendCapabilities requested,
        bool expected)
    {
        // A combined request is all-of, not any-of: asking for
        // MultiChannelInput | ExclusiveAccess of a shared-mode backend is false
        // even though it does have multi-channel input.
        var descriptor = new AudioBackendDescriptor(
            AudioBackend.WasapiShared,
            "WASAPI Shared",
            AudioBackendCapabilities.StableEndpointIds |
                AudioBackendCapabilities.MultiChannelInput);

        Assert.Equal(expected, descriptor.Supports(requested));
    }

    [Theory]
    [InlineData("Loopback 1", true)]
    [InlineData("loopback", true)]
    [InlineData("LOOPBACK L", true)]
    [InlineData("Loop Back R", true)]
    [InlineData("loop back", true)]
    [InlineData("Analog In 1", false)]
    [InlineData("Loop", false)]
    [InlineData("", false)]
    public void IsLoopbackChannel_MatchesEitherSpellingCaseInsensitively(
        string name,
        bool expected)
    {
        var channel = new AsioChannelInfo(0, name);

        Assert.Equal(expected, AsioDeviceCatalog.IsLoopbackChannel(channel));
    }
}
