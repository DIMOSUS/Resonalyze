using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    private void OpenModeSettings(ModeTab tab)
    {
        dockedMeasurementSettingsHost.Close();
        dockedHistoryHost.Close();
        ToggleModeSettingsPanel(tab);
    }

    private void SaveMeasurementSettings(bool captureMeasurementSettings = false)
    {
        MeasurementSettingsFile.SweepMeasurementSettings preservedMeasurementSettings =
            measurementSettings.Measurement;
        // Calibrations survive inside CaptureFrom; the measurement knows nothing about them.
        measurementSettings.CaptureFrom(expSweepMeasurement, viewSettings);
        if (!captureMeasurementSettings)
        {
            measurementSettings.Measurement = preservedMeasurementSettings;
        }

        ScheduleMeasurementSettingsSave();
    }

    private void ScheduleMeasurementSettingsSave()
    {
        if (IsDisposed)
        {
            return;
        }

        measurementSettingsSaver.Schedule();
    }

    private void FlushMeasurementSettings()
    {
        // The wizard debounces band edits; land them before saving or closing within the pause rolls the tune back.
        eqWizardPanel.CommitPendingBankEdit();
        measurementSettingsSaver.Flush();
    }

    private DialogResult ShowSettingsDialog(Form dialog)
    {
        dialog.StartPosition = FormStartPosition.CenterParent;
        return dialog.ShowDialog(this);
    }

    private void ToggleModeOptions<TDialog>(
        ModeTab tab,
        Func<TDialog> create,
        Action<TDialog> initialize,
        Action<TDialog> apply,
        Func<object?>? viewResetKey = null)
        where TDialog : Form
    {
        dockedModeSettingsHost.Toggle(
            tab,
            create,
            initialize,
            async dialog =>
            {
                object? keyBefore = viewResetKey?.Invoke();
                apply(dialog);
                SaveMeasurementSettings();
                // A scale change (linear/log) makes the old zoom meaningless: refit.
                if (viewResetKey != null && !Equals(keyBefore, viewResetKey()))
                {
                    analyzerPlot.ForgetZoom();
                }

                await RefreshCurrentModePlotAsync();
            },
            applyOnChange: true);
    }

    private void ToggleLiveSpectrumOptions()
    {
        dockedModeSettingsHost.Toggle(
            ModeTab.LiveSpectrum,
            () => new LiveSpectrumOpt(),
            opt =>
            {
                opt.Init(
                    viewSettings.LiveSpectrum,
                    microphoneCalibration.GetEntries(),
                    liveSpectrumSession.Display.SplOffsetDb.HasValue,
                    liveSpectrumSession.HasDisplayableCurve,
                    liveSpectrumSession.HasConfiguredLoopback,
                    liveSpectrumSession.SampleRate);
                opt.ShowCalibration(DescribeLiveCalibration());
                opt.ResetAverageRequested += liveSpectrumController.ResetAverage;
            },
            ApplyLiveSpectrumOptionsAsync,
            applyOnChange: true);
    }

    private async Task ApplyLiveSpectrumOptionsAsync(LiveSpectrumOpt dialog)
    {
        LiveSpectrumRestartSnapshot before = LiveSpectrumRestartSnapshot.Capture(viewSettings.LiveSpectrum);
        MagnitudeScale scaleBefore = viewSettings.LiveSpectrum.MagnitudeScale;
        dialog.SetOptions(viewSettings.LiveSpectrum);
        LiveSpectrumRestartSnapshot after = LiveSpectrumRestartSnapshot.Capture(viewSettings.LiveSpectrum);
        if (viewSettings.LiveSpectrum.MagnitudeScale != scaleBefore)
        {
            analyzerPlot.Viewports.Forget(Mode.LiveSpectrum);
        }
        SaveMeasurementSettings();
        RefreshSaveAvailability();

        if (before != after)
        {
            await ApplyMeasurementConfigurationToControllersAsync();
            // A stopped analyzer's curve must not be redrawn under new acquisition parameters (e.g. slope compensation re-tilting pink as white).
            if (!liveSpectrumSession.InProgress)
            {
                liveSpectrumController.DiscardCapturedData();
            }
        }
        else
        {
            liveSpectrumController.ApplyDisplayOptions();
            // It rebuilt the live plot; building it again gives the same model.
            if (CurrentMode == Mode.LiveSpectrum)
            {
                return;
            }
        }

        RefreshCurrentModePlot();
    }

    /// <summary>Settings edits arrive one after another, so the plot builds off the UI thread and only the newest is shown.</summary>
    private async Task RefreshCurrentModePlotAsync()
    {
        if (GetActiveModeDescriptor().HasPlotView && CurrentMode != Mode.LiveSpectrum)
        {
            await analyzerPlot.RedrawAsync();
            return;
        }

        RefreshCurrentModePlot();
    }

    // The active tab redraws what it shows: the plot, the held live capture, or Time Alignment's read.
    private void RefreshCurrentModePlot()
    {
        ModeDescriptor descriptor = GetActiveModeDescriptor();
        if (descriptor.ShowsTimeAlignmentPanel)
        {
            timeAlignmentController.RefreshConfiguration();
            return;
        }

        if (descriptor.Mode == Mode.LiveSpectrum)
        {
            liveSpectrumController.Redraw();
            return;
        }

        analyzerPlot.Redraw();
    }

    private bool HasDockedModeSettings(ModeTab tab) =>
        ModeCatalog.For(tab).HasDockedSettings;

    private void ShowDockedModeSettingsForActiveTab()
    {
        OpenModeSettings(modeController.ActiveTab);
    }

    private void SyncDockedModeSettingsOnModeChange()
    {
        if (!dockedModeSettingsHost.IsOpen)
        {
            return;
        }

        if (HasDockedModeSettings(modeController.ActiveTab))
        {
            // Toggle closes a panel already open for the tab: re-selecting it (a capture loaded in Live Spectrum) would.
            if (!dockedModeSettingsHost.IsShowing(modeController.ActiveTab))
            {
                ShowDockedModeSettingsForActiveTab();
            }
        }
        else
        {
            dockedModeSettingsHost.Close();
        }
    }

    private void buttonCurrentModeSettings_Click(object sender, EventArgs e)
    {
        if (!HasDockedModeSettings(modeController.ActiveTab))
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        OpenModeSettings(modeController.ActiveTab);
    }

    private void UpdateCurrentModeSettingsButton()
    {
        commandController.UpdateModeSettingsButton(dockedModeSettingsHost.IsOpen);
    }

    private void UpdateRecordSettingsButton()
    {
        commandController.UpdateRecordSettingsButton(dockedMeasurementSettingsHost.IsOpen);
    }

    private void UpdateHistoryButton()
    {
        commandController.UpdateHistoryButton(dockedHistoryHost.IsOpen);
    }

    private sealed record LiveSpectrumRestartSnapshot(
        // Changes signal role and accumulation path, so a running capture restarts rather than flipping mid-run.
        LiveAnalysisMode AnalysisMode,
        NoiseColor NoiseColor,
        WindowType WindowType,
        int SequenceLength,
        int OverlapPercent)
    {
        public static LiveSpectrumRestartSnapshot Capture(LiveSpectrumOptions options) =>
            new(
                options.AnalysisMode,
                options.NoiseColor,
                options.WindowType,
                options.SequenceLength,
                options.OverlapPercent);
    }
}
