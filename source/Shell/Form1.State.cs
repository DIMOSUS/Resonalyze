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
