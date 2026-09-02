using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>
/// The panel's side of the Agent Bridge: the snapshot the proposal validator
/// judges a reply against, and the gathering of everything a package is built
/// from. The gathering runs the SAME computations the screen runs — the
/// coordinator's processed responses, the metric block's curves and read-outs,
/// the lower plot's junction views — so a number in the package is a number on
/// screen. The panel does no formatting here; the builder does no reading.
/// </summary>
public partial class VirtualCrossoverPanel
{
    // The id of the package this session most recently copied; a reply naming
    // another one gets a warning in the review. Not persisted — a reopened
    // session cannot vouch for what an earlier one copied, and the expected
    // current values are the guard that matters.
    private string? lastAgentPackageId;

    // One bridge operation at a time: a second Copy while the first gathers
    // would race the coordinator, and an import while a copy gathers would move
    // the settings the package is being read from.
    private bool agentBusy;

    private ContextMenuStrip? agentMenu;

    /// <summary>Records the package this session just copied, for the review's correlation warning.</summary>
    internal void RememberAgentPackage(string packageId) => lastAgentPackageId = packageId;

    // The same two-state toggle the Target button's menu uses: a click while the
    // menu is open closes it; the menu is rebuilt per click so its enabled states
    // are current.
    private void ShowAgentMenu()
    {
        if (agentMenu is { Visible: true })
        {
            agentMenu.Close();
            return;
        }

        agentMenu?.Dispose();
        agentMenu = new ContextMenuStrip();
        agentMenu.Items.Add(new ToolStripMenuItem(
            "Copy for AI",
            null,
            async (_, _) => await CopyForAiAsync())
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Copies the current Virtual DSP settings, your notes and a diagnostic " +
                "summary to the clipboard, ready to paste into a chat assistant. " +
                "Nothing is sent anywhere: you paste it yourself.")
        });
        agentMenu.Items.Add(new ToolStripMenuItem(
            "Import AI proposal…",
            null,
            (_, _) => ImportAiProposal())
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Reads the assistant's reply from the clipboard (copy the whole reply), " +
                "shows every proposed change against the current value, and applies " +
                "only the rows you tick.")
        });
        agentMenu.Items.Add(new ToolStripSeparator());
        agentMenu.Items.Add(new ToolStripMenuItem(
            "Undo AI import",
            null,
            (_, _) => UndoAiImport())
        {
            Enabled = agentUndo != null,
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Puts the channels back exactly as they were before the last import. " +
                "One step; gone once a session is loaded.")
        });
        DropDownMenu.ShowUnder(buttonAi, agentMenu);
    }

    // What the last import wrote, for Undo, and the project generation it was
    // written into: a session loaded since has different settings objects, and
    // the entries would restore into ones nobody displays.
    private List<AgentUndoEntry>? agentUndo;
    private long agentUndoGeneration;

    /// <summary>
    /// Import AI proposal: clipboard → strict parse → review against the live
    /// settings → the dialog → a second review of the ticked rows against the
    /// settings as they are at commit → one write, one save, one redraw.
    /// </summary>
    private void ImportAiProposal()
    {
        if (agentBusy)
        {
            return;
        }

        agentBusy = true;
        RefreshAutoActionsEnabled();
        try
        {
            if (!AgentClipboard.TryRead(out string? text, out string? error))
            {
                ShowError("The AI proposal was not imported.", error!);
                return;
            }

            AgentProposalParseResult parsed = AgentProposalParser.Parse(text);
            if (!parsed.Succeeded)
            {
                ShowError("The AI proposal was not imported.", parsed.Error!);
                return;
            }

            AgentProposal proposal = parsed.Proposal!;
            AgentProposalReview review =
                AgentProposalValidator.Review(proposal, BuildAgentSessionSnapshot());
            HashSet<string> selected;
            using (var dialog = new AgentProposalDialog(review))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    return;
                }

                selected = dialog.Selected.Select(verdict => verdict.Id).ToHashSet(StringComparer.Ordinal);
            }

            string? problem = AgentProposalApplier.Prepare(
                proposal, selected, BuildAgentSessionSnapshot(), out List<AgentOperationVerdict> toApply);
            if (problem != null)
            {
                ShowError("Nothing was applied.", problem);
                return;
            }

            List<AgentUndoEntry> undo = AgentProposalApplier.Apply(toApply);
            RefreshChannelsAfterAgentWrite(undo);
            agentUndo = undo;
            agentUndoGeneration = projectGeneration;
            ScheduleSave();
            RedrawAll();

            int proposed = review.Verdicts.Count;
            MessageBox.Show(
                FindForm(),
                $"Applied {toApply.Count} of {proposed} proposed change{(proposed == 1 ? "" : "s")}. " +
                "Undo AI import is in the same menu.",
                "Import AI proposal",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("The AI proposal was not imported.", exception.Message);
        }
        finally
        {
            agentBusy = false;
            RefreshAutoActionsEnabled();
        }
    }

    private void UndoAiImport()
    {
        if (agentUndo == null || agentBusy)
        {
            return;
        }
        if (agentUndoGeneration != projectGeneration)
        {
            agentUndo = null;
            ShowError("Nothing to undo.", "A session was loaded since the last AI import.");
            return;
        }

        List<AgentUndoEntry> undo = agentUndo;
        agentUndo = null;
        AgentProposalApplier.Restore(undo);
        RefreshChannelsAfterAgentWrite(undo);
        ScheduleSave();
        RedrawAll();
    }

    // The blocks whose settings an import (or its undo) wrote, refreshed the way
    // every other programmatic write refreshes them — the control shows the
    // active side, so a write to the other side shows when the side flips.
    private void RefreshChannelsAfterAgentWrite(IReadOnlyList<AgentUndoEntry> entries)
    {
        foreach (AgentUndoEntry entry in entries)
        {
            foreach ((_, _, VirtualCrossoverChannel channel, bool rightSide) in AgentChannelSlots())
            {
                if (ReferenceEquals(channel.SideSettings(rightSide), entry.Target))
                {
                    ApplySettingsToControl(channel);
                    UpdatePeqReadouts(channel);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Copy for AI: gathers the package at one revision (once more if the session
    /// moved underneath), builds it off the UI thread, and only then puts the whole
    /// text on the clipboard in one write — a failure anywhere copies nothing, so
    /// the clipboard never holds a partial or an older package.
    /// </summary>
    private async Task CopyForAiAsync()
    {
        if (agentBusy)
        {
            return;
        }

        agentBusy = true;
        RefreshAutoActionsEnabled();
        try
        {
            AgentPackageInputs? inputs = await CaptureAgentPackageInputsAsync() ??
                await CaptureAgentPackageInputsAsync();
            if (IsDisposed)
            {
                return;
            }
            if (inputs == null)
            {
                ShowError(
                    "The AI package was not copied.",
                    "The settings changed while the package was being gathered. Try again.");
                return;
            }

            Guid packageId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AgentPackageBuildResult result = await Task.Run(
                () => AgentPackageBuilder.Build(inputs, packageId, now));
            if (IsDisposed)
            {
                return;
            }
            if (!result.Succeeded)
            {
                ShowError("The AI package was not copied.", result.Error!);
                return;
            }
            if (!AgentClipboard.TryWrite(result.Text!, out string? error))
            {
                ShowError("The AI package was not copied.", error!);
                return;
            }

            RememberAgentPackage(packageId.ToString("D"));
            string omitted = result.Omitted.Count > 0
                ? Environment.NewLine + "Left out to fit the size limit: " +
                    string.Join(", ", result.Omitted) + "."
                : string.Empty;
            MessageBox.Show(
                FindForm(),
                $"AI package copied ({(result.JsonBytes + 1023) / 1024} KB). " +
                "Paste it into a chat assistant." + omitted,
                "Copy for AI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("The AI package was not copied.", exception.Message);
        }
        finally
        {
            agentBusy = false;
            RefreshAutoActionsEnabled();
        }
    }

    /// <summary>The channels as the bridge names them, with their live settings.</summary>
    internal AgentSessionSnapshot BuildAgentSessionSnapshot() =>
        new(
            AgentChannelSlots()
                .Select(slot => new AgentChannelSnapshot(
                    slot.Block, slot.Side, slot.Channel.SideSettings(slot.RightSide)))
                .ToList(),
            ProcessorSampleRateHz,
            ProcessorProfile.MaxDelayMs,
            lastAgentPackageId);

    // Every physical channel in block order: a stereo block yields its left and
    // right slots, a mono block its single one (routed to the left slot, as the
    // panel routes it everywhere).
    private IEnumerable<(string Block, AgentChannelSide Side, VirtualCrossoverChannel Channel, bool RightSide)>
        AgentChannelSlots()
    {
        for (int index = 0; index < channels.Count; index++)
        {
            VirtualCrossoverChannel channel = channels[index];
            string block = ChannelNameFor(index);
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

    /// <summary>
    /// Everything a package is built from, read off the current session: both
    /// sides processed through the coordinator (its cache makes the on-screen
    /// side free), the metric block's curves and read-outs per side, the junction
    /// views per adjacent pair, and the stereo and group deltas. Null when the
    /// session changed underneath the gathering — the caller retries once.
    /// </summary>
    internal async Task<AgentPackageInputs?> CaptureAgentPackageInputsAsync()
    {
        long revision = processingCoordinator.CurrentRevision;
        VirtualCrossoverGroupView groupView = SelectedGroupView;
        bool activeRight = project.ActiveSideRight;
        int smoothing = magnitudeGate.SmoothingInverseOctaves;

        var sides = new List<AgentSideInputs>();
        var curves = new Dictionary<
            (VirtualCrossoverChannel Channel, bool RightSide),
            (ProcessedChannel Item, IReadOnlyList<SignalPoint>? Processed, IReadOnlyList<SignalPoint>? Hybrid)>();
        List<ProcessedChannel> activeShown = [];
        foreach (bool rightSide in new[] { false, true })
        {
            AgentChannelSide sideName = rightSide ? AgentChannelSide.Right : AgentChannelSide.Left;
            VirtualCrossoverSideSum? sideSum = await metrics.ComputeSideSumAsync(
                channels, rightSide, revision, minimumChannels: 1);
            if (!processingCoordinator.IsCurrent(revision))
            {
                return null;
            }
            if (sideSum == null)
            {
                sides.Add(new AgentSideInputs(
                    sideName, [], null, null, [], [], [], "no channel with a source on this side"));
                continue;
            }

            // The frame's own filtering: what the view draws, and of that, what it
            // sums (see RedrawMainPlotAsync). Channels outside the view still get
            // their own curves below, built as a set of their own.
            List<ProcessedChannel> all = sideSum.Channels.ToList();
            List<ProcessedChannel> shown = ChannelsShownBy(all, groupView);
            List<ProcessedChannel> summed = ChannelsSummedBy(shown, groupView);
            List<ProcessedChannel> others = all.Except(shown).ToList();
            if (rightSide == activeRight)
            {
                activeShown = shown;
            }

            // The opposite side windows through ITS gate placement, never the
            // active side's pin — the same rule the on-screen opposite sum follows.
            VirtualCrossoverMetrics sideMetrics = rightSide == activeRight
                ? metrics
                : new VirtualCrossoverMetrics(
                    processingCoordinator,
                    BuildOppositeSideMagnitudeCurve,
                    CalibrationFor,
                    BuildOppositeSideSumCurve);

            List<AnalysisCurve>? magnitudes = null;
            AnalysisCurve? sumCurve = null;
            List<SignalPoint>? loss = null;
            if (shown.Count > 0)
            {
                (magnitudes, sumCurve, loss) = sideMetrics.BuildCurves(shown, smoothing, summed);
            }

            bool quotesJunctions =
                VirtualCrossoverGroupViews.LossChainZone(groupView) != null &&
                ProcessedChannels.HasJunction(summed);
            if (!quotesJunctions)
            {
                loss = null;
            }
            List<VirtualCrossoverMetric.Entry> entries = sideMetrics.BuildEntries(shown, loss);
            // The junction phase block reads through the phase gate, placed over
            // the SUMMING channels with THIS side's pin — the same call the frame
            // makes for the active side (see RedrawMainPlotAsync), off the UI
            // thread, with only numbers crossing over.
            List<VirtualCrossoverMetric.PhaseEntry> phaseEntries = [];
            if (quotesJunctions)
            {
                int phaseRate = summed[0].SampleRate;
                double? pinnedOffsetMs = project.PhaseGateFor(rightSide).OffsetMs;
                double gateLeftMs = gatePreview?.LeftMs ?? project.PhaseGateLeftMs;
                double gatePlateauMs = gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs;
                double gateRightMs = gatePreview?.RightMs ?? project.PhaseGateRightMs;
                phaseEntries = await Task.Run(() => sideMetrics.BuildPhaseEntries(
                    summed,
                    ordered => JunctionPhaseSpectra.Build(
                        ordered, phaseRate, pinnedOffsetMs,
                        gateLeftMs, gatePlateauMs, gateRightMs)));
            }
            HybridMagnitudes? hybrid = HybridRequested && magnitudes != null
                ? BuildHybridMagnitudes(shown, magnitudes, rightSide, smoothing)
                : null;

            for (int index = 0; index < shown.Count; index++)
            {
                curves[(shown[index].Channel, rightSide)] = (
                    shown[index],
                    magnitudes?[index].Points,
                    hybrid?.Channels[index]);
            }
            if (others.Count > 0)
            {
                (List<AnalysisCurve>? otherMagnitudes, _, _) =
                    sideMetrics.BuildCurves(others, smoothing);
                for (int index = 0; index < others.Count; index++)
                {
                    curves[(others[index].Channel, rightSide)] =
                        (others[index], otherMagnitudes?[index].Points, null);
                }
            }

            var junctions = new List<AgentJunctionInputs>();
            if (quotesJunctions)
            {
                List<AdjacentPair> pairs = ProcessedChannels.GetAdjacentPairs(
                    ProcessedChannels.OrderByBand(summed));
                List<(JunctionCorrelationView? Correlation, JunctionCoherenceView? Coherence)> views =
                    await Task.Run(() => pairs.Select(pair => BuildJunctionViews(pair, all)).ToList());
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
                loss,
                entries,
                phaseEntries,
                junctions,
                shown.Count == 0
                    ? $"no channels in {VirtualCrossoverGroupViews.DisplayName(groupView)} on this side"
                    : null));

            IReadOnlyList<SignalPoint>? MagnitudeOf(ProcessedChannel item) =>
                curves.TryGetValue((item.Channel, rightSide), out var found) ? found.Processed : null;
        }

        List<VirtualCrossoverMetric.StereoDelta> stereo = await metrics.ComputeStereoDeltasAsync(
            channels,
            revision,
            includePair: pair => VirtualCrossoverGroupViews.IsShown(groupView, pair.Zone),
            hybridLevelDeltaDb: HybridStereoLevelReader());
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groups = activeShown.Count > 0
            ? await metrics.ComputeGroupDeltasAsync(
                activeShown, groupView, revision,
                hybridGroupLevelDeltaDb: HybridGroupLevelReader())
            : [];
        if (!processingCoordinator.IsCurrent(revision))
        {
            return null;
        }

        var channelInputs = new List<AgentChannelInputs>();
        foreach ((string block, AgentChannelSide side, VirtualCrossoverChannel channel, bool rightSide)
            in AgentChannelSlots())
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
                        raw = BuildRawMagnitudeCurve(
                            impulseResponse,
                            state.TransferPeakIndex,
                            state.SampleRate,
                            found.Item.MeasuredBand,
                            CalibrationFor(found.Item)).Points;
                    }
                    if (found.Processed != null && state.TransferCoherence is { Length: > 1 } linear)
                    {
                        IReadOnlyList<double> perPoint =
                            CoherencePerPoint(linear, found.Processed, state.SampleRate);
                        coherence = found.Processed
                            .Select((point, index) => new SignalPoint(point.X, perPoint[index]))
                            .ToList();
                    }
                }

                source = new AgentSourceInputs(
                    state.SampleRate,
                    state.MeasuredBand,
                    state.SpatialAverage != null ? "MovingMic" : state.ArrayCapture != null ? "MicArray" : null,
                    raw,
                    processed ? found.Processed : null,
                    processed ? found.Hybrid : null,
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
                // A copy: the builder runs off the UI thread after this method
                // returns, and the live object may be edited meanwhile — the
                // package must describe one revision, the one the curves are from.
                AgentOperations.CloneEditable(settings),
                ProcessorSampleRateHz,
                source));
        }

        DspProcessorProfile profile = ProcessorProfile;
        var processor = new AgentProcessorInputs(
            profile.ModelId ?? "custom",
            profile.DisplayName,
            profile.IsCustom,
            profile.SampleRateHz,
            ProcessorRateFollowsMeasurements,
            profile.QConvention,
            profile.MaxDelayMs,
            DspProcessorCatalog.Preset(profile.ModelId)?.MaxDelayMs != null);

        var analysis = new AgentAnalysisInputs(
            groupView,
            activeRight,
            // The project's own figure, not the gate snapshot's code (which folds
            // the psychoacoustic flag into its sign).
            project.SmoothingInverseOctaves,
            project.PsychoacousticSmoothing,
            project.SpatialAverageMode,
            HybridRequested,
            project.PhaseWindowMode,
            project.PhaseFdwCycles,
            project.PhaseDetrendMode,
            project.PhaseGateLeftMs,
            project.PhaseGatePlateauMs,
            project.PhaseGateRightMs,
            project.PhaseGateLeft.OffsetMs,
            project.PhaseGateLeft.DetrendMs,
            project.PhaseGateRight.OffsetMs,
            project.PhaseGateRight.DetrendMs,
            project.Calibration?.Name,
            project.StereoSceneOffsetMagnitudeMs,
            project.StereoRightHandDrive,
            project.StereoLevelDifferenceDb,
            project.RearFillOffsetMs);

        VirtualCrossoverTargetSettings targetSettings =
            project.Target ?? new VirtualCrossoverTargetSettings();
        EqTargetCurve target = (targetCurve ?? targetSettings.ToCurve()).Normalized();
        var targetInputs = new AgentTargetInputs(
            project.TargetLevelDb,
            target.Preset,
            target.Spec,
            target.ToleranceDb,
            targetSettings.ImportedName);

        return new AgentPackageInputs(
            ApplicationVersionInfo.GetDisplayVersion(),
            project.AiNotes,
            processor,
            analysis,
            targetInputs,
            channelInputs,
            sides,
            stereo,
            groups);
    }

    // Both junction views of one pair, off the UI thread; a view that fails is
    // reported as missing rather than failing the package — the same best-effort
    // rule the lower plot's redraw follows.
    private static (JunctionCorrelationView?, JunctionCoherenceView?) BuildJunctionViews(
        AdjacentPair pair, IReadOnlyList<ProcessedChannel> scope)
    {
        JunctionCorrelationView? correlation = null;
        JunctionCoherenceView? coherence = null;
        try
        {
            correlation = BuildCorrelationView(pair, scope);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Agent package correlation view failed: {exception}");
        }
        try
        {
            coherence = BuildCoherenceView(pair, scope);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine($"Agent package coherence view failed: {exception}");
        }

        return (correlation, coherence);
    }

    private GatedMagnitude BuildOppositeSideMagnitudeCurve(
        Complex[] impulseResponse,
        int anchorIndex,
        int sampleRate,
        MeasuredBand band,
        CalibrationFile? calibration)
    {
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        return BuildGatedMagnitudeCurve(
            snapshot,
            impulseResponse,
            anchorIndex,
            sampleRate,
            snapshot.ResolveGateOffsetMs(oppositeSide: true, anchorIndex, sampleRate),
            band,
            calibration);
    }

    private GatedMagnitude BuildOppositeSideSumCurve(
        IReadOnlyList<ProcessedChannel> channels, int anchorIndex)
    {
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        return BuildMeasuredSumCurve(
            snapshot,
            channels,
            anchorIndex,
            snapshot.ResolveGateOffsetMs(
                oppositeSide: true, anchorIndex, channels.Count > 0 ? channels[0].SampleRate : 0));
    }
}
