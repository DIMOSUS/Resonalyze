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
            DescribeCalibrationForCapture,
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
            buttonRewExport,
            buttonCurrentModeSettings,
            buttonRecordOpt,
            buttonHistory,
            () => GetActiveModeDescriptor().HasDockedSettings,
            () => CanExportToRew,
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
        // The live panel shows the RIG's calibration and does not choose it, so it is
        // told which one to show rather than asked which it had.
        // The rig's choice describes the NEXT run. A capture already taken — running,
        // held, or waiting to be saved — keeps the calibration frozen on it when it
        // began, so nothing here reaches back into it: no re-render, and no peak hold
        // dropped for a change that cannot touch the curve it holds.
        string? rigCalibrationId =
            measurementSettings.Measurement.MicrophoneCalibrationId;
        liveSpectrumOptions.CalibrationId = rigCalibrationId;
        RefreshLiveCalibrationReadout();
    }

    /// <summary>
    /// Tells the live panel what the plot in front of it is corrected through.
    /// </summary>
    /// <remarks>
    /// The controller answers for the curve on screen — a loaded capture by the name
    /// stored in it, a running or held one by the calibration frozen when its run
    /// began — and only an empty plot falls through to the rig, which is what the NEXT
    /// run will use. Pushed from here rather than read by the panel because the panel
    /// is opened, refreshed and re-opened at moments it does not choose.
    /// </remarks>
    private void RefreshLiveCalibrationReadout() =>
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.ShowCalibration(DescribeLiveCalibration()));

    private string DescribeLiveCalibration()
    {
        // The name a capture carries is already the one a reader was shown when it was
        // taken — its own if it came from a file, this machine's if the capture is
        // still here — so it is displayed as written rather than looked up again.
        // Only an empty plot falls through to the rig, whose id has to be named.
        if (liveSpectrumController.DisplayedCalibrationName is not { } name)
        {
            return NameCalibration(
                measurementSettings.Measurement.MicrophoneCalibrationId);
        }

        return string.IsNullOrWhiteSpace(name) ? "Off" : name;
    }

    /// <summary>
    /// Everything a run has to freeze about the microphone it is taken through: the
    /// curve that corrects it, the name a reader will be shown, and the id behind it.
    /// </summary>
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
        buttonCompare.Click += (_, _) => ShowCompareMenu();
        FormClosing += Form1_FormClosing;
        Shown += Form1_Shown;
        // Files dragged in from Explorer, which the window accepts anywhere on it
        // (see Form1.FileDrop). Last in construction, so the controls the shell has
        // built are all there to be registered; the ones it builds later register
        // themselves as they are added.
        EnableFileDrop();
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
                // A new measurement is read through the microphone that took it. The
                // view is moved even when it was on one of the user's own entries:
                // the run has just frozen a calibration into the result, and leaving
                // the plot on a different one draws the response through a microphone
                // it never passed — silently, and with the selector still naming the
                // curve it was left on.
                SelectAnalysisCalibration(MicrophoneCalibrationIds.Own);
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
            // The same debt the live spectrum pays when it stops, and for the same
            // reason: an Apply made while the sweep held the device left the settings
            // panel's picture of the driver alone rather than probing a device it did
            // not own. The run has just released it — after EVERY completion, since an
            // aborted or failed run releases it exactly as a good one does.
            RefreshOpenMeasurementSettingsDevice();

            if (success)
            {
                NotifyDegradedSweepAverage();
                NotifyResultCaution();
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

    // The result published, and its own shape says something the user would
    // otherwise only find by wondering why a tune fought back. A warning rather
    // than the information icon the run report uses: nothing is wrong with the
    // capture, but the reference it was divided by is suspect.
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
