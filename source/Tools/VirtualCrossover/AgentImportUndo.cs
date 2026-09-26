using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>The session before a write, for Undo AI import and each command's <see cref="VirtualCrossoverUndo"/>. Auto delay
/// commits the scene, tilt and rear-fill offset (CommitAutoDelayResult), so undo carries them.</summary>
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

    /// <summary>Puts the session back as this holds it. Returns the channels whose controls show a restored side and
    /// whether the blocks moved.</summary>
    public (List<VirtualCrossoverChannel> Written, bool Reordered) Restore(
        VirtualCrossoverSession session, AgentSessionReader reader)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reader);
        AgentProposalApplier.Restore(Channels);
        List<VirtualCrossoverChannel> written = ChannelsOf(Channels, reader);
        // Auto crossover can reorder blocks; restored by identity, since the list holds the same objects.
        bool reordered = false;
        if (session.OrderOf(Order) is { } order)
        {
            session.Reorder(order);
            reordered = true;
        }

        session.Project.SpatialAverageMode = SpatialAverageMode;
        session.Project.ShowHybridCurves = HybridTicked;
        session.Project.SetStereoScene(SceneOffsetMagnitudeMs, RightHandDrive);
        session.Project.StereoLevelDifferenceDb = StereoLevelDifferenceDb;
        session.Project.RearFillOffsetMs = RearFillOffsetMs;
        session.Project.TargetLevelDb = (double)VirtualCrossoverLimits.TargetLevel.Clamp(TargetLevelDb);
        return (written, reordered);
    }

    /// <summary>Whether two snapshots hold the same session as far as <see cref="Restore"/> reaches: the view, the
    /// sources and the gate are not in it.</summary>
    public bool SameAs(AgentImportUndo other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Order.SequenceEqual(other.Order) &&
            Channels.Count == other.Channels.Count &&
            Channels.Zip(other.Channels).All(pair =>
                ReferenceEquals(pair.First.Target, pair.Second.Target) &&
                AgentProposalApplier.SameEditable(pair.First.Before, pair.Second.Before)) &&
            SpatialAverageMode == other.SpatialAverageMode &&
            HybridTicked == other.HybridTicked &&
            SceneOffsetMagnitudeMs.Equals(other.SceneOffsetMagnitudeMs) &&
            RightHandDrive == other.RightHandDrive &&
            StereoLevelDifferenceDb.Equals(other.StereoLevelDifferenceDb) &&
            RearFillOffsetMs.Equals(other.RearFillOffsetMs) &&
            TargetLevelDb.Equals(other.TargetLevelDb);
    }

    /// <summary>The blocks holding these settings; a control shows the active side, so a write to the other side shows
    /// when the side flips.</summary>
    public static List<VirtualCrossoverChannel> ChannelsOf(IReadOnlyList<AgentUndoEntry> entries, AgentSessionReader reader)
    {
        var channels = new List<VirtualCrossoverChannel>();
        foreach (AgentUndoEntry entry in entries)
        {
            foreach ((_, _, VirtualCrossoverChannel channel, bool rightSide) in reader.Slots())
            {
                if (ReferenceEquals(channel.SideSettings(rightSide), entry.Target))
                {
                    channels.Add(channel);
                    break;
                }
            }
        }

        return channels;
    }
}
