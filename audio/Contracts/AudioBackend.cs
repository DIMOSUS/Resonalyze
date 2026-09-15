namespace Resonalyze.Audio;

/// <summary>Persisted in settings and measurement files: values must not change.</summary>
public enum AudioBackend
{
    Wave = 0,
    Asio = 1,
    WasapiShared = 2,
    WasapiExclusive = 3
}

public static class AudioBackendExtensions
{
    public static bool IsWasapi(this AudioBackend backend) =>
        backend is AudioBackend.WasapiShared or AudioBackend.WasapiExclusive;
}

[Flags]
public enum AudioBackendCapabilities
{
    None = 0,
    StableEndpointIds = 1 << 0,
    MultiChannelInput = 1 << 1,
    ExclusiveAccess = 1 << 2,
    DriverControlPanel = 1 << 3,
    PersistentSession = 1 << 4
}

public sealed record AudioBackendDescriptor(
    AudioBackend Id,
    string DisplayName,
    AudioBackendCapabilities Capabilities)
{
    /// <remarks>Reserve API: no caller in the solution today (see AGENTS.md).</remarks>
    public bool Supports(AudioBackendCapabilities capability) =>
        (Capabilities & capability) == capability;
}
