namespace Resonalyze.Audio;

/// <summary>Recorded facts only; rejecting a run is the measurement layer's decision.</summary>
[Flags]
public enum AudioCaptureAnomalies
{
    None = 0,
    CaptureDiscontinuity = 1 << 0,
    CaptureTimestampError = 1 << 1,
    RenderUnderrun = 1 << 2
}

public sealed record AudioCaptureResult(
    float[][] Channels,
    int MicrophoneChannel,
    int? LoopbackChannel,
    bool StereoSeparationExpected,
    AudioCaptureAnomalies Anomalies,
    AudioSessionDiagnostics? Diagnostics)
{
    public IReadOnlyList<int> ArrayChannels { get; init; } = [];
}

public sealed record AudioCaptureFrame(
    float[][] Channels,
    int MicrophoneChannel,
    int? LoopbackChannel);
