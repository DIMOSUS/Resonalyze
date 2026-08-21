using OxyPlot;
using OxyPlot.Axes;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    private int asyncPlotRefreshVersion;

    private void buttonWaterfallOpt_Click(object sender, EventArgs e)
    {
        OpenModeSettings(ModeTab.Waterfall);
    }

    private void buttonFROpt_Click(object sender, EventArgs e)
    {
        OpenModeSettings(ModeTab.Frequency);
    }

    private void buttonBurstDecayOpt_Click(object sender, EventArgs e)
    {
        OpenModeSettings(ModeTab.Burst);
    }

    private void buttonGDOpt_Click(object sender, EventArgs e)
    {
        OpenModeSettings(ModeTab.GroupDelay);
    }

    private void buttonPROpt_Click(object sender, EventArgs e)
    {
        OpenModeSettings(ModeTab.Phase);
    }

    private void buttonImpOpt_Click(object sender, EventArgs e)
    {
        OpenModeSettings(ModeTab.Impulse);
    }

    private void OpenModeSettings(ModeTab tab)
    {
        dockedMeasurementSettingsHost.Close();
        dockedHistoryHost.Close();
        ModeDescriptor descriptor = GetModeDescriptor(tab);
        descriptor.OpenSettings?.Invoke();
    }

    private void SaveMeasurementSettings(bool captureMeasurementSettings = false)
    {
        MeasurementSettingsFile.SweepMeasurementSettings preservedMeasurementSettings =
            measurementSettings.Measurement;
        // The calibrations themselves survive the capture inside CaptureFrom: the
        // measurement they are rebuilt from knows nothing about them.
        measurementSettings.CaptureFrom(
            expSweepMeasurement,
            frequencyResponseOptions,
            frequencyResponseVisibility,
            phaseResponseOptions,
            phaseResponseVisibility,
            groupDelayOptions,
            groupDelayVisibility,
            impulseResponseOptions,
            waterfallGenOptions,
            burstDecayGenOptions,
            liveSpectrumOptions,
            timeAlignmentOptions);
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
        // The EQ Wizard turns band edits into a settings change only once the bank
        // goes quiet, so it is asked to land anything still in flight BEFORE the
        // saver runs. Without this, closing the window within that pause writes the
        // state from before the last edit and the tune silently rolls back.
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
                // When a setting changes the axis scale itself (e.g. linear <-> logarithmic),
                // the old zoom is meaningless, so drop it and let the view refit.
                if (viewResetKey != null && !Equals(keyBefore, viewResetKey()))
                {
                    plotViewports.Forget(CurrentMode);
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
                    liveSpectrumOptions,
                    microphoneCalibration.GetEntries(),
                    plotModelFactory.LiveSplOffsetDb.HasValue,
                    liveSpectrumController.HasDisplayableCurve,
                    liveSpectrumController.HasConfiguredLoopback);
                opt.ResetAverageRequested += liveSpectrumController.ResetAverage;
            },
            ApplyLiveSpectrumOptionsAsync,
            applyOnChange: true);
    }

    private async Task ApplyLiveSpectrumOptionsAsync(LiveSpectrumOpt dialog)
    {
        LiveSpectrumRestartSnapshot before = LiveSpectrumRestartSnapshot.Capture(liveSpectrumOptions);
        dialog.SetOptions(liveSpectrumOptions);
        LiveSpectrumRestartSnapshot after = LiveSpectrumRestartSnapshot.Capture(liveSpectrumOptions);
        SaveMeasurementSettings();

        if (before != after)
        {
            await ApplyMeasurementConfigurationToControllersAsync();
            // The snapshot names the ACQUISITION parameters. A running analyzer was
            // just restarted onto a fresh accumulation; a stopped one still holds
            // the previous setup's curve, which must not be redrawn under the new
            // parameters — the display transform reads the options live, so e.g.
            // the slope compensation would re-tilt a stopped pink RTA as if the
            // excitation had been white.
            if (!liveSpectrumController.InProgress)
            {
                liveSpectrumController.DiscardCapturedData();
            }
        }
        else
        {
            liveSpectrumController.ApplyDisplayOptions();
        }

        RefreshCurrentModePlot();
    }

    private async Task RefreshCurrentModePlotAsync()
    {
        ModeDescriptor descriptor = GetActiveModeDescriptor();
        if (descriptor.CreatePlotModel == null || descriptor.Mode == Mode.LiveSpectrum)
        {
            RefreshCurrentModePlot();
            return;
        }

        bool shouldIncludeCurves = descriptor.SupportsCurveDrawing &&
            CanDrawCurrentMeasurement();
        int version = Interlocked.Increment(ref asyncPlotRefreshVersion);
        ModeTab tab = descriptor.Tab;
        PlotModel model = await Task.Run(() => descriptor.CreatePlotModel(shouldIncludeCurves));
        if (IsDisposed ||
            version != Volatile.Read(ref asyncPlotRefreshVersion) ||
            modeController.ActiveTab != tab)
        {
            return;
        }

        ShowPlotModel(model, shouldIncludeCurves, descriptor.ShowOverlayCurves);
    }

    private void RefreshCurrentModePlot()
    {
        Interlocked.Increment(ref asyncPlotRefreshVersion);
        if (GetActiveModeDescriptor().ShowsTimeAlignmentPanel)
        {
            timeAlignmentController.RefreshConfiguration();
            return;
        }

        bool includeCurves = GetActiveModeDescriptor().SupportsCurveDrawing &&
            CanDrawCurrentMeasurement();
        DrawSelectedMode(includeCurves);
    }

    private bool HasDockedModeSettings(ModeTab tab) =>
        GetModeDescriptor(tab).HasDockedSettings;

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
            ShowDockedModeSettingsForActiveTab();
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
        // The analysis mode switches both the playback role of the signal and the
        // accumulation path (transfer vs. mic-only), so changing it must restart a
        // running capture — it must never flip mid-run under the accumulators.
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
