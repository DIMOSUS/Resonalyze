using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>Auto delay and the phase gate it reads through: the button's checks, the dialog, and the commit. The run itself
/// is <see cref="VirtualCrossoverAutoDelay"/>.</summary>
public partial class VirtualCrossoverPanel
{
    private async void AutoAlignDelay()
    {
        (AutoDelayPlan? plan, AutoDelayRefusal? refusal) = VirtualCrossoverAutoDelay.Prepare(
            session, gatePlacement, ConsentToBroadWindowSearch);
        if (plan == null)
        {
            ShowRefusal(refusal!);
            return;
        }

        using var dialog = new VirtualCrossoverAutoDelayDialog();
        // The dialog edits layout-neutral magnitudes; the project stores them layout-signed (see VirtualCrossoverAutoDelay.Commit).
        dialog.Init(
            plan.Stereo,
            session.Project.StereoSceneOffsetMagnitudeMs,
            session.Project.StereoRightHandDrive,
            Math.Abs(session.Project.StereoLevelDifferenceDb),
            plan.Run,
            plan.PolarityWarning,
            plan.HasRearFill,
            session.Project.RearFillOffsetMs);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } result ||
            IsDisposed)
        {
            return;
        }

        await ApplyConfirmedAutoDelayAsync(result);
    }

    private bool ConsentToBroadWindowSearch() =>
        MessageBox.Show(
            FindForm(),
            "No channel has a crossover configured, so the delay search " +
            "will use a broad 100 Hz – 10 kHz window instead of the " +
            "crossover region." +
            Environment.NewLine + Environment.NewLine +
            "For an accurate alignment set the crossover filters first, " +
            "then run Auto delay again." +
            Environment.NewLine + Environment.NewLine +
            "Run the broad-window search anyway?",
            "Virtual DSP",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    private void ShowRefusal(AutoDelayRefusal refusal)
    {
        if (refusal.Message is { } message)
        {
            ShowError(message, refusal.Detail ?? string.Empty);
        }
        else if (refusal.Beep)
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    // Commit first, then the outcome metric best-effort: a metric failure must not read as a failed Apply.
    private async Task ApplyConfirmedAutoDelayAsync(AutoDelayRunResult result)
    {
        try
        {
            CommitAutoDelayResult(result);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Auto delay apply failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError("Auto delay apply failed.", exception.Message);
            }

            return;
        }

        try
        {
            await AppendOutcomeMetricAsync(result);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Auto delay outcome metric failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError(
                    "Auto delay was applied, but the outcome metric could not " +
                    "be computed.",
                    "The settings are in place; only the diagnostic log is " +
                    "missing its final metric.\r\n\r\n" + exception.Message);
            }
        }
    }

    private void CommitAutoDelayResult(AutoDelayRunResult result)
    {
        VirtualCrossoverAutoDelay.Commit(result, session.Project);
        foreach (VirtualCrossoverChannel runtime in
            result.Outcomes.Select(outcome => outcome.Runtime).Distinct())
        {
            ApplySettingsToControl(runtime);
        }

        // "Keep the hidden side's polarity" is invisible to the lock as a difference, so re-remember the result.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
        VirtualCrossoverAutoDelay.WriteLog(result.Log.ToString());
    }

    private async Task AppendOutcomeMetricAsync(AutoDelayRunResult result)
    {
        // RedrawAll pushes the read-out asynchronously, so recompute here; capture the side before the await.
        bool metricSideRight = session.ActiveSideRight;
        VirtualCrossoverProcessedRender? render = await ProcessChannelsAsync();
        List<ProcessedChannel> outcomeChannels = render?.Channels ?? [];
        (_, _, List<SignalPoint>? outcomeLoss) =
            metrics.BuildCurves(outcomeChannels, session.MagnitudeGate.SmoothingInverseOctaves);
        result.Log.AppendLine(
            $"Metric ({(metricSideRight ? "R" : "L")} side):");
        result.Log.AppendLine(VirtualCrossoverMetric.FormatDetail(
            metrics.BuildEntries(outcomeChannels, outcomeLoss)));
        VirtualCrossoverAutoDelay.WriteLog(result.Log.ToString());
    }

    // Both automatic commands are verified on the gated view, so a misplaced gate refuses them.
    private bool GateIsMisplaced => gatePlacement is { CutsChannels: true };

    private bool RefuseOnMisplacedGate(string command)
    {
        if (gatePlacement is not { CutsChannels: true } verdict)
        {
            return false;
        }

        ShowError(verdict.FormatRefusal(command), verdict.FormatDetail());
        return true;
    }

    private async Task OpenPhaseGateDialogAsync()
    {
        VirtualCrossoverProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        List<ProcessedChannel> processed = render.Channels;
        if (processed.Count == 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        int sampleRate = processed[0].SampleRate;
        int reference = ProcessedChannels.SharedStartAnchorIndex(processed);
        double fitOffsetMs = PhaseGatePlacement.EarliestStartMs(
            PlacementChannel.From(processed), sampleRate);

        var traces = processed
            .Select(item => new IrPreviewTrace(
                item.ImpulseResponse,
                item.Channel.Name,
                item.Color))
            .ToList();

        VirtualCrossoverPhaseGate stored = VirtualCrossoverPhaseGate.For(
            session.Project, session.ActiveSideRight, preview: null);
        using var dialog = new VirtualCrossoverGateDialog();
        dialog.Init(
            traces,
            sampleRate,
            stored.SharedOffsetMs(processed, sampleRate),
            stored.LeftMs,
            stored.PlateauMs,
            stored.RightMs,
            // One τ for every curve keeps relative phase through the detrend.
            stored.StoredDetrendMs ?? reference * 1_000.0 / sampleRate,
            stored.WindowMode,
            stored.FdwCycles,
            stored.DetrendMode,
            fitOffsetMs,
            autoOffset: stored.StoredOffsetMs == null);
        // Wired after Init so seeding the controls does not redraw.
        dialog.PreviewChanged = preview =>
        {
            session.GatePreview = preview;
            RequestRedraw();
        };

        try
        {
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // Only the placement is per side; lengths and modes are project-wide.
                VirtualCrossoverPhaseGateSettings gate =
                    session.Project.PhaseGateFor(session.ActiveSideRight);
                // Auto = null: keeps following the earliest channel IR start.
                VirtualCrossoverGatePreview saved = dialog.Gate;
                gate.OffsetMs = saved.AutoOffset ? null : saved.OffsetMs;
                gate.DetrendMs = saved.DetrendMs;
                session.Project.SetPhaseGateLengths(saved.LeftMs, saved.PlateauMs, saved.RightMs);
                session.Project.PhaseWindowMode = saved.WindowMode;
                session.Project.PhaseFdwCycles = saved.FdwCycles;
                session.Project.PhaseDetrendMode = saved.DetrendMode;
                ScheduleSave();
            }
        }
        finally
        {
            session.GatePreview = null;
            RequestRedraw();
        }
    }
}
