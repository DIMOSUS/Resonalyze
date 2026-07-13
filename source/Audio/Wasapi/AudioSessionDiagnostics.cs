using NAudio.Wave;

namespace Resonalyze;

public sealed record AudioSessionDiagnostics(
    string Backend,
    string CaptureEndpointId,
    string RenderEndpointId,
    WaveFormat CaptureFormat,
    WaveFormat RenderFormat,
    int RequestedBufferMilliseconds,
    int ActualBufferFrames,
    long CapturePackets,
    long RenderCallbacks,
    long Discontinuities,
    long SilentPackets,
    long TimestampErrors,
    long CaptureOverruns,
    long RenderUnderruns);
