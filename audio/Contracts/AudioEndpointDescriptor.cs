namespace Resonalyze.Audio;

public enum AudioEndpointDirection
{
    Capture,
    Render
}

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
