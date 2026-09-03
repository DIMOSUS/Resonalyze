using System.Globalization;
using System.Numerics;
using System.Text;
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
    // The id of the package this session most recently copied and the session
    // fingerprint it was copied at (ComputeAgentFingerprint); a reply naming
    // another package, or this one after the session has changed, gets a warning
    // in the review and its engine requests refused. Not persisted — a reopened
    // session cannot vouch for what an earlier one copied, and the expected
    // current values are the guard that matters for the settings rows.
    private string? lastAgentPackageId;
    private string? lastAgentPackageFingerprint;

    // One bridge operation at a time: a second Copy while the first gathers
    // would race the coordinator, and an import while a copy gathers would move
    // the settings the package is being read from.
    private bool agentBusy;

    private ContextMenuStrip? agentMenu;

    // The smoothing the package's hybrid curves and sums travel at: the width of
    // the package's own 12-point-per-octave grid, the nearest a grid can come to
    // the Off the manual reads the hybrid view at (see CaptureAgentPackageInputsAsync).
    private const int AgentHybridSmoothingInverseOctaves = 12;

    /// <summary>
    /// The EQ Wizard's Auto Tune settings at the moment an import fits a bank
    /// without it — wired by the host so the import produces the bank the
    /// wizard's button would; the wizard's opening values when nothing is wired.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<EqAutoTunePolicy>? AutoTunePolicyProvider { get; set; }

    /// <summary>
    /// Records the package this session just copied and the session it was copied
    /// from, for the review's staleness check.
    /// </summary>
    internal void RememberAgentPackage(string packageId, string fingerprint)
    {
        lastAgentPackageId = packageId;
        lastAgentPackageFingerprint = fingerprint;
    }

    /// <summary>
    /// The session as one hash (<see cref="AgentSessionFingerprint"/>): everything a
    /// package vouches for that an expected current value does not already guard.
    /// The blocks in order (their letters are the channel ids), what each side is
    /// measured and averaged with, every chain — an import undone puts the chains
    /// back under a package that was copied without them — and the project figures
    /// the diagnostics were computed under, the side on screen and the view among
    /// them, since the engines read those. Not the zoom or the smoothing selector:
    /// they change what is shown, not what was measured, and the package has its
    /// own smoothing anyway.
    /// </summary>
    /// <remarks>
    /// Taken at Copy and again at every review, so no path that changes the session
    /// has to remember to forget the package: a source picked by hand, a capture
    /// attached, a gate moved, a project loaded all change the hash and the review
    /// reads the difference. The same session loaded again hashes the same, and a
    /// package copied from it stays good — which forgetting could never say.
    /// </remarks>
    internal string ComputeAgentFingerprint()
    {
        var lines = new List<string>
        {
            $"processor;{ProcessorProfile.ModelId};{ProcessorSampleRateHz}",
            $"average;{SpatialAverageMode};{checkBoxHybrid.Checked}",
            // The side on screen and the view are what the package was computed
            // for, and what the engines read: Auto crossover proposes from the
            // shown side's measurements, a single-sided Auto delay aligns it, and
            // whether Auto-tune's default source is the hybrid follows the view.
            $"view;{project.ActiveSideRight};{SelectedGroupView}",
            $"phase;{project.PhaseWindowMode};{project.PhaseFdwCycles};{project.PhaseDetrendMode};" +
                $"{Number(project.PhaseGateLeftMs)};{Number(project.PhaseGatePlateauMs)};" +
                $"{Number(project.PhaseGateRightMs)};" +
                $"{Number(project.PhaseGateLeft.OffsetMs)};{Number(project.PhaseGateLeft.DetrendMs)};" +
                $"{Number(project.PhaseGateRight.OffsetMs)};{Number(project.PhaseGateRight.DetrendMs)}",
            $"stereo;{Number(project.StereoSceneOffsetMagnitudeMs)};{project.StereoRightHandDrive};" +
                $"{Number(project.StereoLevelDifferenceDb)};{Number(project.RearFillOffsetMs)}",
            // The selected correction by id AND by its points: a curve re-read or
            // edited under the same id (ReconcileCalibrationSelection) is another
            // correction on every measurement the package reads.
            $"calibration;{project.CalibrationId};{ownCalibrationSelected};{Curve(Calibration)}",
            $"target;{Number((double)numericTargetLevel.Value)};{TargetShape(project.Target)}",
            // The assistant reasons from the notes as much as from the curves.
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
                // The measurement: its reference, and the CONTENT actually loaded
                // behind it — a file re-measured and saved over its own name is a
                // different impulse response with the same reference, length and
                // rate — with what the package reads off it: the peak, the measured
                // band, the coherence, the calibration it was read through.
                settings.HistoryEntryId, settings.SourceFilePath, settings.DisplayName,
                Digest(state.TransferImpulseResponse), state.SampleRate, state.TransferPeakIndex,
                Number(state.MeasuredBand.LowestHz), Number(state.MeasuredBand.HighestHz),
                Digest(state.TransferCoherence),
                // The correction this side's curves are actually read through —
                // its own under "Own (as measured)", the selected one otherwise —
                // by its points.
                Curve(CalibrationFor(state)),
                // The captures by session: a pass re-recorded over the same file is
                // a new capture session with a new id.
                settings.SpatialAveragePath, Capture(state.SpatialAverage), Capture(state.ArrayCapture),
                Number(settings.GainDb), Number(settings.DelayMs), settings.InvertPolarity,
                settings.CrossoverKind, Edge(settings.HighPassEdge), Edge(settings.LowPassEdge),
                AgentPeqHash.Compute(settings.PeqPreampDb, settings.PeqBands)));
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
                "Each measured channel's excess group delay — the part of its phase a " +
                "PEQ cannot touch: arrivals and reflections — read through the phase gate " +
                "as the analyzer shows it.")
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

            // The probes go FIRST, before anything is written: a probe answers a
            // question about the tune as it stands, and reading it after this
            // import's own rows had landed would answer a different one.
            await RunAgentProbesAsync(toApply, summary);

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

                case TuneJunctionOperation junction:
                    ran |= await RunAgentTuneJunctionAsync(junction, summary);
                    break;

                // Read before any of this ran, and wrote nothing; the import's
                // own loop has nothing left to do with it.
                case ProbeOperation:
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
        // After the wizard (the review refuses the pair together anyway) and
        // before Auto delay, which realigns whatever the crossover became.
        TuneJunctionOperation => 2,
        RunAutoDelayOperation => 3,
        _ => 4
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

    // The junction tune without a dialog. The review resolved the junction on a
    // session snapshot; the same two blocks are read off the live channels here,
    // every side the pair is measured on goes to the tuner with its raw
    // responses and its current chains, and the one crossover the tuner settles
    // on is written to both sides of both blocks — as the wizard writes, and as
    /// <summary>
    /// The two blocks of a junction as the tuner and the probes read them: every
    /// side that carries both measurements, with each side's own chain — or the
    /// one side asked for, where a reading belongs to a single physical channel.
    /// A mono block is routed to both sides, as the panel sums it.
    /// </summary>
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
                lower.SideSettings(rightSide && !lower.Pair.Mono).ToChain(),
                upperState.TransferImpulseResponse,
                upper.SideSettings(rightSide && !upper.Pair.Mono).ToChain(),
                lowerState.SampleRate));
        }

        return sides.Count == 0
            ? ([], "no side has both blocks measured")
            : (sides, null);
    }

    /// <summary>
    /// The probes of one import: readings the reply asked for, computed on the
    /// tune as it stands and written NOWHERE. Every probe of the import goes
    /// into ONE document — the clipboard holds one text, and the user pastes
    /// once — and the summary says it is there and asks for it to be pasted
    /// back. A probe that cannot be computed says so in its own entry rather
    /// than taking the others down with it.
    /// </summary>
    /// <returns>Whether a document reached the clipboard.</returns>
    private async Task<bool> RunAgentProbesAsync(
        IReadOnlyList<AgentOperationVerdict> toApply, List<string> summary)
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

        var reports = new List<AgentProbeReport>(probes.Count);
        UseWaitCursor = true;
        try
        {
            foreach (ProbeOperation probe in probes)
            {
                // One reading that cannot be taken is one entry saying so: a
                // probe reads several things the user ticked together, and a
                // throw from any of them must not take the others — or the
                // import around them — down.
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
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
            }
        }

        // The package the reading belongs beside, while this is still the
        // session it was copied from — the same rule the diagnostic follows.
        bool matches = lastAgentPackageFingerprint != null &&
            lastAgentPackageFingerprint == ComputeAgentFingerprint();
        AgentProbeBuildResult result = AgentProbeBuilder.Build(
            reports, matches ? lastAgentPackageId : null, matches, DateTimeOffset.UtcNow);
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

        return true;
    }

    // One probe's answer. Every reading is taken off snapshots — the responses,
    // the chains, the gate — so the compute runs off the UI thread and the tune
    // is never touched.
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

        // A probe reads the side its junction id names: a variant's changes are
        // that side's channels' settings, and a reading of the other side would
        // answer a question nobody asked. (A crossover is one filter for both
        // sides, so a reply weighing one asks for both junctions.)
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

        return new AgentProbeReport(
            probe.Id, probe.Probe, probe.JunctionId, lowerSnapshot!.Id, upperSnapshot!.Id, null,
            [
                AgentCurveSampling.Frequency(probed.SharedBandLowHz),
                AgentCurveSampling.Frequency(probed.SharedBandHighHz)
            ],
            probed.Entries.Select(AgentProbeEntryOf).ToList(),
            null,
            null);
    }

    // The FIRST entry is the tune as it stands, because the panel builds it
    // first — read off the position, never off the label, which is the reply's
    // own text and may say anything, "current" included.
    private static AgentProbeEntry AgentProbeEntryOf(JunctionProbeEntry entry, int index) =>
        new(
            entry.Label,
            index == 0,
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

    /// <summary>
    /// How a probe's own baseline entry — the tune as it stands — is labelled.
    /// A reply's variant may carry the same word; what marks the baseline in
    /// the document is its POSITION, not this text.
    /// </summary>
    internal const string AgentProbeCurrentLabel = "current";

    // The settings a junction probe reads the junction under, the tune's own
    // first. A variant's changes go onto COPIES of the two channels' settings —
    // held to the same limits a settings operation is, through the validator's
    // own path — and the chains come off those copies exactly as the panel
    // builds its own. Nothing here touches a live setting.
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

            // One side, and the snapshot's settings ARE that side's, so the
            // copies' chains are the whole variant — nothing to merge.
            var chains = new JunctionProbeChains(lowerCopy.ToChain(), upperCopy.ToChain());
            variants.Add(new JunctionProbeVariant(
                string.IsNullOrWhiteSpace(variant.Label) ? $"variant {index}" : variant.Label,
                sides.Select(_ => chains).ToList()));
            index++;
        }

        return (variants, null);
    }

    // Every measured channel's excess group delay as a diagnostic series — the
    // menu item's own reading, computed the same way, for a probe that asked
    // for it by name.
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

        (double, double, double) gate =
            (project.PhaseGateLeftMs, project.PhaseGatePlateauMs, project.PhaseGateRightMs);
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

    // Undo AI import puts back. The compute runs off the UI thread under a
    // wait cursor, fingerprinted on both sides so an edit landing under it
    // drops the result instead of being written over.
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
            // One slope for both edges unless the reply frees them: a junction is
            // one crossover, and the free search costs slopes² per corner.
            operation.IndependentSlopes ?? false,
            ProcessorSampleRateHz);

        // The compute runs off the UI thread under a wait cursor, and the
        // session is fingerprinted on both sides of it rather than the panel
        // disabled around it: disabling and re-enabling a panel this size
        // repaints every plot twice, which on a session with spatial averages
        // costs seconds more than the tune itself. An edit that lands under the
        // compute moves the fingerprint, and the result is then dropped rather
        // than written against chains the tuner never saw.
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
        ScheduleSave();
        RedrawAll();

        summary.Add($"{label}: applied — {before} → {after} ({window}, {scoreDelta}).");
        AppendJunctionReadings(summary, result, best: true);
        return true;
    }

    // The readings per side on the junction's own octave-each-side band — the
    // package's band, so they compare with what the assistant read: the current
    // crossover's, and the applied one's where the tune changed something —
    // loss, dip and ripple of the sum at the current delays, and what the best
    // delay of the upper channel would leave.
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
                    .Append(alignment.InvertUpper ? ", inverted" : string.Empty);
            }
            summary.Add(line.Append('.').ToString());
        }

        static string Db(double value) =>
            (value + 0).ToString("0.0", CultureInfo.InvariantCulture) + " dB";
    }

    private static string JunctionText(JunctionTuneCandidate candidate, string lowerBlock, string upperBlock) =>
        $"{lowerBlock} {(candidate.LowerLowPass is { } low ? "LP " + AgentEdgeText(low) : "no low-pass")} + " +
        $"{upperBlock} {(candidate.UpperHighPass is { } high ? "HP " + AgentEdgeText(high) : "no high-pass")}";

    // The abbreviation the channel block's own family combo uses.
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
        // The wizard's own refusal: kept all-pass bands that fill Max Filters
        // leave the fit nothing to place, and a bank over the limit is not one
        // the button would ever hand back.
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
            // The field's ValueChanged writes the project through OnViewChanged,
            // which is what the suppression above silences — so the project's
            // datum, the one the package and the saved session read, is written
            // here by hand, as the Hybrid tick's is.
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
    // A diagnostic the assistant asked for by name: every measured channel's
    // excess group delay, as the analyzer shows it for one impulse response, in
    // a text of its own beside the package — which is already the size a chat
    // takes. Named after the last package copied, so the reader lays the two
    // side by side by channel id.
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
            // Snapshot on the UI thread — the responses, their anchors and bands,
            // the gate shape — and compute off it: a gated FFT with a
            // minimum-phase reconstruction per channel is not a UI-thread job on
            // a large installation.
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

            (double, double, double) gate =
                (project.PhaseGateLeftMs, project.PhaseGatePlateauMs, project.PhaseGateRightMs);
            // The package the curves belong beside — while this is still the
            // session it was copied from. Changed, the id would tie the curves to
            // channel ids and gates that no longer mean what they meant there.
            string? packageId = lastAgentPackageFingerprint == ComputeAgentFingerprint()
                ? lastAgentPackageId
                : null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            (AgentDiagnosticBuildResult result, int count) = await Task.Run(() =>
            {
                var channels = new List<AgentDiagnosticChannel>(measured.Count);
                foreach ((string id, Complex[] response, int peakIndex, int sampleRate, MeasuredBand band) in measured)
                {
                    IReadOnlyList<SignalPoint>? curve = BuildExcessGroupDelayCurve(
                        response, peakIndex, sampleRate, band, gate);
                    if (curve != null)
                    {
                        channels.Add(new AgentDiagnosticChannel(id, curve));
                    }
                }

                return (AgentDiagnosticBuilder.BuildExcessGroupDelay(channels, packageId, now), channels.Count);
            });
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
                await GatherAgentPackageAsync() ?? await GatherAgentPackageAsync();
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

    // The inputs and the fingerprint of ONE session state. The capture vouches
    // that the coordinator's revision held while it gathered, but the fingerprint
    // reads things the revision does not cover — the target level, the Hybrid
    // tick, a gate pin — so it is taken on both sides of the gather and the pair
    // is kept only when the two agree. Null otherwise; the caller retries once,
    // as it does for the capture's own refusal.
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
        // One smoothing for every package, whatever the display shows: the Sum
        // loss and the curves a reader compares across sessions and across users
        // must not move with a combo box, and a dip's depth at 1/48 octave is not
        // the same reading as at 1/6. The panel's own psychoacoustic setting is
        // the one the manual reads a tune at, so it is the one the package uses.
        int smoothing = SpectrumSmoothing.PsychoacousticCode;
        MagnitudeGateSnapshot packageGate = magnitudeGate with { SmoothingInverseOctaves = smoothing };
        // Except the hybrid curves and their sum, which the manual reads with the
        // smoothing OFF: the average has already averaged the position-dependent
        // wiggles down, and a fractional-octave window straddling a crossover's
        // skirt pulls the level up toward the passband, right where the acoustic
        // slopes are judged. Off cannot travel on a 12-point-per-octave grid, so
        // they go at the grid's own width, 1/12 octave — one step, no more.
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

            // A metric block of the package's own, for BOTH sides: the panel's
            // `metrics` builds its channel and sum curves through the live gate
            // snapshot — the display's smoothing — and BuildCurves' smoothing
            // argument reaches only the loss curve. These delegates window through
            // the package gate instead, so processedDb, sumDb and the loss all
            // carry the package's smoothing. The opposite side windows through ITS
            // gate placement, never the active side's pin — the same rule the
            // on-screen opposite sum follows.
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
            // The hybrid set's references at the hybrid's own width: a channel the
            // array mode falls back to its point measurement for enters the hybrid
            // sum through these, and one smoothed psychoacoustically first would be
            // smoothed twice, at two widths, in a sum that claims one. Built only
            // when the hybrid is asked for — it is a second gated pass.
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
            HybridMagnitudes? hybrid = hybridReferences != null
                ? BuildHybridMagnitudes(shown, hybridReferences, rightSide, AgentHybridSmoothingInverseOctaves)
                : null;
            // The sum the hybrid view draws beside the measured one: the same two
            // constructions the plot uses (see RedrawMainPlotAsync), the active
            // side's from the shown set, the opposite side's under its own gate
            // placement — and null, as on screen, when the sides cannot be held
            // to one offset.
            IReadOnlyList<SignalPoint>? hybridSum = hybrid == null || hybridReferences == null
                ? null
                : rightSide == activeRight
                    ? BuildActiveHybridSumCurve(shown, hybridReferences, hybrid, hybridGate)
                    : BuildOppositeHybridSumCurve(sideSum, hybrid.OffsetDb, hybridGate)?.Points;

            for (int index = 0; index < shown.Count; index++)
            {
                // The hybrid curves are stored on the captures' own level axis and
                // drawn shifted by the set's datum onto the impulse responses' axis
                // (see BuildMagnitudeCurves); the package carries what is drawn, so
                // every column of a channel compares with every other. The pre-DSP
                // twin is the same capture through no chain, on the same axis.
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
            // The package's own smoothing, not the display's (see the capture's
            // note): psychoacoustic, 1/6 octave at its narrowest.
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

    // The measurement's excess group delay as the analyzer shows it for one
    // impulse response: the raw transfer response through the project's phase
    // gate, placed at the channel's OWN arrival (the handoff's rule for a
    // measurement read without the chain), at the group-delay view's default
    // smoothing. The
    // minimum-phase part — what the magnitude dictates and a minimum-phase PEQ
    // straightens along with it — is taken out; what remains is what no PEQ can
    // touch, which is the question a junction that will not sum asks.
    // Pure: a gated FFT, a minimum-phase reconstruction and the difference, so
    // it runs off the UI thread on a snapshot of the channel and the gate shape.
    private static IReadOnlyList<SignalPoint>? BuildExcessGroupDelayCurve(
        Complex[] impulseResponse, int peakIndex, int sampleRate, MeasuredBand band,
        (double LeftMs, double PlateauMs, double RightMs) gate)
    {
        int anchorIndex = ProcessedChannels.StartAnchorIndex(impulseResponse, peakIndex, sampleRate);
        GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
            new ImpulseMeasurementView(impulseResponse, anchorIndex, sampleRate)
            {
                LowestMeasuredFrequencyHz = band.LowEdgeHz,
                HighestMeasuredFrequencyHz = band.HighEdgeHz
            },
            anchorIndex * 1_000.0 / sampleRate,
            gate.LeftMs,
            gate.PlateauMs,
            gate.RightMs,
            // The group-delay view's own default (1/12 octave), not the magnitude
            // curves' psychoacoustic setting: a group delay is a phase slope, and
            // a psychoacoustic width is a hearing model for levels, not for time.
            FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves,
            includeMinimumPhase: true);
        return curves.Excess?.Points;
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

}
