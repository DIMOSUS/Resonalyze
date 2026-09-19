namespace Resonalyze;

public partial class Form1
{
    private void buttonOverlayShowAll_Click(object? sender, EventArgs e) => analyzerPlot.ShowAllOverlays();

    private void buttonOverlayHideAll_Click(object? sender, EventArgs e) => analyzerPlot.HideAllOverlays();

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

    // A switch shows the new tab's panels and buttons before the plot draws; Live Spectrum brings back its held capture.
    private void ShowModeSurfaces(ModeDescriptor descriptor)
    {
        chromeTitleBar.SetActiveModeTab(descriptor.Tab);
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
        analyzerPlot.RefreshLabels();
        if (descriptor.Mode == Mode.LiveSpectrum)
        {
            RestoreLiveCurveIfStopped();
        }
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

    private void RestoreLiveCurveIfStopped()
    {
        if (!liveSpectrumController.InProgress)
        {
            liveSpectrumController.RestoreLastCurve();
        }
    }
}
