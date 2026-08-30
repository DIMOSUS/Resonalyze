namespace Resonalyze;

// Save and Load in MMM act on the mode's OWN measurement — the spatial average the
// analyzer has been integrating — not on the impulse response the rest of the app
// carries. The two are different measurements of different things, and a moving-mic
// pass has nowhere else to be stored.
//
// The routing is deliberately narrow: MMM only, not the whole Live Spectrum mode. A
// document describes a band-power (dB SPL) capture, which is what a spatial average
// is defined on; a relative RTA or a transfer function would need fields this format
// does not have, and inventing them for a case nobody asked for is how a format ends
// up unable to say what it means.
public partial class Form1
{
    private const string LiveCaptureFilter =
        "Resonalyze moving-mic capture (*.json)|*.json|All files (*.*)|*.*";

    // Shared by both Load buttons, so either can open either kind of measurement.
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

    /// <summary>
    /// Enables Save for whichever measurement currently owns the button.
    /// </summary>
    /// <remarks>
    /// Routing the CLICK was not enough. The button's availability was owned solely by
    /// the impulse-response lifecycle, so on a fresh session — the normal way into a
    /// moving-mic pass — Save stayed frozen and the routing could never fire. One
    /// place decides it now, and both owners are asked; anything that changes either
    /// answer (a mode switch, an analyzer start or stop, an analysis-mode apply) calls
    /// here.
    /// </remarks>
    private void RefreshSaveAvailability() =>
        commandController.SetSaveAvailable(
            LiveCaptureOwnsSaveLoad
                ? liveSpectrumController.HasCaptureToSave
                : expSweepMeasurement.HasImpulseResponse);

    /// <summary>
    /// Opens <paramref name="path"/> as a stored capture when that is what it is,
    /// switching the application to the mode it belongs to first. Returns false when
    /// the file is not a capture, leaving it to the impulse-response loader.
    /// </summary>
    /// <remarks>
    /// The shared Load button used to refuse a capture with "Unsupported file format",
    /// which is true and useless: the file says exactly what it is and which analysis
    /// mode produced it, so the application can simply go there. The analysis mode
    /// follows the document rather than being left as it was — otherwise Save and Load
    /// would still belong to the impulse response while a capture sits on the plot.
    /// </remarks>
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
            // The acquisition parameters just changed, so the previous setup's curve
            // must go — and it must go BEFORE the capture is shown, since discarding
            // clears the loaded one too.
            liveSpectrumController.DiscardCapturedData();
            dockedModeSettingsHost.InvokeIfOpen<Options.LiveSpectrumOpt>(
                panel => panel.ForceAnalysisMode(document.Recipe.AnalysisMode));
        }

        liveSpectrumController.ShowLoadedCapture(document);
        // The plot now holds someone else's capture, corrected by whatever they used.
        RefreshLiveCalibrationReadout();
        UpdateLastImpulseResponseDirectory(path);
        RefreshSaveAvailability();
        return true;
    }

    private async Task SaveLiveCaptureAsync()
    {
        // A capture is a finished measurement, so the analyzer stops first — and stops
        // the way the record button does, harvesting the final accumulation. Aborting
        // instead would leave every frame since the last redraw out of the file.
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

        // The file name is the capture's identity for the rest of the workflow — it
        // is what a channel attachment will show — so it becomes the title rather
        // than leaving one more thing to type.
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
            // Ask who owns the button rather than enabling it outright: a blanket
            // enable leaves Save clickable in a mode that has nothing to save.
            RefreshSaveAvailability();
            commandController.SetLoadAvailable(true);
        }
    }

    private async Task LoadLiveCaptureAsync()
    {
        await StopLiveCaptureAsync();

        // The same choice the main Load button offers. Which measurement a file holds
        // is the file's business, not the button's: refusing an impulse response here
        // told the user the format was wrong when only the mode was.
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

        // Whichever it is: a capture stays here, an impulse response moves the
        // application to Frequency Response — the mirror of a capture bringing it
        // here.
        await OpenMeasurementFileAsync(dialog.FileName);
    }
}
