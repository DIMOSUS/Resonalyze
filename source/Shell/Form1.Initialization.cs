using System.Windows.Forms;

namespace Resonalyze;

public partial class Form1
{
    private void ConfigureToolTips()
    {
        toolTip1.SetToolTip(
            inputLevelMeterPanel,
            "Input level meter.\r\n" +
            "Numbers are shown as Peak / RMS in dBFS.\r\n" +
            "The bar shows the filtered RMS level.\r\n" +
            "The bright vertical marker is Peak Hold.");
    }

    private Form1ControllerDependencies CreateControllerDependencies()
    {
        ChromeTitleBarController createdTitleBarController = new(
            this,
            plotView1,
            UpdateMaximizedBounds,
            CreateModeTabActions());
        OverlayCollection createdOverlayCollection = new(
            this,
            overlays,
            plotView1,
            toolTip1,
            UpdatePlotLabelsPanel);
        PlotLabelsPanelController createdPlotLabelsPanelController = new(
            plotView1,
            () => CurrentMode);
        PlotModelFactory createdPlotModelFactory = new(
            expSweepMeasurement,
            noiseMeasurement,
            calibration,
            frequencyResponseOptions,
            phaseResponseOptions,
            groupDelayOptions,
            impulseResponseOptions,
            liveSpectrumOptions,
            waterfallGenOptions,
            burstDecayGenOptions);
        LiveSpectrumController createdLiveSpectrumController = new(
            this,
            noiseMeasurement,
            plotView1,
            createdPlotModelFactory,
            overlays,
            createdOverlayCollection,
            () => CurrentMode,
            () => SelectModeAsync(ModeTab.LiveSpectrum),
            UpdateOverlayAvailability,
            UpdateDrawButtonText,
            UpdateRecordButtonForCurrentMode,
            UpdateClearButtonState,
            UpdatePlotLabelsPanel,
            liveSpectrumOptions);
        ModeController createdModeController = new(
            ChangeModeAsync,
            SetActiveModeTab,
            createdOverlayCollection.HideAll,
            DrawSelectedMode,
            CanDrawCurrentMeasurement,
            UpdateDrawButtonText,
            tab => GetModeDescriptor(tab).Mode,
            tab => GetModeDescriptor(tab).SupportsCurveDrawing);
        MainCommandController createdCommandController = new(
            buttonSave,
            buttonLoad,
            buttonDraw,
            buttonClear,
            buttonCurrentModeSettings,
            buttonRecordOpt,
            () => GetActiveModeDescriptor().HasDockedSettings,
            () => GetActiveModeDescriptor().SupportsCurveDrawing,
            CanDrawCurrentMeasurement,
            () => plotView1.Model?.Series.Count > 0,
            () => IsHandleCreated);
        TimeAlignmentPanelController createdTimeAlignmentController = new(
            this,
            expSweepMeasurement,
            timeAlignmentOptions,
            text => buttonRecord.Text = text,
            SaveMeasurementSettings,
            () => createdModeController.ActiveTab == ModeTab.TimeAlignment);
        InputLevelMeterController createdInputLevelMeterController = new(
            this,
            inputLevelMeterPanel,
            expSweepMeasurement,
            noiseMeasurement,
            createdTimeAlignmentController.Measurement);
        DockedModeSettingsHost createdDockedModeSettingsHost = new(this, plotView1);
        DockedModeSettingsHost createdDockedMeasurementSettingsHost = new(this, plotView1);

        return new Form1ControllerDependencies(
            createdTitleBarController,
            createdOverlayCollection,
            createdPlotLabelsPanelController,
            createdPlotModelFactory,
            createdLiveSpectrumController,
            createdModeController,
            createdCommandController,
            createdTimeAlignmentController,
            createdInputLevelMeterController,
            createdDockedModeSettingsHost,
            createdDockedMeasurementSettingsHost);
    }

    private void ApplyPersistedSettings()
    {
        measurementSettings.ApplyTo(
            expSweepMeasurement,
            frequencyResponseOptions,
            phaseResponseOptions,
            groupDelayOptions,
            impulseResponseOptions,
            waterfallGenOptions,
            burstDecayGenOptions,
            liveSpectrumOptions,
            timeAlignmentOptions);
    }

    private void WireControllerEvents()
    {
        dockedModeSettingsHost.StateChanged += (_, _) => UpdateCurrentModeSettingsButton();
        dockedMeasurementSettingsHost.StateChanged += (_, _) => UpdateRecordSettingsButton();
        expSweepMeasurement.Completed += HandleMeasurementCompleted;
    }

    private void InitializeStartupState()
    {
        ApplyMeasurementConfigurationToControllers();
        commandController.Initialize();
        UpdatePeakInfo();
        ApplicationUpdateService.Initialize(this);
        _ = SelectModeAsync(ModeTab.Frequency);
    }

    private void WireFormEvents()
    {
        FormClosing += Form1_FormClosing;
        Shown += Form1_Shown;
    }

    private void HandleMeasurementCompleted(bool success)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke((MethodInvoker)delegate
        {
            if (success)
            {
                buttonRecord.Text = "Ready";
                plotModelFactory.SetImpulseResponseFileName(null);
                SetImpulseResponseAvailability(true);
            }
            else
            {
                buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                SetImpulseResponseAvailability(false);
            }

            UpdatePeakInfo();
            UpdateDrawButtonText();

            if (success && CurrentMode != Mode.LiveSpectrum)
            {
                DrawSelectedMode(true);
            }

            UpdateClearButtonState();
        });
    }

    private sealed record Form1ControllerDependencies(
        ChromeTitleBarController TitleBarController,
        OverlayCollection OverlayCollection,
        PlotLabelsPanelController PlotLabelsPanelController,
        PlotModelFactory PlotModelFactory,
        LiveSpectrumController LiveSpectrumController,
        ModeController ModeController,
        MainCommandController CommandController,
        TimeAlignmentPanelController TimeAlignmentController,
        InputLevelMeterController InputLevelMeterController,
        DockedModeSettingsHost DockedModeSettingsHost,
        DockedModeSettingsHost DockedMeasurementSettingsHost);
}
