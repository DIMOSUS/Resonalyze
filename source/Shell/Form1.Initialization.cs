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
            GetMicrophoneCalibration,
            frequencyResponseOptions,
            phaseResponseOptions,
            groupDelayOptions,
            impulseResponseOptions,
            liveSpectrumOptions,
            waterfallGenOptions,
            burstDecayGenOptions);
        createdPlotModelFactory.SetCompareSourceProvider(GetCompareAnalysisSource);
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
            GetTimeAlignmentCompareMeasurement);
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

    private bool HasMicrophoneCalibration(MicrophoneCalibrationMode mode) =>
        mode == MicrophoneCalibrationMode.Degrees90
            ? HasApproximateNinetyDegreeCalibration()
            : GetMicrophoneCalibrationPath(mode) != null;

    private CalibrationFile? GetMicrophoneCalibration(MicrophoneCalibrationMode mode)
    {
        if (mode == MicrophoneCalibrationMode.Degrees90 &&
            GetMicrophoneCalibrationPath(MicrophoneCalibrationMode.Degrees90) == null)
        {
            return GetApproximateNinetyDegreeCalibration();
        }

        string? path = GetMicrophoneCalibrationPath(mode);
        if (path == null)
        {
            return null;
        }

        if (!calibrationCache.TryGetValue(path, out CalibrationFile? calibrationFile))
        {
            calibrationFile = new CalibrationFile(path);
            calibrationCache[path] = calibrationFile;
        }

        return calibrationFile;
    }

    private CalibrationFile? GetApproximateNinetyDegreeCalibration()
    {
        string? zeroDegreePath = GetMicrophoneCalibrationPath(
            MicrophoneCalibrationMode.Degrees0);
        if (zeroDegreePath == null)
        {
            return null;
        }

        string cacheKey = $"approx90:{zeroDegreePath}";
        if (!calibrationCache.TryGetValue(cacheKey, out CalibrationFile? calibrationFile))
        {
            CalibrationFile zeroDegreeCalibration =
                GetMicrophoneCalibration(MicrophoneCalibrationMode.Degrees0)
                ?? throw new InvalidOperationException(
                    "0 degree microphone calibration is not available.");
            calibrationFile = CalibrationFile.CreateNinetyDegreeApproximation(
                zeroDegreeCalibration);
            calibrationCache[cacheKey] = calibrationFile;
        }

        return calibrationFile;
    }

    private string? GetMicrophoneCalibrationPath(MicrophoneCalibrationMode mode)
    {
        string? path = mode switch
        {
            MicrophoneCalibrationMode.Degrees0 =>
                measurementSettings.Measurement.MicrophoneCalibration0DegreesPath,
            MicrophoneCalibrationMode.Degrees90 =>
                measurementSettings.Measurement.MicrophoneCalibration90DegreesPath,
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(path))
        {
            return File.Exists(path) ? path : null;
        }

        if (mode == MicrophoneCalibrationMode.Degrees0)
        {
            string legacyPath = Path.Combine(AppContext.BaseDirectory, "calibration.txt");
            if (File.Exists(legacyPath))
            {
                return legacyPath;
            }
        }

        return null;
    }

    private bool HasApproximateNinetyDegreeCalibration() =>
        GetMicrophoneCalibrationPath(MicrophoneCalibrationMode.Degrees90) != null ||
        GetMicrophoneCalibrationPath(MicrophoneCalibrationMode.Degrees0) != null;

    private void RefreshCalibrationConsumers()
    {
        calibrationCache.Clear();
        if (virtualCrossoverPanel != null)
        {
            virtualCrossoverPanel.ConfigureCalibration(
                GetMicrophoneCalibration,
                HasMicrophoneCalibration(MicrophoneCalibrationMode.Degrees0),
                HasMicrophoneCalibration(MicrophoneCalibrationMode.Degrees90));
        }
    }

    private void WireFormEvents()
    {
        measurementSettingsSaveTimer.Tick += MeasurementSettingsSaveTimer_Tick;
        recordButtonLongPressTimer.Tick += RecordButtonLongPressTimer_Tick;
        buttonRecord.MouseDown += buttonRecord_MouseDown;
        buttonRecord.MouseLeave += buttonRecord_MouseLeave;
        buttonRecord.MouseUp += buttonRecord_MouseUp;
        buttonCompare.Click += buttonCompare_Click;
        FormClosing += Form1_FormClosing;
        Shown += Form1_Shown;
    }

    private void MeasurementSettingsSaveTimer_Tick(object? sender, EventArgs e)
    {
        FlushMeasurementSettings();
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
                currentHistoryEntryId = measurementHistoryService.AddMeasurement(
                    expSweepMeasurement,
                    CaptureCurrentSessionSnapshot());
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
        });
    }

    private void HandleAverageProgressChanged(SweepAverageProgress progress)
    {
        TryBeginInvokeOnUiThread(() =>
        {
            buttonRecord.Text = progress.State == SweepAverageProgressState.WaitingForConfirmation
                ? $"Next run ({progress.CurrentRun + 1}/{progress.TotalRuns})"
                : $"Running {progress.CurrentRun}/{progress.TotalRuns}...";
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
