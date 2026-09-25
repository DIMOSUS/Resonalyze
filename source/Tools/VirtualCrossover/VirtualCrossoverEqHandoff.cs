using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A redraw's processed channels, kept whole: the junction views and the handoffs read channels the view does not
/// draw.</summary>
internal sealed record VirtualCrossoverProcessedRender(long Revision, List<ProcessedChannel> Channels);

/// <summary>What one channel side hands the EQ Wizard, and whether the wizard's bank may come back to it, read off the
/// session and the last redraw. See docs/tech/virtual-dsp-panel.md#hybrid-handoff-to-the-eq-wizard.</summary>
internal sealed class VirtualCrossoverEqHandoff(
    VirtualCrossoverSession session,
    VirtualCrossoverProcessingCoordinator coordinator,
    VirtualCrossoverMetrics metrics,
    VirtualCrossoverHybrid hybridReader)
{
    /// <summary>The last redraw while it describes the current settings, else null.</summary>
    private VirtualCrossoverProcessedRender? CurrentRender =>
        session.LastRender is { } render && coordinator.IsCurrent(render.Revision) ? render : null;

    /// <summary>Null when the side has no measurement.</summary>
    /// <param name="targetLevelDb">A level the fit is run against instead of the project's; the project keeps its own
    /// until the fit lands.</param>
    public VirtualDspEqHandoffRequest? Request(
        VirtualCrossoverChannel channel,
        bool withChain,
        bool hybridRequested,
        (LiveCaptureDocument? Capture, double OffsetDb) spatialAverage,
        double? targetLevelDb = null)
    {
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        // Only a render describing the CURRENT settings may place the window; stale -> the builder reads the channel's front.
        int? renderAnchor = CurrentRender is { Channels.Count: >= 2 } render
            ? ProcessedChannels.SharedStartAnchorIndex(render.Channels)
            : null;
        try
        {
            return VirtualDspEqHandoff.Build(
                channel,
                channel.ActiveRight,
                withChain,
                session.ProcessorProfile,
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                renderAnchor,
                PhaseContext(channel),
                targetLevelDb ?? session.Project.TargetLevelDb,
                (double)VirtualCrossoverLimits.TargetLevel.Minimum,
                (double)VirtualCrossoverLimits.TargetLevel.Maximum,
                snapshot.SmoothingInverseOctaves,
                // The wizard pins what the panel rendered with, including per-channel Own calibration.
                session.Calibration.For(channel.SideState(channel.ActiveRight)),
                session.Calibration.NameFor(channel.SideState(channel.ActiveRight)),
                session.Calibration.SpatialAverageFor(),
                session.ProjectGeneration,
                spatialAverage.Capture,
                spatialAverage.OffsetDb,
                hybridRequested &&
                    session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray &&
                    spatialAverage.Capture == null);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Other drivers as processed IRs (not drawn curves, so the wizard can re-gate), plus window and τ.</summary>
    /// <remarks>Resolved over the set the wizard draws (<see cref="ProcessedChannels.PhaseNeighbourhood"/>); null when the render is stale.</remarks>
    public EqWizardPhaseContext? PhaseContext(VirtualCrossoverChannel channel)
    {
        if (CurrentRender is not { } render)
        {
            return null;
        }

        List<ProcessedChannel> drawn =
            ProcessedChannels.PhaseNeighbourhood(render.Channels, channel);
        int index = drawn.FindIndex(item => ReferenceEquals(item.Channel, channel));
        if (index < 0)
        {
            return null;
        }

        // Off the snapshot: a concurrent import rebinds channels and the live rate reads zero.
        int sampleRate = drawn[0].SampleRate;
        VirtualCrossoverPhaseGate gate = session.Gate;
        double referenceOffsetMs = gate.ReferenceOffsetMs(drawn, sampleRate);
        double detrendMs = gate.CommonDetrendMs(drawn, referenceOffsetMs, sampleRate);
        List<double> offsets = gate.PerCurveOffsets(drawn, referenceOffsetMs, sampleRate);

        return new EqWizardPhaseContext(
            // Curves render as Manual against one τ for the whole set, but the user's detrend mode must arrive intact.
            gate.Settings(
                referenceOffsetMs,
                gate.DetrendMode,
                detrendMs),
            offsets[index],
            detrendMs,
            gate.PinnedOffsetMs is not null,
            // The source responses travel too, so the wizard re-resolves placements the same way when its window changes.
            PlacementChannel.From(drawn[index]),
            sampleRate,
            drawn[index].Color,
            drawn
                .Select((item, position) => (item, position))
                .Where(entry => entry.position != index)
                .Select(entry => new EqWizardPhaseNeighbour(
                    entry.item.Channel.Name,
                    entry.item.Color,
                    PlacementChannel.From(entry.item),
                    offsets[entry.position]))
                .ToList());
    }

    /// <summary>Lands the wizard's bank on the side it came from; false, writing nothing, when the channel is gone or
    /// anything the bank was fitted against moved. The target level is the caller's to write once this holds.</summary>
    public bool TryReturn(
        VirtualDspEqReturnToken token,
        EqualizationCurve curve,
        LiveCaptureDocument? spatialAverage)
    {
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        // The pin of the side the bank was gated through, not of the side shown now: L/R may have flipped meanwhile.
        double? pinnedOffsetMs = token.GateRightSide == session.ActiveSideRight
            ? snapshot.PinnedOffsetMs
            : snapshot.OppositePinnedOffsetMs;
        return VirtualDspEqHandoff.TryApplyReturn(
            session.Channels,
            token,
            curve,
            session.ProjectGeneration,
            // Per side: under Own the panel holds no single calibration, and null would refuse every return.
            session.Calibration.For(token.Channel.SideState(token.RightSide)),
            session.Calibration.SpatialAverageFor(),
            snapshot.Template,
            pinnedOffsetMs,
            session.Project.TargetLevelDb,
            spatialAverage,
            session.ProcessorSampleRateHz);
    }

    /// <summary>The spatial average handed to the EQ Wizard, or null when the hybrid is not drawn.</summary>
    /// <remarks>Cheap and redraw-independent so the handoff and the return guard cannot disagree mid-redraw.</remarks>
    public LiveCaptureDocument? HybridCapture(
        VirtualCrossoverChannel channel, bool rightSide, bool hybridRequested) =>
        hybridRequested
            ? channel.SideState(rightSide).SpatialAverageFor(session.SpatialAverageMode)
            : null;

    /// <summary>The capture plus its offset onto the IR axis; resolved here when no current magnitude render carries it.</summary>
    public (LiveCaptureDocument? Capture, double OffsetDb) SpatialAverage(
        VirtualCrossoverChannel channel, bool rightSide, bool hybridRequested)
    {
        if (HybridCapture(channel, rightSide, hybridRequested) is not { } capture)
        {
            return (null, 0.0);
        }

        if (session.LastHybridOffset is { } cached && coordinator.IsCurrent(cached.Revision))
        {
            return (capture, cached.OffsetDb);
        }

        if (CurrentRender is not { } render)
        {
            return (null, 0.0);
        }

        (List<AnalysisCurve>? magnitudes, _, _) =
            metrics.BuildCurves(render.Channels, session.MagnitudeGate.SmoothingInverseOctaves);
        if (magnitudes == null ||
            hybridReader.Build(
                render.Channels,
                magnitudes,
                rightSide,
                session.MagnitudeGate.SmoothingInverseOctaves) is not { } hybrid)
        {
            return (null, 0.0);
        }

        return (capture, hybrid.OffsetDb);
    }
}
