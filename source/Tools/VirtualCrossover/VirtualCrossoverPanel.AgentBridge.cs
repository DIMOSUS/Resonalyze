using System.Globalization;
using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>Panel side of the Agent Bridge: the review snapshot and package gathering, which reuses the screen's own computations so package numbers match the screen. See docs/tech/agent-bridge.md#package-gathering.</summary>
public partial class VirtualCrossoverPanel
{
    // Id and fingerprint of the last copied package; a reply naming another package or a changed session is warned and its engine requests refused. Not persisted.
    private string? lastAgentPackageId;
    private string? lastAgentPackageFingerprint;

    // One bridge operation at a time: a concurrent Copy or import would race the coordinator or move the settings being read.
    private bool agentBusy;

    private ContextMenuStrip? agentMenu;

    // The package grid is 12 points/octave: the nearest a grid gets to the hybrid view's Off smoothing.
    private const int AgentHybridSmoothingInverseOctaves = 12;

    /// <summary>EQ Wizard Auto Tune settings an import fits a bank with; the wizard's opening values when unwired.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<EqAutoTunePolicy>? AutoTunePolicyProvider { get; set; }

    internal void RememberAgentPackage(string packageId, string fingerprint)
    {
        lastAgentPackageId = packageId;
        lastAgentPackageFingerprint = fingerprint;
    }

