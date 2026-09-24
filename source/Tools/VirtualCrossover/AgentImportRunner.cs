using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>The panel's side of an AI import: the view the bridge reads, and the controls and dialogs an engine's run needs.</summary>
internal interface IAgentImportHost : IVirtualCrossoverWorkHost
{
    /// <summary>The view the bridge's readings follow, read off the controls.</summary>
    AgentViewInputs View();

    GatePlacementVerdict? GatePlacement { get; }

    bool HybridRequested { get; }

    EqAutoTunePolicy AutoTunePolicy();

    /// <summary>Sets the mode and ticks the Hybrid box together; the project saves and redraws once.</summary>
    void UseSpatialAverage(VirtualCrossoverSpatialAverageMode mode);

    /// <summary>The crossover wizard; null when it applied, else why it did not.</summary>
    string? OpenAutoSetupWizard();

    Task ApplyAutoDelayAsync(AutoDelayRunResult result);

    void ShowChannel(VirtualCrossoverChannel channel);

    void ShowBank(VirtualCrossoverChannel channel);

    /// <summary>Writes the project's target level and shows it, as an edit of the field would.</summary>
    void SetTargetLevel(double levelDb);

    /// <summary>The side lock takes the pairs as they stand, not as a difference to carry.</summary>
    void RememberSides();

    void SaveAndRedraw();
}

