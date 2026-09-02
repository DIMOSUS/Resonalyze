using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// Everything the package builder needs, gathered by the panel from the same
/// computations that draw the screen, and handed over as immutable data. The
/// builder itself reads no control, no project and no coordinator — this record
/// is the whole of its input, which is what makes it testable on synthetic
/// curves and what keeps the package's numbers the screen's numbers.
/// </summary>
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

/// <param name="MaxDelayFromCatalog">
/// Whether <paramref name="MaxDelayMs"/> came from the device's manual (a catalog
/// line) or is the engine's long-standing default for a device nobody looked up.
/// </param>
internal sealed record AgentProcessorInputs(
    string ModelId,
    string DisplayName,
    bool IsCustom,
    int SampleRateHz,
    bool FollowsMeasurements,
    PeqQConvention QConvention,
    double MaxDelayMs,
    bool MaxDelayFromCatalog);

/// <summary>The settings that decide what the diagnostics MEAN, no view-only flags.</summary>
internal sealed record AgentAnalysisInputs(
    VirtualCrossoverGroupView GroupView,
    bool ActiveSideRight,
    int SmoothingInverseOctaves,
    bool PsychoacousticSmoothing,
    VirtualCrossoverSpatialAverageMode? SpatialAverageMode,
    bool HybridOn,
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

/// <param name="Spec">The shape, parametric terms and imported curve alike; evaluated on the grid.</param>
internal sealed record AgentTargetInputs(
    double LevelDb,
    TargetPreset Preset,
    TargetCurveSpec Spec,
    double ToleranceDb,
    string? ImportedName);

/// <summary>One physical channel: identity, settings, and what was measured on it.</summary>
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

/// <summary>
/// The measurement behind a channel and the curves the screen draws from it.
/// Every curve is optional: a muted channel has a measurement and no curves, a
/// point measurement has no hybrid, a sweep has no coherence.
/// </summary>
/// <param name="PreDsp">The acoustic response before the chain — the Raw curve.</param>
/// <param name="Processed">After the chain — the Processed curve, shared window with the side's sum.</param>
/// <param name="HybridPreDsp">The spatial average before the chain, on the impulse responses' level axis (the hybrid datum applied), when the hybrid mode is on.</param>
/// <param name="HybridProcessed">The spatial average through the chain, on the same axis — the curve the hybrid view draws.</param>
/// <param name="Coherence">The measurement's γ² per frequency, when the source carried it.</param>
internal sealed record AgentSourceInputs(
    int SampleRateHz,
    MeasuredBand MeasuredBand,
    string? SpatialAverage,
    IReadOnlyList<SignalPoint>? PreDsp,
    IReadOnlyList<SignalPoint>? Processed,
    IReadOnlyList<SignalPoint>? HybridPreDsp,
    IReadOnlyList<SignalPoint>? HybridProcessed,
    IReadOnlyList<SignalPoint>? Coherence,
    string? UnavailableReason);

/// <summary>
/// One side of the car in the current group view: its sum, its summation loss
/// and the junction read-outs exactly as the panel's metric block quotes them.
/// </summary>
/// <param name="ChannelIds">The channels the view drew on this side — the set the sum, the loss and the junctions were computed from.</param>
/// <param name="Entries">The Sum loss rows, per junction plus the total where the chain is continuous.</param>
/// <param name="PhaseEntries">The Junction phase rows.</param>
internal sealed record AgentSideInputs(
    AgentChannelSide Side,
    IReadOnlyList<string> ChannelIds,
    IReadOnlyList<SignalPoint>? Sum,
    IReadOnlyList<SignalPoint>? Loss,
    IReadOnlyList<VirtualCrossoverMetric.Entry> Entries,
    IReadOnlyList<VirtualCrossoverMetric.PhaseEntry> PhaseEntries,
    IReadOnlyList<AgentJunctionInputs> Junctions,
    string? UnavailableReason);

/// <summary>
/// One adjacent pair along the spectrum, with the two views the lower plot can
/// draw for it — the correlation view carries the score sweep the lobes are read
/// off, the coherence view the ladder.
/// </summary>
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
