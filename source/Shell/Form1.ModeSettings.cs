using OxyPlot;
using OxyPlot.Axes;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
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
        ModeDescriptor descriptor = GetModeDescriptor(tab);
        descriptor.OpenSettings?.Invoke();
    }

    private void SaveMeasurementSettings()
    {
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
        Action<TDialog> apply)
        where TDialog : Form
    {
        dockedModeSettingsHost.Toggle(
            tab,
            create,
            initialize,
            async dialog =>
            {
                IReadOnlyList<AxisViewport> axisViewports = CaptureAxisViewports();
                apply(dialog);
                SaveMeasurementSettings();
                await ApplyMeasurementConfigurationToControllersAsync();
                RefreshCurrentModePlot();
                RestoreAxisViewports(axisViewports);
            });
    }

    private void RefreshCurrentModePlot()
    {
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

    private void RestoreAxisViewports(IReadOnlyList<AxisViewport> viewports)
    {
        if (viewports.Count == 0 || plotView1.Model == null)
        {
            return;
        }

        foreach (Axis axis in plotView1.Model.Axes)
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

        plotView1.Model.InvalidatePlot(false);
        plotView1.Refresh();
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

    private sealed record AxisViewport(
        AxisPosition Position,
        Type AxisType,
        double Minimum,
        double Maximum);
}
