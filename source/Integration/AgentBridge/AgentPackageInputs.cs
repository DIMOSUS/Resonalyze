using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>The builder's whole input, gathered from the screen's own computations: the builder reads no control, project or coordinator.</summary>
internal sealed record AgentPackageInputs(
    string ApplicationVersion,
    string? Notes,
    AgentProcessorInputs Processor,
    AgentAnalysisInputs Analysis,
    AgentTargetInputs Target,
    IReadOnlyList<AgentChannelInputs> Channels,
    IReadOnlyList<AgentSideInputs> Sides,
    IReadOnlyList<VirtualCrossoverMetric.StereoDelta> Stereo,
    IReadOnlyList<VirtualCrossoverMetric.GroupDelta> Groups);

/// <param name="MaxDelayFromCatalog">Whether MaxDelayMs came from the device's manual or is the engine default.</param>
internal sealed record AgentProcessorInputs(
    string ModelId,
    string DisplayName,
    bool IsCustom,
    int SampleRateHz,
    bool FollowsMeasurements,
    PeqQConvention QConvention,
    double MaxDelayMs,
    bool MaxDelayFromCatalog);

internal sealed record AgentAnalysisInputs(
    VirtualCrossoverGroupView GroupView,
    bool ActiveSideRight,
    int SmoothingInverseOctaves,
    bool PsychoacousticSmoothing,
    VirtualCrossoverSpatialAverageMode? SpatialAverageMode,
    // Both travel: "ticked but not drawn" is a different fix from "not ticked".
    bool HybridTicked,
    bool HybridDrawn,
    int HybridSmoothingInverseOctaves,
    PhaseWindowMode PhaseWindowMode,
    int FdwCycles,
    PhaseDetrendMode DetrendMode,
    double GateLeftMs,
    double GatePlateauMs,
    double GateRightMs,
    double? LeftGateOffsetMs,
    double? LeftDetrendMs,
    double? RightGateOffsetMs,
    double? RightDetrendMs,
    string? CalibrationName,
    double StereoSceneOffsetMs,
    bool RightHandDrive,
    double StereoLevelDifferenceDb,
    double RearFillOffsetMs);

internal sealed record AgentTargetInputs(
    double LevelDb,
    TargetPreset Preset,
    TargetCurveSpec Spec,
    double ToleranceDb,
    string? ImportedName);

internal sealed record AgentChannelInputs(
    string Block,
    AgentChannelSide Side,
    VirtualCrossoverZone Zone,
    string DisplayName,
    bool Enabled,
    bool Bypass,
    VirtualCrossoverChannelSettings Settings,
    int ProcessorSampleRateHz,
    AgentSourceInputs? Source)
{
    public string Id => AgentChannelIds.Format(Block, Side);
}

/// <summary>The measurement behind a channel and the curves drawn from it; every curve is optional. Hybrid curves are on the impulse responses' level axis.</summary>
internal sealed record AgentSourceInputs(
    int SampleRateHz,
    MeasuredBand MeasuredBand,
    string? SpatialAverage,
    IReadOnlyList<string> SpatialAverageCaptures,
    IReadOnlyList<SignalPoint>? PreDsp,
    IReadOnlyList<SignalPoint>? Processed,
    IReadOnlyList<SignalPoint>? HybridPreDsp,
    IReadOnlyList<SignalPoint>? HybridProcessed,
    IReadOnlyList<SignalPoint>? Coherence,
    string? UnavailableReason);

/// <summary>One side in the current group view, read exactly as the metric block quotes it. HybridSum is an estimate, present only while hybrid curves are drawn.</summary>
internal sealed record AgentSideInputs(
    AgentChannelSide Side,
    IReadOnlyList<string> ChannelIds,
    IReadOnlyList<SignalPoint>? Sum,
    IReadOnlyList<SignalPoint>? HybridSum,
    IReadOnlyList<SignalPoint>? Loss,
    IReadOnlyList<VirtualCrossoverMetric.Entry> Entries,
    IReadOnlyList<VirtualCrossoverMetric.PhaseEntry> PhaseEntries,
    IReadOnlyList<AgentJunctionInputs> Junctions,
    string? UnavailableReason,
    // Direct-sound (FDW-8) read; null where it has no metric.
    IReadOnlyList<SignalPoint>? DirectLoss = null,
    IReadOnlyList<VirtualCrossoverMetric.Entry>? DirectEntries = null);

internal sealed record AgentJunctionInputs(
    string LowerBlock,
    string UpperBlock,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz,
    IReadOnlyList<SignalPoint>? LowerMagnitude,
    IReadOnlyList<SignalPoint>? UpperMagnitude,
    JunctionCorrelationView? Correlation,
    JunctionCoherenceView? Coherence);
