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
        PlotModelFactory createdPlotModelFactory = new(
            analyzerDocument,
            expSweepMeasurement,
            noiseMeasurement,
            ResolveCalibration,
            viewSettings);
        createdPlotModelFactory.SetCompareSourceProvider(compareSelection.GetAnalysisSource);
        AnalyzerPlot createdAnalyzerPlot = new(
            this,
            plotView1,
            overlays,
            buttonOverlayShowAll,
            buttonOverlayHideAll,
            toolTip1,
            analyzerDocument,
            compareSelection,
            createdPlotModelFactory);
        LiveSpectrumController createdLiveSpectrumController = new(
            this,
            noiseMeasurement,
            createdAnalyzerPlot,
            () => SelectModeAsync(ModeTab.LiveSpectrum),
            UpdateRecordButtonForCurrentMode,
            viewSettings.LiveSpectrum,
            DescribeCalibrationForCapture,
            () => closingInProgress);
        ModeController createdModeController = new(
            createdAnalyzerPlot,
            StopRunningForModeSwitchAsync,
            ShowModeSurfaces);
        MainCommandController createdCommandController = new(
            buttonSave,
            buttonLoad,
            buttonRewExport,
            buttonRewImport,
            buttonCurrentModeSettings,
            buttonRecordOpt,
            buttonHistory,
            () => GetActiveModeDescriptor().HasDockedSettings,
            () => CanExportToRew,
            () => IsHandleCreated);
        TimeAlignmentPanelController createdTimeAlignmentController = new(
            this,
            timeAlignmentPanel,
            viewSettings.TimeAlignment,
            analyzerDocument,
            () => SaveMeasurementSettings(),
            compareSelection);
        InputLevelMeterController createdInputLevelMeterController = new(
            this,
            inputLevelMeterPanel,
            expSweepMeasurement,
            noiseMeasurement);
        DockedModeSettingsHost createdDockedModeSettingsHost = new(this, plotView1);
        DockedModeSettingsHost createdDockedMeasurementSettingsHost = new(this, plotView1);
        DockedModeSettingsHost createdDockedHistoryHost = new(this, plotView1);

        return new Form1ControllerDependencies(
            createdPlotModelFactory,
            createdAnalyzerPlot,
            createdLiveSpectrumController,
            createdModeController,
            createdCommandController,
            createdTimeAlignmentController,
            createdInputLevelMeterController,
            createdDockedModeSettingsHost,
            createdDockedMeasurementSettingsHost,
            createdDockedHistoryHost);
    }

    private void ApplyPersistedSettings() =>
        measurementSettings.ApplyTo(expSweepMeasurement, viewSettings);

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
        analyzerPlot.UpdatePeakInfo();
        ApplicationUpdateService.Initialize(this);
        _ = SelectModeAsync(ModeTab.Frequency);
    }

    private void RefreshCalibrationConsumers()
    {
        microphoneCalibration.InvalidateCache();
        IReadOnlyList<MicrophoneCalibrationEntry> entries = microphoneCalibration.GetEntries();
        // Only analysis selectors get the measurement's own curve; VDSP and wizard have their own calibration sources.
        IReadOnlyList<MicrophoneCalibrationEntry> analysisEntries = CalibrationEntries();
        virtualCrossoverPanel?.ConfigureCalibration(
            microphoneCalibration.Get, entries, AddSessionCalibration);
        eqWizardPanel?.ConfigureCalibration(microphoneCalibration.Get, entries);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshCalibrationEntries(analysisEntries));
        // The rig's choice describes the next run; a capture taken keeps its frozen calibration (no re-render, no peak-hold drop).
        string? rigCalibrationId =
            measurementSettings.Measurement.MicrophoneCalibrationId;
        viewSettings.LiveSpectrum.CalibrationId = rigCalibrationId;
        RefreshLiveCalibrationReadout();
    }

    /// <summary>Pushes to the live panel what the on-screen curve is corrected through; an empty plot falls through to the rig.</summary>
    private void RefreshLiveCalibrationReadout() =>
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.ShowCalibration(DescribeLiveCalibration()));

    private string DescribeLiveCalibration()
    {
        // A capture's stored name is shown as written; only the rig's id needs a lookup.
        if (liveSpectrumController.DisplayedCalibrationName is not { } name)
        {
            return NameCalibration(
                measurementSettings.Measurement.MicrophoneCalibrationId);
        }

        return string.IsNullOrWhiteSpace(name) ? "Off" : name;
    }

    private CapturedMicrophoneCalibration DescribeCalibrationForCapture(
        string? calibrationId) =>
        ResolveCalibration(calibrationId) is { } curve
            ? new CapturedMicrophoneCalibration(
                calibrationId, NameCalibration(calibrationId), curve)
            : CapturedMicrophoneCalibration.None;

    private string NameCalibration(string? calibrationId)
    {
        if (MicrophoneCalibrationIds.IsOff(calibrationId))
        {
            return "Off";
        }

        MicrophoneCalibrationEntry? entry = microphoneCalibration
            .GetEntries()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id, calibrationId, StringComparison.OrdinalIgnoreCase));
        return entry?.Name ?? "Deleted calibration";
    }

    // Writes the session's curve to app data as a regular file entry. Returns the new id, or null.
    private string? AddSessionCalibration(VirtualCrossoverSessionCalibration session)
    {
        List<MicrophoneCalibrationDefinition> definitions =
            measurementSettings.Measurement.AdditionalMicrophoneCalibrations;
        string path;
        try
        {
            string directory = ApplicationDataPaths.Current.CalibrationsDirectory;
            Directory.CreateDirectory(directory);
            path = SessionCalibrationFiles.UniquePath(
                directory,
                session.FileName ?? session.Name,
                File.Exists);
            File.WriteAllText(path, session.Curve.ToText());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                this,
                "The calibration could not be written to the application data folder." +
                $"{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }

        var definition = new MicrophoneCalibrationDefinition
        {
            Id = MicrophoneCalibrationDefinition.CreateId(definitions),
            Name = SessionCalibrationFiles.UniqueName(
                session.Name,
                definitions.Select(existing => existing.Name)),
            Kind = MicrophoneCalibrationKind.File,
            Path = path
        };
        definitions.Add(definition);
        // An open Record Settings panel writes its own copy back on Apply, so it must learn the entry.
        dockedMeasurementSettingsHost.InvokeIfOpen<Options.MeasurementOptions>(
            panel => panel.AdoptAdditionalCalibrations(definitions));
        ScheduleMeasurementSettingsSave();
        RefreshCalibrationConsumers();
        return definition.Id;
    }

    private void WireFormEvents()
    {
        buttonCompare.Click += (_, _) => ShowCompareMenu();
        FormClosing += Form1_FormClosing;
        Shown += Form1_Shown;
        // Last in construction so built controls exist to register; later ones register when added.
        EnableFileDrop();
    }

    private void HandleMeasurementCompleted(MeasurementResult? result)
    {
        TryBeginInvokeOnUiThread(() =>
        {
            AnalyzerDocument.Request? run = runRequest;
            runRequest = null;
            run?.Dispose();
            // New session aborts a run, but one finishing meanwhile still completes: it is dropped like an aborted one.
            MeasurementResult? landed =
                result != null && run?.Install(result, sourceName: null) == true ? result : null;
            bool success = landed != null;
            if (landed != null)
            {
                buttonRecord.Text = "Ready";
                RefreshMeasurementCommands();
                sessionTracker.MarkMeasurementCompleted(landed);
                // Move the view to the run's frozen calibration even from a user entry, or the response is drawn through the wrong mic.
                SelectAnalysisCalibration(MicrophoneCalibrationIds.Own);
            }
            else
            {
                buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                RefreshMeasurementCommands();
                ShowMeasurementError("The measurement failed.", expSweepMeasurement.LastError);
            }

            // A run that landed nothing still ends "measuring...".
            analyzerPlot.UpdatePeakInfo();
            // The run released the device (success or not); refresh the settings panel's deferred device view.
            RefreshOpenMeasurementSettingsDevice();

            if (success)
            {
                NotifyDegradedSweepAverage();
                NotifyResultCaution();
            }
        });
    }

    // Covers a published run with fewer averages than requested; a bad run is refused elsewhere.
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

    // Warning icon: the capture is fine but its reference is suspect.
    private void NotifyResultCaution()
    {
        SweepResultCaution? caution = expSweepMeasurement.ResultCaution;
        if (caution == null || closingInProgress)
        {
            return;
        }

        MessageBox.Show(
            this,
            caution.Describe(),
            "Measurement",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void HandleAverageProgressChanged(SweepAverageProgress progress)
    {
        TryBeginInvokeOnUiThread(() =>
        {
            buttonRecord.Text = $"Running {progress.CurrentRun}/{progress.TotalRuns}...";
        });
    }

    private sealed record Form1ControllerDependencies(
        PlotModelFactory PlotModelFactory,
        AnalyzerPlot AnalyzerPlot,
        LiveSpectrumController LiveSpectrumController,
        ModeController ModeController,
        MainCommandController CommandController,
        TimeAlignmentPanelController TimeAlignmentController,
        InputLevelMeterController InputLevelMeterController,
        DockedModeSettingsHost DockedModeSettingsHost,
        DockedModeSettingsHost DockedMeasurementSettingsHost,
        DockedModeSettingsHost DockedHistoryHost);
}