    /// <summary>The session as one hash for the review's staleness check. See docs/tech/agent-bridge.md#session-fingerprint.</summary>
    internal string ComputeAgentFingerprint()
    {
        var lines = new List<string>
        {
            $"processor;{ProcessorProfile.ModelId};{ProcessorSampleRateHz}",
            $"average;{SpatialAverageMode};{checkBoxHybrid.Checked}",
            // Engines read the shown side and the view (Auto crossover, single-sided Auto delay, Auto-tune's source).
            $"view;{project.ActiveSideRight};{SelectedGroupView}",
            $"phase;{project.PhaseWindowMode};{project.PhaseFdwCycles};{project.PhaseDetrendMode};" +
                $"{Number(project.PhaseGateLeftMs)};{Number(project.PhaseGatePlateauMs)};" +
                $"{Number(project.PhaseGateRightMs)};" +
                $"{Number(project.PhaseGateLeft.OffsetMs)};{Number(project.PhaseGateLeft.DetrendMs)};" +
                $"{Number(project.PhaseGateRight.OffsetMs)};{Number(project.PhaseGateRight.DetrendMs)}",
            $"stereo;{Number(project.StereoSceneOffsetMagnitudeMs)};{project.StereoRightHandDrive};" +
                $"{Number(project.StereoLevelDifferenceDb)};{Number(project.RearFillOffsetMs)}",
            // By id AND points: a curve re-read under the same id is a different correction.
            $"calibration;{project.CalibrationId};{ownCalibrationSelected};{Curve(Calibration)}",
            $"target;{Number((double)numericTargetLevel.Value)};{TargetShape(project.Target)}",
            $"notes;{project.AiNotes}"
        };
        foreach ((string block, AgentChannelSide side, VirtualCrossoverChannel channel, bool rightSide)
            in AgentChannelSlots())
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
                Curve(CalibrationFor(state)),
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

    // Same two-state toggle as the Target menu; rebuilt per click so enabled states are current.
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
        var diagnostics = new ToolStripMenuItem("Copy diagnostics for AI")
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Smaller texts the assistant may ask for by name, beside the package: " +
                "copy the one it named and paste it into the same chat.")
        };
        diagnostics.DropDownItems.Add(new ToolStripMenuItem(
            "Excess group delay",
            null,
            async (_, _) => await CopyExcessGroupDelayForAiAsync())
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Each measured channel's excess group delay, read through the project's " +
                "phase gate and window. Under FDW a windowed reading; excess that lives " +
                "only at a band edge can be the gate truncating a steep filter's ringing.")
        });
        agentMenu.Items.Add(diagnostics);
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

    // The last import's undo and the project generation it wrote into: after a session load the entries would restore into settings nobody displays.
    private AgentImportUndo? agentUndo;
    private long agentUndoGeneration;

    /// <summary>Pre-import state. Every channel's chain is taken: engines write channels no row names, and the crossover wizard can reorder blocks.</summary>
    // Scene, tilt and rear-fill offset are committed by Auto delay (CommitAutoDelayResult), so undo carries them.
    private sealed record AgentImportUndo(
        IReadOnlyList<AgentUndoEntry> Channels,
        VirtualCrossoverSpatialAverageMode? SpatialAverageMode,
        bool HybridTicked,
        IReadOnlyList<VirtualCrossoverChannel> Order,
        double SceneOffsetMagnitudeMs,
        bool RightHandDrive,
        double StereoLevelDifferenceDb,
        double RearFillOffsetMs,
        double TargetLevelDb);

    /// <summary>Import AI proposal. See docs/tech/agent-bridge.md#import-flow.</summary>
    private async void ImportAiProposal()
    {
        if (agentBusy)
        {
            return;
        }

        agentBusy = true;
        RefreshAutoActionsEnabled();
        var summary = new List<string>();
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
            AgentSessionSnapshot reviewedSession = BuildAgentSessionSnapshot();
            AgentProposalReview review =
                AgentProposalValidator.Review(proposal, reviewedSession);
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
                proposal, selected, reviewedSession.Fingerprint, BuildAgentSessionSnapshot(),
                out List<AgentOperationVerdict> toApply, out List<string> unseenWarnings);
            if (problem != null)
            {
                ShowError("Nothing was applied.", problem);
                return;
            }
            // The ticked subset can leave a state the review never showed: warn, do not refuse.
            if (unseenWarnings.Count > 0 &&
                MessageBox.Show(
                    FindForm(),
                    "With only the ticked rows applied:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, unseenWarnings) + Environment.NewLine + Environment.NewLine +
                    "Apply anyway?",
                    "Import AI proposal",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            bool ran = await AgentProgressDialog.RunAsync(
                FindForm(),
                "Import AI proposal",
                "Reading the reply…",
                async progress =>
                {
                    // Probes run before any write: they answer about the tune as it stands.
                    await RunAgentProbesAsync(toApply, summary, progress);

                    return await CommitAgentImportAsync(
                        proposal, selected, reviewedSession.Fingerprint,
                        review.Verdicts.Count, summary, progress);
                });

            ScheduleSave();
            RedrawAll();
            MessageBox.Show(
                FindForm(),
                string.Join(Environment.NewLine, summary) + Environment.NewLine +
                Environment.NewLine + "Undo AI import is in the same menu.",
                "Import AI proposal",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Summary entries did happen; do not claim 'not imported' over them.
            ShowError(
                summary.Count == 0
                    ? "The AI proposal was not imported."
                    : "The AI proposal was imported only in part.",
                summary.Count == 0
                    ? exception.Message
                    : string.Join(Environment.NewLine, summary) + Environment.NewLine +
                        Environment.NewLine + "Then it stopped: " + exception.Message +
                        Environment.NewLine + Environment.NewLine +
                        "Undo AI import puts the whole import back.");
        }
        finally
        {
            agentBusy = false;
            RefreshAutoActionsEnabled();
        }
    }

    /// <summary>Runs engine requests in a fixed order regardless of reply order; cancelling one skips only it. Returns whether any changed the project. See docs/tech/agent-bridge.md#engine-order.</summary>
    private async Task<bool> RunAgentEngineRequests(
        IReadOnlyList<AgentOperationVerdict> toApply,
        List<string> summary,
        AgentProgressDialog? progress = null)
    {
        bool ran = false;
        // One target level for every fit of this import: the stated one (the review made them agree), else the project's.
        double importTargetLevelDb = ImportTargetLevelDb(toApply, (double)numericTargetLevel.Value);
        EqAutoTunePolicy policy = AutoTunePolicyProvider?.Invoke() ?? EqAutoTunePolicy.Default;
        // Iterate verdicts: the snapshot's settings object still names the channel after the crossover wizard re-letters blocks.
        foreach (AgentOperationVerdict verdict in toApply
            .Where(verdict => verdict.Applicable)
            .Where(verdict => verdict.Operation is not AgentSettingsOperation)
            .OrderBy(verdict => AgentEngineOrder(verdict.Operation!)))
        {
            AgentOperation operation = verdict.Operation!;
            if (operation is not ProbeOperation)
            {
                progress?.Report(AgentStepText(operation, verdict));
            }

            switch (operation)
            {
                case UseSpatialAverageOperation spatial:
                    bool applied = ApplyAgentSpatialAverage(spatial);
                    summary.Add(applied
                        ? $"Spatial average: {spatial.Mode}, hybrid on."
                        : $"Spatial average: skipped ('{spatial.Mode}' names no capture family).");
                    ran |= applied;
                    break;

                case RunAutoCrossoverOperation:
                    string? refused = OpenAutoSetupWizard();
                    summary.Add(refused == null
                        ? "Auto crossover: applied."
                        : $"Auto crossover: skipped ({refused}).");
                    ran |= refused == null;
                    break;

                case TuneJunctionOperation junction:
                    ran |= await RunAgentTuneJunctionAsync(junction, summary);
                    break;

                // Probes already ran before any write.
                case ProbeOperation:
                    break;

                case RunAutoDelayOperation delay:
                    ran |= await RunAgentAutoDelayAsync(delay, summary);
                    break;

                case AutoTunePeqOperation tune:
                    ran |= await RunAgentAutoTuneAsync(
                        tune, verdict.Channel!, importTargetLevelDb, policy, summary);
                    break;

                // Unreachable: the review refuses operations this build does not run.
                default:
                    summary.Add(
                        $"{operation.Parameter}: skipped " +
                        "(not available in this version of Resonalyze).");
                    break;
            }
        }

        return ran;
    }

    private static string AgentStepText(AgentOperation operation, AgentOperationVerdict verdict) =>
        operation switch
        {
            UseSpatialAverageOperation spatial => $"Spatial average: {spatial.Mode}…",
            RunAutoCrossoverOperation => "Auto crossover: the wizard is opening…",
            TuneJunctionOperation junction =>
                $"Junction tune {verdict.ChannelLabel}: searching the crossover…",
            RunAutoDelayOperation => "Auto delay: searching delays and polarities…",
            AutoTunePeqOperation => $"Auto-tune {verdict.ChannelLabel}: fitting the bank…",
            _ => $"{operation.Parameter}…"
        };

    private static int AgentEngineOrder(AgentOperation operation) => operation switch
    {
        UseSpatialAverageOperation => 0,
        RunAutoCrossoverOperation => 1,
        // After the wizard, before Auto delay, which realigns whatever the crossover became.
        TuneJunctionOperation => 2,
        RunAutoDelayOperation => 3,
        _ => 4
    };

    // Mode and tick together: either alone leaves the point measurement in charge. Project events are suppressed so the import saves and redraws once.
    private bool ApplyAgentSpatialAverage(UseSpatialAverageOperation operation)
    {
        // Guard only: the review already refused unknown modes.
        if (!AgentOperations.TryParseName(
            operation.Mode, out VirtualCrossoverSpatialAverageMode mode))
        {
            return false;
        }

        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            SetSpatialAverageMode(mode);
            checkBoxHybrid.Checked = true;
            project.ShowHybridCurves = true;
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }

        // SetSpatialAverageMode returns early on an unchanged mode; the tick alone still changes what can be drawn.
        RefreshHybridAvailability();
        return true;
    }

    // The dialog's opening inputs (also the package's Current column): layout-neutral magnitudes, since the layout toggle owns signs; gain balance unticked, since the project stores the tilt, not the opt-in.
    private AgentAutoDelaySettings AgentAutoDelayDefaults() =>
        new(
            project.StereoSceneOffsetMagnitudeMs,
            project.StereoRightHandDrive,
            AdjustGains: false,
            Math.Abs(project.StereoLevelDifferenceDb),
            project.RearFillOffsetMs);

    /// <summary>Request inputs: stated values, dialog defaults for the rest. UI-free so the rule can be pinned.</summary>
    internal static AutoDelayRunRequest BuildAutoDelayRequest(
        RunAutoDelayOperation operation, AgentAutoDelaySettings defaults) =>
        new(
            operation.SceneOffsetMs ?? defaults.SceneOffsetMs,
            operation.RightHandDrive ?? defaults.RightHandDrive,
            operation.AdjustGains ?? defaults.AdjustGains,
            operation.NearSideCutDb ?? defaults.NearSideCutDb,
            operation.RearFillOffsetMs ?? defaults.RearFillOffsetMs);

    // Auto delay without its dialog: the button's checks (headless), the dialog's compute and its Apply commit.
    // The panel is disabled during compute: the dialog's modality is what kept the chain still.
    private async Task<bool> RunAgentAutoDelayAsync(
        RunAutoDelayOperation operation, List<string> summary)
    {
        (AutoDelayLaunch? launch, string? refusal) = PrepareAutoDelay(interactive: false);
        if (launch == null)
        {
            summary.Add($"Auto delay: skipped ({refusal}).");
            return false;
        }

        AutoDelayRunRequest request = BuildAutoDelayRequest(operation, AgentAutoDelayDefaults());
        AutoDelayRunResult result;
        bool wasEnabled = Enabled;
        Enabled = false;
        UseWaitCursor = true;
        try
        {
            result = await launch.Runner(request);
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                Enabled = wasEnabled;
            }
        }
        if (IsDisposed)
        {
            return false;
        }

        await ApplyConfirmedAutoDelayAsync(result);
        summary.Add(
            $"Auto delay: applied ({(launch.Stereo ? "stereo" : "single side")}, " +
            $"scene {request.SceneOffsetMs:0.00} ms {(request.RightHandDrive ? "RHD" : "LHD")}, " +
            $"gains {(request.AdjustGains ? "balanced" : "kept")}).");
        if (launch.PolarityWarning != null)
        {
            summary.Add(launch.PolarityWarning);
        }
        // A message box does not scroll: show the report's head and where the rest went.
        string[] lines = result.ReportText.Split(
            ["\r\n", "\n"], StringSplitOptions.None);
        summary.Add(lines.Length <= AutoDelayReportLinesInSummary
            ? result.ReportText
            : string.Join(Environment.NewLine, lines.Take(AutoDelayReportLinesInSummary)) +
                Environment.NewLine +
                $"… {lines.Length - AutoDelayReportLinesInSummary} more lines in the alignment log.");
        return true;
    }

    private const int AutoDelayReportLinesInSummary = 16;

    /// <summary>Writes an import. Re-judges the ticked rows against the live session first: the probes take seconds and the panel stays editable under them. Returns whether an engine changed the project.</summary>
    private async Task<bool> CommitAgentImportAsync(
        AgentProposal proposal,
        IReadOnlySet<string> selected,
        string? reviewedFingerprint,
        int proposedRows,
        List<string> summary,
        AgentProgressDialog? progress)
    {
        string? moved = AgentProposalApplier.Prepare(
            proposal, selected, reviewedFingerprint, BuildAgentSessionSnapshot(),
            out List<AgentOperationVerdict> toApply, out _);
        if (moved != null)
        {
            summary.Add($"Nothing was written: {moved}");
            return false;
        }

        // Armed before the first write: an engine can throw after the rows landed. The previous undo returns only if nothing moved.
        AgentImportUndo undo = CaptureAgentUndo();
        AgentImportUndo? previousUndo = agentUndo;
        long previousUndoGeneration = agentUndoGeneration;
        agentUndo = undo;
        agentUndoGeneration = projectGeneration;

        List<AgentUndoEntry> written = AgentProposalApplier.Apply(toApply);
        if (written.Count > 0)
        {
            RefreshChannelsAfterAgentWrite(written);
            int rows = toApply.Count(verdict => verdict.Operation is AgentSettingsOperation);
            summary.Add(
                $"Applied {rows} of {proposedRows} proposed change{(proposedRows == 1 ? "" : "s")}.");
            // The rows name their sides: the side lock takes them as written. Before the engines, whose junction-tune save would read the rows as a hand edit.
            sideLock.Remember(channels.Select(channel => channel.Pair));
        }

        bool engines = await RunAgentEngineRequests(toApply, summary, progress);
        if (written.Count == 0 && !engines)
        {
            agentUndo = previousUndo;
            agentUndoGeneration = previousUndoGeneration;
        }

        return engines;
    }

    /// <summary>A junction's two blocks as the tuner reads them: every side carrying both measurements with its own chain, or the one side asked for. A mono block is routed to both sides.</summary>
    private static (List<JunctionTuneSide> Sides, string? Refusal) BuildJunctionTuneSides(
        VirtualCrossoverChannel lower, VirtualCrossoverChannel upper, bool? rightSideOnly)
    {
        var sides = new List<JunctionTuneSide>();
        foreach (bool rightSide in new[] { false, true })
        {
            if (rightSideOnly is { } only && only != rightSide)
            {
                continue;
            }
            if (rightSide && lower.Pair.Mono && upper.Pair.Mono && rightSideOnly == null)
            {
                continue;
            }

            VirtualCrossoverChannelState lowerState = lower.SideState(rightSide && !lower.Pair.Mono);
            VirtualCrossoverChannelState upperState = upper.SideState(rightSide && !upper.Pair.Mono);
            if (lowerState.TransferImpulseResponse == null || upperState.TransferImpulseResponse == null)
            {
                continue;
            }
            if (lowerState.SampleRate != upperState.SampleRate)
            {
                return ([], "the two measurements on the " +
                    $"{(rightSide ? "right" : "left")} side have different sample rates");
            }

            sides.Add(new JunctionTuneSide(
                rightSide ? "right" : "left",
                lowerState.TransferImpulseResponse,
                lower.SideSettings(rightSide && !lower.Pair.Mono).ToChain(lower.Pair.Zone),
                upperState.TransferImpulseResponse,
                upper.SideSettings(rightSide && !upper.Pair.Mono).ToChain(upper.Pair.Zone),
                lowerState.SampleRate));
        }

        return sides.Count == 0
            ? ([], "no side has both blocks measured")
            : (sides, null);
    }

    /// <summary>Runs an import's probes on the tune as it stands, writing nothing; all probes go into one clipboard document, and a failing probe reports in its own entry. Returns whether a document reached the clipboard.</summary>
    private async Task<bool> RunAgentProbesAsync(
        IReadOnlyList<AgentOperationVerdict> toApply,
        List<string> summary,
        AgentProgressDialog? progress = null)
    {
        List<ProbeOperation> probes = toApply
            .Where(verdict => verdict.Applicable)
            .Select(verdict => verdict.Operation)
            .OfType<ProbeOperation>()
            .ToList();
        if (probes.Count == 0)
        {
            return false;
        }

        // Readings of one document should describe one session: an edit between readings is declared, not refused.
        // Compared at every reading's boundary: a change-and-revert would pass a first-to-last check. See docs/tech/agent-bridge.md#probes.
        string state = ComputeAgentFingerprint();
        bool steady = true;
        var reports = new List<AgentProbeReport>(probes.Count);
        UseWaitCursor = true;
        try
        {
            int index = 0;
            foreach (ProbeOperation probe in probes)
            {
                index++;
                progress?.Report(
                    $"Probe {index} of {probes.Count} ({probe.Probe}" +
                    $"{(probe.JunctionId is { } id ? " " + id : string.Empty)}): reading…");
                // One failing reading must not take the other probes or the import down.
                try
                {
                    reports.Add(await BuildAgentProbeReportAsync(probe));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    reports.Add(new AgentProbeReport(
                        probe.Id, probe.Probe, probe.JunctionId, null, null,
                        exception.Message.TrimEnd('.'), null, null, null, null));
                }
                if (IsDisposed)
                {
                    return false;
                }

                string after = ComputeAgentFingerprint();
                steady &= string.Equals(state, after, StringComparison.Ordinal);
                state = after;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
            }
        }

        // Same rule as the diagnostic: link to the package only while this is the session it was copied from.
        bool matches = lastAgentPackageFingerprint != null &&
            lastAgentPackageFingerprint == state;
        AgentProbeBuildResult result = AgentProbeBuilder.Build(
            reports, matches ? lastAgentPackageId : null, matches, steady, DateTimeOffset.UtcNow);
        // Series probes are not thinned for chat, but an untrusted reply must not grow the clipboard unboundedly.
        if (result.JsonBytes > AgentProtocol.MaxProbeDocumentBytes)
        {
            summary.Add(
                $"Probe: the reading came to {(result.JsonBytes + 1023) / 1024} KB, over the " +
                $"{AgentProtocol.MaxProbeDocumentBytes / 1024} KB ceiling, and was not copied. " +
                "Ask for fewer series, fewer channels or a lower density.");
            return false;
        }
        if (!AgentClipboard.TryWrite(result.Text, out string? error))
        {
            summary.Add($"Probe: the reading was computed but not copied ({error}).");
            return false;
        }

        int answered = reports.Count(report => report.Unavailable == null);
        summary.Add(
            $"Probe: {answered} of {reports.Count} reading{(reports.Count == 1 ? "" : "s")} " +
            $"computed and copied to the clipboard ({(result.JsonBytes + 1023) / 1024} KB). " +
            "Nothing in the tune was changed — paste the clipboard into the same chat as your reply.");
        foreach (AgentProbeReport report in reports.Where(item => item.Unavailable != null))
        {
            summary.Add($"  {report.Probe} {report.JunctionId ?? string.Empty}: {report.Unavailable}");
        }
        if (!steady)
        {
            summary.Add(
                "  The tune changed while the readings were being taken, so they do not all " +
                "describe one state; the text says so and a fresh probe would settle it.");
        }

        return true;
    }

    // Readings come off snapshots, so the compute runs off the UI thread and the tune is never touched.
    private async Task<AgentProbeReport> BuildAgentProbeReportAsync(ProbeOperation probe)
    {
        if (probe.Probe == AgentProtocol.ExcessGroupDelayProbe)
        {
            IReadOnlyList<AgentDiagnosticSeries> channels = await BuildAgentExcessGroupDelaySeriesAsync();
            return new AgentProbeReport(
                probe.Id, probe.Probe, null, null, null,
                channels.Count == 0 ? "no channel has a measurement to read" : null,
                null, null, null, channels.Count == 0 ? null : channels);
        }

        AgentProbeReport Unavailable(string reason) => new(
            probe.Id, probe.Probe, probe.JunctionId, null, null, reason, null, null, null, null);

        if (probe.Probe == AgentProtocol.SeriesProbe)
        {
            // The package's own gather at the reply's density, so rows line up with the package by channel and junction id.
            AgentPackageInputs? inputs = await CaptureAgentPackageInputsAsync();
            return inputs == null
                ? Unavailable("the session changed while the reading was taken")
                : AgentSeriesProbe.Build(probe, inputs);
        }

        string? problem = AgentProposalValidator.ResolveJunction(
            BuildAgentSessionSnapshot(), probe.JunctionId ?? string.Empty,
            out AgentChannelSnapshot? lowerSnapshot, out AgentChannelSnapshot? upperSnapshot);
        if (problem != null)
        {
            return Unavailable(problem.TrimEnd('.'));
        }
        if (GateIsMisplaced)
        {
            return Unavailable("the phase gate is misplaced");
        }

        VirtualCrossoverChannel? lower = channels.FirstOrDefault(channel =>
            string.Equals(channel.Name, lowerSnapshot!.Block, StringComparison.Ordinal));
        VirtualCrossoverChannel? upper = channels.FirstOrDefault(channel =>
            string.Equals(channel.Name, upperSnapshot!.Block, StringComparison.Ordinal));
        if (lower == null || upper == null)
        {
            return Unavailable("the blocks changed while the import ran");
        }

        // A probe reads the side its junction id names: variant changes are that side's settings.
        AgentChannelSide namedSide = AgentJunctionIds.TryParse(
            probe.JunctionId, out AgentChannelSide side, out _, out _)
            ? side
            : AgentChannelSide.Left;
        (List<JunctionTuneSide> sides, string? refusal) = BuildJunctionTuneSides(
            lower, upper, namedSide == AgentChannelSide.Right);
        if (refusal != null)
        {
            return Unavailable(refusal);
        }

        int processorRate = ProcessorSampleRateHz;
        if (probe.Probe == AgentProtocol.JunctionDelayProbe)
        {
            IReadOnlyList<JunctionDelayProbeSide> read = await Task.Run(
                () => CrossoverJunctionTuner.ProbeAlignment(sides, processorRate));
            return new AgentProbeReport(
                probe.Id, probe.Probe, probe.JunctionId, lowerSnapshot!.Id, upperSnapshot!.Id,
                null, null, null,
                read.Select(item => new AgentProbeDelaySide(
                    item.Side,
                    [AgentCurveSampling.Frequency(item.BandLowHz), AgentCurveSampling.Frequency(item.BandHighHz)],
                    AgentCurveSampling.Round(item.SearchHalfWindowMs, 2),
                    item.Unavailable,
                    item.Candidates.Select(candidate => new AgentProbeDelayCandidate(
                        AgentCurveSampling.Round(candidate.ExtraDelayMs, 3),
                        candidate.InvertUpper,
                        AgentCurveSampling.Round(candidate.ScoreDb, 2),
                        AgentCurveSampling.Round(candidate.LossDb, 2),
                        AgentCurveSampling.Round(candidate.DipDb, 2),
                        candidate.Chosen)).ToList())).ToList(),
                null);
        }

        (List<JunctionProbeVariant> variants, string? variantProblem) = BuildAgentProbeVariants(
            probe, sides, lowerSnapshot!, upperSnapshot!, BuildAgentSessionSnapshot());
        if (variantProblem != null)
        {
            return Unavailable(variantProblem);
        }

        JunctionProbeResult probed;
        try
        {
            probed = await Task.Run(() => CrossoverJunctionTuner.Probe(sides, processorRate, variants));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Unavailable(exception.Message.TrimEnd('.'));
        }

        // Per-entry affected junctions, not pooled over the probe: pooled lists would point at junctions the winning variant never touched. Entries follow BuildAgentProbeVariants order, baseline first.
        AgentSessionSnapshot session = BuildAgentSessionSnapshot();
        IReadOnlyList<AgentProbeVariant> asked = probe.Variants ?? [];
        return new AgentProbeReport(
            probe.Id, probe.Probe, probe.JunctionId, lowerSnapshot!.Id, upperSnapshot!.Id, null,
            [
                AgentCurveSampling.Frequency(probed.SharedBandLowHz),
                AgentCurveSampling.Frequency(probed.SharedBandHighHz)
            ],
            probed.Entries.Select((entry, index) => AgentProbeEntryOf(
                entry, index,
                index == 0 || index > asked.Count
                    ? null
                    : AgentProposalValidator.NeighbourJunctionIds(
                        session, probe.JunctionId ?? string.Empty,
                        asked[index - 1].Changes
                            .Select(change => change.ChannelId)
                            .Distinct(StringComparer.Ordinal)
                            .ToList()))).ToList(),
            null,
            null);
    }

    // The baseline is identified by position, never by label: the reply's own labels may say "current" too.
    private static AgentProbeEntry AgentProbeEntryOf(
        JunctionProbeEntry entry, int index, IReadOnlyList<string>? affected) =>
        new(
            entry.Label,
            index == 0,
            affected is { Count: > 0 } ? affected : null,
            entry.LowerLowPass is { } low ? Edge(low) : null,
            entry.UpperHighPass is { } high ? Edge(high) : null,
            [
                AgentCurveSampling.Frequency(entry.BandLowHz),
                AgentCurveSampling.Frequency(entry.BandHighHz)
            ],
            entry.Unavailable,
            entry.Sides.Select((reading, index) => new AgentProbeSide(
                reading.Side,
                AgentCurveSampling.Round(reading.LossDb, 2),
                AgentCurveSampling.Round(reading.DipDb, 2),
                AgentCurveSampling.Round(reading.RippleDb, 2),
                index < entry.SharedBandSides.Count
                    ? new AgentProbeBandReading(
                        AgentCurveSampling.Round(entry.SharedBandSides[index].LossDb, 2),
                        AgentCurveSampling.Round(entry.SharedBandSides[index].DipDb, 2),
                        AgentCurveSampling.Round(entry.SharedBandSides[index].RippleDb, 2))
                    : null,
                entry.AfterDelay.FirstOrDefault(item => item.Side == reading.Side) is { } alignment
                    ? new AgentProbeAfterDelay(
                        AgentCurveSampling.Round(alignment.ExtraDelayMs, 3),
                        alignment.InvertUpper,
                        AgentCurveSampling.Round(alignment.LossDb, 2),
                        AgentCurveSampling.Round(alignment.DipDb, 2))
                    : null,
                entry.Phase.FirstOrDefault(item => item.Side == reading.Side)?.Result is { } phase
                    ? new AgentProbePhaseReading(
                        AgentCurveSampling.Round(phase.PhaseAtCrossoverDeg, 1),
                        AgentCurveSampling.Round(phase.PhaseConsistency, 2),
                        AgentCurveSampling.Round(phase.CurrentScore, 2),
                        AgentCurveSampling.Round(phase.BestScore, 2),
                        AgentCurveSampling.Round(phase.BestExtraDelayMs, 3),
                        phase.BestInvert,
                        AgentCurveSampling.Round(phase.FitRmsDeg, 1))
                    : null)).ToList());

    private static AgentPackageEdge Edge(CrossoverEdge edge) =>
        new(edge.Family.ToString(), AgentCurveSampling.Frequency(edge.FrequencyHz),
            edge.SlopeDbPerOctave, edge.RippleDb);

    /// <summary>Label of a probe's baseline entry; the baseline is marked by position, not by this text.</summary>
    internal const string AgentProbeCurrentLabel = "current";

    // Variant changes go onto copies of the two channels' settings, validated through the validator's path; no live setting is touched.
    private static (List<JunctionProbeVariant> Variants, string? Problem) BuildAgentProbeVariants(
        ProbeOperation probe,
        IReadOnlyList<JunctionTuneSide> sides,
        AgentChannelSnapshot lower,
        AgentChannelSnapshot upper,
        AgentSessionSnapshot session)
    {
        var variants = new List<JunctionProbeVariant>
        {
            new(AgentProbeCurrentLabel,
                sides.Select(side => new JunctionProbeChains(side.LowerChain, side.UpperChain)).ToList())
        };
        int index = 1;
        foreach (AgentProbeVariant variant in probe.Variants ?? [])
        {
            VirtualCrossoverChannelSettings lowerCopy = AgentOperations.CloneEditable(lower.Settings);
            VirtualCrossoverChannelSettings upperCopy = AgentOperations.CloneEditable(upper.Settings);
            foreach (AgentProbeChange change in variant.Changes)
            {
                bool isLower = string.Equals(change.ChannelId, lower.Id, StringComparison.Ordinal);
                if (!isLower && !string.Equals(change.ChannelId, upper.Id, StringComparison.Ordinal))
                {
                    return ([], $"'{change.ChannelId}' is not one of the junction's channels");
                }

                string? problem = AgentProposalValidator.ApplyProbeChange(
                    change, session, isLower ? lowerCopy : upperCopy);
                if (problem != null)
                {
                    return ([], problem.TrimEnd('.'));
                }
            }

            // One side, and the snapshot's settings are that side's: nothing to merge.
            var chains = new JunctionProbeChains(
                lowerCopy.ToChain(lower.Zone),
                upperCopy.ToChain(upper.Zone));
            variants.Add(new JunctionProbeVariant(
                string.IsNullOrWhiteSpace(variant.Label) ? $"variant {index}" : variant.Label,
                sides.Select(_ => chains).ToList()));
            index++;
        }

        return (variants, null);
    }

    // The excess-group-delay menu item's reading, for a probe that asks for it by name.
    private async Task<IReadOnlyList<AgentDiagnosticSeries>> BuildAgentExcessGroupDelaySeriesAsync()
    {
        var measured = new List<(string Id, Complex[] Response, int PeakIndex, int SampleRate, MeasuredBand Band)>();
        foreach ((string block, AgentChannelSide side, VirtualCrossoverChannel channel, bool rightSide)
            in AgentChannelSlots())
        {
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            if (state.TransferImpulseResponse is { } impulseResponse)
            {
                measured.Add((
                    AgentChannelIds.Format(block, side), impulseResponse,
                    state.TransferPeakIndex, state.SampleRate, state.MeasuredBand));
            }
        }
        if (measured.Count == 0)
        {
            return [];
        }

        PhaseAnalysisSettings gate = AgentGroupDelayWindow();
        return await Task.Run(() =>
        {
            var curves = new List<AgentDiagnosticChannel>(measured.Count);
            foreach ((string id, Complex[] response, int peakIndex, int sampleRate, MeasuredBand band) in measured)
            {
                IReadOnlyList<SignalPoint>? curve = BuildExcessGroupDelayCurve(
                    response, peakIndex, sampleRate, band, gate);
                if (curve != null)
                {
                    curves.Add(new AgentDiagnosticChannel(id, curve));
                }
            }

            return AgentDiagnosticBuilder.ExcessGroupDelaySeries(curves);
        });
    }

    // Junction tune without a dialog: the tuner's one crossover is written to both sides of both blocks, as the wizard writes and Undo AI import restores.
    private async Task<bool> RunAgentTuneJunctionAsync(
        TuneJunctionOperation operation, List<string> summary)
    {
        string? problem = AgentProposalValidator.ResolveJunction(
            BuildAgentSessionSnapshot(), operation.JunctionId,
            out AgentChannelSnapshot? lowerSnapshot, out AgentChannelSnapshot? upperSnapshot);
        if (problem != null)
        {
            summary.Add($"Junction tune {operation.JunctionId}: skipped ({problem.TrimEnd('.')}).");
            return false;
        }

        string label = $"Junction tune {lowerSnapshot!.Block}/{upperSnapshot!.Block}";
        VirtualCrossoverChannel? lower = channels.FirstOrDefault(channel =>
            string.Equals(channel.Name, lowerSnapshot.Block, StringComparison.Ordinal));
        VirtualCrossoverChannel? upper = channels.FirstOrDefault(channel =>
            string.Equals(channel.Name, upperSnapshot.Block, StringComparison.Ordinal));
        if (lower == null || upper == null)
        {
            summary.Add($"{label}: skipped (the blocks changed while the import ran).");
            return false;
        }
        if (GateIsMisplaced)
        {
            summary.Add($"{label}: skipped (the phase gate is misplaced).");
            return false;
        }

        (List<JunctionTuneSide> sides, string? sideRefusal) = BuildJunctionTuneSides(lower, upper, null);
        if (sideRefusal != null)
        {
            summary.Add($"{label}: skipped ({sideRefusal}).");
            return false;
        }

        double currentHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
            lowerSnapshot.Settings, upperSnapshot.Settings);
        (double defaultMinHz, double defaultMaxHz) = AgentProposalValidator.DefaultJunctionWindow(currentHz);
        var families = new List<CrossoverFilterFamily>();
        foreach (string name in operation.Families ?? [])
        {
            if (AgentOperations.TryParseName(name, out CrossoverFilterFamily family))
            {
                families.Add(family);
            }
        }
        if (families.Count == 0)
        {
            families.AddRange(AgentProposalValidator.CurrentFamilies(
                lowerSnapshot.Settings, upperSnapshot.Settings));
        }
        var options = new JunctionTuneOptions(
            families,
            operation.Slopes,
            operation.MinHz ?? defaultMinHz,
            operation.MaxHz ?? defaultMaxHz,
            // One slope for both edges unless the reply frees them: the free search costs slopes² per corner.
            operation.IndependentSlopes ?? false,
            ProcessorSampleRateHz);

        // Fingerprinted around the compute instead of disabling the panel: disable/enable repaints every plot twice, costing seconds with spatial averages. A moved fingerprint drops the result.
        string fingerprintBefore = ComputeAgentFingerprint();
        JunctionTuneResult result;
        UseWaitCursor = true;
        try
        {
            result = await Task.Run(() => CrossoverJunctionTuner.Tune(sides, options));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            summary.Add($"{label}: skipped ({exception.Message.TrimEnd('.')}).");
            return false;
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
            }
        }
        if (IsDisposed)
        {
            return false;
        }
        if (!string.Equals(fingerprintBefore, ComputeAgentFingerprint(), StringComparison.Ordinal))
        {
            summary.Add($"{label}: skipped (the session changed while the tune ran; nothing was written).");
            return false;
        }

        string before = JunctionText(result.Current, lower.Name, upper.Name);
        string after = JunctionText(result.Best, lower.Name, upper.Name);
        string window = $"{result.CandidatesEvaluated} candidates over {Hz(options.MinCrossoverHz)}–" +
            $"{Hz(options.MaxCrossoverHz)}, ranked on {Hz(result.RankingBandLowHz)}–{Hz(result.RankingBandHighHz)}";
        string scoreDelta = (result.Best.RankingScoreDb - result.Current.RankingScoreDb)
            .ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " dB on the score";
        if (!result.Changed)
        {
            summary.Add(
                $"{label}: kept — {before} stands; the best of {window} ({after}, {scoreDelta}) is not " +
                $"{options.KeepMarginDb.ToString("0.00", CultureInfo.InvariantCulture)} dB better on the score" +
                (result.Best.ScoreDb > result.Current.ScoreDb
                    ? ", or reads worse on its own junction band."
                    : "."));
            AppendJunctionReadings(summary, result, best: false);
            return false;
        }

        CrossoverEdge lowPass = result.Best.LowerLowPass!.Value;
        CrossoverEdge highPass = result.Best.UpperHighPass!.Value;
        foreach (bool rightSide in new[] { false, true })
        {
            if (!lower.Pair.Mono || !rightSide)
            {
                VirtualCrossoverChannelSettings settings = lower.SideSettings(rightSide);
                settings.LowPassEdge = lowPass;
                settings.CrossoverKind = settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
                    ? CrossoverKind.BandPass
                    : CrossoverKind.LowPass;
            }
            if (!upper.Pair.Mono || !rightSide)
            {
                VirtualCrossoverChannelSettings settings = upper.SideSettings(rightSide);
                settings.HighPassEdge = highPass;
                settings.CrossoverKind = settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass
                    ? CrossoverKind.BandPass
                    : CrossoverKind.HighPass;
            }
        }
        ApplySettingsToControl(lower);
        ApplySettingsToControl(upper);
        // Remember the result as it stands: read as a difference, a hidden side already holding the new edge would look untouched and get the shown side's whole crossover.
        sideLock.Remember(channels.Select(channel => channel.Pair));
        ScheduleSave();
        RedrawAll();

        summary.Add($"{label}: applied — {before} → {after} ({window}, {scoreDelta}).");
        AppendJunctionReadings(summary, result, best: true);
        return true;
    }

    // Readings on the package's octave-each-side junction band, so they compare with what the assistant read.
    private static void AppendJunctionReadings(List<string> summary, JunctionTuneResult result, bool best)
    {
        foreach (JunctionTuneReading current in result.Current.Sides)
        {
            JunctionTuneReading? tuned = best
                ? result.Best.Sides.FirstOrDefault(side => side.Side == current.Side)
                : null;
            string Pair(double was, double now) => tuned == null
                ? Db(was)
                : $"{Db(was)} → {Db(now)}";
            var line = new StringBuilder();
            line.Append("  ").Append(current.Side).Append(": sum loss ")
                .Append(Pair(current.LossDb, tuned?.LossDb ?? 0))
                .Append(", dip ").Append(Pair(current.DipDb, tuned?.DipDb ?? 0))
                .Append(", ripple ").Append(Pair(current.RippleDb, tuned?.RippleDb ?? 0));
            JunctionTuneAlignment? alignment = (best ? result.BestAfterDelay : result.CurrentAfterDelay)
                .FirstOrDefault(item => item.Side == current.Side);
            if (alignment != null)
            {
                line.Append("; after the best delay ").Append(Db(alignment.LossDb))
                    .Append(" at ").Append(alignment.ExtraDelayMs >= 0 ? "+" : string.Empty)
                    .Append(alignment.ExtraDelayMs.ToString("0.00", CultureInfo.InvariantCulture))
                    .Append(" ms on the upper block")
                    .Append(alignment.InvertUpper ? ", with it inverted" : string.Empty);
            }
            summary.Add(line.Append('.').ToString());
        }

        static string Db(double value) =>
            (value + 0).ToString("0.0", CultureInfo.InvariantCulture) + " dB";
    }

    private static string JunctionText(JunctionTuneCandidate candidate, string lowerBlock, string upperBlock) =>
        $"{lowerBlock} {(candidate.LowerLowPass is { } low ? "LP " + AgentEdgeText(low) : "no low-pass")} + " +
        $"{upperBlock} {(candidate.UpperHighPass is { } high ? "HP " + AgentEdgeText(high) : "no high-pass")}";

    private static string AgentEdgeText(CrossoverEdge edge)
    {
        string family = edge.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "LR",
            CrossoverFilterFamily.Butterworth => "BW",
            CrossoverFilterFamily.Bessel => "Bessel",
            _ => "Cheb"
        };
        return $"{family}{edge.SlopeDbPerOctave} {Hz(edge.FrequencyHz)}";
    }

    private static string Hz(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + " Hz";

    /// <summary>The target level every Auto-tune of one import fits to: the first stated level, else the project's. UI-free so it can be pinned.</summary>
    internal static double ImportTargetLevelDb(
        IReadOnlyList<AgentOperationVerdict> toApply, double currentTargetLevelDb) =>
        toApply
            .Where(verdict => verdict.Status != AgentVerdictStatus.Rejected)
            .Select(verdict => verdict.Operation)
            .OfType<AutoTunePeqOperation>()
            .Select(tune => tune.TargetLevelDb)
            .FirstOrDefault(level => level != null)
            ?? currentTargetLevelDb;

    private async Task<bool> RunAgentAutoTuneAsync(
        AutoTunePeqOperation operation,
        AgentChannelSnapshot target,
        double targetLevelDb,
        EqAutoTunePolicy policy,
        List<string> summary)
    {
        string label = $"Auto-tune {operation.ChannelId}";
        // By the settings object, not the id: the crossover wizard earlier in this import may have re-lettered the blocks.
        (VirtualCrossoverChannel Channel, bool RightSide)? slot = AgentChannelSlots()
            .Where(item => ReferenceEquals(item.Channel.SideSettings(item.RightSide), target.Settings))
            .Select(item => ((VirtualCrossoverChannel, bool)?)(item.Channel, item.RightSide))
            .FirstOrDefault();
        if (slot is not { } found)
        {
            summary.Add($"{label}: skipped (the channel is no longer in the project).");
            return false;
        }

        (VirtualCrossoverChannel channel, bool rightSide) = found;
        // Guards a snapshot the side selector moved under; the review refused the other side already.
        if (!channel.Pair.Mono && rightSide != channel.ActiveRight)
        {
            summary.Add(
                $"{label}: skipped (the other side is on screen; switch the L/R " +
                "selector and import again).");
            return false;
        }

        (LiveCaptureDocument? Capture, double OffsetDb) average =
            HandoffSpatialAverage(channel, channel.ActiveRight);
        if (operation.Source == AgentProposalValidator.PointSource)
        {
            average = (null, 0.0);
        }
        else if (operation.Source == AgentProposalValidator.SpatialAverageSource &&
            average.Capture == null)
        {
            summary.Add(
                $"{label}: skipped (the hybrid view is not drawing this channel's " +
                "spatial average; ask for useSpatialAverage first).");
            return false;
        }

        // A stated target level travels in the request and reaches the panel only once the fit lands: a skipped run must leave nothing, since undo is dropped when nothing ran.
        VirtualDspEqHandoffRequest? request = BuildPeqHandoffRequest(
            channel, withChain: true, average, targetLevelDb);
        if (request == null)
        {
            summary.Add($"{label}: skipped (no measurement to fit against).");
            return false;
        }

        VirtualCrossoverTargetSettings targetSettings =
            project.Target ?? new VirtualCrossoverTargetSettings();
        TargetCurveSpec spec = (targetCurve ?? targetSettings.ToCurve()).Normalized().Spec;
        // The wizard's own refusal: kept all-pass bands filling Max Filters leave the fit no room.
        int room = EqAutoTuneHeadless.RoomUnderMaxFilters(request, policy);
        if (room <= 0)
        {
            int kept = request.BankSeed.Bands.Count(band => band.Type.IsAllPass());
            summary.Add(
                $"{label}: skipped (keeping {kept} all-pass band{(kept == 1 ? "" : "s")} " +
                $"leaves no room under Max Filters ({policy.MaxBands})).");
            return false;
        }

        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request, spec, policy, operation.MinHz, operation.MaxHz,
            operation.AllowShelves, operation.CutsOnly);
        bool cutsOnly = inputs.CutsOnly;
        // The wizard beeps at a source it cannot draw; the tuner must not get one.
        if (inputs.Source.Count < 2)
        {
            summary.Add($"{label}: skipped (the measurement gives no usable curve).");
            return false;
        }
        // A snapshot the crossover moved under can still leave a stated edge past the other.
        if (!EqAutoTuneHeadless.IsUsableWindow(inputs.MinHz, inputs.MaxHz))
        {
            summary.Add(
                $"{label}: skipped (the window {inputs.MinHz:0}–{inputs.MaxHz:0} Hz " +
                "has its lower edge above its upper).");
            return false;
        }

        string? levelWarning = EqTargetLevelCheck.Warning(
            EqTargetLevelCheck.TargetAboveSourceDb(
                inputs.Source, inputs.Target, inputs.MinHz, inputs.MaxHz),
            cutsOnly, inputs.MinHz, inputs.MaxHz);
        if (levelWarning != null)
        {
            summary.Add($"{label}: skipped ({levelWarning.Split('.')[0]}).");
            return false;
        }

        double? before = EqAutoTuneHeadless.RmsErrorDb(
            inputs.Source, inputs.Target, inputs.MinHz, inputs.MaxHz);
        EqualizationCurve fitted;
        double? after;
        bool wasEnabled = Enabled;
        Enabled = false;
        UseWaitCursor = true;
        try
        {
            (fitted, after) = await Task.Run(() =>
            {
                EqualizationCurve curve = EqAutoTuneHeadless.Fit(inputs);
                IReadOnlyList<SignalPoint> corrected = EqAutoTuneHeadless.SourceCurve(
                    request.Source, request.SmoothingInverseOctaves, curve);
                return (curve, EqAutoTuneHeadless.RmsErrorDb(
                    corrected, inputs.Target, inputs.MinHz, inputs.MaxHz));
            });
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                Enabled = wasEnabled;
            }
        }
        if (IsDisposed)
        {
            return false;
        }

        // Landed as the wizard's Return lands, against the capture the request was built with (the token says which). The fit's datum becomes the project's, as Return moves it.
        decimal previousTargetLevel = numericTargetLevel.Value;
        if (!((double)numericTargetLevel.Value).Equals(request.TargetLevelDb))
        {
            numericTargetLevel.Value = numericTargetLevel.ClampValue(request.TargetLevelDb);
        }

        VirtualCrossoverChannelState state = channel.SideState(channel.ActiveRight);
        MagnitudeGateSnapshot snapshot = magnitudeGate;
        if (!VirtualDspEqHandoff.TryApplyReturn(
                channels,
                request.Token,
                fitted,
                projectGeneration,
                CalibrationFor(state),
                SpatialAverageCalibrationFor(state),
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                (double)numericTargetLevel.Value,
                average.Capture,
                ProcessorSampleRateHz))
        {
            numericTargetLevel.Value = previousTargetLevel;
            summary.Add($"{label}: skipped (the channel changed while the fit ran).");
            return false;
        }

        channel.SideSettings(channel.ActiveRight).PeqSourceName = "Auto-tune (AI import)";
        UpdatePeqReadouts(channel);
        int fittedBands = fitted.Bands.Count(band => !band.Type.IsAllPass());
        summary.Add(
            $"{label}: applied — {fittedBands} band{(fittedBands == 1 ? "" : "s")}" +
            (inputs.KeptAllPass.Count > 0 ? $" + {inputs.KeptAllPass.Count} all-pass kept" : string.Empty) +
            $", preamp {fitted.PreampDb:0.0} dB, {inputs.MinHz:0}–{inputs.MaxHz:0} Hz, " +
            $"{(cutsOnly ? "cuts only" : "cuts and boosts")}, on the " +
            $"{(average.Capture != null ? "spatial average" : "point measurement")}; " +
            $"RMS error {Rms(before)} -> {Rms(after)}.");
        return true;

        static string Rms(double? value) => value is { } rms ? $"{rms:0.0} dB" : "n/a";
    }

    private AgentImportUndo CaptureAgentUndo() =>
        new(
            AgentChannelSlots()
                .Select(slot => slot.Channel.SideSettings(slot.RightSide))
                .Select(settings => new AgentUndoEntry(
                    settings, AgentOperations.CloneEditable(settings)))
                .ToList(),
            project.SpatialAverageMode,
            checkBoxHybrid.Checked,
            channels.ToList(),
            project.StereoSceneOffsetMagnitudeMs,
            project.StereoRightHandDrive,
            project.StereoLevelDifferenceDb,
            project.RearFillOffsetMs,
            (double)numericTargetLevel.Value);

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

        AgentImportUndo undo = agentUndo;
        agentUndo = null;
        AgentProposalApplier.Restore(undo.Channels);
        RefreshChannelsAfterAgentWrite(undo.Channels);
        RestoreAgentChannelOrder(undo.Order);

        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            project.SpatialAverageMode = undo.SpatialAverageMode;
            checkBoxHybrid.Checked = undo.HybridTicked;
            project.ShowHybridCurves = undo.HybridTicked;
            project.SetStereoScene(undo.SceneOffsetMagnitudeMs, undo.RightHandDrive);
            project.StereoLevelDifferenceDb = undo.StereoLevelDifferenceDb;
            project.RearFillOffsetMs = undo.RearFillOffsetMs;
            numericTargetLevel.Value = numericTargetLevel.ClampValue(undo.TargetLevelDb);
            // ValueChanged's project write is suppressed above, so the datum the package and session read is written by hand.
            project.TargetLevelDb = (double)numericTargetLevel.Value;
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }

        foreach (VirtualCrossoverChannel channel in channels)
        {
            RefreshSpatialAverageStatus(channel);
        }

        RefreshHybridAvailability();
        // Remember the restored state as it stands: a difference could carry a side where it never was (L=A,R=B; import wrote L=B; undo restores L=A and would carry A onto R).
        sideLock.Remember(channels.Select(channel => channel.Pair));
        ScheduleSave();
        RedrawAll();
    }

    // Auto crossover can reorder blocks; restored by identity, since the list holds the same objects.
    private void RestoreAgentChannelOrder(IReadOnlyList<VirtualCrossoverChannel> order)
    {
        if (order.Count != channels.Count || order.SequenceEqual(channels))
        {
            return;
        }

        List<int> indices = order.Select(channel => channels.IndexOf(channel)).ToList();
        if (indices.All(index => index >= 0))
        {
            ApplyChannelOrder(indices);
        }
    }

    // The control shows the active side, so a write to the other side shows when the side flips.
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

    // Excess group delay per measured channel, as a separate text beside the package (which already fills a chat), named after the last package so the two line up by channel id.
    private async Task CopyExcessGroupDelayForAiAsync()
    {
        if (agentBusy)
        {
            return;
        }

        agentBusy = true;
        RefreshAutoActionsEnabled();
        try
        {
            // Snapshot on the UI thread; the gated FFT and minimum-phase reconstruction per channel run off it.
            var measured = new List<(string Id, Complex[] Response, int PeakIndex, int SampleRate, MeasuredBand Band)>();
            foreach ((string block, AgentChannelSide side, VirtualCrossoverChannel channel, bool rightSide)
                in AgentChannelSlots())
            {
                VirtualCrossoverChannelState state = channel.SideState(rightSide);
                if (state.TransferImpulseResponse is { } impulseResponse)
                {
                    measured.Add((
                        AgentChannelIds.Format(block, side), impulseResponse,
                        state.TransferPeakIndex, state.SampleRate, state.MeasuredBand));
                }
            }
            if (measured.Count == 0)
            {
                ShowError("The diagnostic was not copied.", "No channel has a measurement to read.");
                return;
            }

            PhaseAnalysisSettings gate = AgentGroupDelayWindow();
            // Tie to the package only while this is the session it was copied from.
            string? packageId = lastAgentPackageFingerprint == ComputeAgentFingerprint()
                ? lastAgentPackageId
                : null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            (AgentDiagnosticBuildResult result, int count) = await AgentProgressDialog.RunAsync(
                FindForm(),
                "Copy diagnostics for AI",
                "Excess group delay…",
                progress => Task.Run(() =>
                {
                    var channels = new List<AgentDiagnosticChannel>(measured.Count);
                    foreach ((string id, Complex[] response, int peakIndex, int sampleRate, MeasuredBand band) in measured)
                    {
                        progress.Report($"Excess group delay: {id}…");
                        IReadOnlyList<SignalPoint>? curve = BuildExcessGroupDelayCurve(
                            response, peakIndex, sampleRate, band, gate);
                        if (curve != null)
                        {
                            channels.Add(new AgentDiagnosticChannel(id, curve));
                        }
                    }

                    return (
                        AgentDiagnosticBuilder.BuildExcessGroupDelay(
                            channels, packageId, now, AgentDiagnosticWindow.From(gate)),
                        channels.Count);
                }));
            if (IsDisposed)
            {
                return;
            }
            if (!AgentClipboard.TryWrite(result.Text, out string? error))
            {
                ShowError("The diagnostic was not copied.", error!);
                return;
            }

            MessageBox.Show(
                FindForm(),
                $"Excess group delay diagnostic copied ({(result.JsonBytes + 1023) / 1024} KB, " +
                $"{count} channel{(count == 1 ? "" : "s")}). Paste it into the " +
                "same chat as the package.",
                "Copy diagnostics for AI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("The diagnostic was not copied.", exception.Message);
        }
        finally
        {
            agentBusy = false;
            RefreshAutoActionsEnabled();
        }
    }

    /// <summary>Copy for AI: gathers at one revision (retrying once), builds off the UI thread, then writes the whole text to the clipboard at once, so a failure copies nothing.</summary>
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
            (AgentPackageInputs Inputs, string Fingerprint)? gathered =
                await AgentProgressDialog.RunAsync(
                    FindForm(),
                    "Copy for AI",
                    "Reading the tune…",
                    async progress =>
                    {
                        (AgentPackageInputs, string)? first = await GatherAgentPackageAsync();
                        if (first != null)
                        {
                            return first;
                        }

                        progress.Report("The tune moved while it was read — reading again…");
                        return await GatherAgentPackageAsync();
                    });
            if (IsDisposed)
            {
                return;
            }
            if (gathered is not { } package)
            {
                ShowError(
                    "The AI package was not copied.",
                    "The settings changed while the package was being gathered. Try again.");
                return;
            }

            (AgentPackageInputs inputs, string fingerprint) = package;
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

            RememberAgentPackage(packageId.ToString("D"), fingerprint);
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

    // The fingerprint covers what the coordinator revision does not (target level, Hybrid tick, gate pin), so it is taken on both sides of the gather; null when they differ, and the caller retries once.
    private async Task<(AgentPackageInputs Inputs, string Fingerprint)?> GatherAgentPackageAsync()
    {
        string before = ComputeAgentFingerprint();
        AgentPackageInputs? inputs = await CaptureAgentPackageInputsAsync();
        if (inputs == null || IsDisposed || ComputeAgentFingerprint() != before)
        {
            return null;
        }

        return (inputs, before);
    }

    /// <summary>The channels as the bridge names them, with live settings, plus the project figures engine requests are judged against.</summary>
    internal AgentSessionSnapshot BuildAgentSessionSnapshot() =>
        new(
            AgentChannelSlots()
                .Select(slot => new AgentChannelSnapshot(
                    slot.Block,
                    slot.Side,
                    slot.Channel.SideSettings(slot.RightSide),
                    slot.Channel.SideState(slot.RightSide).TransferImpulseResponse != null,
                    AgentSpatialAverageCaptures(slot.Channel.SideState(slot.RightSide)),
                    slot.Channel.Pair.Zone,
                    slot.Channel.Pair.Enabled,
                    slot.Channel.Pair.Bypass))
                .ToList(),
            ProcessorSampleRateHz,
            ProcessorProfile.MaxDelayMs,
            lastAgentPackageId,
            AgentAutoDelayDefaults(),
            SpatialAverageMode,
            checkBoxHybrid.Checked,
            project.ActiveSideRight,
            lastAgentPackageFingerprint,
            ComputeAgentFingerprint());

    // A mono block yields one slot, routed to the left as everywhere in the panel.
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

    /// <summary>Everything a package is built from, read off the current session. Null when the session changed underneath; the caller retries once. See docs/tech/agent-bridge.md#package-gathering.</summary>
    internal async Task<AgentPackageInputs?> CaptureAgentPackageInputsAsync()
    {
        long revision = processingCoordinator.CurrentRevision;
        VirtualCrossoverGroupView groupView = SelectedGroupView;
        bool activeRight = project.ActiveSideRight;
        // One smoothing for every package, independent of the display. See docs/tech/agent-bridge.md#package-smoothing.
        int smoothing = SpectrumSmoothing.PsychoacousticCode;
        MagnitudeGateSnapshot packageGate = magnitudeGate with { SmoothingInverseOctaves = smoothing };
        // Hybrid curves and their sum go at 1/12 octave, the grid's width (the manual reads them unsmoothed).
        MagnitudeGateSnapshot hybridGate =
            magnitudeGate with { SmoothingInverseOctaves = AgentHybridSmoothingInverseOctaves };

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
                channels, rightSide, revision, minimumChannels: 1);
            if (!processingCoordinator.IsCurrent(revision))
            {
                return null;
            }
            if (sideSum == null)
            {
                sides.Add(new AgentSideInputs(
                    sideName, [], null, null, null, [], [], [], "no channel with a source on this side"));
                continue;
            }

            // The frame's own filtering (see RedrawMainPlotAsync); channels outside the view get their own curves below.
            List<ProcessedChannel> all = sideSum.Channels.ToList();
            List<ProcessedChannel> shown = ChannelsShownBy(all, groupView);
            List<ProcessedChannel> summed = ChannelsSummedBy(shown, groupView);
            List<ProcessedChannel> others = all.Except(shown).ToList();
            if (rightSide == activeRight)
            {
                activeShown = shown;
            }

            // The panel's `metrics` smooths at the display's width; these delegates window through the package gate, the opposite side through its own gate placement. See docs/tech/agent-bridge.md#package-smoothing.
            bool oppositeSide = rightSide != activeRight;
            VirtualCrossoverMetrics MetricsThrough(MagnitudeGateSnapshot gate) =>
                new(
                    processingCoordinator,
                    (impulseResponse, anchorIndex, sampleRate, band, calibration) =>
                        BuildGatedMagnitudeCurve(
                            gate,
                            impulseResponse,
                            anchorIndex,
                            sampleRate,
                            gate.ResolveGateOffsetMs(oppositeSide, anchorIndex, sampleRate),
                            band,
                            calibration),
                    CalibrationFor,
                    (members, anchorIndex) =>
                        BuildMeasuredSumCurve(
                            gate,
                            members,
                            anchorIndex,
                            gate.ResolveGateOffsetMs(
                                oppositeSide, anchorIndex, members.Count > 0 ? members[0].SampleRate : 0)));
            VirtualCrossoverMetrics sideMetrics = MetricsThrough(packageGate);

            List<AnalysisCurve>? magnitudes = null;
            AnalysisCurve? sumCurve = null;
            List<SignalPoint>? loss = null;
            // At the hybrid's width so a point-measurement fallback is not smoothed twice; built only when the hybrid is asked for (a second gated pass).
            List<AnalysisCurve>? hybridReferences = null;
            if (shown.Count > 0)
            {
                (magnitudes, sumCurve, loss) = sideMetrics.BuildCurves(shown, smoothing, summed);
                if (HybridRequested)
                {
                    (hybridReferences, _, _) = MetricsThrough(hybridGate)
                        .BuildCurves(shown, AgentHybridSmoothingInverseOctaves, summed);
                }
            }

            bool quotesJunctions =
                VirtualCrossoverGroupViews.LossChainZone(groupView) != null &&
                ProcessedChannels.HasJunction(summed);
            if (!quotesJunctions)
            {
                loss = null;
            }
            // Rows from the SUMMING channels, as UpdateMetric does: a drawn-but-unsummed centre would invent junctions (see VirtualCrossoverMetricsTests.BuildEntries_ReadsJunctionsOffTheSummingSet).
            List<VirtualCrossoverMetric.Entry> entries = sideMetrics.BuildEntries(summed, loss);
            // Phase gate over the summing channels with this side's pin, as RedrawMainPlotAsync does for the active side.
            List<VirtualCrossoverMetric.PhaseEntry> phaseEntries = [];
            // The direct-sound loss travels whatever the Sum loss selector shows (PROTOCOL §1.8), off the junction phase spectra.
            List<SignalPoint>? directLoss = null;
            List<VirtualCrossoverMetric.Entry> directEntries = [];
            if (quotesJunctions)
            {
                int phaseRate = summed[0].SampleRate;
                double? pinnedOffsetMs = project.PhaseGateFor(rightSide).OffsetMs;
                double gateLeftMs = gatePreview?.LeftMs ?? project.PhaseGateLeftMs;
                double gatePlateauMs = gatePreview?.PlateauMs ?? project.PhaseGatePlateauMs;
                double gateRightMs = gatePreview?.RightMs ?? project.PhaseGateRightMs;
                (phaseEntries, directLoss) = await Task.Run(() =>
                {
                    IReadOnlyList<ProcessedChannel>? orderedSet = null;
                    IReadOnlyList<Complex[]>? spectra = null;
                    List<VirtualCrossoverMetric.PhaseEntry> built = sideMetrics.BuildPhaseEntries(
                        summed,
                        ordered =>
                        {
                            orderedSet = ordered;
                            spectra = JunctionPhaseSpectra.Build(
                                ordered, phaseRate, pinnedOffsetMs,
                                gateLeftMs, gatePlateauMs, gateRightMs);
                            return spectra;
                        });
                    List<SignalPoint>? direct = spectra != null
                        ? sideMetrics.BuildDirectLossCurve(orderedSet!, spectra, smoothing)
                        : null;
                    return (built, direct);
                });
                directEntries = sideMetrics.BuildEntries(summed, directLoss);
            }
            HybridMagnitudes? hybrid = hybridReferences != null
                ? BuildHybridMagnitudes(shown, hybridReferences, rightSide, AgentHybridSmoothingInverseOctaves)
                : null;
            // The hybrid view's sum (see RedrawMainPlotAsync); null, as on screen, when the sides cannot share one offset.
            IReadOnlyList<SignalPoint>? hybridSum = hybrid == null || hybridReferences == null
                ? null
                : rightSide == activeRight
                    ? BuildActiveHybridSumCurve(shown, hybridReferences, hybrid, hybridGate)
                    : BuildOppositeHybridSumCurve(sideSum, hybrid.OffsetDb, hybridGate)?.Points;

            for (int index = 0; index < shown.Count; index++)
            {
                // Hybrid curves are carried shifted by the set's datum onto the impulse responses' axis, as drawn (see BuildMagnitudeCurves).
                IReadOnlyList<SignalPoint>? hybridProcessed = null;
                IReadOnlyList<SignalPoint>? hybridPreDsp = null;
                if (hybrid != null && hybridReferences != null && !hybrid.PointMeasuredChannels[index])
                {
                    hybridProcessed = ShiftedBy(hybrid.Channels[index], hybrid.OffsetDb);
                    hybridPreDsp = BuildHybridPreDspCurve(
                        shown[index].Channel, rightSide, hybridReferences[index].Points,
                        AgentHybridSmoothingInverseOctaves, hybrid.OffsetDb);
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
                            CalibrationFor(found.Item),
                            packageGate).Points;
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
                    // The selected mode's family, not whichever capture the side holds.
                    state.SpatialAverageFor(SpatialAverageMode) != null ? SpatialAverageMode.ToString() : null,
                    // Every family held: distinguishes "no average" from "one the view is not using".
                    AgentSpatialAverageCaptures(state),
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
            // The package's smoothing, not the display's.
            SpectrumSmoothing.PsychoacousticBaseInverseOctaves,
            true,
            project.SpatialAverageMode,
            checkBoxHybrid.Checked,
            HybridRequested,
            AgentHybridSmoothingInverseOctaves,
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

    // The project's phase gate, window mode and cycles, with the offset left for the channel's own arrival.
    private PhaseAnalysisSettings AgentGroupDelayWindow() => new(
        project.PhaseWindowMode,
        project.PhaseFdwCycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: 0.0,
        project.PhaseGateLeftMs,
        project.PhaseGatePlateauMs,
        project.PhaseGateRightMs,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    // Excess group delay at the channel's own arrival: minimum-phase part removed, leaving arrivals and reflections. Pure, runs off the UI thread. See docs/tech/agent-bridge.md#excess-group-delay-diagnostic.
    private static IReadOnlyList<SignalPoint>? BuildExcessGroupDelayCurve(
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
    private static IReadOnlyList<string> AgentSpatialAverageCaptures(VirtualCrossoverChannelState state)
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

    // The spatial average through no chain, on the impulse responses' level axis; null without a capture of the selected family.
    private IReadOnlyList<SignalPoint>? BuildHybridPreDspCurve(
        VirtualCrossoverChannel channel,
        bool rightSide,
        IReadOnlyList<SignalPoint> grid,
        int smoothingCode,
        double offsetDb)
    {
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        if (state.SpatialAverageFor(SpatialAverageMode) is not { } document || grid.Count == 0)
        {
            return null;
        }

        IReadOnlyList<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            channel.ProcessorSampleRateFor(rightSide),
            SpatialAverageCalibrationFor(state),
            grid.Select(point => point.X).ToList(),
            smoothingCode);
        return curve == null ? null : ShiftedBy(curve, offsetDb);
    }

    // A failing view is reported missing rather than failing the package, as the lower plot's redraw does.
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

}
