namespace Resonalyze.Audio;

/// <summary>Immutable counters and negotiated formats of one session; may be persisted in a measurement file.</summary>
public sealed record AudioSessionDiagnostics(
    string Backend,
    string CaptureEndpointId,
    string RenderEndpointId,
    AudioFormat CaptureFormat,
    AudioFormat RenderFormat,
    int RequestedBufferMilliseconds,
    int ActualBufferFrames,
    long CapturePackets,
    long RenderCallbacks,
    long Discontinuities,
    long SilentPackets,
    long TimestampErrors,
    long CaptureOverruns,
    long RenderUnderruns);
