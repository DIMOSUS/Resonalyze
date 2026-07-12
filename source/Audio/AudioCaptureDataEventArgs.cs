using NAudio.Wave;

namespace Resonalyze;

public sealed class AudioCaptureDataEventArgs : EventArgs
{
    public required ReadOnlyMemory<byte> Buffer { get; init; }
    public required int BytesRecorded { get; init; }
    public required WaveFormat Format { get; init; }
    public long? DevicePositionFrames { get; init; }
    public long? QpcPosition { get; init; }
    public bool Discontinuity { get; init; }
    public bool Silent { get; init; }
}
