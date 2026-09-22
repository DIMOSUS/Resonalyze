using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>The session before a write, for Undo AI import and Tune junction's Undo last Apply. Auto delay commits the
/// scene, tilt and rear-fill offset (CommitAutoDelayResult), so undo carries them.</summary>
internal sealed record AgentImportUndo(
    IReadOnlyList<AgentUndoEntry> Channels,
    VirtualCrossoverSpatialAverageMode? SpatialAverageMode,
    bool HybridTicked,
    IReadOnlyList<VirtualCrossoverChannel> Order,
    double SceneOffsetMagnitudeMs,
    bool RightHandDrive,
    double StereoLevelDifferenceDb,
    double RearFillOffsetMs,
    double TargetLevelDb)
{
    /// <summary>Every channel: engines write channels no row names, and the crossover wizard reorders blocks.</summary>
    public static AgentImportUndo Capture(
        VirtualCrossoverSession session, AgentSessionReader reader, AgentViewInputs view) =>
        new(
            reader.Slots()
                .Select(slot => slot.Channel.SideSettings(slot.RightSide))
                .Select(settings => new AgentUndoEntry(
                    settings, AgentOperations.CloneEditable(settings)))
                .ToList(),
            session.Project.SpatialAverageMode,
            view.HybridTicked,
            session.Channels.ToList(),
            session.Project.StereoSceneOffsetMagnitudeMs,
            session.Project.StereoRightHandDrive,
            session.Project.StereoLevelDifferenceDb,
            session.Project.RearFillOffsetMs,
            view.TargetLevelDb);
}
