using Resonalyze.Dsp;

namespace Resonalyze;

public partial class Form1
{
    private void ApplyMeasurementConfigurationToControllers()
    {
        liveSpectrumController.ConfigureFrom(measurementSettings.Measurement);
        timeAlignmentController.RefreshConfiguration();
    }

    private async Task ApplyMeasurementConfigurationToControllersAsync()
    {
        await liveSpectrumController.ReconfigureFromAsync(measurementSettings.Measurement);
        timeAlignmentController.RefreshConfiguration();
        // Routing may have gained or lost the loopback (needed by live Transfer); every routing change passes here.
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.RefreshAvailability(
                plotModelFactory.LiveSplOffsetDb.HasValue,
                liveSpectrumController.HasDisplayableCurve,
                liveSpectrumController.HasConfiguredLoopback));
    }

    private void PrepareSweepMeasurementForRun()
    {
        measurementSettings.Measurement.ApplyTo(expSweepMeasurement);
        // The IR is raw, so the file must carry the calibration curve it was read through. From Record Settings, not the FR view,
        // so a later view selection neither relabels nor strips past runs.
        expSweepMeasurement.MicrophoneCalibration =
            FreezeCalibration(measurementSettings.Measurement.MicrophoneCalibrationId);
        expSweepMeasurement.ArrayMicrophoneMetadata =
            measurementSettings.Measurement.ArrayMicrophones
                .Select(microphone => new ArrayMicrophoneMetadata(
                    microphone.ChannelOffset,
                    microphone.Note,
                    FreezeCalibration(microphone.CalibrationId)))
                .ToList();
    }

    /// <summary>Installs what belongs to the result, not the next run; shared by file open and history (Init clears all of it).</summary>
    /// <remarks>The SPL anchor's capture identity stands in for the result's input, so a re-save validates against the measured input.</remarks>
    private void AdoptRestoredResult(
        SplCalibration? splCalibration,
        VirtualCrossoverCalibrationSettings? microphoneCalibration,
        IReadOnlyList<ArrayMicrophoneCurve> arrayMicrophones,
        ProtectiveHighPassConfiguration? protectiveHighPass)
    {
        expSweepMeasurement.MeasurementSplCalibration = splCalibration;
        expSweepMeasurement.MeasurementMicrophoneCalibration = microphoneCalibration;
        expSweepMeasurement.ArrayMicrophones = arrayMicrophones;
        // Including null: "unknown filter" differs from "none".
        expSweepMeasurement.MeasurementProtectiveHighPass = protectiveHighPass;
        expSweepMeasurement.MeasurementInput = splCalibration?.CaptureIdentity;
        // For the history path; a no-op when the file path already selected it.
        SelectAnalysisCalibration(MicrophoneCalibrationIds.Own);
    }

    /// <remarks>The measurement's own curve is not in the calibration service's list, so always resolve through here.</remarks>
    private CalibrationFile? ResolveCalibration(string? calibrationId) =>
        MicrophoneCalibrationIds.IsOwn(calibrationId)
            ? expSweepMeasurement.MeasurementMicrophoneCalibration?.ToCalibrationFile()
            : microphoneCalibration.Get(calibrationId);

    /// <remarks><see cref="MicrophoneCalibrationIds.Own"/> is a regular entry, marked unavailable when the open measurement carries none.</remarks>
    private IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntries() =>
    [
        new MicrophoneCalibrationEntry(
            MicrophoneCalibrationIds.Own,
            "Own (as measured)",
            expSweepMeasurement.MeasurementMicrophoneCalibration != null),
        .. microphoneCalibration.GetEntries()
    ];

    private void SelectAnalysisCalibration(string? calibrationId) =>
        SelectFrequencyResponseCalibration(calibrationId);

    /// <summary>Portable curve: points decide, since each machine mints its own ids.</summary>
    private VirtualCrossoverCalibrationSettings? FreezeCalibration(string? calibrationId)
    {
        if (MicrophoneCalibrationIds.IsOff(calibrationId))
        {
            return null;
        }

        CalibrationFile? curve = ResolveCalibration(calibrationId);
        if (curve == null)
        {
            return null;
        }

        MicrophoneCalibrationEntry? entry = microphoneCalibration
            .GetEntries()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                calibrationId,
                StringComparison.OrdinalIgnoreCase));
        return VirtualCrossoverCalibrationSettings.From(
            curve,
            entry?.Name ?? calibrationId ?? string.Empty,
            entry?.FileName);
    }

    private void SetImpulseResponseAvailability(bool available)
    {
        sessionTracker.SetImpulseResponseAvailable(available);
        // In a capture mode the button belongs to the live analyzer.
        RefreshSaveAvailability();
        commandController.SetLoadAvailable(true);
    }

    private void EnterMeasurementRunningState()
    {
        buttonRecord.Text = "Running...";
        sessionTracker.Reset();
        SetImpulseResponseSourceFile(null);
        UpdatePeakInfo();
        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
    }

    private void ApplyLoadedImpulseResponseState(string? filePath)
    {
        // Every file from disk is read through its own calibration; imports carry none and must not get the user's mic curve.
        SelectAnalysisCalibration(MicrophoneCalibrationIds.Own);
        ApplyMeasurementConfigurationToControllers();
        SetImpulseResponseSourceFile(filePath);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            UpdateLastImpulseResponseDirectory(filePath);
        }
        sessionTracker.SetImpulseResponseAvailable(true);
        UpdatePeakInfo();
        RefreshCurrentModePlot();
    }

    private void FinalizeMeasurementCommandState()
    {
        commandController.SetLoadAvailable(true);
        UpdatePeakInfo();
    }
}
