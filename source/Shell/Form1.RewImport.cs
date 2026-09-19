using Resonalyze.Integration.Rew;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
    private bool rewImportInFlight;

    /// <summary>The dialog reads and checks the measurement; only an import that passed lands here.</summary>
    private async void buttonRewImport_Click(object? sender, EventArgs e)
    {
        if (rewImportInFlight || analyzerDocument.IsBusy)
        {
            return;
        }

        rewImportInFlight = true;
        try
        {
            await RunRewImportAsync();
        }
        finally
        {
            rewImportInFlight = false;
        }
    }

    private async Task RunRewImportAsync()
    {
        await StopLiveCaptureAsync();

        int configuredSampleRate =
            measurementSettings.Measurement.BuildConfiguration().Signal.SampleRate;
        RewPreparedImport? import;
        using (var dialog = new RewImportDialog(
            RewBaseUrl,
            configuredSampleRate,
            (address, token) => CreateRewImport(address).ListAsync(RewProbeTimeout, token),
            (address, measurement, offsetSeconds, levelDbfs, token) => CreateRewImport(address)
                .PrepareAsync(measurement, offsetSeconds, levelDbfs, configuredSampleRate, token)))
        {
            DialogResult result = dialog.ShowDialog(this);
            if (dialog.AnsweringBaseUrl is { } answering)
            {
                measurementSettings.RewApiBaseUrl = answering;
                ScheduleMeasurementSettingsSave();
            }

            import = result == DialogResult.OK ? dialog.Import : null;
        }

        if (import == null || IsDisposed || Disposing || closingInProgress)
        {
            return;
        }

        // Supersedes a load or Open in analyzers still reading, as a plain Load does.
        if (analyzerDocument.TryBegin() is not { } request)
        {
            return;
        }

        if (!GetActiveModeDescriptor().ShowsLoadedMeasurement)
        {
            await SelectModeAsync(ModeTab.Frequency);
        }

        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
        try
        {
            InstallRewMeasurementImport(request, import);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to import the REW measurement.\r\n\r\n{exception.Message}",
                "Import failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            commandController.SetSaveAvailable(analyzerDocument.HasResult);
            FinalizeMeasurementCommandState();
        }
    }

    private void InstallRewMeasurementImport(AnalyzerDocument.Request request, RewPreparedImport import)
    {
        MeasurementResult result = RewMeasurementImport.ToResult(
            import.Samples,
            import.Referenced,
            import.SampleRate,
            import.LowFrequencyHz,
            import.HighFrequencyHz,
            import.SweepLengthSamples,
            sweepCount: 1,
            import.Plan.Reference);
        if (FinishRewImport(request, result, RewSourceName(import.Measurement.Title), fromFile: false))
        {
            NotifyImportDecisions("REW measurement imported", RewImportNotes.Describe(import));
        }
    }

    private RewMeasurementImport CreateRewImport(Uri baseAddress) =>
        new(new RewApiClient(RewHttpClient, baseAddress));

    /// <summary>The plot title reads the name as a file name, so path characters in a REW title would cut it.</summary>
    private static string RewSourceName(string? title)
    {
        string name = string.IsNullOrWhiteSpace(title) ? "REW measurement" : title.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }
}
