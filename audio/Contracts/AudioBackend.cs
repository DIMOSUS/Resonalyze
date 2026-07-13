namespace Resonalyze.Audio;

/// <summary>
/// The persisted identifier of an audio backend. The numeric values are part of
/// the on-disk settings/measurement file formats (serialized by name via
/// <c>JsonStringEnumConverter</c>) and must not change. The registry maps this
/// identifier to a concrete <see cref="IAudioBackend"/> implementation, so the
/// value doubles as a stable id — no parallel "backend id" enum is needed.
/// </summary>
public enum AudioBackend
{
    Wave = 0,
    Asio = 1,
    WasapiShared = 2,
    WasapiExclusive = 3
}

/// <summary>
/// What a backend can do, so the UI (and callers) can ask about a capability
/// instead of comparing against specific <see cref="AudioBackend"/> values.
/// </summary>
[Flags]
public enum AudioBackendCapabilities
{
    None = 0,
    /// <summary>Endpoint identifiers are stable across sessions (WASAPI, ASIO by name).</summary>
    StableEndpointIds = 1 << 0,
    /// <summary>Arbitrary multi-channel input routing (WASAPI, ASIO).</summary>
    MultiChannelInput = 1 << 1,
    /// <summary>Opens the device with exclusive access (WASAPI Exclusive).</summary>
    ExclusiveAccess = 1 << 2,
    /// <summary>Exposes a driver control panel (ASIO).</summary>
    DriverControlPanel = 1 << 3,
    /// <summary>Keeps the device open and reuses it across measurement runs.</summary>
    PersistentSession = 1 << 4
}

/// <summary>Static, user-facing description of a backend.</summary>
public sealed record AudioBackendDescriptor(
    AudioBackend Id,
    string DisplayName,
    AudioBackendCapabilities Capabilities)
{
    public bool Supports(AudioBackendCapabilities capability) =>
        (Capabilities & capability) == capability;
}
