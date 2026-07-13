namespace Resonalyze.Audio;

/// <summary>
/// Hardware-reported anomalies observed during one capture run. The audio layer
/// only records these facts; whether they should reject a measurement run is a
/// decision for the application/measurement layer.
/// </summary>
[Flags]
public enum AudioCaptureAnomalies
{
    None = 0,
    CaptureDiscontinuity = 1 << 0,
    CaptureTimestampError = 1 << 1,
    RenderUnderrun = 1 << 2
}

/// <summary>
/// The result of one finite play-and-capture run: the captured channels and
/// where the microphone / loopback roles landed within them, plus anomalies and
/// a diagnostics snapshot.
/// </summary>
public sealed record AudioCaptureResult(
    float[][] Channels,
    int MicrophoneChannel,
    int? LoopbackChannel,
    bool StereoSeparationExpected,
    AudioCaptureAnomalies Anomalies,
    AudioSessionDiagnostics? Diagnostics);

/// <summary>
/// One fixed-length sequence delivered by a streaming capture session, with the
/// microphone / loopback roles located within the channel array.
/// </summary>
public sealed record AudioCaptureFrame(
    float[][] Channels,
    int MicrophoneChannel,
    int? LoopbackChannel);
