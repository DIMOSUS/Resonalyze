namespace Resonalyze;

public partial class Form1
{
    private void ApplyMeasurementConfigurationToControllers()
    {
        liveSpectrumController.ConfigureFrom(expSweepMeasurement);
        timeAlignmentController.RefreshConfiguration();
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
        hasCurrentImpulseResponse = false;
        SetImpulseResponseSourceFile(null);
        UpdatePeakInfo();
        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
        UpdateDrawButtonText();
    }

    private void ApplyLoadedImpulseResponseState(string filePath)
    {
        ApplyMeasurementConfigurationToControllers();
        SetImpulseResponseSourceFile(filePath);
        UpdateLastImpulseResponseDirectory(filePath);
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
