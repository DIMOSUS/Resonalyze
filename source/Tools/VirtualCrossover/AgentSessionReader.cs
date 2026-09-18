using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>What the bridge reads off the controls: the view the engines and the package follow.</summary>
internal sealed record AgentViewInputs(
    VirtualCrossoverGroupView GroupView,
    bool HybridTicked,
    bool HybridRequested,
    double TargetLevelDb,
    EqTargetCurve? TargetCurve);

/// <summary>One measured side as the package names it.</summary>
internal sealed record MeasuredChannel(
    string Id,
    Complex[] Response,
    int PeakIndex,
    int SampleRate,
    MeasuredBand Band);

/// <summary>The session as the Agent Bridge reads it: the blocks by the bridge's names, the fingerprint and review
/// snapshot, the package inputs and the diagnostics, and the last package copied. It reuses the screen's own
/// computations so package numbers match the screen. See docs/tech/agent-bridge.md#package-gathering.</summary>
internal sealed class AgentSessionReader(
    VirtualCrossoverSession session,
    VirtualCrossoverProcessingCoordinator coordinator,
    VirtualCrossoverMetrics metrics,
    VirtualCrossoverHybrid hybridReader)
{
    // The package grid is 12 points/octave: the nearest a grid gets to the hybrid view's Off smoothing.
    public const int HybridSmoothingInverseOctaves = 12;

    public VirtualCrossoverSession Session => session;

    // Id and fingerprint of the last copied package; a reply naming another package or a changed session is warned and
    // its engine requests refused. Not persisted.
    public string? LastPackageId { get; private set; }

    public string? LastPackageFingerprint { get; private set; }

    public void RememberPackage(string packageId, string fingerprint)
    {
        LastPackageId = packageId;
        LastPackageFingerprint = fingerprint;
    }

    /// <summary>The package id while this is still the session it was copied from, else null.</summary>
    public string? PackageIdFor(string fingerprint) =>
        LastPackageFingerprint == fingerprint ? LastPackageId : null;

    // A mono block yields one slot, routed to the left as everywhere in the panel.
    public IEnumerable<(string Block, AgentChannelSide Side, VirtualCrossoverChannel Channel, bool RightSide)>
        Slots()
    {
        for (int index = 0; index < session.Channels.Count; index++)
        {
            VirtualCrossoverChannel channel = session.Channels[index];
            string block = VirtualCrossoverSheet.ChannelName(index);
            if (channel.Pair.Mono)
            {
                yield return (block, AgentChannelSide.Mono, channel, false);
            }
            else
            {
                yield return (block, AgentChannelSide.Left, channel, false);
                yield return (block, AgentChannelSide.Right, channel, true);
            }
        }
    }

    /// <summary>The session as one hash for the review's staleness check. See docs/tech/agent-bridge.md#session-fingerprint.</summary>
    public string Fingerprint(AgentViewInputs view)
    {
        var lines = new List<string>
        {
            $"processor;{session.ProcessorProfile.ModelId};{session.ProcessorSampleRateHz}",
            $"average;{session.SpatialAverageMode};{view.HybridTicked}",
            // Engines read the shown side and the view (Auto crossover, single-sided Auto delay, Auto-tune's source).
            $"view;{session.Project.ActiveSideRight};{view.GroupView}",
            $"phase;{session.Project.PhaseWindowMode};{session.Project.PhaseFdwCycles};{session.Project.PhaseDetrendMode};" +
                $"{Number(session.Project.PhaseGateLeftMs)};{Number(session.Project.PhaseGatePlateauMs)};" +
                $"{Number(session.Project.PhaseGateRightMs)};" +
                $"{Number(session.Project.PhaseGateLeft.OffsetMs)};{Number(session.Project.PhaseGateLeft.DetrendMs)};" +
                $"{Number(session.Project.PhaseGateRight.OffsetMs)};{Number(session.Project.PhaseGateRight.DetrendMs)}",
            $"stereo;{Number(session.Project.StereoSceneOffsetMagnitudeMs)};{session.Project.StereoRightHandDrive};" +
                $"{Number(session.Project.StereoLevelDifferenceDb)};{Number(session.Project.RearFillOffsetMs)}",
            // By id AND points: a curve re-read under the same id is a different correction.
            $"calibration;{session.Project.CalibrationId};{session.Calibration.Own};{Curve(session.Calibration.Selected)}",
            $"target;{Number(view.TargetLevelDb)};{TargetShape(session.Project.Target)}",
            $"notes;{session.Project.AiNotes}"
        };
        foreach ((string block, AgentChannelSide side, VirtualCrossoverChannel channel, bool rightSide)
            in Slots())
        {
            VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            lines.Add(string.Join(';',
                block, AgentChannelIds.SideName(side), channel.Pair.Zone,
                channel.Pair.Enabled, channel.Pair.Bypass,
                // Content digest, not just the reference: a file re-measured over its own name keeps reference, length and rate.
                settings.HistoryEntryId, settings.SourceFilePath, settings.DisplayName,
                Digest(state.TransferImpulseResponse), state.SampleRate, state.TransferPeakIndex,
                Number(state.MeasuredBand.LowestHz), Number(state.MeasuredBand.HighestHz),
                Digest(state.TransferCoherence),
                Curve(session.Calibration.For(state)),
                // A re-recorded pass is a new capture session id.
                settings.SpatialAveragePath, Capture(state.SpatialAverage), Capture(state.ArrayCapture),
                Number(settings.GainDb), Number(settings.DelayMs), settings.InvertPolarity,
                settings.CrossoverKind, Edge(settings.HighPassEdge), Edge(settings.LowPassEdge),
                Number(settings.PhaseRotationDegrees),
                AgentPeqHash.Compute(settings.PeqPreampDb, settings.PeqBands),
                // Kernel by content; the imported name is only a label.
                Digest(settings.Fir?.Taps.ToArray()),
                FirDesign(settings.FirDesign)));
        }

        return AgentSessionFingerprint.Compute(lines);

        static string Number(double? value) => AgentSessionFingerprint.Number(value);

        static string Digest<T>(T[]? values) where T : unmanaged =>
            AgentSessionFingerprint.ContentDigest(values);

        static string Curve(CalibrationFile? calibration) =>
            calibration == null
                ? string.Empty
                : AgentSessionFingerprint.ContentDigest(
                    calibration.Points.SelectMany(point => new[] { point.FrequencyHz, point.Decibels }));

        static string Capture(LiveCaptureDocument? document) =>
            document == null
                ? string.Empty
                : $"{document.CaptureSessionId:D}/{document.SavedAtUtc.UtcTicks}/{document.Method}";

        static string Edge(CrossoverEdge edge) =>
            $"{edge.Family}/{Number(edge.FrequencyHz)}/{edge.SlopeDbPerOctave}/{Number(edge.RippleDb)}";

        static string FirDesign(FirCrossoverDesign? design) =>
            design == null
                ? string.Empty
                : $"{design.Kind}/{Edge(design.HighPassEdge)}/{Edge(design.LowPassEdge)}/" +
                  $"{design.Method}/{design.Window}/{Number(design.KaiserBeta)}/" +
                  $"{design.TapCount}/{design.SampleRateHz}";

        static string TargetShape(VirtualCrossoverTargetSettings? target) =>
            target == null
                ? string.Empty
                : string.Join('/',
                    target.Preset, Number(target.TiltDbPerOctave),
                    Number(target.BassShelfGainDb), Number(target.BassShelfFrequencyHz),
                    Number(target.BassShelfWidthOctaves),
                    Number(target.TrebleShelfGainDb), Number(target.TrebleShelfFrequencyHz),
                    Number(target.TrebleShelfWidthOctaves),
                    Number(target.PresenceGainDb), Number(target.PresenceFrequencyHz),
                    Number(target.PresenceWidthOctaves),
                    Number(target.ToleranceDb), target.ImportedName, Digest(target.ImportedCurve));
    }

    /// <summary>The channels as the bridge names them, with live settings, plus the project figures engine requests are judged against.</summary>
    public AgentSessionSnapshot Snapshot(AgentViewInputs view) =>
        new(
            Slots()
                .Select(slot => new AgentChannelSnapshot(
                    slot.Block,
                    slot.Side,
                    slot.Channel.SideSettings(slot.RightSide),
                    slot.Channel.SideState(slot.RightSide).TransferImpulseResponse != null,
                    SpatialAverageCaptures(slot.Channel.SideState(slot.RightSide)),
                    slot.Channel.Pair.Zone,
                    slot.Channel.Pair.Enabled,
                    slot.Channel.Pair.Bypass))
                .ToList(),
            session.ProcessorSampleRateHz,
            session.ProcessorProfile.MaxDelayMs,
            LastPackageId,
            AutoDelayDefaults(),
            session.SpatialAverageMode,
            view.HybridTicked,
            session.Project.ActiveSideRight,
            LastPackageFingerprint,
            Fingerprint(view));

    // The dialog's opening inputs (also the package's Current column): layout-neutral magnitudes, since the layout toggle owns signs; gain balance unticked, since the project stores the tilt, not the opt-in.
    public AgentAutoDelaySettings AutoDelayDefaults() =>
        new(
            session.Project.StereoSceneOffsetMagnitudeMs,
            session.Project.StereoRightHandDrive,
            AdjustGains: false,
            Math.Abs(session.Project.StereoLevelDifferenceDb),
            session.Project.RearFillOffsetMs);

    /// <summary>Everything a package is built from, read off the current session. Null when the session changed underneath; the caller retries once. See docs/tech/agent-bridge.md#package-gathering.</summary>
    public async Task<AgentPackageInputs?> CaptureInputsAsync(AgentViewInputs view)
    {
        long revision = coordinator.CurrentRevision;
        VirtualCrossoverGroupView groupView = view.GroupView;
        bool activeRight = session.Project.ActiveSideRight;
        // One smoothing for every package, independent of the display. See docs/tech/agent-bridge.md#package-smoothing.
        int smoothing = SpectrumSmoothing.PsychoacousticCode;
        MagnitudeGateSnapshot packageGate = session.MagnitudeGate with { SmoothingInverseOctaves = smoothing };
        // Hybrid curves and their sum go at 1/12 octave, the grid's width (the manual reads them unsmoothed).
        MagnitudeGateSnapshot hybridGate =
            session.MagnitudeGate with { SmoothingInverseOctaves = HybridSmoothingInverseOctaves };

        var sides = new List<AgentSideInputs>();
        var curves = new Dictionary<
            (VirtualCrossoverChannel Channel, bool RightSide),
            (ProcessedChannel Item, IReadOnlyList<SignalPoint>? Processed,
                IReadOnlyList<SignalPoint>? HybridPreDsp, IReadOnlyList<SignalPoint>? HybridProcessed)>();
        List<ProcessedChannel> activeShown = [];
        foreach (bool rightSide in new[] { false, true })
        {
            AgentChannelSide sideName = rightSide ? AgentChannelSide.Right : AgentChannelSide.Left;
            VirtualCrossoverSideSum? sideSum = await metrics.ComputeSideSumAsync(
                session.Channels, rightSide, revision, minimumChannels: 1);
            if (!coordinator.IsCurrent(revision))
            {
                return null;
            }
            if (sideSum == null)
            {
                sides.Add(new AgentSideInputs(
                    sideName, [], null, null, null, [], [], [], "no channel with a source on this side"));
                continue;
            }

            // The screen's own frame; channels outside the view get their own curves below.
            var frame = VirtualCrossoverFrame.Of(sideSum.Channels, groupView);
            List<ProcessedChannel> all = [.. frame.All];
            List<ProcessedChannel> shown = frame.Shown;
            List<ProcessedChannel> summed = frame.Summed;
            List<ProcessedChannel> others = all.Except(shown).ToList();
            if (rightSide == activeRight)
            {
                activeShown = shown;
            }

            // The panel's `metrics` smooths at the display's width; these delegates window through the package gate, the opposite side through its own gate placement. See docs/tech/agent-bridge.md#package-smoothing.
            bool oppositeSide = rightSide != activeRight;
            VirtualCrossoverMetrics MetricsThrough(MagnitudeGateSnapshot gate) =>
                VirtualCrossoverMetrics.Through(
                    coordinator,
                    () => gate,
                    oppositeSide,
                    channel => session.Calibration.For(channel));
            VirtualCrossoverMetrics sideMetrics = MetricsThrough(packageGate);

            List<AnalysisCurve>? magnitudes = null;
            AnalysisCurve? sumCurve = null;
            List<SignalPoint>? loss = null;
            // At the hybrid's width so a point-measurement fallback is not smoothed twice; built only when the hybrid is asked for (a second gated pass).
            List<AnalysisCurve>? hybridReferences = null;
            if (shown.Count > 0)
            {
                (magnitudes, sumCurve, loss) = sideMetrics.BuildCurves(shown, smoothing, summed);
                if (view.HybridRequested)
                {
                    (hybridReferences, _, _) = MetricsThrough(hybridGate)
                        .BuildCurves(shown, HybridSmoothingInverseOctaves, summed);
                }
            }

            if (!frame.QuotesJunctions)
            {
                loss = null;
            }
            // Rows from the SUMMING channels, as the screen's read-out: a drawn-but-unsummed centre would invent junctions (see VirtualCrossoverMetricsTests.BuildEntries_ReadsJunctionsOffTheSummingSet).
            List<VirtualCrossoverMetric.Entry> entries = sideMetrics.BuildEntries(summed, loss);
            // Phase gate with this side's own pin; the direct-sound loss travels whatever the Sum loss selector shows (PROTOCOL §1.8).
            VirtualCrossoverPhaseGate sideGate = session.GateFor(rightSide);
            (List<VirtualCrossoverMetric.PhaseEntry> phaseEntries, List<SignalPoint>? directLoss) =
                await frame.ReadJunctionsAsync(
                    sideMetrics, sideGate, sideGate.StoredOffsetMs, smoothing, withDirectLoss: true);
            List<VirtualCrossoverMetric.Entry> directEntries = frame.QuotesJunctions
                ? sideMetrics.BuildEntries(summed, directLoss)
                : [];
            HybridMagnitudes? hybrid = hybridReferences != null
                ? hybridReader.Build(shown, hybridReferences, rightSide, HybridSmoothingInverseOctaves)
                : null;
            // The hybrid view's sum; null, as on screen, when the sides cannot share one offset.
            IReadOnlyList<SignalPoint>? hybridSum = hybrid == null || hybridReferences == null
                ? null
                : rightSide == activeRight
                    ? hybridReader.ActiveSum(shown, hybridReferences, hybrid, hybridGate)
                    : hybridReader.OppositeSum(sideSum, hybrid.OffsetDb, hybridGate)?.Points;

            for (int index = 0; index < shown.Count; index++)
            {
                // Hybrid curves are carried shifted by the set's datum onto the impulse responses' axis, as drawn.
                IReadOnlyList<SignalPoint>? hybridProcessed = null;
                IReadOnlyList<SignalPoint>? hybridPreDsp = null;
                if (hybrid != null && hybridReferences != null && !hybrid.PointMeasuredChannels[index])
                {
                    hybridProcessed = VirtualCrossoverHybrid.ShiftedBy(hybrid.Channels[index], hybrid.OffsetDb);
                    hybridPreDsp = hybridReader.PreDspCurve(
                        shown[index].Channel, rightSide, hybridReferences[index].Points,
                        HybridSmoothingInverseOctaves, hybrid.OffsetDb);
                }

                curves[(shown[index].Channel, rightSide)] = (
                    shown[index],
                    magnitudes?[index].Points,
                    hybridPreDsp,
                    hybridProcessed);
            }
            if (others.Count > 0)
            {
                (List<AnalysisCurve>? otherMagnitudes, _, _) =
                    sideMetrics.BuildCurves(others, smoothing);
                for (int index = 0; index < others.Count; index++)
                {
                    curves[(others[index].Channel, rightSide)] =
                        (others[index], otherMagnitudes?[index].Points, null, null);
                }
            }

            var junctions = new List<AgentJunctionInputs>();
            if (frame.QuotesJunctions)
            {
                List<AdjacentPair> pairs = ProcessedChannels.GetAdjacentPairs(
                    ProcessedChannels.OrderByBand(summed));
                List<(JunctionCorrelationView? Correlation, JunctionCoherenceView? Coherence)> views =
                    await Task.Run(() => pairs.Select(pair => JunctionViews.BuildBoth(pair, all)).ToList());
                for (int index = 0; index < pairs.Count; index++)
                {
                    AdjacentPair pair = pairs[index];
                    junctions.Add(new AgentJunctionInputs(
                        pair.Lower.Channel.Name,
                        pair.Upper.Channel.Name,
                        pair.CrossoverHz,
                        pair.BandLowHz,
                        pair.BandHighHz,
                        MagnitudeOf(pair.Lower),
                        MagnitudeOf(pair.Upper),
                        views[index].Correlation,
                        views[index].Coherence));
                }
            }

            sides.Add(new AgentSideInputs(
                sideName,
                shown.Select(item => AgentChannelIds.Format(
                    item.Channel.Name,
                    item.Channel.Pair.Mono ? AgentChannelSide.Mono : sideName)).ToList(),
                sumCurve?.Points,
                hybridSum,
                loss,
                entries,
                phaseEntries,
                junctions,
                shown.Count == 0
                    ? $"no channels in {VirtualCrossoverGroupViews.DisplayName(groupView)} on this side"
                    : null,
                directLoss,
                directEntries));

            IReadOnlyList<SignalPoint>? MagnitudeOf(ProcessedChannel item) =>
                curves.TryGetValue((item.Channel, rightSide), out var found) ? found.Processed : null;
        }

        List<VirtualCrossoverMetric.StereoDelta> stereo = await metrics.ComputeStereoDeltasAsync(
            session.Channels,
            revision,
            includePair: pair => VirtualCrossoverGroupViews.IsShown(groupView, pair.Zone),
            hybridLevelDeltaDb: hybridReader.StereoLevelReader(view.HybridRequested));
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groups = activeShown.Count > 0
            ? await metrics.ComputeGroupDeltasAsync(
                activeShown, groupView, revision,
                hybridGroupLevelDeltaDb: hybridReader.GroupLevelReader(view.HybridRequested))
            : [];
        if (!coordinator.IsCurrent(revision))
        {
            return null;
        }

        var channelInputs = new List<AgentChannelInputs>();
        foreach ((string block, AgentChannelSide side, VirtualCrossoverChannel channel, bool rightSide)
            in Slots())
        {
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
            AgentSourceInputs? source = null;
            if (state.ProcessingSource != null)
            {
                bool processed = curves.TryGetValue((channel, rightSide), out var found);
                IReadOnlyList<SignalPoint>? raw = null;
                IReadOnlyList<SignalPoint>? coherence = null;
                if (processed)
                {
                    if (state.TransferImpulseResponse is { } impulseResponse)
                    {
                        raw = packageGate.Raw(
                            impulseResponse,
                            state.TransferPeakIndex,
                            state.SampleRate,
                            found.Item.MeasuredBand,
                            session.Calibration.For(found.Item)).Points;
                    }
                    if (found.Processed != null && state.TransferCoherence is { Length: > 1 } linear)
                    {
                        IReadOnlyList<double> perPoint =
                            CoherenceCurves.PerPoint(linear, found.Processed, state.SampleRate);
                        coherence = found.Processed
                            .Select((point, index) => new SignalPoint(point.X, perPoint[index]))
                            .ToList();
                    }
                }

                source = new AgentSourceInputs(
                    state.SampleRate,
                    state.MeasuredBand,
                    // The selected mode's family, not whichever capture the side holds.
                    state.SpatialAverageFor(session.SpatialAverageMode) != null ? session.SpatialAverageMode.ToString() : null,
                    // Every family held: distinguishes "no average" from "one the view is not using".
                    SpatialAverageCaptures(state),
                    raw,
                    processed ? found.Processed : null,
                    processed ? found.HybridPreDsp : null,
                    processed ? found.HybridProcessed : null,
                    coherence,
                    processed ? null : channel.Pair.Enabled ? "not processed" : "channel muted");
            }

            channelInputs.Add(new AgentChannelInputs(
                block,
                side,
                channel.Pair.Zone,
                settings.DisplayName,
                channel.Pair.Enabled,
                channel.Pair.Bypass,
                // A copy: the builder runs off the UI thread and the live object may be edited meanwhile.
                AgentOperations.CloneEditable(settings),
                session.ProcessorSampleRateHz,
                source));
        }

        DspProcessorProfile profile = session.ProcessorProfile;
        var processor = new AgentProcessorInputs(
            profile.ModelId ?? "custom",
            profile.DisplayName,
            profile.IsCustom,
            profile.SampleRateHz,
            session.Project.DspProcessorRateFollowsMeasurements,
            profile.QConvention,
            profile.MaxDelayMs,
            DspProcessorCatalog.Preset(profile.ModelId)?.MaxDelayMs != null);

        var analysis = new AgentAnalysisInputs(
            groupView,
            activeRight,
            // The package's smoothing, not the display's.
            SpectrumSmoothing.PsychoacousticBaseInverseOctaves,
            true,
            session.Project.SpatialAverageMode,
            view.HybridTicked,
            view.HybridRequested,
            HybridSmoothingInverseOctaves,
            session.Project.PhaseWindowMode,
            session.Project.PhaseFdwCycles,
            session.Project.PhaseDetrendMode,
            session.Project.PhaseGateLeftMs,
            session.Project.PhaseGatePlateauMs,
            session.Project.PhaseGateRightMs,
            session.Project.PhaseGateLeft.OffsetMs,
            session.Project.PhaseGateLeft.DetrendMs,
            session.Project.PhaseGateRight.OffsetMs,
            session.Project.PhaseGateRight.DetrendMs,
            session.Project.Calibration?.Name,
            session.Project.StereoSceneOffsetMagnitudeMs,
            session.Project.StereoRightHandDrive,
            session.Project.StereoLevelDifferenceDb,
            session.Project.RearFillOffsetMs);

        VirtualCrossoverTargetSettings targetSettings =
            session.Project.Target ?? new VirtualCrossoverTargetSettings();
        EqTargetCurve target = (view.TargetCurve ?? targetSettings.ToCurve()).Normalized();
        var targetInputs = new AgentTargetInputs(
            session.Project.TargetLevelDb,
            target.Preset,
            target.Spec,
            target.ToleranceDb,
            targetSettings.ImportedName);

        return new AgentPackageInputs(
            ApplicationVersionInfo.GetDisplayVersion(),
            session.Project.AiNotes,
            processor,
            analysis,
            targetInputs,
            channelInputs,
            sides,
            stereo,
            groups);
    }

    /// <summary>Every side holding a measurement, named as the package names it.</summary>
    public List<MeasuredChannel> MeasuredChannels()
    {
        var measured = new List<MeasuredChannel>();
        foreach ((string block, AgentChannelSide side, VirtualCrossoverChannel channel, bool rightSide)
            in Slots())
        {
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            if (state.TransferImpulseResponse is { } impulseResponse)
            {
                measured.Add(new MeasuredChannel(
                    AgentChannelIds.Format(block, side), impulseResponse,
                    state.TransferPeakIndex, state.SampleRate, state.MeasuredBand));
            }
        }

        return measured;
    }

    // The project's phase gate, window mode and cycles, with the offset left for the channel's own arrival.
    public PhaseAnalysisSettings GroupDelayWindow() => new(
        session.Project.PhaseWindowMode,
        session.Project.PhaseFdwCycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: 0.0,
        session.Project.PhaseGateLeftMs,
        session.Project.PhaseGatePlateauMs,
        session.Project.PhaseGateRightMs,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    // The excess-group-delay menu item's reading, for a probe that asks for it by name.
    public async Task<IReadOnlyList<AgentDiagnosticSeries>> ExcessGroupDelaySeriesAsync()
    {
        List<MeasuredChannel> measured = MeasuredChannels();
        if (measured.Count == 0)
        {
            return [];
        }

        PhaseAnalysisSettings gate = GroupDelayWindow();
        return await Task.Run(() =>
        {
            var curves = new List<AgentDiagnosticChannel>(measured.Count);
            foreach (MeasuredChannel channel in measured)
            {
                if (ExcessGroupDelayCurve(channel, gate) is { } curve)
                {
                    curves.Add(new AgentDiagnosticChannel(channel.Id, curve));
                }
            }

            return AgentDiagnosticBuilder.ExcessGroupDelaySeries(curves);
        });
    }

    public static IReadOnlyList<SignalPoint>? ExcessGroupDelayCurve(
        MeasuredChannel channel, PhaseAnalysisSettings window) =>
        ExcessGroupDelayCurve(
            channel.Response, channel.PeakIndex, channel.SampleRate, channel.Band, window);

    // Excess group delay at the channel's own arrival: minimum-phase part removed, leaving arrivals and reflections. Pure, runs off the UI thread. See docs/tech/agent-bridge.md#excess-group-delay-diagnostic.
    private static IReadOnlyList<SignalPoint>? ExcessGroupDelayCurve(
        Complex[] impulseResponse, int peakIndex, int sampleRate, MeasuredBand band,
        PhaseAnalysisSettings window)
    {
        int anchorIndex = ProcessedChannels.StartAnchorIndex(impulseResponse, peakIndex, sampleRate);
        GroupDelaySpectra spectra = DataHelper.GetGroupDelayAnalysisSpectra(
            new ImpulseMeasurementView(impulseResponse, anchorIndex, sampleRate),
            window with { GateOffsetMs = anchorIndex * 1_000.0 / sampleRate },
            out int extractionStart);
        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            spectra,
            extractionStart,
            sampleRate,
            window,
            // Group-delay default (1/12 octave), not psychoacoustic: that is a hearing model for levels, not time.
            FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves,
            includeMinimumPhase: true,
            lowestMeasuredFrequencyHz: band.LowEdgeHz,
            highestMeasuredFrequencyHz: band.HighEdgeHz);
        return curves.Excess?.Points;
    }

    // Mode enum names, so the per-channel list and analysis.spatialAverage.mode agree.
    private static IReadOnlyList<string> SpatialAverageCaptures(VirtualCrossoverChannelState state)
    {
        var captures = new List<string>(2);
        if (state.SpatialAverage != null)
        {
            captures.Add(VirtualCrossoverSpatialAverageMode.MovingMic.ToString());
        }

        if (state.ArrayCapture != null)
        {
            captures.Add(VirtualCrossoverSpatialAverageMode.MicArray.ToString());
        }

        return captures;
    }
}
