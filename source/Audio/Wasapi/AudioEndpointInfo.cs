using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze;

public sealed record AudioEndpointInfo(
    string Id,
    string FriendlyName,
    DataFlow Direction,
    DeviceState State,
    WaveFormat MixFormat,
    int Channels,
    bool IsDefault)
{
    public bool IsAvailable => State == DeviceState.Active;

    public override string ToString()
    {
        string prefix = IsAvailable ? string.Empty : "[Unavailable] ";
        string suffix = IsDefault ? " (Default)" : string.Empty;
        return $"{prefix}{FriendlyName}{suffix}";
    }
}
