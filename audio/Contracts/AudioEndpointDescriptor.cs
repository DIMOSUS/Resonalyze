namespace Resonalyze.Audio;

public enum AudioEndpointDirection
{
    Capture,
    Render
}

/// <summary>
/// A backend-neutral description of a selectable audio endpoint (a WASAPI
/// device, a Wave/MME numbered device, or an ASIO channel group). Replaces the
/// NAudio-flavoured <c>AudioEndpointInfo</c> at the public boundary.
/// </summary>
public sealed record AudioEndpointDescriptor(
    string Id,
    string DisplayName,
    AudioEndpointDirection Direction,
    AudioFormat PreferredFormat,
    int ChannelCount,
    bool IsAvailable,
    bool IsDefault)
{
    public override string ToString()
    {
        string prefix = IsAvailable ? string.Empty : "[Unavailable] ";
        string suffix = IsDefault ? " (Default)" : string.Empty;
        return $"{prefix}{DisplayName}{suffix}";
    }
}
