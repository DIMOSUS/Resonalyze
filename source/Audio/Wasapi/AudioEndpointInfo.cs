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
    bool IsDefault);
