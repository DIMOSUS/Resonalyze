using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

public partial class Form1
{
    private void DrawSelectedMode(bool includeCurves)
    {
        using var _ = AppProfiler.Zone("Form1.DrawSelectedMode");
        ModeDescriptor descriptor = GetActiveModeDescriptor();
        if (descriptor.CreatePlotModel == null)
        {
            if (descriptor.ShowsTimeAlignmentPanel)
            {
                timeAlignmentController.RefreshConfiguration();
            }
            return;
        }

        if (descriptor.Mode == Mode.LiveSpectrum)
        {
            // While running, the live timer owns the plot. When idle, restore the
            // last curve, peak hold and overlays instead of an empty plot.
            if (!liveSpectrumController.InProgress)
            {
                liveSpectrumController.RestoreLastCurve();
            }
            return;
        }

        bool shouldIncludeCurves = includeCurves && descriptor.SupportsCurveDrawing;
        ShowPlotModel(
            descriptor.CreatePlotModel(shouldIncludeCurves),
            shouldIncludeCurves,
            descriptor.ShowOverlayCurves);
    }

    private void ShowPlotModel(
        PlotModel model,
        bool includeCurves,
        bool showOverlay)
    {
        using var _ = AppProfiler.Zone("Form1.ShowPlotModel");
        plotView1.Model = model;
        UpdatePeakInfo();

        if (includeCurves && showOverlay)
        {
            overlayCollection.Show(CurrentMode);
        }
        else
        {
            UpdatePlotLabelsPanel();
        }
    }

    private void UpdateOverlayAvailability()
    {
        bool available = OverlaysAvailableForCurrentMode();
        overlays.Enabled = available;
        RefreshOverlayButtons();
        if (!available)
        {
            overlayCollection.HideAll();
        }
    }

    private bool OverlaysAvailableForCurrentMode() =>
        OverlayCollection.SupportsMode(CurrentMode);

    private void CaptureActiveOverlaySlotsForCurrentMode()
    {
        // Modes without a main plot (e.g. EQ Wizard) share the Frequency overlay
        // slots but never draw them, so their "active" state is empty. Capturing it
        // would wipe the real Frequency selection, so skip those modes.
        if (!GetActiveModeDescriptor().HasPlotView ||
            !OverlayCollection.SupportsMode(CurrentMode))
        {
            return;
        }

        activeOverlaySlots.Store(
            OverlayCollection.OverlayModeFor(CurrentMode),
            overlayCollection.CaptureActiveSlots(CurrentMode));
    }

    private void RestoreActiveOverlaySlotsForCurrentMode()
    {
        // Restoring into a mode with no main plot would call Overlay.Show() with a
        // null model, which unchecks the slots and loses the saved selection.
        if (!GetActiveModeDescriptor().HasPlotView ||
            !OverlayCollection.SupportsMode(CurrentMode))
        {
            return;
        }

        Mode overlayMode = OverlayCollection.OverlayModeFor(CurrentMode);
        if (activeOverlaySlots.TryGet(overlayMode, out List<int> activeSlots))
        {
            overlayCollection.RestoreActiveSlots(CurrentMode, activeSlots);
        }
    }

    // Saves a curve produced by the Virtual DSP tool as a Captured overlay
    // in the first free Frequency Response slot; returns the slot, or null when
    // all twelve are occupied. The file is picked up by Prepare() on the next
    // switch to a frequency-based mode, and the slot joins the active set so it
    // arrives already checked.
    internal int? SaveVirtualCrossoverOverlay(string title, OverlayPoint[] points)
    {
        for (int slot = 1; slot <= OverlayFile.MaximumSlotCount; slot++)
        {
            bool occupied;
            try
            {
                occupied = OverlayFile.Load(Mode.FrequencyResponse, slot) != null;
            }
            catch (Exception)
            {
                // An unreadable slot file still owns its slot.
                occupied = true;
            }
            if (occupied)
            {
                continue;
            }

            var file = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = slot,
                Kind = OverlayKind.Captured,
                Title = title,
                ColorArgb = Color.FromArgb(230, 184, 0).ToArgb(),
                Points = points
            };
            file.Save();
            activeOverlaySlots.MarkActive(Mode.FrequencyResponse, slot);
            return slot;
        }

