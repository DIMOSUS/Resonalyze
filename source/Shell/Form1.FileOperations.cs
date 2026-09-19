using Resonalyze.Dsp;
using Resonalyze.Integration.Rew;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
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
        if (LiveCaptureOwnsSaveLoad)
        {
            await SaveLiveCaptureAsync();
            return;
        }

        if (analyzerDocument.Result is { } result && !analyzerDocument.IsBusy)
        {
            await StopLiveCaptureAsync();

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
                ImpulseResponseFile file = ImpulseResponseFile.From(result);
                await file.SaveAsync(dialog.FileName);
                sessionTracker.MarkSavedFile(dialog.FileName, file, result);
                analyzerDocument.Rename(dialog.FileName);
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
        if (LiveCaptureOwnsSaveLoad)
        {
            await LoadLiveCaptureAsync();
            return;
        }

        if (!analyzerDocument.IsBusy)
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

    /// <summary>A stored capture goes to its own mode, anything else to the impulse-response side. Shared by both Load buttons and file drop.</summary>
    /// <remarks>A file claiming to be a capture that fails to parse is reported as a broken capture, not misdiagnosed by the IR loader.</remarks>
    private async Task OpenMeasurementFileAsync(string path)
    {
        // Needed only by the drop path; the buttons already stopped the analyzer.
        await StopLiveCaptureAsync();

        try
        {
            if (await TryOpenLiveCaptureAsync(path))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The capture could not be loaded.\r\n\r\n{exception.Message}",
                "Load failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        await LoadImpulseResponseLikeAsync(path);
    }

    private async Task LoadImpulseResponseLikeAsync(string path)
    {
        // An IR has nowhere to show in the live analyzer or source-reading tools; ask the mode, since a drop can arrive in any mode.
        if (!GetActiveModeDescriptor().ShowsLoadedMeasurement)
        {
            await SelectModeAsync(ModeTab.Frequency);
        }

        string extension = Path.GetExtension(path);
        bool importRecording = string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase);
        bool importRewExport = string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
        try
        {
            if (importRecording)
            {
                await ImportRecordedSweepAsync(path);
            }
            else if (importRewExport)
            {
                await ImportRewImpulseResponseAsync(path);
            }
            else
            {
                await LoadImpulseResponseFileAsync(path);
            }
        }
        catch (Exception exception)
        {
            string failure = importRecording
                ? "Failed to import the recorded sweep."
                : importRewExport
                    ? "Failed to import the REW impulse response."
                    : "Failed to load the impulse response.";
            MessageBox.Show(
                this,
                $"{failure}\r\n\r\n{exception.Message}",
                importRecording || importRewExport ? "Import failed" : "Load failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            commandController.SetSaveAvailable(analyzerDocument.HasResult);
            FinalizeMeasurementCommandState();
        }
    }

    // Takes its request before the read: a disabled Load button does not stop a VDSP Open in analyzers landing first.
    private async Task LoadImpulseResponseFileAsync(string path)
    {
        if (analyzerDocument.TryBegin() is not { } request)
        {
            return;
        }

        ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
        ApplyImpulseResponseFile(request, file, path);
    }

    private void SelectFrequencyResponseCalibration(string? calibrationId)
    {
        viewSettings.FrequencyResponse.CalibrationId = calibrationId;
        IReadOnlyList<MicrophoneCalibrationEntry> entries = CalibrationEntries();
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.SelectCalibration(calibrationId, entries));
    }

    private void ApplyImpulseResponseFile(AnalyzerDocument.Request request, ImpulseResponseFile file, string path)
    {
        MeasurementResult result = file.ToResult();
        if (!ShowLoadedMeasurement(request, result, path, fromFile: true))
        {
            return;
        }

        sessionTracker.MarkLoadedFile(path, file, result);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
    }

    // VDSP Open in analyzers: history-backed sources use full entry activation (the tab switch queues after its mode restore);
    // file-backed sources switch first, then load like the Load button.
    private async Task OpenVirtualDspSourceInAnalyzersAsync(
        Guid? historyEntryId, string? filePath)
    {
        if (analyzerDocument.TryBegin() is not { } request)
        {
            return;
        }

        // The entry is tried, not trusted: its file may be gone while VDSP handed a relocated path; fall through on Unavailable.
        if (historyEntryId is { } entryId &&
            measurementHistoryService.FindById(entryId) != null)
        {
            switch (await ActivateHistoryEntryAsync(entryId, request))
            {
                case HistoryActivation.Landed:
                    await SelectModeAsync(ModeTab.Frequency);
                    return;

                // A newer activation is landing; falling back to the file would overwrite it.
                case HistoryActivation.Superseded:
                    return;
            }
        }

        if (filePath == null)
        {
            return;
        }

        await StopLiveCaptureAsync();

        await SelectModeAsync(ModeTab.Frequency);
        commandController.SetSaveAvailable(false);
        commandController.SetLoadAvailable(false);
        try
        {
            // A later jump may have landed meanwhile; the request then installs nothing.
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(filePath);
            ApplyImpulseResponseFile(request, file, filePath);
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
            commandController.SetSaveAvailable(analyzerDocument.HasResult);
            FinalizeMeasurementCommandState();
        }
    }

    // REW's text export is the only REW route stating sample 0's time, so a loopback-referenced sweep lands on our absolute base.
    // Undecidable facts are reported, not assumed. No coherence, level snapshot or SPL anchor; the IR is uncalibrated.
    private async Task ImportRewImpulseResponseAsync(string path)
    {
        RewImpulseResponseTextFile file;
        RewImportTimingPlan plan;
        EssSweepRateEstimate? sweepRate;
        MeasurementResult result;
        if (analyzerDocument.TryAcquire() is not { } hold)
        {
            return;
        }

        // Held from the read so a sweep cannot start meanwhile; Install releases it before the redraw (busy draws nothing).
        using (hold)
        {
            string text = await File.ReadAllTextAsync(path);
            file = await Task.Run(
                () => RewImpulseResponseTextFile.Parse(text));
            // The sweep is generated at the configured rate; another rate describes a different signal.
            int configuredSampleRate =
                measurementSettings.Measurement.BuildConfiguration().Signal.SampleRate;
            if (file.SampleRate != configuredSampleRate)
            {
                throw new InvalidOperationException(
                    $"This export is {file.SampleRate} Hz while the measurement is configured " +
                    $"for {configuredSampleRate} Hz. Set the sample rate in Measurement Options " +
                    "to match the file, then import it again.");
            }

            if (!file.IsLoopbackReferenced)
            {
                // Without a loopback reference the arrival time is meaningless and would be summed with real ones.
                throw new InvalidOperationException(
                    "This export was not measured against a loopback timing reference" +
                    (string.IsNullOrWhiteSpace(file.Excitation)
                        ? string.Empty
                        : $" (REW says: \u201c{file.Excitation}\u201d)") +
                    ". Its shape is real, but nothing ties its zero to anything outside its " +
                    "own measurement, so it cannot be placed on this session's time base. " +
                    "Measure it in REW with a loopback as the timing reference to import it.");
            }

            // Asked while the document is held, so no sweep starts during the dialog.
            if (!TryPlanRewImportTiming(file, out plan))
            {
                return;
            }

            double[] samples = file.Samples;
            double[] referenced = await Task.Run(
                () => file.ToLoopbackReferencedImpulseResponse(plan.OffsetSeconds));
            // The header's band and length do not give REW's sweep rate (H2 at -174.9 ms predicted, -138.6 ms measured).
            sweepRate = await Task.Run(() => RewMeasurementImport.EstimateSweepRate(samples, file.SampleRate));
            if (file.SweepLevelDbfs is { } levelDbfs && levelDbfs <= 0)
            {
                RewMeasurementImport.TakeLevelOut(samples, levelDbfs);
                RewMeasurementImport.TakeLevelOut(referenced, levelDbfs);
            }

            double lowHz = file.LowFrequencyHz ?? RewMeasurementImport.FallbackLowFrequencyHz;
            double highHz = Math.Min(file.HighFrequencyHz ?? double.MaxValue, file.SampleRate / 2.0);
            result = RewMeasurementImport.ToResult(
                samples,
                referenced,
                file.SampleRate,
                lowHz,
                highHz,
                RewMeasurementImport.SweepLengthSamples(
                    sweepRate, lowHz, highHz, file.SampleRate, file.SweepLengthSamples ?? samples.Length),
                file.SweepCount ?? 1,
                plan.Reference);
        }

        if (FinishRewImport(hold, result, path, fromFile: true))
        {
            NotifyImportDecisions("REW impulse response imported", RewImportNotes.Describe(file, plan, sweepRate));
        }
    }

    // Enters as a measurement, not a file that could be saved back over its source.
    private bool FinishRewImport(
        AnalyzerDocument.Request request, MeasurementResult result, string sourceName, bool fromFile)
    {
        if (!ShowLoadedMeasurement(request, result, sourceName, fromFile))
        {
            return false;
        }

        sessionTracker.MarkMeasurementCompleted(result);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
        return true;
    }

    // False means cancelled: not an error, no notice.
    private bool TryPlanRewImportTiming(
        RewImpulseResponseTextFile file,
        out RewImportTimingPlan plan)
    {
        using var dialog = new RewTimingOffsetDialog(
            file.ImpliedArrivalSamples / file.SampleRate * 1000.0);
        RewTimingOffsetChoice choice = dialog.ShowDialog(this);
        if (choice == RewTimingOffsetChoice.Cancel)
        {
            plan = null!;
            return false;
        }

        double? statedOffsetSeconds =
            choice == RewTimingOffsetChoice.Stated ? dialog.OffsetSeconds : null;
        if (!RewImportTiming.TryResolve(
                statedOffsetSeconds,
                file.TimeZeroIndex,
                file.PeakIndex,
                file.Samples.Length,
                file.SampleRate,
                out RewImportTimingPlan? resolved,
                out string? problem) ||
            resolved == null)
        {
            throw new InvalidOperationException(
                $"This REW impulse-response export cannot be imported — {problem}.");
        }

        plan = resolved;
        return true;
    }

    // An imported measurement looks like a measured one; the notes say how it differs. Silent when nothing was decided.
    private void NotifyImportDecisions(string title, IReadOnlyList<string> notes)
    {
        if (closingInProgress || notes.Count == 0)
        {
            return;
        }

        MessageBox.Show(
            this,
            string.Join("\r\n\r\n", notes),
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // Analyzed against the sweep the current settings describe; enters history like a finished sweep.
    private async Task ImportRecordedSweepAsync(string path)
    {
        AudioFileContent recording;
        RecordedSweepImport import;
        if (analyzerDocument.TryAcquire() is not { } hold)
        {
            return;
        }

        // Held before a decode that can take seconds; Install releases it before the redraw.
        using (hold)
        {
            recording = await Task.Run(() => RecordedSweepFile.Load(path));
            // Handed over rather than applied first, so a rejected recording leaves the screen alone.
            SweepMeasurementConfiguration configuration =
                measurementSettings.Measurement.BuildConfiguration();
            // Sweep matching picks the channel only when one channel holds it: a reference track copy of the excitation would win
            // and pass every check, so when another channel matches comparably (IsAmbiguous) the user is asked.
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

            import = await Task.Run(() => ExpSweepMeasurement.ImportRecordedSweep(
                configuration,
                recording.Channels,
                recording.SampleRate,
                channel));
        }

        // New session during the decode supersedes it.
        if (!ShowLoadedMeasurement(hold, import.Result, path, fromFile: true))
        {
            return;
        }

        sessionTracker.MarkMeasurementCompleted(import.Result);
        // An import has no SPL anchor; re-evaluate availability downward.
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
        NotifyImportDecisions("Recorded sweep", RecordedSweepFile.DescribeImport(recording, import));
    }
}
