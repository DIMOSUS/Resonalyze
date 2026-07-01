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
        measurementSettings.CaptureFrom(
            expSweepMeasurement,
            frequencyResponseOptions,
            phaseResponseOptions,
            groupDelayOptions,
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

        measurementSettingsSavePending = true;
        measurementSettingsSaveTimer.Stop();
        measurementSettingsSaveTimer.Start();
    }

    private void FlushMeasurementSettings()
    {
        measurementSettingsSaveTimer.Stop();
        if (!measurementSettingsSavePending)
        {
            return;
        }

        measurementSettingsSavePending = false;
        measurementSettings.Save();
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
                IReadOnlyList<AxisViewport> axisViewports = CaptureAxisViewports();
                apply(dialog);
                SaveMeasurementSettings();
                // When a setting changes the axis scale itself (e.g. linear <-> logarithmic),
                // the old zoom is meaningless, so refit the view instead of restoring it.
                bool refit = viewResetKey != null && !Equals(keyBefore, viewResetKey());
                await RefreshCurrentModePlotAsync(refit ? null : axisViewports);
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
                opt.Init(liveSpectrumOptions);
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
        }
        else
        {
            liveSpectrumController.ApplyDisplayOptions();
        }

        RefreshCurrentModePlot();
    }

    private async Task RefreshCurrentModePlotAsync(
        IReadOnlyList<AxisViewport>? restoreViewports = null)
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

        // Apply the saved zoom to the new model's axes BEFORE showing it. Overlays
        // (e.g. a calculated overlay) force a synchronous repaint while drawing; if the
        // zoom were restored only afterwards, that repaint would flash the default scale
        // first, so the plot appears to jump on every settings change.
        if (restoreViewports != null)
        {
            ApplyAxisViewports(model, restoreViewports);
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

    private IReadOnlyList<AxisViewport> CaptureAxisViewports()
    {
        PlotModel? model = plotView1.Model;
        if (model == null)
        {
            return Array.Empty<AxisViewport>();
        }

        // ActualMinimum/ActualMaximum only refresh on render, so a rebuild triggered
        // before the previous paint settled (common with Compare, whose model is
        // slower to build) would capture the nominal range and drop the user's zoom.
        // Update the model in place first so the actual range reflects the live
        // pan/zoom synchronously, independent of paint timing.
        ((IPlotModel)model).Update(false);

        var viewports = new List<AxisViewport>(model.Axes.Count);
        foreach (Axis axis in model.Axes)
        {
            viewports.Add(new AxisViewport(
                axis.Position,
                axis.GetType(),
                axis.ActualMinimum,
                axis.ActualMaximum));
        }

        return viewports;
    }

    private static void ApplyAxisViewports(
        PlotModel model,
        IReadOnlyList<AxisViewport> viewports)
    {
        if (viewports.Count == 0)
        {
            return;
        }

        foreach (Axis axis in model.Axes)
        {
            AxisViewport? viewport = viewports.FirstOrDefault(
                item => item.Position == axis.Position &&
                    item.AxisType == axis.GetType());
            if (viewport == null)
            {
                continue;
            }

            axis.Zoom(viewport.Minimum, viewport.Maximum);
        }
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

    private sealed record AxisViewport(
        AxisPosition Position,
        Type AxisType,
        double Minimum,
        double Maximum);

    private sealed record LiveSpectrumRestartSnapshot(
        NoiseColor NoiseColor,
        WindowType WindowType,
        int SequenceLength,
        int OverlapPercent)
    {
        public static LiveSpectrumRestartSnapshot Capture(LiveSpectrumOptions options) =>
            new(
                options.NoiseColor,
                options.WindowType,
                options.SequenceLength,
                options.OverlapPercent);
    }
}
