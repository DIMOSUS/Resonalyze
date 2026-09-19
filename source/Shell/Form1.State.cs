using Resonalyze.Dsp;

namespace Resonalyze;

public partial class Form1
{
    private void ApplyMeasurementConfigurationToControllers()
    {
        liveSpectrumSession.Configure(measurementSettings.Measurement);
        timeAlignmentController.RefreshConfiguration();
    }

    private async Task ApplyMeasurementConfigurationToControllersAsync()
    {
        await liveSpectrumController.ReconfigureFromAsync(measurementSettings.Measurement);
        timeAlignmentController.RefreshConfiguration();
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

    /// <summary>Makes <paramref name="result"/> the open measurement; every input that restores one comes through here.</summary>
    /// <remarks>
    /// The plot, Time Alignment and the mode panels follow the document. Read through its own calibration: every file
    /// carries it, and imports carry none, so they must not get the user's mic curve.
    /// </remarks>
    /// <param name="sourceName">A file path, or a title for a measurement with no file (no path separators).</param>
    /// <param name="fromFile">The next Load dialog opens in the file's folder.</param>
    /// <returns>False when a newer request superseded <paramref name="request"/>: nothing changed.</returns>
    private bool InstallMeasurement(
        AnalyzerDocument.Request request, MeasurementResult result, string? sourceName, bool fromFile = false)
    {
        if (!request.Install(result, sourceName))
        {
            return false;
        }

        inputLevelMeterController.Show(result.Levels);
        SelectAnalysisCalibration(MicrophoneCalibrationIds.Own);
        if (fromFile && sourceName != null)
        {
            UpdateLastImpulseResponseDirectory(sourceName);
        }

        return true;
    }

    /// <remarks>The measurement's own curve is not in the calibration service's list, so always resolve through here.</remarks>
    private CalibrationFile? ResolveCalibration(string? calibrationId) =>
        MicrophoneCalibrationIds.IsOwn(calibrationId)
            ? analyzerDocument.Result?.MicrophoneCalibration?.ToCalibrationFile()
            : microphoneCalibration.Get(calibrationId);

    /// <remarks><see cref="MicrophoneCalibrationIds.Own"/> is a regular entry, marked unavailable when the open measurement carries none.</remarks>
    private IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntries() =>
    [
        new MicrophoneCalibrationEntry(
            MicrophoneCalibrationIds.Own,
            "Own (as measured)",
            analyzerDocument.Result?.MicrophoneCalibration != null),
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

    private void RefreshMeasurementCommands()
    {
        // In a capture mode the button belongs to the live analyzer.
        RefreshSaveAvailability();
        commandController.SetLoadAvailable(true);
    }

    private void EnterMeasurementRunningState()
    {
        buttonRecord.Text = "Running...";
        sessionTracker.Reset();
        analyzerPlot.UpdatePeakInfo();
        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
    }

    private void FinalizeMeasurementCommandState() => commandController.SetLoadAvailable(true);
}