/// <summary>Runs an AI import once the review has been answered: the probes, the re-check before anything is written, the
/// settings rows and the engines in their fixed order, and the one step of undo. The panel owns the menu, the clipboard
/// and every dialog. See docs/tech/agent-bridge.md#import-flow.</summary>
internal sealed class AgentImportRunner(
    VirtualCrossoverSession session,
    AgentSessionReader reader,
    VirtualCrossoverEqHandoff handoff,
    IAgentImportHost host)
{
    private const int AutoDelayReportLinesInSummary = 16;

    private long undoGeneration;

    /// <summary>The last import's undo; it restores only into the project generation it was taken in.</summary>
    public AgentImportUndo? Undo { get; private set; }

    public string Fingerprint() => reader.Fingerprint(host.View());

    public AgentSessionSnapshot Snapshot() => reader.Snapshot(host.View());

    public AgentImportUndo CaptureUndo() => AgentImportUndo.Capture(session, reader, host.View());

    /// <summary>A bound project replaces the settings objects the undo would restore into.</summary>
    public void ForgetUndo() => Undo = null;

    /// <summary>The undo to restore, or why there is none; either way it is spent.</summary>
    public (AgentImportUndo? Undo, string? Refusal) TakeUndo()
    {
        AgentImportUndo? undo = Undo;
        Undo = null;
        if (undo == null)
        {
            return (null, null);
        }

        return undoGeneration == session.ProjectGeneration
            ? (undo, null)
            : (null, "A session was loaded since the last AI import.");
    }

    /// <summary>Runs an import's probes on the tune as it stands, writing nothing; all probes go into one clipboard document,
    /// and a failing probe reports in its own entry. Returns whether a document reached the clipboard.</summary>
    public async Task<bool> ProbesAsync(
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

        // Compared at every reading's boundary: a change-and-revert would pass a first-to-last check. See docs/tech/agent-bridge.md#probes.
        string state = Fingerprint();
        bool steady = true;
        var reports = new List<AgentProbeReport>(probes.Count);
        using (host.Busy(disable: false))
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
                        probe, reader, host.View(), host.GatePlacement is { CutsChannels: true }));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    reports.Add(new AgentProbeReport(
                        probe.Id, probe.Probe, probe.JunctionId, null, null,
                        exception.Message.TrimEnd('.'), null, null, null, null));
                }
                if (host.IsGone)
                {
                    return false;
                }

                string after = Fingerprint();
                steady &= string.Equals(state, after, StringComparison.Ordinal);
                state = after;
            }
        }

        // Same rule as the diagnostic: link to the package only while this is the session it was copied from.
        bool matches = reader.LastPackageFingerprint != null &&
            reader.LastPackageFingerprint == state;
        AgentProbeBuildResult result = AgentProbeBuilder.Build(
            reports, matches ? reader.LastPackageId : null, matches, steady, DateTimeOffset.UtcNow);
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

    /// <summary>Writes an import. Re-judges the ticked rows against the live session first: the probes take seconds and the
    /// panel stays editable under them. Returns whether an engine changed the project.</summary>
    public async Task<bool> CommitAsync(
        AgentProposal proposal,
        IReadOnlySet<string> selected,
        string? reviewedFingerprint,
        int proposedRows,
        List<string> summary,
        AgentProgressDialog? progress)
    {
        string? moved = AgentProposalApplier.Prepare(
            proposal, selected, reviewedFingerprint, Snapshot(),
            out List<AgentOperationVerdict> toApply, out _);
        if (moved != null)
        {
            summary.Add($"Nothing was written: {moved}");
            return false;
        }

        // Armed before the first write: an engine can throw after the rows landed. The previous undo returns only if nothing moved.
        AgentImportUndo undo = CaptureUndo();
        AgentImportUndo? previousUndo = Undo;
        long previousUndoGeneration = undoGeneration;
        Undo = undo;
        undoGeneration = session.ProjectGeneration;

        List<AgentUndoEntry> written = AgentProposalApplier.Apply(toApply);
        if (written.Count > 0)
        {
            ShowWritten(written);
            int rows = toApply.Count(verdict => verdict.Operation is AgentSettingsOperation);
            summary.Add(
                $"Applied {rows} of {proposedRows} proposed change{(proposedRows == 1 ? "" : "s")}.");
            // The rows name their sides: the side lock takes them as written. Before the engines, whose junction-tune save would read the rows as a hand edit.
            host.RememberSides();
        }

        bool engines = await EnginesAsync(toApply, summary, progress);
        if (written.Count == 0 && !engines)
        {
            Undo = previousUndo;
            undoGeneration = previousUndoGeneration;
        }

        return engines;
    }

    /// <summary>Runs engine requests in a fixed order regardless of reply order; cancelling one skips only it. Returns whether
    /// any changed the project. See docs/tech/agent-bridge.md#engine-order.</summary>
    public async Task<bool> EnginesAsync(
        IReadOnlyList<AgentOperationVerdict> toApply,
        List<string> summary,
        AgentProgressDialog? progress = null)
    {
        bool ran = false;
        // One target level for every fit of this import: the stated one (the review made them agree), else the project's.
        double importTargetLevelDb = AgentEngineRequests.TargetLevelDb(toApply, session.Project.TargetLevelDb);
        EqAutoTunePolicy policy = host.AutoTunePolicy();
        // Iterate verdicts: the snapshot's settings object still names the channel after the crossover wizard re-letters blocks.
        foreach (AgentOperationVerdict verdict in toApply
            .Where(verdict => verdict.Applicable)
            .Where(verdict => verdict.Operation is not AgentSettingsOperation)
            .OrderBy(verdict => AgentEngineRequests.Order(verdict.Operation!)))
        {
            AgentOperation operation = verdict.Operation!;
            if (operation is not ProbeOperation)
            {
                progress?.Report(AgentEngineRequests.StepText(operation, verdict));
            }

            switch (operation)
            {
                case UseSpatialAverageOperation spatial:
                    bool applied = UseSpatialAverage(spatial);
                    summary.Add(applied
                        ? $"Spatial average: {spatial.Mode}, hybrid on."
                        : $"Spatial average: skipped ('{spatial.Mode}' names no capture family).");
                    ran |= applied;
                    break;

                case RunAutoCrossoverOperation:
                    string? refused = host.OpenAutoSetupWizard();
                    summary.Add(refused == null
                        ? "Auto crossover: applied."
                        : $"Auto crossover: skipped ({refused}).");
                    ran |= refused == null;
                    break;

                case TuneJunctionOperation junction:
                    ran |= await TuneJunctionAsync(junction, summary);
                    break;

                // Probes already ran before any write.
                case ProbeOperation:
                    break;

                case RunAutoDelayOperation delay:
                    ran |= await AutoDelayAsync(delay, summary);
                    break;

                case AutoTunePeqOperation tune:
                    ran |= await AutoTuneAsync(tune, verdict.Channel!, importTargetLevelDb, policy, summary);
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

    /// <summary>False, changing nothing, for a mode the review should already have refused.</summary>
    public bool UseSpatialAverage(UseSpatialAverageOperation operation)
    {
        if (!AgentOperations.TryParseName(
            operation.Mode, out VirtualCrossoverSpatialAverageMode mode))
        {
            return false;
        }

        host.UseSpatialAverage(mode);
        return true;
    }

    // The button's checks (headless), the dialog's compute and its Apply commit. The panel is disabled during compute: the
    // dialog's modality is what kept the chain still.
    private async Task<bool> AutoDelayAsync(RunAutoDelayOperation operation, List<string> summary)
    {
        (AutoDelayPlan? launch, AutoDelayRefusal? refusal) = VirtualCrossoverAutoDelay.Prepare(
            session, host.GatePlacement, consentToBroadWindow: null);
        if (launch == null)
        {
            summary.Add($"Auto delay: skipped ({refusal!.Summary}).");
            return false;
        }

        AutoDelayRunRequest request = AgentEngineRequests.AutoDelayRequest(operation, reader.AutoDelayDefaults());
        AutoDelayRunResult result;
        using (host.Busy(disable: true))
        {
            result = await launch.Run(request);
        }
        if (host.IsGone)
        {
            return false;
        }

        await host.ApplyAutoDelayAsync(result);
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

    // What the tune writes and says lives in AgentJunctionTune.
    private async Task<bool> TuneJunctionAsync(TuneJunctionOperation operation, List<string> summary)
    {
        (JunctionTunePlan? plan, string? skipped) = AgentJunctionTune.Prepare(
            operation, Snapshot(), session, host.GatePlacement is { CutsChannels: true });
        if (plan == null)
        {
            summary.Add(skipped!);
            return false;
        }

        JunctionTuneRunOutcome outcome = await VirtualCrossoverJunctionTuneRun.RunAsync(plan, Fingerprint, host);
        if (outcome.Failure is { } failure)
        {
            summary.Add($"{plan.Label}: skipped ({failure}).");
            return false;
        }
        if (outcome.Gone)
        {
            return false;
        }
        if (outcome.Result is not { } result)
        {
            summary.Add($"{plan.Label}: skipped (the session changed while the tune ran; nothing was written).");
            return false;
        }

        if (result.Changed)
        {
            AgentJunctionTune.Write(result, plan.Lower, plan.Upper);
            host.ShowChannel(plan.Lower);
            host.ShowChannel(plan.Upper);
            // Remember the result as it stands: read as a difference, a hidden side already holding the new edge would look untouched and get the shown side's whole crossover.
            host.RememberSides();
            host.SaveAndRedraw();
        }
        AgentJunctionTune.Describe(summary, plan, result);
        return result.Changed;
    }

    private async Task<bool> AutoTuneAsync(
        AutoTunePeqOperation operation,
        AgentChannelSnapshot target,
        double targetLevelDb,
        EqAutoTunePolicy policy,
        List<string> summary)
    {
        string label = $"Auto-tune {operation.ChannelId}";
        // By the settings object, not the id: the crossover wizard earlier in this import may have re-lettered the blocks.
        (VirtualCrossoverChannel Channel, bool RightSide)? slot = reader.Slots()
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
            handoff.SpatialAverage(channel, channel.ActiveRight, host.HybridRequested);
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

        // A stated target level travels in the request and reaches the project only once the fit lands: a skipped run must leave nothing, since undo is dropped when nothing ran.
        VirtualDspEqHandoffRequest? request = handoff.Request(
            channel, withChain: true, host.HybridRequested, average, targetLevelDb);
        if (request == null)
        {
            summary.Add($"{label}: skipped (no measurement to fit against).");
            return false;
        }

        VirtualCrossoverTargetSettings targetSettings =
            session.Project.Target ?? new VirtualCrossoverTargetSettings();
        TargetCurveSpec spec = (host.View().TargetCurve ?? targetSettings.ToCurve()).Normalized().Spec;
        // The wizard's own refusal: kept bands filling Max Filters leave the fit no room.
        int room = EqAutoTuneHeadless.RoomUnderMaxFilters(request, policy);
        if (room <= 0)
        {
            string kept = EqWizardFit.DescribeKeptCount(EqAutoTuneHeadless.KeptBands(request.BankSeed));
            summary.Add(
                $"{label}: skipped (keeping {kept} " +
                $"leaves no room under Max Filters ({policy.MaxBands})).");
            return false;
        }

        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request, spec, policy, operation.MinHz, operation.MaxHz,
            operation.AllowShelves, operation.BoostMode);
        bool lifts = inputs.Boosts == EqAutoTuneBoosts.Allowed;
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
        // The wizard's refusal, as a skip: an empty fit would replace this channel's bank with nothing, and one
        // channel outside its measured band must not stop the channels after it.
        if (EqAutoTuneHeadless.NoMeasuredDataRefusal(inputs) is { } noData)
        {
            summary.Add($"{label}: skipped ({noData}).");
            return false;
        }

        string? levelWarning = EqTargetLevelCheck.Warning(
            EqTargetLevelCheck.TargetAboveSourceDb(
                inputs.Source, inputs.Target, inputs.MinHz, inputs.MaxHz),
            !lifts, inputs.MinHz, inputs.MaxHz);
        if (levelWarning != null)
        {
            summary.Add($"{label}: skipped ({levelWarning.Split('.')[0]}).");
            return false;
        }

        double? before = EqAutoTuneHeadless.RmsErrorDb(
            inputs.Source, inputs.Target, inputs.MinHz, inputs.MaxHz);
        EqualizationCurve fitted;
        double? after;
        using (host.Busy(disable: true))
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
        if (host.IsGone)
        {
            return false;
        }

        // Landed as the wizard's Return lands, against the capture the request was built with (the token says which). The fit's datum becomes the project's, as Return moves it.
        double previousTargetLevel = session.Project.TargetLevelDb;
        host.SetTargetLevel(request.TargetLevelDb);
        if (!handoff.TryReturn(request.Token, fitted, average.Capture))
        {
            host.SetTargetLevel(previousTargetLevel);
            summary.Add($"{label}: skipped (the channel changed while the fit ran).");
            return false;
        }

        channel.SideSettings(channel.ActiveRight).PeqSourceName = "Auto-tune (AI import)";
        host.ShowBank(channel);
        int fittedBands = fitted.Bands.Count - inputs.Kept.Count;
        summary.Add(
            $"{label}: applied — {fittedBands} band{(fittedBands == 1 ? "" : "s")}" +
            (inputs.Kept.Count > 0 ? $" + {EqWizardFit.DescribeKeptCount(inputs.Kept)} kept" : string.Empty) +
            $", preamp {fitted.PreampDb:0.0} dB, {inputs.MinHz:0}–{inputs.MaxHz:0} Hz, " +
            $"{EqWizardFit.DescribeBoosts(inputs.Boosts)}, on the " +
            $"{(average.Capture != null ? "spatial average" : "point measurement")}; " +
            $"RMS error {Rms(before)} -> {Rms(after)}.");
        return true;

        static string Rms(double? value) => value is { } rms ? $"{rms:0.0} dB" : "n/a";
    }

    private void ShowWritten(IReadOnlyList<AgentUndoEntry> entries)
    {
        foreach (VirtualCrossoverChannel channel in AgentImportUndo.ChannelsOf(entries, reader))
        {
            host.ShowChannel(channel);
        }
    }
}
