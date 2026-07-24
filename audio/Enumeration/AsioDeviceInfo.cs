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
    // Every standard rate the driver accepts, probed during the same open as the
    // rest of this record: opening an ASIO driver is a synchronous COM call that
    // some drivers refuse while a previous instance is still being released, so a
    // caller that needs both the channels and the rate list must not open twice.
    IReadOnlyList<int> SupportedSampleRates,
    string? ErrorMessage);
