namespace Resonalyze.Audio;

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
    // Probed in the same open: some drivers refuse a second open while the previous instance is released.
    IReadOnlyList<int> SupportedSampleRates,
    string? ErrorMessage);
