using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>Callback-scoped: consumers must copy data that outlives the callback.</summary>
internal readonly record struct AudioCapturePacket(
    ReadOnlyMemory<byte> Buffer,
    int BytesRecorded,
    WaveFormat Format,
    long? DevicePositionFrames = null,
    long? QpcPosition = null,
    bool Discontinuity = false,
    bool Silent = false,
    bool TimestampError = false);
