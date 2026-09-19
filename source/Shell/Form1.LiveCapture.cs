namespace Resonalyze;

// MMM Save/Load act on the mode's own spatial average, not the app's impulse response. MMM only: the document
// describes a band-power capture; relative RTA or transfer would need fields the format lacks.
public partial class Form1
{
    private const string LiveCaptureFilter =
        "Resonalyze moving-mic capture (*.json)|*.json|All files (*.*)|*.*";

    internal const string MeasurementFileFilter =
        "Measurements (*.json;*.wav;*.txt)|*.json;*.wav;*.txt|" +
        "Resonalyze impulse response (*.json)|*.json|" +
        "Resonalyze moving-mic capture (*.json)|*.json|" +
        "Recorded sweep (*.wav)|*.wav|" +
        "REW impulse response export (*.txt)|*.txt|" +
        "All files (*.*)|*.*";

    internal const string MeasurementFileDialogTitle =
        "Load impulse response, recorded sweep, REW export or capture";

    private bool LiveCaptureOwnsSaveLoad =>
        CurrentMode == Mode.LiveSpectrum &&
        plotModelFactory.EffectiveLiveAnalysisMode.IsSpatialAverageCapture();

    /// <summary>One place decides Save availability, asking both owners; call on anything that changes either answer.</summary>
    private void RefreshSaveAvailability() =>
        commandController.SetSaveAvailable(
            LiveCaptureOwnsSaveLoad
                ? liveSpectrumController.HasCaptureToSave
                : analyzerDocument.HasResult);

    /// <summary>Opens a stored capture in its own mode; false when not a capture (IR loader takes it).</summary>
    private async Task<bool> TryOpenLiveCaptureAsync(string path)
    {
        if (!LiveCaptureDocument.TryLoad(path, out LiveCaptureDocument document))
        {
            return false;
        }

        await SelectModeAsync(ModeTab.LiveSpectrum);
        if (liveSpectrumOptions.AnalysisMode != document.Recipe.AnalysisMode)
        {
            liveSpectrumOptions.AnalysisMode = document.Recipe.AnalysisMode;
            SaveMeasurementSettings();
            await ApplyMeasurementConfigurationToControllersAsync();
            // Discard before showing: discarding clears the loaded capture too.
            liveSpectrumController.DiscardCapturedData();
            dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
                panel => panel.ForceAnalysisMode(document.Recipe.AnalysisMode));
        }

        liveSpectrumController.ShowLoadedCapture(document);
        RefreshLiveCalibrationReadout();
        UpdateLastImpulseResponseDirectory(path);
        RefreshSaveAvailability();
        return true;
    }

    private async Task SaveLiveCaptureAsync()
    {
        // Stop like the record button (harvesting the final accumulation); aborting would lose frames since the last redraw.
        await liveSpectrumController.StopAndHoldAsync();

        LiveCaptureDocument? document = liveSpectrumController.BuildCaptureDocument();
        if (document == null)
        {
            MessageBox.Show(
                this,
                "There is no completed capture to save. Run the analyzer in MMM, walk " +
                "the microphone through the listening area, then stop and save.",
                "Save capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "json",
            Filter = LiveCaptureFilter,
            FileName = $"Resonalyze-MMM-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json",
            InitialDirectory = GetImpulseResponseDialogDirectory(),
            RestoreDirectory = true,
            Title = "Save moving-mic capture"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The file name is the capture's identity (shown by channel attachments).
        document.Title = Path.GetFileNameWithoutExtension(dialog.FileName);
        commandController.FreezeSaveLoad();
        try
        {
            document.Save(dialog.FileName);
            UpdateLastImpulseResponseDirectory(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The capture could not be saved.\n\n{exception.Message}",
                "Save capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            // A blanket enable would leave Save clickable in a mode with nothing to save.
            RefreshSaveAvailability();
            commandController.SetLoadAvailable(true);
        }
    }

    private async Task LoadLiveCaptureAsync()
    {
        await StopLiveCaptureAsync();

        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = MeasurementFileFilter,
            InitialDirectory = GetImpulseResponseDialogDirectory(),
            Multiselect = false,
            RestoreDirectory = true,
            Title = MeasurementFileDialogTitle
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await OpenMeasurementFileAsync(dialog.FileName);
    }
}
