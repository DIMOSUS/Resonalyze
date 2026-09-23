using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>The upper plot: the view and group selectors, the frame <see cref="AcousticViewBuilder"/> builds, and the metric
/// and warning lines the host shows under it.</summary>
public partial class VirtualCrossoverPanel
{
    private void OnViewModeChanged()
    {
        acousticPlot.ConfigureForView(CurrentAcousticView());
        UpdateViewDependentControls();
        // Before the write-back, or OnViewChanged copies the old view's answer into the toggle.
        ApplySumToggleForView();
        OnViewChanged();
    }

    // Mutes toggles on views that cannot draw the curve; see docs/tech/virtual-dsp-panel.md#view-dependent-controls.
    private void UpdateViewDependentControls()
    {
        comboBoxSmoothing.Enabled = !radioViewImpulse.Checked && !radioViewStep.Checked;
        Ui.UiStyle.SetTextEnabledLook(
            checkBoxShowSum, !radioViewImpulse.Checked, interactive: true);
        // Uses intent, not Enabled: during a load Enabled reads false through the parent and the label would stay muted.
        bool lossQuoted =
            VirtualCrossoverGroupViews.LossChainZone(SelectedGroupView) != null;
        comboBoxSumLoss.Enabled = lossQuoted;
        Ui.UiStyle.SetTextEnabledLook(labelSumLoss, lossQuoted);
        bool groupSums = VirtualCrossoverGroupViews.DrawsGroupSums(SelectedGroupView);
        if (groupSums)
        {
            Ui.UiStyle.SetTextEnabledLook(checkBoxShowSum, false, interactive: true);
        }

        Ui.UiStyle.SetTextEnabledLook(radioViewPhase, !groupSums, interactive: true);
        Ui.UiStyle.SetTextEnabledLook(radioViewImpulse, !groupSums, interactive: true);
        Ui.UiStyle.SetTextEnabledLook(radioViewGroupDelay, !groupSums, interactive: true);
        Ui.UiStyle.SetTextEnabledLook(radioViewStep, !groupSums, interactive: true);
        RefreshHybridAvailability();
        UpdateTargetToggleLook();
        numericTargetLevel.Enabled = radioViewMagnitude.Checked;
    }

