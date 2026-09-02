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

    /// <summary>
    /// The EQ Wizard's Auto Tune settings at the moment an import fits a bank
    /// without it — wired by the host so the import produces the bank the
    /// wizard's button would; the wizard's opening values when nothing is wired.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<EqAutoTunePolicy>? AutoTunePolicyProvider { get; set; }

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

    // What the last import moved, for Undo, and the project generation it was
    // written into: a session loaded since has different settings objects, and
    // the entries would restore into ones nobody displays.
    private AgentImportUndo? agentUndo;
    private long agentUndoGeneration;

    /// <summary>
    /// Everything one import can move, as it stood before it ran. Every channel's
    /// chain is taken, not only the ones a row names: an engine writes channels no
    /// row mentions, and the crossover wizard can reorder the blocks as well.
    /// </summary>
    // The stereo scene, the level tilt and the rear-fill offset are what an Auto
    // delay run commits beside the channels (CommitAutoDelayResult), so an undo
    // of an import that ran one has to carry them too.
    private sealed record AgentImportUndo(
        IReadOnlyList<AgentUndoEntry> Channels,
        VirtualCrossoverSpatialAverageMode? SpatialAverageMode,
        bool HybridTicked,
        IReadOnlyList<VirtualCrossoverChannel> Order,
        double SceneOffsetMagnitudeMs,
        bool RightHandDrive,
        double StereoLevelDifferenceDb,
        double RearFillOffsetMs,
        // The datum an Auto-tune request may move, as the wizard's return does.
        double TargetLevelDb);

    /// <summary>
    /// Import AI proposal: clipboard → strict parse → review against the live
    /// settings → the dialog → a second review of the ticked rows against the
    /// settings as they are at commit → one write of the settings rows, then the
    /// engine requests in their fixed order, then one summary. Undo is armed
    /// before anything is written, so a failure part-way through still leaves the
    /// whole import undoable.
    /// </summary>
    // async void as the button handlers are: the engines that run without their
    // dialogs await their compute, and the try/finally below is what keeps the
    // busy flag honest across those awaits.
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
                proposal, selected, BuildAgentSessionSnapshot(),
                out List<AgentOperationVerdict> toApply, out List<string> unseenWarnings);
            if (problem != null)
            {
                ShowError("Nothing was applied.", problem);
                return;
            }
            // The review judged every row together; the ticked subset can leave a
            // state it never showed. Say so and let the user decide — a warning,
            // not a refusal, as in the review itself.
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

            // Armed BEFORE the first write, not after the last one: an engine can
            // throw with the settings rows already in the tune, and an import the
            // user cannot undo is the worst thing this menu could leave behind.
            // The previous import's undo is put back only if nothing moved at all.
            AgentImportUndo undo = CaptureAgentUndo();
            AgentImportUndo? previousUndo = agentUndo;
            long previousUndoGeneration = agentUndoGeneration;
            agentUndo = undo;
            agentUndoGeneration = projectGeneration;

            List<AgentUndoEntry> written = AgentProposalApplier.Apply(toApply);
            if (written.Count > 0)
            {
                RefreshChannelsAfterAgentWrite(written);
                int proposed = review.Verdicts.Count;
                int rows = toApply.Count(verdict => verdict.Operation is AgentSettingsOperation);
                summary.Add(
                    $"Applied {rows} of {proposed} proposed change{(proposed == 1 ? "" : "s")}.");
            }

            bool ran = await RunAgentEngineRequests(toApply, summary);
            if (written.Count == 0 && !ran)
            {
                agentUndo = previousUndo;
                agentUndoGeneration = previousUndoGeneration;
            }

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
            // What the summary already holds DID happen; saying "not imported"
            // over it would send the reader looking for changes that are there.
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

    /// <summary>
    /// The engine requests of one import, in the fixed order an import runs them,
    /// whatever order the reply listed: the spatial average first (it decides
    /// which curves the rest read), then Auto crossover, then Auto delay, then
    /// Auto-tune, which is last because it fits the bank to everything the others
    /// left behind. The settings rows are already written by the time this runs.
    /// Each engine keeps its own confirmation: cancelling one skips that
    /// operation and the import carries on with the next.
    /// </summary>
    /// <returns>Whether any of them changed the project.</returns>
    private async Task<bool> RunAgentEngineRequests(
        IReadOnlyList<AgentOperationVerdict> toApply, List<string> summary)
    {
        bool ran = false;
        // One target level for every fit of this import, decided before the first
        // runs: the level the reply states (the review made the stated ones
        // agree), else the project's as it stands now. Read per operation, a row
        // that states none would fit at the old datum and the next row move it.
        double importTargetLevelDb = ImportTargetLevelDb(toApply, (double)numericTargetLevel.Value);
        EqAutoTunePolicy policy = AutoTunePolicyProvider?.Invoke() ?? EqAutoTunePolicy.Default;
        // The verdicts, not the operations alone: a channel operation's target is
        // the verdict's channel snapshot, held by its settings object, which is
        // what still names the channel after the crossover wizard has reordered
        // the blocks and re-lettered them.
        foreach (AgentOperationVerdict verdict in toApply
            .Where(verdict => verdict.Applicable)
            .Where(verdict => verdict.Operation is not AgentSettingsOperation)
            .OrderBy(verdict => AgentEngineOrder(verdict.Operation!)))
        {
            AgentOperation operation = verdict.Operation!;
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

                case RunAutoDelayOperation delay:
                    ran |= await RunAgentAutoDelayAsync(delay, summary);
                    break;

                case AutoTunePeqOperation tune:
                    ran |= await RunAgentAutoTuneAsync(
                        tune, verdict.Channel!, importTargetLevelDb, policy, summary);
                    break;

                // Every operation the protocol names is executed above; one this
                // build does not run never reaches here, the review refuses it.
                default:
                    summary.Add(
                        $"{operation.Parameter}: skipped " +
                        "(not available in this version of Resonalyze).");
                    break;
            }
        }

        return ran;
    }

    private static int AgentEngineOrder(AgentOperation operation) => operation switch
    {
        UseSpatialAverageOperation => 0,
        RunAutoCrossoverOperation => 1,
        RunAutoDelayOperation => 2,
        _ => 3
    };

    // The mode and the tick together: either one alone leaves the point
    // measurement in charge, which is the thing the operation exists to fix. The
    // panel's own project events are suppressed around the pair so the import
    // saves and redraws once, at the end, rather than after each step.
    /// <returns>Whether the mode named by the request is one the panel has.</returns>
    private bool ApplyAgentSpatialAverage(UseSpatialAverageOperation operation)
    {
        // The review has already refused an unknown mode, so this is a guard, not
        // a path — but the summary is written from the answer, so it must be one.
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

        // SetSpatialAverageMode returns early when the mode is already the one
        // asked for, and the tick alone still changes what can be drawn.
        RefreshHybridAvailability();
        return true;
    }

    // The Auto delay inputs as the dialog would open with them: the project's
    // figures as layout-neutral magnitudes (the layout toggle owns every sign),
    // and the gain balance unticked — the project stores the tilt it would
    // apply, never the opt-in. Printed in the package's "Current" column and
    // filled in for every input a request leaves out, so the two agree.
    private AgentAutoDelaySettings AgentAutoDelayDefaults() =>
        new(
            project.StereoSceneOffsetMagnitudeMs,
            project.StereoRightHandDrive,
            AdjustGains: false,
            Math.Abs(project.StereoLevelDifferenceDb),
            project.RearFillOffsetMs);

    /// <summary>
    /// The run inputs a request asks for: what it states, and the dialog's own
    /// answer for what it leaves out. UI-free so the rule can be pinned.
    /// </summary>
    internal static AutoDelayRunRequest BuildAutoDelayRequest(
        RunAutoDelayOperation operation, AgentAutoDelaySettings defaults) =>
        new(
            operation.SceneOffsetMs ?? defaults.SceneOffsetMs,
            operation.RightHandDrive ?? defaults.RightHandDrive,
            operation.AdjustGains ?? defaults.AdjustGains,
            operation.NearSideCutDb ?? defaults.NearSideCutDb,
            operation.RearFillOffsetMs ?? defaults.RearFillOffsetMs);

    // Auto delay without its dialog: the button's own checks (headless, so a
    // refusal is a phrase for the summary rather than a box), the same compute
    // delegate the dialog's Run would call, and the same commit its Apply would
    // make — report, log and outcome metric included. The review was the gate.
    // The panel is disabled for the compute's span: the dialog's modality is
    // what kept the channel configuration still under the button's run, and an
    // edit landing mid-search would be aligned against a chain that is gone.
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
        // The summary is a message box, which does not scroll: the report's head
        // (the table is what the dialog showed first) and where the rest went.
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

    // Auto-tune without the wizard: the same handoff the PEQ menu would build
    // for the channel, the same curves and options the wizard would fit
    // (EqAutoTuneHeadless, pinned against the wizard's own render), and the
    // same landing the wizard's Return takes — every guard included, so a
    // channel that moved while the fit ran is refused rather than written.
    // The review was the gate; the target-level check the wizard would have
    // asked about is a refusal here, with the phrase in the summary.
    /// <summary>
    /// The target level every Auto-tune of one import fits to: the first level a
    /// ticked request states, else the project's own. UI-free so it can be pinned.
    /// </summary>
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
        // By the settings object the review judged, not by the id: the crossover
        // wizard, run earlier in the same import, may have reordered the blocks,
        // and the letter the reply used then names another channel.
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
        // The review refused the other side already; this guards a snapshot the
        // side selector moved under.
        if (!channel.Pair.Mono && rightSide != channel.ActiveRight)
        {
            summary.Add(
                $"{label}: skipped (the other side is on screen; switch the L/R " +
                "selector and import again).");
            return false;
        }

        // What the wizard would open on: the average while the hybrid view draws
        // it, the point measurement otherwise — unless the reply chose.
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

        // A stated target level is built into the request (the token carries it)
        // and reaches the panel only once the fit has landed — a run that skips
        // itself must leave nothing behind, since the import's undo is dropped
        // when nothing ran.
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
        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request, spec, policy, operation.MinHz, operation.MaxHz,
            operation.AllowShelves, operation.CutsOnly);
        bool cutsOnly = inputs.CutsOnly;
        // The wizard beeps at a source it cannot draw; the tuner must not be
        // handed one.
        if (inputs.Source.Count < 2)
        {
            summary.Add($"{label}: skipped (the measurement gives no usable curve).");
            return false;
        }
        // The review held the window to the wizard's fields; a snapshot the
        // crossover moved under can still leave a stated edge past the other.
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
                // The corrected curve as the wizard's Source + EQ draws it: the
                // bank in the chain, through the window or over the average.
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

        // Landed the way the wizard's Return lands, against the capture the
        // request was built with — the reply may have asked for the point
        // measurement under a hybrid view, and the token says which it was.
        // The datum the fit was built against becomes the project's now, as the
        // wizard's Return moves it on the way back; the token carries the same
        // value, which is what the landing checks.
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
            // Nothing landed, so nothing of the request stays — the datum included.
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

    // Everything the import could move, before it moves any of it.
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
        ScheduleSave();
        RedrawAll();
    }

    // The Auto crossover wizard can reorder the blocks, and the block letters a
    // reply used are that order. Restored by identity rather than by index: the
    // list holds the same objects, in another arrangement.
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

    /// <summary>
    /// The channels as the bridge names them, with their live settings and what
    /// each side carries, plus the project figures an engine request is judged
    /// and described against.
    /// </summary>
    internal AgentSessionSnapshot BuildAgentSessionSnapshot() =>
        new(
            AgentChannelSlots()
                .Select(slot => new AgentChannelSnapshot(
                    slot.Block,
                    slot.Side,
                    slot.Channel.SideSettings(slot.RightSide),
                    slot.Channel.SideState(slot.RightSide).TransferImpulseResponse != null,
                    AgentSpatialAverageCaptures(slot.Channel.SideState(slot.RightSide))))
                .ToList(),
            ProcessorSampleRateHz,
            ProcessorProfile.MaxDelayMs,
            lastAgentPackageId,
            AgentAutoDelayDefaults(),
            SpatialAverageMode,
            checkBoxHybrid.Checked,
            project.ActiveSideRight);

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
            // The sum the hybrid view draws beside the measured one: the same two
            // constructions the plot uses (see RedrawMainPlotAsync), the active
            // side's from the shown set, the opposite side's under its own gate
            // placement — and null, as on screen, when the sides cannot be held
            // to one offset.
            IReadOnlyList<SignalPoint>? hybridSum = hybrid == null || magnitudes == null
                ? null
                : rightSide == activeRight
                    ? BuildActiveHybridSumCurve(shown, magnitudes, hybrid)
                    : BuildOppositeHybridSumCurve(sideSum, hybrid.OffsetDb)?.Points;

            for (int index = 0; index < shown.Count; index++)
            {
                // The hybrid curves are stored on the captures' own level axis and
                // drawn shifted by the set's datum onto the impulse responses' axis
                // (see BuildMagnitudeCurves); the package carries what is drawn, so
                // every column of a channel compares with every other. The pre-DSP
                // twin is the same capture through no chain, on the same axis.
                IReadOnlyList<SignalPoint>? hybridProcessed = null;
                IReadOnlyList<SignalPoint>? hybridPreDsp = null;
                if (hybrid != null && magnitudes != null && !hybrid.PointMeasuredChannels[index])
                {
                    hybridProcessed = ShiftedBy(hybrid.Channels[index], hybrid.OffsetDb);
                    hybridPreDsp = BuildHybridPreDspCurve(
                        shown[index].Channel, rightSide, magnitudes[index].Points,
                        smoothing, hybrid.OffsetDb);
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
                    // The family the hybrid curves are built from — the selected
                    // mode's capture — not whichever capture the side happens to hold.
                    state.SpatialAverageFor(SpatialAverageMode) != null ? SpatialAverageMode.ToString() : null,
                    // And every family it holds, read or not: the difference between
                    // "has no average" and "has one the view is not using".
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
            checkBoxHybrid.Checked,
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

    // Every capture family the side holds, in the mode enum's own names so the
    // package's per-channel list and its analysis.spatialAverage.mode agree.
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

    // The channel's spatial average through NO chain, on the impulse responses'
    // level axis: the same capture, calibration and grid the hybrid view draws
    // the channel with, the chain replaced by identity, the set's datum applied.
    // Null when the channel has no capture of the selected family.
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
