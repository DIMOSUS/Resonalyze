namespace Resonalyze.Audio;

/// <summary>
/// Low-level capture counters a session can fold into an
/// <see cref="AudioSessionDiagnostics"/> snapshot. Only backends that actually
/// track them (WASAPI) implement it; MME/ASIO produce no diagnostics.
/// </summary>
internal interface ICaptureDiagnosticsSource
{
    string EndpointId { get; }
    long CapturePackets { get; }
    long Discontinuities { get; }
    long SilentPackets { get; }
    long TimestampErrors { get; }
}

/// <summary>Low-level render counters for an <see cref="AudioSessionDiagnostics"/> snapshot.</summary>
internal interface IRenderDiagnosticsSource
{
    string EndpointId { get; }
    long RenderCallbacks { get; }
    long RenderUnderruns { get; }
    int ActualBufferFrames { get; }
}
