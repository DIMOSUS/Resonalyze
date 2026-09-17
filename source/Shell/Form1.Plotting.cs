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
        plotViewports.Show(model, CurrentMode);
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
        // Modes without a main plot share Frequency slots but never draw them; capturing their empty set would wipe the selection.
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
        // Show() with a null model unchecks the slots and loses the saved selection.
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

    // Returns the slot, or null when all twelve are occupied. Prepare() loads it on the next frequency-mode switch, already checked.
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
                ColorArgb = UiPalette.CurveOverlayDefault.ToArgb(),
                Points = points
            };
            file.Save();
            activeOverlaySlots.MarkActive(Mode.FrequencyResponse, slot);
            return slot;
        }

        return null;
    }

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
            [ModeTab.ToolsVirtualCrossover] = () => _ = SelectModeAsync(ModeTab.ToolsVirtualCrossover),
            [ModeTab.ToolsFirConstructor] = () => _ = SelectModeAsync(ModeTab.ToolsFirConstructor)
        };

    private void SetActiveModeTab(ModeTab activeTab)
    {
        ModeDescriptor descriptor = GetModeDescriptor(activeTab);
        chromeTitleBar.SetActiveModeTab(activeTab);
        UpdateCurrentModeSettingsButton();
        UpdateRecordButtonForCurrentMode();
        ApplyMainContentLayout();
        plotView1.Visible = descriptor.HasPlotView;
        SetCaptureControlsVisible(descriptor.HasCaptureControls);
        overlays.Visible = descriptor.HasOverlayPanel;
        buttonOverlayShowAll.Visible = descriptor.HasOverlayPanel;
        buttonOverlayHideAll.Visible = descriptor.HasOverlayPanel;
        timeAlignmentController.SetVisible(descriptor.ShowsTimeAlignmentPanel);
        eqWizardPanel.Visible = descriptor.ShowsEqWizardPanel;
        signalGeneratorPanel.Visible = descriptor.ShowsSignalGeneratorPanel;
        if (descriptor.ShowsSignalGeneratorPanel)
        {
            signalGeneratorPanel.RefreshAudioSettings();
        }
        virtualCrossoverPanel.Visible = descriptor.ShowsVirtualCrossoverPanel;
        firConstructorPanel.Visible = descriptor.ShowsFirConstructorPanel;
        virtualDspMetricLabel.Visible = descriptor.ShowsVirtualCrossoverPanel;
        virtualDspWarningLabel.Visible = descriptor.ShowsVirtualCrossoverPanel &&
            virtualDspWarningLabel.Text.Length > 0;
        if (descriptor.ShowsVirtualCrossoverPanel)
        {
            virtualCrossoverPanel.OnPanelShown();
        }
        eqResultsPanel.Visible = descriptor.ShowsEqWizardPanel;
        SyncDockedModeSettingsOnModeChange();
        UpdatePlotLabelsPanel();
    }

    // Record Settings and History dock as separate windows, so they must be closed, not just their buttons hidden.
    private void SetCaptureControlsVisible(bool visible)
    {
        inputLevelMeterPanel.Visible = visible;
        panel1.Visible = visible;
        buttonHistory.Visible = visible;
        buttonCurrentModeSettings.Visible = visible;
        if (!visible)
        {
            dockedMeasurementSettingsHost.Close();
            dockedHistoryHost.Close();
        }
    }

    private void UpdateRecordButtonForCurrentMode()
    {
        // Before the tab guard: Save follows capture appearance whichever tab is open.
        RefreshSaveAvailability();
        if (modeController.ActiveTab != ModeTab.LiveSpectrum)
        {
            return;
        }

        buttonRecord.Text = liveSpectrumController.InProgress ? "Stop" : "Start";
        RefreshLiveCalibrationReadout();
        // Start/stop/completion is when an uncalibrated dB SPL choice becomes or stops being a conflict; refresh the amber state.
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.RefreshAvailability(
                plotModelFactory.LiveSplOffsetDb.HasValue,
                liveSpectrumController.HasDisplayableCurve,
                liveSpectrumController.HasConfiguredLoopback));
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
            TextColor = UiPalette.GraphAxisText.ToOxy(),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
        });
        model.InvalidatePlot(false);
    }

    private bool CanDrawCurrentMeasurement() =>
        sessionTracker.HasImpulseResponse && !expSweepMeasurement.InProgress;
}