        return null;
    }

    // The bulk overlay buttons act only when the current mode actually has
    // populated overlay slots to show or hide.
    private void RefreshOverlayButtons()
    {
        bool hasOverlays = OverlaysAvailableForCurrentMode() &&
            overlayCollection.HasOverlays(CurrentMode);
        buttonOverlayShowAll.Enabled = hasOverlays;
        buttonOverlayHideAll.Enabled = hasOverlays;
    }

    private void buttonOverlayShowAll_Click(object? sender, EventArgs e)
    {
        overlayCollection.Show(CurrentMode);
        UpdatePlotLabelsPanel();
    }

    private void buttonOverlayHideAll_Click(object? sender, EventArgs e)
    {
        overlayCollection.HideAll();
        UpdatePlotLabelsPanel();
    }

    private Task SelectModeAsync(ModeTab tab) => modeController.SelectAsync(tab);

    internal async void OpenEqWizardForTargetOverlay(int targetOverlaySlot)
    {
        await SelectModeAsync(ModeTab.ToolsEqWizard);
        eqWizardPanel.SetTargetOverlayOptions(
            overlayCollection.GetTargetOverlayOptions(CurrentMode));
        eqWizardPanel.SelectTargetOverlaySlot(targetOverlaySlot);
    }

    // Opens the settings of the target overlay the EQ Wizard is tuning, then
    // refreshes the wizard so any edits (spec, tolerance, title) take effect.
    private void OpenEqWizardOverlaySettings(int targetOverlaySlot)
    {
        if (!overlayCollection.OpenEqWizardTargetSettings(targetOverlaySlot))
        {
            return;
        }

        eqWizardPanel.SetTargetOverlayOptions(
            overlayCollection.GetTargetOverlayOptions(CurrentMode));
        eqWizardPanel.SelectTargetOverlaySlot(targetOverlaySlot);
        eqWizardPanel.RefreshCurves();
    }

    private Dictionary<ModeTab, Action> CreateModeTabActions() =>
        new()
        {
            [ModeTab.Impulse] = () => _ = SelectModeAsync(ModeTab.Impulse),
            [ModeTab.Frequency] = () => _ = SelectModeAsync(ModeTab.Frequency),
            [ModeTab.Phase] = () => _ = SelectModeAsync(ModeTab.Phase),
            [ModeTab.GroupDelay] = () => _ = SelectModeAsync(ModeTab.GroupDelay),
            [ModeTab.Waterfall] = () => _ = SelectModeAsync(ModeTab.Waterfall),
            [ModeTab.Burst] = () => _ = SelectModeAsync(ModeTab.Burst),
            [ModeTab.LiveSpectrum] = () => _ = SelectModeAsync(ModeTab.LiveSpectrum),
            [ModeTab.Autocorrelation] = () => _ = SelectModeAsync(ModeTab.Autocorrelation),
            [ModeTab.TimeAlignment] = () => _ = SelectModeAsync(ModeTab.TimeAlignment),
            [ModeTab.ToolsEqWizard] = () => _ = SelectModeAsync(ModeTab.ToolsEqWizard),
            [ModeTab.ToolsSignalGenerator] = () => _ = SelectModeAsync(ModeTab.ToolsSignalGenerator),
            [ModeTab.ToolsVirtualCrossover] = () => _ = SelectModeAsync(ModeTab.ToolsVirtualCrossover)
        };

    private void SetActiveModeTab(ModeTab activeTab)
    {
        ModeDescriptor descriptor = GetModeDescriptor(activeTab);
        chromeTitleBar.SetActiveModeTab(activeTab);
        UpdateCurrentModeSettingsButton();
        UpdateRecordButtonForCurrentMode();
        ApplyMainContentLayout();
        plotView1.Visible = descriptor.HasPlotView;
        overlays.Visible = descriptor.HasOverlayPanel;
        buttonOverlayShowAll.Visible = descriptor.HasOverlayPanel;
        buttonOverlayHideAll.Visible = descriptor.HasOverlayPanel;
        timeAlignmentController.SetVisible(descriptor.ShowsTimeAlignmentPanel);
        eqWizardPanel.Visible = descriptor.ShowsEqWizardPanel;
        if (descriptor.ShowsEqWizardPanel)
        {
            eqWizardPanel.SetTargetOverlayOptions(
                overlayCollection.GetTargetOverlayOptions(CurrentMode));
        }
        signalGeneratorPanel.Visible = descriptor.ShowsSignalGeneratorPanel;
        if (descriptor.ShowsSignalGeneratorPanel)
        {
            signalGeneratorPanel.RefreshAudioSettings();
        }
        virtualCrossoverPanel.Visible = descriptor.ShowsVirtualCrossoverPanel;
        virtualDspMetricLabel.Visible = descriptor.ShowsVirtualCrossoverPanel;
        if (descriptor.ShowsVirtualCrossoverPanel)
        {
            virtualCrossoverPanel.OnPanelShown();
        }
        eqResultsPanel.Visible = descriptor.ShowsEqWizardPanel;
        SyncDockedModeSettingsOnModeChange();
        UpdatePlotLabelsPanel();
    }

    private void UpdateRecordButtonForCurrentMode()
    {
        if (modeController.ActiveTab != ModeTab.LiveSpectrum)
        {
            return;
        }

        buttonRecord.Text = liveSpectrumController.InProgress ? "Stop" : "Start";
    }

    private void UpdatePlotLabelsPanel()
    {
        plotLabelsPanelController.Refresh();
        RefreshOverlayButtons();
    }

    private void UpdatePeakInfo()
    {
        PlotModel? model = plotView1.Model;
        if (model == null)
        {
            return;
        }

        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (model.Annotations[index] is OverlayTextAnnotation
                {
                    Tag: PeakInfoAnnotationTag
                })
            {
                model.Annotations.RemoveAt(index);
            }
        }

        if (modeController.ActiveTab is not (ModeTab.Phase or ModeTab.GroupDelay))
        {
            model.InvalidatePlot(false);
            return;
        }

        string transferPeak;
        if (expSweepMeasurement.Transfer is not { } transfer)
        {
            transferPeak = "--";
        }
        else
        {
            int peakSamples = transfer.PeakIndex;
            double peakMs = expSweepMeasurement.SampleRate > 0
                ? peakSamples * 1000.0 / expSweepMeasurement.SampleRate
                : 0;
            transferPeak = $"{peakMs:0.000} ms ({peakSamples} samples)";
        }
        string text = expSweepMeasurement.InProgress
            ? "Peaks: measuring..."
            : "Transfer IR Peak: " + transferPeak;
        model.Annotations.Add(new OverlayTextAnnotation
        {
            Tag = PeakInfoAnnotationTag,
            Text = text,
            TextPosition = new DataPoint(0.01, 0),
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 12,
            FontWeight = 700,
            TextColor = OxyColors.White,
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
        });
        model.InvalidatePlot(false);
    }

    private bool CanDrawCurrentMeasurement() =>
        sessionTracker.HasImpulseResponse && !expSweepMeasurement.InProgress;
}
