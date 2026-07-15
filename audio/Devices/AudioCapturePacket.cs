using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// Describes callback-scoped capture data without allocating an event-arguments object.
/// Consumers must copy data that needs to outlive the callback.
/// </summary>
internal readonly record struct AudioCapturePacket(
    ReadOnlyMemory<byte> Buffer,
    int BytesRecorded,
    WaveFormat Format,
    long? DevicePositionFrames = null,
    long? QpcPosition = null,
    bool Discontinuity = false,
    bool Silent = false,
    bool TimestampError = false);
