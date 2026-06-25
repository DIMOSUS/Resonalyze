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
    }

    private void PrepareSweepMeasurementForRun()
    {
        measurementSettings.Measurement.ApplyTo(expSweepMeasurement);
    }

    private void SetImpulseResponseAvailability(bool available)
    {
        hasCurrentImpulseResponse = available;
        commandController.SetSaveAvailable(available);
        commandController.SetLoadAvailable(true);
    }

    private void EnterMeasurementRunningState()
    {
        buttonRecord.Text = "Running...";
        currentHistoryEntryId = null;
        hasCurrentImpulseResponse = false;
        SetImpulseResponseSourceFile(null);
        UpdatePeakInfo();
        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
        UpdateDrawButtonText();
    }

    private void ApplyLoadedImpulseResponseState(string? filePath)
    {
        ApplyMeasurementConfigurationToControllers();
        SetImpulseResponseSourceFile(filePath);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            UpdateLastImpulseResponseDirectory(filePath);
        }
        hasCurrentImpulseResponse = true;
        UpdatePeakInfo();
        RefreshCurrentModePlot();
    }

    private void FinalizeMeasurementCommandState()
    {
        commandController.SetLoadAvailable(true);
        UpdatePeakInfo();
        UpdateDrawButtonText();
    }
}
