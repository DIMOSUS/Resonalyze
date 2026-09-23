namespace Resonalyze.Options;

/// <summary>What a settings panel's impulse preview draws, read from its session: the panel redraws when it changes.</summary>
internal abstract record ImpulsePreviewInput(MeasurementResult? Result);

/// <summary>A window in samples around the IR's peak or start.</summary>
internal sealed record SampleWindowPreview(
    MeasurementResult? Result,
    int Window,
    int Left,
    int Right,
    int Offset,
    IrPreviewSource Source) : ImpulsePreviewInput(Result);

/// <summary>A millisecond gate on the transfer IR, with the compared measurement's when there is one.</summary>
internal sealed record GatePreview(
    MeasurementResult? Result,
    double OffsetMs,
    double LeftMs,
    double PlateauMs,
    double RightMs,
    CompareAnalysisSource? Compare) : ImpulsePreviewInput(Result);
