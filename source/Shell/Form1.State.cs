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
        // The audio routing may have gained or lost the loopback reference — the
        // prerequisite of the live Transfer mode — so an open live panel re-evaluates
        // its amber states here, the one chokepoint every routing change passes.
        dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
            panel => panel.RefreshAvailability(
                plotModelFactory.LiveSplOffsetDb.HasValue,
                liveSpectrumController.HasDisplayableCurve,
                liveSpectrumController.HasConfiguredLoopback));
    }

    private void PrepareSweepMeasurementForRun()
    {
        measurementSettings.Measurement.ApplyTo(expSweepMeasurement);
        // The calibration a response is READ through belongs to the result. The
        // impulse response itself is raw — no calibration is ever baked into one —
        // so unless the file carries the curve, a recipient draws a different
        // response from the author's and nothing says why. Pushed here, the one
        // chokepoint every run passes; the measurement freezes it at run start.
        //
        // From Record Settings, beside the array microphones' own choices, and NOT
        // from the Frequency Response view: a view is where a chart is read, and
        // taking the stamp from it meant a calibration selected after the sweeps
        // labelled none of them, while one adopted to read someone else's file
        // labelled all of them with a stranger's microphone.
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

    /// <summary>
    /// Installs everything that belongs to the RESULT rather than to the next run.
    /// </summary>
    /// <remarks>
    /// Both paths that bring a result in — opening a file and stepping back through
    /// history — go through here, because they diverged once already. Restoring an
    /// impulse response goes through Init, which clears ALL of this, so a path that
    /// forgets a line hands over a measurement that differs from the stored one with
    /// nothing on screen to say so: no array, and the tools fall back to the one
    /// point the response came from; no microphone calibration, and a re-save writes
    /// a file that lost it; no protective high-pass, and the band the filter took
    /// past recovering stops being masked and draws the analysis window's leakage as
    /// a driver rolloff.
    /// <para>
    /// The SPL anchor's capture identity stands in for the result's input, so
    /// re-saving validates the anchor against the input it was MEASURED on rather
    /// than against the app's current device, and keeps it.
    /// </para>
    /// </remarks>
    private void AdoptRestoredResult(
        SplCalibration? splCalibration,
        VirtualCrossoverCalibrationSettings? microphoneCalibration,
        IReadOnlyList<ArrayMicrophoneCurve> arrayMicrophones,
        ProtectiveHighPassConfiguration? protectiveHighPass)
    {
        expSweepMeasurement.MeasurementSplCalibration = splCalibration;
        expSweepMeasurement.MeasurementMicrophoneCalibration = microphoneCalibration;
        expSweepMeasurement.ArrayMicrophones = arrayMicrophones;
        // Including "nothing": the app's own setting describes the next run, not the
        // response just restored, and "nobody recorded which filter this passed
        // through" is a different answer from "none".
        expSweepMeasurement.MeasurementProtectiveHighPass = protectiveHighPass;
        expSweepMeasurement.MeasurementInput = splCalibration?.CaptureIdentity;
        AdoptFileCalibration(microphoneCalibration);
    }

    /// <summary>
    /// The entry from the user's OWN list that a loaded measurement's calibration
    /// displaced, so it can be handed back when that calibration goes away.
    /// </summary>
    /// <remarks>
    /// The file's entry exists only while the measurement carrying it is open, and
    /// the selection pointing at it must not outlive it. Falling back to the 0° slot
    /// would be a guess at what the user was reading through — it is the only slot
    /// that is always there, not the only one anybody uses.
    /// </remarks>
    private string? displacedLocalCalibrationId;

    /// <summary>
    /// The calibration a selector id names, the loaded measurement's own included.
    /// </summary>
    /// <remarks>
    /// Every consumer resolves through here rather than through the calibration
    /// service directly, because the file's curve is not in the service's list —
    /// it belongs to whatever measurement is open, not to this machine.
    /// </remarks>
    private CalibrationFile? ResolveCalibration(string? calibrationId) =>
        FileCalibrationSelection.IsFile(calibrationId)
            ? expSweepMeasurement.MeasurementMicrophoneCalibration?.ToCalibrationFile()
            : microphoneCalibration.Get(calibrationId);

    /// <summary>
    /// The selector list, with the loaded measurement's calibration appended when
    /// no local entry already holds that curve.
    /// </summary>
    private IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntries() =>
        FileCalibrationSelection.EntriesWith(
            microphoneCalibration.GetEntries(),
            expSweepMeasurement.MeasurementMicrophoneCalibration,
            microphoneCalibration.Get);

    /// <summary>
    /// One calibration as a portable CURVE: the name and file name are what the
    /// author's list showed, and the points are what actually decide, because two
    /// machines' calibration lists mint their own ids.
    /// </summary>
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

        if (FileCalibrationSelection.IsFile(calibrationId))
        {
            // Deliberately kept: the user was asked before the run started and
            // chose to measure through the loaded file's curve.
            return expSweepMeasurement.MeasurementMicrophoneCalibration;
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
        // Through the shared decision: in a capture mode the button belongs to the
        // live analyzer, and setting it straight from the impulse-response state
        // would take it away from a finished moving-mic pass.
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
