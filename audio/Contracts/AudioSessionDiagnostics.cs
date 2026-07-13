namespace Resonalyze.Audio;

/// <summary>
/// An immutable snapshot of one capture/render session's low-level counters and
/// negotiated formats. Produced by the audio layer; the application may persist
/// a copy in a measurement file. Backend-neutral — formats are
/// <see cref="AudioFormat"/>, never NAudio <c>WaveFormat</c>.
/// </summary>
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
