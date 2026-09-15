namespace Resonalyze.Audio;

/// <summary>Only WASAPI implements it; MME/ASIO produce no diagnostics.</summary>
internal interface ICaptureDiagnosticsSource
{
    string EndpointId { get; }
    long CapturePackets { get; }
    long Discontinuities { get; }
    long SilentPackets { get; }
    long TimestampErrors { get; }
}

internal interface IRenderDiagnosticsSource
{
    string EndpointId { get; }
    long RenderCallbacks { get; }
    long RenderUnderruns { get; }
    int ActualBufferFrames { get; }
}
