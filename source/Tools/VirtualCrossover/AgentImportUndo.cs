using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>The session as it stood before a write, as Undo AI import and Tune junction's Undo last Apply put it back.
/// Scene, tilt and rear-fill offset are committed by Auto delay (CommitAutoDelayResult), so undo carries them.</summary>
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
    /// <summary>Every channel's chain is taken: engines write channels no row names, and the crossover wizard can
    /// reorder blocks.</summary>
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
