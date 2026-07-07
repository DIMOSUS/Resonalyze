namespace Resonalyze;

public sealed record AsioDeviceInfo(string DriverName, bool Missing = false)
{
    public override string ToString() =>
        Missing ? $"(missing) {DriverName}" : DriverName;
}

public sealed record AsioChannelInfo(int Offset, string Name)
{
    public override string ToString() => $"{Offset + 1}: {Name}";
}

public sealed record AsioDriverInfo(
    string DriverName,
    IReadOnlyList<AsioChannelInfo> InputChannels,
    IReadOnlyList<AsioChannelInfo> OutputChannels,
    int FramesPerBuffer,
    int PlaybackLatency,
    bool SupportsSampleRate,
    string? ErrorMessage);
