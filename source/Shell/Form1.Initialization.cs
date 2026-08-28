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
            ResolveCalibration,
            new PlotPresentationOptions(
                FrequencyResponse: frequencyResponseOptions,
                PhaseResponse: phaseResponseOptions,
                GroupDelay: groupDelayOptions,
                FrequencyResponseVisibility: frequencyResponseVisibility,
                PhaseResponseVisibility: phaseResponseVisibility,
                GroupDelayVisibility: groupDelayVisibility,
                ImpulseResponse: impulseResponseOptions,
                LiveSpectrum: liveSpectrumOptions,
                Waterfall: waterfallGenOptions,
                BurstDecay: burstDecayGenOptions));
        createdPlotModelFactory.SetCompareSourceProvider(compareSelection.GetAnalysisSource);
        PlotViewportMemory createdPlotViewports = new(plotView1);
        LiveSpectrumController createdLiveSpectrumController = new(
            this,
            noiseMeasurement,
            plotView1,
            createdPlotViewports,
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
            createdPlotViewports,
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

    private void RefreshCalibrationConsumers()
    {
        microphoneCalibration.InvalidateCache();
        IReadOnlyList<MicrophoneCalibrationEntry> entries = microphoneCalibration.GetEntries();
        // Only the analysis selectors are offered the loaded measurement's own
        // curve. The Virtual DSP and wizard panels carry their own calibration
        // story (a session's, a handoff's), and a third source in those lists
        // would be one more thing meaning "not from your list" beside two that
        // already do.
        IReadOnlyList<MicrophoneCalibrationEntry> analysisEntries = CalibrationEntries();
        virtualCrossoverPanel?.ConfigureCalibration(
            microphoneCalibration.Get, entries, AddSessionCalibration);
        eqWizardPanel?.ConfigureCalibration(microphoneCalibration.Get, entries);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshCalibrationEntries(analysisEntries));
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.RefreshCalibrationEntries(entries));
    }

    // Adds a calibration curve a Virtual DSP session carried in to the configured
    // list, as a file entry like any other: the curve is written to the application
    // data folder under its original file name and an entry is created for it, so
    // every view can pick it. Returns the new entry's id, or null when nothing was
    // added.
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
        // An open Record Settings panel works on its own copy of the list and
        // writes it back on Apply, so it has to learn about the entry or it would
        // overwrite it.
        dockedMeasurementSettingsHost.InvokeIfOpen<Options.MeasurementOptions>(
            panel => panel.AdoptAdditionalCalibrations(definitions));
        ScheduleMeasurementSettingsSave();
        RefreshCalibrationConsumers();
        return definition.Id;
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
                // A finished sweep is the current measurement, so it supersedes any
                // read still in flight — a file picked before the run started must
                // not land on top of what was just measured.
                measurementActivationRevision++;
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

            // The Frequency Response panel evaluates dB SPL availability only when it
            // opens; a run changes it in both directions (a good run captures the
            // loopback level SPL needs; a failed/aborted run clears the level snapshot),
            // so recolour the SPL choice — full or view-only — after EVERY completion,
            // not just success.
            dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
                panel => panel.RefreshSplAvailability());

            if (success)
            {
                NotifyDegradedSweepAverage();
            }
        });
    }

    // Sweep-run acceptance: a bad run stops the measurement, and the refusal
    // says why. This notice covers what the refusal cannot — a report left on a
    // measurement that did publish, which is any run count short of the request.
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
                _ => $"Running {progress.CurrentRun}/{progress.TotalRuns}..."
            };
        });
    }

    private sealed record Form1ControllerDependencies(
        PlotViewportMemory PlotViewports,
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
