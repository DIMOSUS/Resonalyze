using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.History;

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
        chromeTitleBar.Initialize(
            this,
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
            microphoneCalibration.Get,
            frequencyResponseOptions,
            phaseResponseOptions,
            groupDelayOptions,
            frequencyResponseVisibility,
            phaseResponseVisibility,
            groupDelayVisibility,
            impulseResponseOptions,
            liveSpectrumOptions,
            waterfallGenOptions,
            burstDecayGenOptions);
        createdPlotModelFactory.SetCompareSourceProvider(compareSelection.GetAnalysisSource);
        LiveSpectrumController createdLiveSpectrumController = new(
            this,
            noiseMeasurement,
            plotView1,
            createdPlotModelFactory,
            createdOverlayCollection,
            () => CurrentMode,
            () => SelectModeAsync(ModeTab.LiveSpectrum),
            UpdateOverlayAvailability,
            UpdateRecordButtonForCurrentMode,
            UpdatePlotLabelsPanel,
            liveSpectrumOptions,
            () => closingInProgress);
        ModeController createdModeController = new(
            ChangeModeAsync,
            SetActiveModeTab,
            DrawSelectedMode,
            RestoreActiveOverlaySlotsForCurrentMode,
            CanDrawCurrentMeasurement,
            tab => GetModeDescriptor(tab).Mode,
            tab => GetModeDescriptor(tab).SupportsCurveDrawing);
        MainCommandController createdCommandController = new(
            buttonSave,
            buttonLoad,
            buttonCurrentModeSettings,
            buttonRecordOpt,
            buttonHistory,
            () => GetActiveModeDescriptor().HasDockedSettings,
            () => IsHandleCreated);
        TimeAlignmentPanelController createdTimeAlignmentController = new(
            this,
            timeAlignmentPanel,
            timeAlignmentOptions,
            expSweepMeasurement,
            () => SaveMeasurementSettings(),
            () => plotModelFactory.ImpulseResponseFileName,
            compareSelection.GetTimeAlignmentMeasurement);
        InputLevelMeterController createdInputLevelMeterController = new(
            this,
            inputLevelMeterPanel,
            expSweepMeasurement,
            noiseMeasurement);
        DockedModeSettingsHost createdDockedModeSettingsHost = new(this, plotView1);
        DockedModeSettingsHost createdDockedMeasurementSettingsHost = new(this, plotView1);
        DockedModeSettingsHost createdDockedHistoryHost = new(this, plotView1);

        return new Form1ControllerDependencies(
            createdOverlayCollection,
            createdPlotLabelsPanelController,
            createdPlotModelFactory,
            createdLiveSpectrumController,
            createdModeController,
            createdCommandController,
            createdTimeAlignmentController,
            createdInputLevelMeterController,
            createdDockedModeSettingsHost,
            createdDockedMeasurementSettingsHost,
            createdDockedHistoryHost);
    }

    private void ApplyPersistedSettings()
    {
        measurementSettings.ApplyTo(
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
    }

    private void WireControllerEvents()
    {
        dockedModeSettingsHost.StateChanged += (_, _) =>
        {
            UpdateCurrentModeSettingsButton();
            FlushMeasurementSettingsIfClosed(dockedModeSettingsHost);
        };
        dockedMeasurementSettingsHost.StateChanged += (_, _) =>
        {
            UpdateRecordSettingsButton();
            FlushMeasurementSettingsIfClosed(dockedMeasurementSettingsHost);
        };
        dockedHistoryHost.StateChanged += (_, _) => UpdateHistoryButton();
        compareSelection.Changed += OnCompareMeasurementChanged;
        expSweepMeasurement.Completed += HandleMeasurementCompleted;
        expSweepMeasurement.AverageProgressChanged += HandleAverageProgressChanged;
        measurementHistoryService.Changed += HandleHistoryChanged;
    }

    private void FlushMeasurementSettingsIfClosed(DockedModeSettingsHost host)
    {
        if (!host.IsOpen)
        {
            FlushMeasurementSettings();
        }
    }

    private void InitializeStartupState()
    {
        ApplyMeasurementConfigurationToControllers();
        commandController.Initialize();
        ApplyMainContentLayout();
        UpdateCompareButton();
        UpdateHistoryButton();
        UpdatePeakInfo();
        ApplicationUpdateService.Initialize(this);
        _ = SelectModeAsync(ModeTab.Frequency);
    }

    private string? GetConfiguredMicrophoneCalibrationPath(MicrophoneCalibrationMode mode) =>
        mode switch
        {
            MicrophoneCalibrationMode.Degrees0 =>
                measurementSettings.Measurement.MicrophoneCalibration0DegreesPath,
            MicrophoneCalibrationMode.Degrees90 =>
                measurementSettings.Measurement.MicrophoneCalibration90DegreesPath,
            _ => null
        };

    private void RefreshCalibrationConsumers()
    {
        microphoneCalibration.InvalidateCache();
        if (virtualCrossoverPanel != null)
        {
            virtualCrossoverPanel.ConfigureCalibration(
                microphoneCalibration.Get,
                microphoneCalibration.Has(MicrophoneCalibrationMode.Degrees0),
                microphoneCalibration.Has(MicrophoneCalibrationMode.Degrees90));
        }
    }

    private void WireFormEvents()
    {
        buttonCompare.Click += buttonCompare_Click;
        FormClosing += Form1_FormClosing;
        Shown += Form1_Shown;
    }

    private void HandleMeasurementCompleted(bool success)
    {
        TryBeginInvokeOnUiThread(() =>
        {
            if (success)
            {
                buttonRecord.Text = "Ready";
                plotModelFactory.SetImpulseResponseFileName(null);
                SetImpulseResponseAvailability(true);
                sessionTracker.MarkMeasurementCompleted(expSweepMeasurement);
            }
            else
            {
                buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                SetImpulseResponseAvailability(false);
                ShowMeasurementError("The measurement failed.", expSweepMeasurement.LastError);
            }

            UpdatePeakInfo();

            if (success && CurrentMode != Mode.LiveSpectrum)
            {
                DrawSelectedMode(true);
            }

            if (success)
            {
                NotifyDegradedSweepAverage();
            }
        });
    }

    // Sweep-run acceptance: rejected runs (and their failed retries) are
    // excluded silently while the measurement is running; the user is told
    // once, at the end, when the average holds fewer runs than requested.
    private void NotifyDegradedSweepAverage()
    {
        SweepRunQualityReport? report = expSweepMeasurement.QualityReport;
        if (report is not { IsDegraded: true } || closingInProgress)
        {
            return;
        }

        MessageBox.Show(
            this,
            report.Describe(),
            "Measurement",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void HandleAverageProgressChanged(SweepAverageProgress progress)
    {
        TryBeginInvokeOnUiThread(() =>
        {
            buttonRecord.Text = progress.State switch
            {
                SweepAverageProgressState.WaitingForConfirmation =>
                    $"Next run ({progress.CurrentRun + 1}/{progress.TotalRuns})",
                SweepAverageProgressState.Retrying =>
                    $"Retrying {progress.CurrentRun}/{progress.TotalRuns}...",
                _ => $"Running {progress.CurrentRun}/{progress.TotalRuns}..."
            };
        });
    }

    private sealed record Form1ControllerDependencies(
        OverlayCollection OverlayCollection,
        PlotLabelsPanelController PlotLabelsPanelController,
        PlotModelFactory PlotModelFactory,
        LiveSpectrumController LiveSpectrumController,
        ModeController ModeController,
        MainCommandController CommandController,
        TimeAlignmentPanelController TimeAlignmentController,
        InputLevelMeterController InputLevelMeterController,
        DockedModeSettingsHost DockedModeSettingsHost,
        DockedModeSettingsHost DockedMeasurementSettingsHost,
        DockedModeSettingsHost DockedHistoryHost);
}
