namespace Resonalyze;

public partial class Form1
{
    private void SetImpulseResponseSourceFile(string? path)
    {
        plotModelFactory.SetImpulseResponseFileName(path);
    }

    private string GetImpulseResponseDialogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(measurementSettings.LastImpulseResponseDirectory) &&
            Directory.Exists(measurementSettings.LastImpulseResponseDirectory))
        {
            return measurementSettings.LastImpulseResponseDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void UpdateLastImpulseResponseDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        measurementSettings.LastImpulseResponseDirectory = directory;
        ScheduleMeasurementSettingsSave();
    }

    private async void buttonSave_Click(object sender, EventArgs e)
    {
        if (expSweepMeasurement.HasImpulseResponse && !expSweepMeasurement.InProgress)
        {
            if (liveSpectrumController.InProgress || liveSpectrumController.TimerEnabled)
            {
                await liveSpectrumController.AbortAsync();
            }

            using var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = "json",
                Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"Resonalyze-IR-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json",
                InitialDirectory = GetImpulseResponseDialogDirectory(),
                RestoreDirectory = true,
                Title = "Save impulse response"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            commandController.FreezeSaveLoad();
            try
            {
                ImpulseResponseFile file =
                    ImpulseResponseFile.Capture(expSweepMeasurement);
                await file.SaveAsync(dialog.FileName);
                sessionTracker.MarkSavedFile(dialog.FileName, file);
                SetImpulseResponseSourceFile(dialog.FileName);
                UpdateLastImpulseResponseDirectory(dialog.FileName);
                RefreshCurrentModePlot();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    $"Failed to save the impulse response.\r\n\r\n{exception.Message}",
                    "Save failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                commandController.SetSaveAvailable(true);
                commandController.SetLoadAvailable(true);
            }
        }
    }

    private async void buttonLoad_Click(object sender, EventArgs e)
    {
        if (!expSweepMeasurement.InProgress)
        {
            if (liveSpectrumController.InProgress || liveSpectrumController.TimerEnabled)
            {
                await liveSpectrumController.AbortAsync();
            }

            using var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter =
                    "Measurements (*.json;*.wav)|*.json;*.wav|" +
                    "Resonalyze impulse response (*.json)|*.json|" +
                    "Recorded sweep (*.wav)|*.wav|" +
                    "All files (*.*)|*.*",
                InitialDirectory = GetImpulseResponseDialogDirectory(),
                Multiselect = false,
                RestoreDirectory = true,
                Title = "Load impulse response or recorded sweep"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            bool importRecording = string.Equals(
                Path.GetExtension(dialog.FileName),
                ".wav",
                StringComparison.OrdinalIgnoreCase);
            commandController.SetSaveAvailable(false);
            commandController.SetLoadAvailable(false);
            try
            {
                if (importRecording)
                {
                    await ImportRecordedSweepAsync(dialog.FileName);
                }
                else
                {
                    await LoadImpulseResponseFileAsync(dialog.FileName);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    importRecording
                        ? $"Failed to import the recorded sweep.\r\n\r\n{exception.Message}"
                        : $"Failed to load the impulse response.\r\n\r\n{exception.Message}",
                    importRecording ? "Import failed" : "Load failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                commandController.SetSaveAvailable(
                    expSweepMeasurement.HasImpulseResponse);
                FinalizeMeasurementCommandState();
            }
        }
    }

    private async Task LoadImpulseResponseFileAsync(string path)
    {
        ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
        (double restoredLowHz, double restoredHighHz) = file.ResolveSweepBand();
        (double achievedLowHz, double achievedHighHz) = file.ResolveAchievedSweepBand();
        expSweepMeasurement.RestoreImpulseResponse(
            restoredLowHz,
            restoredHighHz,
            file.SampleRate,
            file.Bits,
            file.SweepDurationSeconds,
            file.PlayChannel,
            file.GetSweepDeconvolutionImpulseResponse(),
            file.SweepDeconvolutionPeakIndex,
            file.MeasurementMode,
            file.GetTransferImpulseResponse(),
            file.TransferPeakIndex,
            file.TransferCoherence,
            file.AverageRunCount,
            file.AcceptedAverageRunCount,
            achievedLowHz,
            achievedHighHz,
            file.TimingReference);
        expSweepMeasurement.RestoreLevelSnapshot(file.GetMeterSnapshot());
        // The loaded file's own calibration is this result's snapshot (what
        // it was measured under), so it can be shown in dB SPL. The configured
        // calibration for the next new run is left untouched. Its capture
        // identity stands in for the result's input, so re-saving the file
        // validates the anchor against the input it was measured on — not the
        // app's current device — and keeps it.
        expSweepMeasurement.MeasurementSplCalibration = file.SplCalibration;
        expSweepMeasurement.MeasurementInput = file.SplCalibration?.CaptureIdentity;
        ApplyLoadedImpulseResponseState(path);
        sessionTracker.MarkLoadedFile(path, file);
        // A loaded file carries its own SPL calibration and loopback level, so
        // an open Frequency Response panel can now show dB SPL as fully
        // available (not just view-only) for it.
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
    }

    // The Virtual DSP "Open in analyzers" jump: brings one channel side's
    // measurement into the analysis modes and lands on Frequency Response (every
    // analyzer tab reads the same loaded measurement, so one landing tab serves
    // them all). A history-backed source goes through the standard entry
    // activation — the full restore the History window runs, saved working state
    // included — and the tab switch queues after it, because the restore selects
    // the entry's own saved mode on the way. A file-backed source switches first
    // and then loads exactly as the Load button would, ceremony and all.
    private async Task OpenVirtualDspSourceInAnalyzersAsync(
        Guid? historyEntryId, string? filePath)
    {
        if (expSweepMeasurement.InProgress)
        {
            return;
        }

        // The entry is TRIED, not trusted: it can still be listed while the file
        // behind it is gone, in which case the restore lands nothing — and Virtual
        // DSP may well have relocated that measurement and handed a working path
        // alongside. Falling through on failure is what keeps the jump from leaving
        // the previous measurement on screen and calling it done.
        if (historyEntryId is { } entryId &&
            measurementHistoryService.FindById(entryId) != null &&
            await ActivateHistoryEntryAsync(entryId))
        {
            await SelectModeAsync(ModeTab.Frequency);
            return;
        }

        if (filePath == null)
        {
            return;
        }

        if (liveSpectrumController.InProgress || liveSpectrumController.TimerEnabled)
        {
            await liveSpectrumController.AbortAsync();
        }

        await SelectModeAsync(ModeTab.Frequency);
        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
        try
        {
            await LoadImpulseResponseFileAsync(filePath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to load the impulse response.\r\n\r\n{exception.Message}",
                "Load failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            commandController.SetSaveAvailable(expSweepMeasurement.HasImpulseResponse);
            FinalizeMeasurementCommandState();
        }
    }

    // A sweep recorded elsewhere — a phone, a handheld recorder, a DAW — analyzed
    // against the sweep the CURRENT settings describe, which is the same signal
    // the measurement options export as a WAV file. The outcome is a measurement
    // rather than a loaded file: nothing on disk holds this impulse response, so
    // it enters the history the way a finished sweep does.
    private async Task ImportRecordedSweepAsync(string path)
    {
        AudioFileContent recording;
        // Claimed BEFORE the decode, which on a long recording is seconds of its
        // own: the record button gates on the measurement being busy, and without
        // the claim a run started during the decode would finish and then be
        // replaced by the import landing on top of it. Released before the redraw
        // below — a busy measurement draws no curves.
        using (expSweepMeasurement.Claim())
        {
            recording = await Task.Run(() => RecordedSweepFile.Load(path));
            // The current settings decide the excitation, exactly as they would for
            // the next sweep. They are handed over rather than applied first: a
            // rejected recording must leave the measurement on screen alone.
            SweepMeasurementConfiguration configuration =
                measurementSettings.Measurement.BuildConfiguration();
            // Which channel holds the measurement is a question about the sweep,
            // not about loudness — but matching only answers it when ONE channel
            // holds the sweep. A recorder that also wrote the played signal to a
            // reference track put a copy of the excitation in the file, and a copy
            // matches better than any acoustic take: it would win, measure flat,
            // and pass every credibility check on the way. Nothing in the numbers
            // says which track the microphone was on, so that choice is asked for.
            double[] qualities = recording.ChannelCount > 1
                ? await Task.Run(() =>
                    RecordedSweepChannels.Rank(configuration, recording.Channels))
                : [0.0];
            int channel = RecordedSweepChannels.Best(qualities);
            if (RecordedSweepChannels.IsAmbiguous(qualities))
            {
                using var dialog = new Options.RecordedSweepChannelDialog(
                    recording.Channels, qualities);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                channel = dialog.SelectedChannel;
            }

            await Task.Run(() => expSweepMeasurement.ImportRecordedSweep(
                configuration,
                recording.Channels,
                recording.SampleRate,
                channel));
        }

        ApplyLoadedImpulseResponseState(path);
        sessionTracker.MarkMeasurementCompleted(expSweepMeasurement);
        // An import carries no SPL anchor — the recording chain's gain is unknown
        // — so an open panel has to re-evaluate dB SPL availability downward.
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
        NotifyImportDecisions(recording);
    }

    // The decisions the import made on the user's behalf: which channel it
    // measured, and whether it had to stretch the reference to match the
    // recording. Silent when there was nothing to decide — a mono file that
    // needed no correction says nothing at all.
    private void NotifyImportDecisions(AudioFileContent recording)
    {
        if (closingInProgress)
        {
            return;
        }

        var notes = new List<string>();
        if (recording.ChannelCount > 1)
        {
            int chosen = expSweepMeasurement.ImportedChannelIndex;
            AudioChannelLevel level = RecordedLevelMetering.MeasureSamples(
                recording.Channels[chosen]);
            notes.Add(FormattableString.Invariant(
                $"The recording has {recording.ChannelCount} channels; {RecordedSweepFile.DescribeChannel(chosen, recording.ChannelCount)} was measured — {level.RmsDbFs:0.0} dBFS RMS, peak {level.PeakDbFs:0.0} dBFS."));
        }
        if (expSweepMeasurement.ImportedTimeScalePpm is { } scalePpm)
        {
            notes.Add(FormattableString.Invariant(
                $"The recording ran {Math.Abs(scalePpm):0} ppm {(scalePpm > 0 ? "slower" : "faster")} than the configured sweep, and the reference was rebuilt to match. That is what two devices with their own clocks do — and what a per-octave time in whole milliseconds cannot always express. Left uncorrected it smears the arrival and the phase at the top of the band."));
        }
        if (notes.Count == 0)
        {
            return;
        }

        MessageBox.Show(
            this,
            string.Join("\r\n\r\n", notes),
            "Recorded sweep",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
