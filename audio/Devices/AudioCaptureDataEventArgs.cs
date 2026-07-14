using NAudio.Wave;

namespace Resonalyze.Audio;

internal sealed class AudioCaptureDataEventArgs : EventArgs
{
    /// <summary>
    /// Gets callback-scoped capture data. Consumers must copy data that needs to outlive the
    /// <see cref="IAudioCaptureDevice.DataAvailable"/> callback.
    /// </summary>
    public required ReadOnlyMemory<byte> Buffer { get; init; }
    public required int BytesRecorded { get; init; }
    public required WaveFormat Format { get; init; }
    public long? DevicePositionFrames { get; init; }
    public long? QpcPosition { get; init; }
    public bool Discontinuity { get; init; }
    public bool Silent { get; init; }
    public bool TimestampError { get; init; }
}
