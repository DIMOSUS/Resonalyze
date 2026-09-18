using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>Panel side of the Agent Bridge: the menu, the import flow and its undo. What the bridge reads off the session
/// lives in <see cref="AgentSessionReader"/> and <see cref="AgentProbeReader"/>. See docs/tech/agent-bridge.md.</summary>
public partial class VirtualCrossoverPanel
{
    // One bridge operation at a time: a concurrent Copy or import would race the coordinator or move the settings being read.
    private bool agentBusy;

    private ContextMenuStrip? agentMenu;

    /// <summary>EQ Wizard Auto Tune settings an import fits a bank with; the wizard's opening values when unwired.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<EqAutoTunePolicy>? AutoTunePolicyProvider { get; set; }

    // The view the bridge's readings follow, off the controls.
    private AgentViewInputs AgentView() => new(
        SelectedGroupView,
        checkBoxHybrid.Checked,
        HybridRequested,
        (double)numericTargetLevel.Value,
        targetCurve);

    /// <summary>The session as one hash for the review's staleness check. See docs/tech/agent-bridge.md#session-fingerprint.</summary>
    internal string ComputeAgentFingerprint() => agentReader.Fingerprint(AgentView());

    /// <summary>The channels as the bridge names them, with live settings, plus the project figures engine requests are judged against.</summary>
    internal AgentSessionSnapshot BuildAgentSessionSnapshot() => agentReader.Snapshot(AgentView());

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
            session.Project.ShowHybridCurves = true;
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }

        // SetSpatialAverageMode returns early on an unchanged mode; the tick alone still changes what can be drawn.
        RefreshHybridAvailability();
        return true;
    }

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
        (AutoDelayPlan? launch, AutoDelayRefusal? refusal) = VirtualCrossoverAutoDelay.Prepare(
            session, gatePlacement, consentToBroadWindow: null);
        if (launch == null)
        {
            summary.Add($"Auto delay: skipped ({refusal!.Summary}).");
            return false;
        }

        AutoDelayRunRequest request = BuildAutoDelayRequest(operation, agentReader.AutoDelayDefaults());
        AutoDelayRunResult result;
        bool wasEnabled = Enabled;
        Enabled = false;
        UseWaitCursor = true;
        try
        {
            result = await launch.Run(request);
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
            sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        }

        bool engines = await RunAgentEngineRequests(toApply, summary, progress);
        if (written.Count == 0 && !engines)
        {
            agentUndo = previousUndo;
            agentUndoGeneration = previousUndoGeneration;
        }

        return engines;
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
                    reports.Add(await AgentProbeReader.ReadAsync(
                        probe, agentReader, AgentView(), GateIsMisplaced));
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
        bool matches = agentReader.LastPackageFingerprint != null &&
            agentReader.LastPackageFingerprint == state;
        AgentProbeBuildResult result = AgentProbeBuilder.Build(
            reports, matches ? agentReader.LastPackageId : null, matches, steady, DateTimeOffset.UtcNow);
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

    // What the tune writes and says lives in AgentJunctionTune; the panel runs it off the UI thread and repaints.
    private async Task<bool> RunAgentTuneJunctionAsync(
        TuneJunctionOperation operation, List<string> summary)
    {
        (JunctionTunePlan? plan, string? skipped) = AgentJunctionTune.Prepare(
            operation, BuildAgentSessionSnapshot(), session, GateIsMisplaced);
        if (plan == null)
        {
            summary.Add(skipped!);
            return false;
        }

        // Fingerprinted around the compute instead of disabling the panel: disable/enable repaints every plot twice, costing seconds with spatial averages. A moved fingerprint drops the result.
        string fingerprintBefore = ComputeAgentFingerprint();
        JunctionTuneResult result;
        UseWaitCursor = true;
        try
        {
            result = await Task.Run(() => CrossoverJunctionTuner.Tune(plan.Sides, plan.Options));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            summary.Add($"{plan.Label}: skipped ({exception.Message.TrimEnd('.')}).");
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
            summary.Add($"{plan.Label}: skipped (the session changed while the tune ran; nothing was written).");
            return false;
        }

        if (result.Changed)
        {
            AgentJunctionTune.Write(result, plan.Lower, plan.Upper);
            ApplySettingsToControl(plan.Lower);
            ApplySettingsToControl(plan.Upper);
            // Remember the result as it stands: read as a difference, a hidden side already holding the new edge would look untouched and get the shown side's whole crossover.
            sideLock.Remember(session.Channels.Select(channel => channel.Pair));
            ScheduleSave();
            RedrawAll();
        }
        AgentJunctionTune.Describe(summary, plan, result);
        return result.Changed;
    }

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
        (VirtualCrossoverChannel Channel, bool RightSide)? slot = agentReader.Slots()
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
            session.Project.Target ?? new VirtualCrossoverTargetSettings();
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
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        if (!VirtualDspEqHandoff.TryApplyReturn(
                session.Channels,
                request.Token,
                fitted,
                projectGeneration,
                session.Calibration.For(state),
                session.Calibration.SpatialAverageFor(),
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                (double)numericTargetLevel.Value,
                average.Capture,
                session.ProcessorSampleRateHz))
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
            agentReader.Slots()
                .Select(slot => slot.Channel.SideSettings(slot.RightSide))
                .Select(settings => new AgentUndoEntry(
                    settings, AgentOperations.CloneEditable(settings)))
                .ToList(),
            session.Project.SpatialAverageMode,
            checkBoxHybrid.Checked,
            session.Channels.ToList(),
            session.Project.StereoSceneOffsetMagnitudeMs,
            session.Project.StereoRightHandDrive,
            session.Project.StereoLevelDifferenceDb,
            session.Project.RearFillOffsetMs,
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
            session.Project.SpatialAverageMode = undo.SpatialAverageMode;
            checkBoxHybrid.Checked = undo.HybridTicked;
            session.Project.ShowHybridCurves = undo.HybridTicked;
            session.Project.SetStereoScene(undo.SceneOffsetMagnitudeMs, undo.RightHandDrive);
            session.Project.StereoLevelDifferenceDb = undo.StereoLevelDifferenceDb;
            session.Project.RearFillOffsetMs = undo.RearFillOffsetMs;
            numericTargetLevel.Value = numericTargetLevel.ClampValue(undo.TargetLevelDb);
            // ValueChanged's project write is suppressed above, so the datum the package and session read is written by hand.
            session.Project.TargetLevelDb = (double)numericTargetLevel.Value;
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }

        foreach (VirtualCrossoverChannel channel in session.Channels)
        {
            RefreshSpatialAverageStatus(channel);
        }

        RefreshHybridAvailability();
        // Remember the restored state as it stands: a difference could carry a side where it never was (L=A,R=B; import wrote L=B; undo restores L=A and would carry A onto R).
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        ScheduleSave();
        RedrawAll();
    }

    // Auto crossover can reorder blocks; restored by identity, since the list holds the same objects.
    private void RestoreAgentChannelOrder(IReadOnlyList<VirtualCrossoverChannel> order)
    {
        if (order.Count != session.Channels.Count || order.SequenceEqual(session.Channels))
        {
            return;
        }

        List<int> indices = order.Select(channel => session.Channels.IndexOf(channel)).ToList();
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
            foreach ((_, _, VirtualCrossoverChannel channel, bool rightSide) in agentReader.Slots())
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
            List<MeasuredChannel> measured = agentReader.MeasuredChannels();
            if (measured.Count == 0)
            {
                ShowError("The diagnostic was not copied.", "No channel has a measurement to read.");
                return;
            }

            PhaseAnalysisSettings gate = agentReader.GroupDelayWindow();
            // Tie to the package only while this is the session it was copied from.
            string? packageId = agentReader.PackageIdFor(ComputeAgentFingerprint());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            (AgentDiagnosticBuildResult result, int count) = await AgentProgressDialog.RunAsync(
                FindForm(),
                "Copy diagnostics for AI",
                "Excess group delay…",
                progress => Task.Run(() =>
                {
                    var channels = new List<AgentDiagnosticChannel>(measured.Count);
                    foreach (MeasuredChannel channel in measured)
                    {
                        progress.Report($"Excess group delay: {channel.Id}…");
                        if (AgentSessionReader.ExcessGroupDelayCurve(channel, gate) is { } curve)
                        {
                            channels.Add(new AgentDiagnosticChannel(channel.Id, curve));
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

            agentReader.RememberPackage(packageId.ToString("D"), fingerprint);
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
        AgentPackageInputs? inputs = await agentReader.CaptureInputsAsync(AgentView());
        if (inputs == null || IsDisposed || ComputeAgentFingerprint() != before)
        {
            return null;
        }

        return (inputs, before);
    }
}
