using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The Gate dialog's candidates while it is open. <see cref="AutoOffset"/> gates per curve as Save will, while
/// <see cref="OffsetMs"/> is where the dialog draws its window.</summary>
internal sealed record VirtualCrossoverGatePreview(
    double OffsetMs,
    bool AutoOffset,
    double LeftMs,
    double PlateauMs,
    double RightMs,
    PhaseWindowMode WindowMode,
    int FdwCycles,
    PhaseDetrendMode DetrendMode,
    double DetrendMs);

/// <summary>The phase gate one side's views read: the project's shared lengths and modes, that side's placement, and the
/// Gate dialog's candidates while it is open. Lengths and modes are shared; offset and detrend belong to the side.</summary>
internal sealed record VirtualCrossoverPhaseGate(
    PhaseWindowMode WindowMode,
    int FdwCycles,
    PhaseDetrendMode DetrendMode,
    double LeftMs,
    double PlateauMs,
    double RightMs,
    double? StoredOffsetMs,
    double? StoredDetrendMs,
    VirtualCrossoverGatePreview? Preview)
{
    public static VirtualCrossoverPhaseGate For(
        VirtualCrossoverProjectFile project,
        bool rightSide,
        VirtualCrossoverGatePreview? preview)
    {
        VirtualCrossoverPhaseGateSettings placement = project.PhaseGateFor(rightSide);
        return new VirtualCrossoverPhaseGate(
            preview?.WindowMode ?? project.PhaseWindowMode,
            preview?.FdwCycles ?? project.PhaseFdwCycles,
            preview?.DetrendMode ?? project.PhaseDetrendMode,
            preview?.LeftMs ?? project.PhaseGateLeftMs,
            preview?.PlateauMs ?? project.PhaseGatePlateauMs,
            preview?.RightMs ?? project.PhaseGateRightMs,
            placement.OffsetMs,
            placement.DetrendMs,
            preview);
    }

    /// <summary>Null = Auto: magnitude anchors one shared window, phase curves follow each arrival START.</summary>
    public double? PinnedOffsetMs => Preview is { } preview
        ? preview.AutoOffset ? null : preview.OffsetMs
        : StoredOffsetMs;

    public double? DetrendMs => Preview is { } preview ? preview.DetrendMs : StoredDetrendMs;

    /// <summary>The stored pin, else the set's earliest front; the dialog's candidate does not move it.</summary>
    public double SharedOffsetMs(IReadOnlyList<ProcessedChannel> channels, int sampleRate) =>
        PhaseGatePlacement.ResolveSharedOffsetMs(
            PlacementChannel.From(channels), sampleRate, StoredOffsetMs);

    /// <summary>Where the views draw the window: the open dialog's candidate, else <see cref="SharedOffsetMs"/>.</summary>
    public double ReferenceOffsetMs(IReadOnlyList<ProcessedChannel> channels, int sampleRate) =>
        Preview?.OffsetMs ?? SharedOffsetMs(channels, sampleRate);

    public PhaseAnalysisSettings Settings(
        double gateOffsetMs,
        PhaseDetrendMode detrendMode,
        double manualDetrendMilliseconds) => new(
            WindowMode,
            FdwCycles,
            detrendMode,
            manualDetrendMilliseconds,
            gateOffsetMs,
            LeftMs,
            PlateauMs,
            RightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

    // Placement arithmetic lives in PhaseGatePlacement, shared with the EQ Wizard so both read the same windows and τ.
    public List<double> PerCurveOffsets(
        IReadOnlyList<ProcessedChannel> gatedChannels,
        double sharedOffsetMs,
        int sampleRate) =>
        PhaseGatePlacement.ResolvePerCurveOffsets(
            PlacementChannel.From(gatedChannels),
            sharedOffsetMs,
            sampleRate,
            PinnedOffsetMs,
            LeftMs,
            PlateauMs,
            RightMs);

    /// <summary>One τ for every curve keeps relative phase through the detrend.</summary>
    public double CommonDetrendMs(
        IReadOnlyList<ProcessedChannel> channels,
        double gateOffsetMs,
        int sampleRate) =>
        PhaseGatePlacement.ResolveCommonDetrendMs(
            PlacementChannel.From(channels),
            sampleRate,
            Settings(gateOffsetMs, PhaseDetrendMode.Auto, manualDetrendMilliseconds: 0.0),
            DetrendMode,
            DetrendMs);

    /// <summary>The magnitude reads the FIXED steady-state window; only the offset comes from this gate.
    /// See docs/tech/virtual-dsp-panel.md#magnitude-window.</summary>
    public MagnitudeGateSnapshot MagnitudeGate(
        double? oppositePinnedOffsetMs,
        int smoothingInverseOctaves) =>
        new(
            Settings(gateOffsetMs: 0.0, PhaseDetrendMode.Off, manualDetrendMilliseconds: 0.0) with
            {
                WindowMode = PhaseWindowMode.Fixed,
                LeftMs = FrequencyResponseOptions.SteadyStateLeftMs,
                PlateauMs = FrequencyResponseOptions.SteadyStatePlateauMs,
                RightMs = FrequencyResponseOptions.SteadyStateRightMs
            },
            PinnedOffsetMs,
            oppositePinnedOffsetMs,
            smoothingInverseOctaves);
}