    // One answer per view; the impulse view has no sum, writes nothing and shows the magnitude answer.
    private void ApplySumToggleForView()
    {
        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            checkBoxShowSum.Checked = radioViewPhase.Checked
                ? session.Project.ShowSumCurveOnPhase
                : radioViewGroupDelay.Checked
                    ? session.Project.ShowSumCurveOnGroupDelay
                    : radioViewStep.Checked
                        ? session.Project.ShowSumCurveOnStep
                        : session.Project.ShowSumCurve;
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }
    }

    private void OnViewChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        if (radioViewPhase.Checked)
        {
            session.Project.ShowSumCurveOnPhase = checkBoxShowSum.Checked;
        }
        else if (radioViewGroupDelay.Checked)
        {
            session.Project.ShowSumCurveOnGroupDelay = checkBoxShowSum.Checked;
        }
        else if (radioViewStep.Checked)
        {
            session.Project.ShowSumCurveOnStep = checkBoxShowSum.Checked;
        }
        else if (radioViewMagnitude.Checked)
        {
            session.Project.ShowSumCurve = checkBoxShowSum.Checked;
        }

        session.Project.SumLossWindowMode = SelectedSumLossWindow;
        session.Project.ShowHybridCurves = checkBoxHybrid.Checked;
        session.Project.ShowTargetCurve = checkBoxShowTarget.Checked;
        // Newer view flags are written beside older ones so an older build opens the nearest view.
        session.Project.ShowPhaseView = radioViewPhase.Checked || radioViewGroupDelay.Checked;
        session.Project.ShowImpulseView = radioViewImpulse.Checked || radioViewStep.Checked;
        session.Project.ShowGroupDelayView = radioViewGroupDelay.Checked;
        session.Project.ShowStepView = radioViewStep.Checked;
        session.Project.SetSmoothingCode(comboBoxSmoothing.SelectedItem is int value
            ? value
            : 12);
        session.Project.GroupView = SelectedGroupView;
        SaveAndRedraw();
    }

    private AcousticView CurrentAcousticView() =>
        radioViewImpulse.Checked ? AcousticView.Impulse
        : radioViewStep.Checked ? AcousticView.Step
        : radioViewPhase.Checked ? AcousticView.Phase
        : radioViewGroupDelay.Checked ? AcousticView.GroupDelay
        : AcousticView.Magnitude;

    /// <summary>Read from the control, not the project, so a mid-edit redraw draws the new pick.</summary>
    private VirtualCrossoverGroupView SelectedGroupView =>
        comboBoxGroupView.SelectedItem is VirtualCrossoverGroupView view
            ? view
            : VirtualCrossoverGroupView.FrontAndSub;

    private SumLossWindow SelectedSumLossWindow =>
        comboBoxSumLoss.SelectedItem is SumLossWindow window
            ? window
            : SumLossWindow.Direct;

    private void InitializeSumLossComboBox()
    {
        foreach (SumLossWindow window in SumLossWindows.All)
        {
            comboBoxSumLoss.Items.Add(window);
        }

        comboBoxSumLoss.Format += (_, args) =>
        {
            if (args.ListItem is SumLossWindow window)
            {
                args.Value = SumLossWindows.DisplayName(window);
            }
        };
        comboBoxSumLoss.SelectedItem = SumLossWindow.Direct;
    }

    private void InitializeGroupViewComboBox()
    {
        comboBoxGroupView.Items.AddRange(
            [.. VirtualCrossoverGroupViews.All.Cast<object>()]);
        comboBoxGroupView.Format += (_, args) =>
        {
            if (args.ListItem is VirtualCrossoverGroupView view)
            {
                args.Value = VirtualCrossoverGroupViews.DisplayName(view);
            }
        };
        comboBoxGroupView.SelectedItem = VirtualCrossoverGroupView.FrontAndSub;
    }

    private void InitializeSmoothingComboBox()
    {
        foreach (int value in OverlaySmoothing.SupportedInverseOctaves)
        {
            comboBoxSmoothing.Items.Add(value);
        }

        comboBoxSmoothing.Format += (_, args) =>
        {
            if (args.ListItem is int value)
            {
                args.Value = OverlaySmoothing.GetLabel(value);
            }
        };
        comboBoxSmoothing.SelectedItem = 12;
    }

    private async Task RedrawMainPlotAsync()
    {
        // Old curves stay on screen until new data is ready (no flicker).
        VirtualCrossoverProcessedRender? render = await ProcessChannelsAsync();
        if (render == null || mainPlotView.IsDisposed)
        {
            return;
        }
        long revision = render.Revision;
        List<ProcessedChannel> processed = render.Channels;
        if (!processingCoordinator.IsCurrent(revision))
        {
            return;
        }

        // The whole set is kept: the junction views and opposite-side read-outs need channels this view does not draw.
        session.LastRender = render;

        // Read once: a control changed during the awaits below requests the next frame. Filtered by group view once, so
        // curves, sum, loss and read-out describe the same channels.
        VirtualCrossoverViewState view = CaptureViewState();
        VirtualCrossoverGroupView groupView = view.GroupView;
        var frame = VirtualCrossoverFrame.Of(processed, groupView);
        if (frame.Shown.Count == 0)
        {
            // With nothing resolved at all the zone hint would mislead, so show the no-sources hint.
            acousticPlot.Draw(new AcousticRender(
                processed.Count == 0
                    ? AcousticViewBuilder.NoSourcesHint
                    : AcousticViewBuilder.EmptyViewHint(groupView),
                [],
                null));
            MetricChanged?.Invoke(string.Empty, string.Empty);
            // A warning about channels no longer visible would read as a fault in this view.
            HideWarning();
            return;
        }

        // Direct loss (FDW-8) sums the junction read-out's own gated spectra, so it is built with it.
        VirtualCrossoverPhaseGate gate = session.Gate;
        (List<VirtualCrossoverMetric.PhaseEntry> phaseEntries, List<SignalPoint>? directLoss) =
            await frame.ReadJunctionsAsync(
                metrics,
                gate,
                gate.PinnedOffsetMs,
                session.MagnitudeGate.SmoothingInverseOctaves,
                withDirectLoss: view.LossWindow == SumLossWindow.Direct);

        // Narrowed by the Show filter, never a shortened list: a block's list position is its cache identity.
        List<VirtualCrossoverMetric.StereoDelta> stereoDeltas =
            await metrics.ComputeStereoDeltasAsync(
                session.Channels,
                revision,
                includePair: pair =>
                    VirtualCrossoverGroupViews.IsShown(groupView, pair.Zone),
                hybridLevelDeltaDb: hybridReader.StereoLevelReader(view.HybridRequested));
        // Quoted by cross-group views instead of a loss; adds only arrival FFTs.
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groupDeltas =
            await metrics.ComputeGroupDeltasAsync(
                frame.Shown, groupView, revision,
                hybridGroupLevelDeltaDb: hybridReader.GroupLevelReader(view.HybridRequested));
        // The curve windows through the OPPOSITE side's gate placement; both sides must be drawn by the same method.
        VirtualCrossoverSideSum? oppositeSide = null;
        if (view.ShowSum && view.View is AcousticView.Magnitude or AcousticView.Step)
        {
            oppositeSide = await metrics.ComputeSideSumAsync(
                session.Channels, !view.RightSide, revision, minimumChannels: 2,
                includePair: pair =>
                    VirtualCrossoverGroupViews.ParticipatesInTotalSum(
                        groupView, pair.Zone));
        }

        // Envelopes (Hilbert over the whole record, 2^17+ samples) are warmed off the UI thread: a drag hands
        // each frame a new array. Memoized per array.
        if (view.View == AcousticView.Impulse)
        {
            Complex[][] drawnResponses =
                [.. frame.Shown
                    .Where(item => item.Channel.Pair.ShowProcessedCurve)
                    .Select(item => item.ImpulseResponse)];
            await Task.Run(() =>
            {
                using var _ = AppProfiler.Zone("VirtualDSP.WarmImpulseEnvelopes");
                drawnResponses.AsParallel().ForAll(
                    response => ImpulseWindowPreview.EnvelopeOf(response));
            });
        }
        if (mainPlotView.IsDisposed || !processingCoordinator.IsCurrent(revision))
        {
            return;
        }

        // The synchronous UI-thread part of the frame; each step carries its own zone.
        using var _ = AppProfiler.Zone("VirtualDSP.RedrawMainPlot");
        List<AnalysisCurve>? magnitudes;
        AnalysisCurve? sumCurve;
        List<SignalPoint>? lossCurve;
        using (AppProfiler.Zone("VirtualDSP.BuildCurves"))
        {
            (magnitudes, sumCurve, lossCurve) = metrics.BuildCurves(
                frame.Shown, session.MagnitudeGate.SmoothingInverseOctaves, frame.Summed);
        }

        // Decided before the awaits, where the junction phase block uses it.
        if (!frame.QuotesJunctions)
        {
            lossCurve = null;
        }

        // Disable draws no curve but the column keeps the full read.
        bool lossDirect = view.LossWindow == SumLossWindow.Direct;
        List<SignalPoint>? shownLoss = lossDirect ? directLoss : lossCurve;
        List<SignalPoint>? drawnLoss = view.LossWindow == SumLossWindow.Off ? null : shownLoss;

        // Before warnings and render: both read it.
        HybridMagnitudes? hybrid = null;
        if (view.HybridRequested && magnitudes != null && view.View == AcousticView.Magnitude)
        {
            using (AppProfiler.Zone("VirtualDSP.BuildHybrid"))
            {
                hybrid = hybridReader.Build(
                    frame.Shown,
                    magnitudes,
                    view.RightSide,
                    session.MagnitudeGate.SmoothingInverseOctaves);
            }

            if (hybrid != null)
            {
                session.LastHybridOffset = (revision, hybrid.OffsetDb);
            }
        }

        // No hybrid capture on the other side -> drop the curve: a mixed-method sum reads as a false L/R difference.
        AnalysisCurve? oppositeSum = null;
        if (oppositeSide != null)
        {
            using (AppProfiler.Zone("VirtualDSP.BuildOppositeSum"))
            {
                oppositeSum = hybrid == null
                    ? session.MagnitudeGate.OppositeSum(oppositeSide, session.Calibration.For).Display
                    : hybridReader.OppositeSum(oppositeSide, hybrid.OffsetDb);
            }
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateMetric"))
        {
            UpdateMetric(
                frame.Summed, shownLoss, phaseEntries, stereoDeltas, hybrid,
                groupDeltas, lossDirect);
        }

        using (AppProfiler.Zone("VirtualDSP.UpdateWarnings"))
        {
            UpdateWarnings(processed, hybrid, view.RightSide);
        }

        // Split from the draw so the profiler separates curve building from OxyPlot.
        AcousticRender acousticRender;
        using (AppProfiler.Zone("VirtualDSP.BuildAcousticRender"))
        {
            acousticRender = viewBuilder.Build(
                view,
                frame,
                new AcousticFrameCurves(
                    magnitudes, sumCurve, drawnLoss, lossDirect, oppositeSum, oppositeSide, hybrid),
                loadingProject);
        }

        using (AppProfiler.Zone("VirtualDSP.AcousticPlotDraw"))
        {
            acousticPlot.Draw(acousticRender);
        }
    }

    // Junction read-outs use the SUMMED channels: a drawn-only centre would invent a crossover.
    private void UpdateMetric(
        List<ProcessedChannel> summed,
        List<SignalPoint>? lossCurve,
        IReadOnlyList<VirtualCrossoverMetric.PhaseEntry> phaseEntries,
        IReadOnlyList<VirtualCrossoverMetric.StereoDelta> stereoDeltas,
        HybridMagnitudes? hybrid,
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> groupDeltas,
        bool lossDirect)
    {
        // Zoned apart from formatting: the per-junction banded analysis is the real work.
        List<VirtualCrossoverMetric.Entry> entries;
        using (AppProfiler.Zone("VirtualDSP.BuildEntries"))
        {
            entries = metrics.BuildEntries(summed, lossCurve);
        }

        (string compact, string detail) = VirtualCrossoverMetric.FormatReadOut(
            entries, lossDirect, phaseEntries, groupDeltas, stereoDeltas, hybridReader.ReadOut(hybrid));
        MetricChanged?.Invoke(compact, detail);
    }

    // Read once per frame; the Target curve travels only when it is shown.
    private VirtualCrossoverViewState CaptureViewState() => new(
        CurrentAcousticView(),
        SelectedGroupView,
        session.ActiveSideRight,
        checkBoxShowSum.Checked,
        SelectedSumLossWindow,
        HybridRequested,
        checkBoxShowTarget.Checked ? targetCurve : null,
        session.Project.TargetLevelDb);

    private void UpdateWarnings(
        List<ProcessedChannel> processed, HybridMagnitudes? hybrid, bool rightSide)
    {
        gatePlacement = GatePlacementVerdict.Judge(processed, session.MagnitudeGate, rightSide);
        if (warnings.Judge(processed, hybrid, gatePlacement) is not { } warning)
        {
            HideWarning();
            return;
        }

        WarningChanged?.Invoke(
            warning.Text,
            warning.Detail,
            warning.Level switch
            {
                // Amber: the view cannot be read yet, not a tuning error.
                VirtualCrossoverWarningLevel.Caution => UiPalette.Warning,
                VirtualCrossoverWarningLevel.Information => UiPalette.TextAccent,
                _ => UiPalette.Error
            });
    }

    private void HideWarning() =>
        WarningChanged?.Invoke(string.Empty, string.Empty, UiPalette.Error);
}
