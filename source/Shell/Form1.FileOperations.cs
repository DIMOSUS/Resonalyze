using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Ui.Dialogs;

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
        if (LiveCaptureOwnsSaveLoad)
        {
            await SaveLiveCaptureAsync();
            return;
        }

        if (expSweepMeasurement.HasImpulseResponse && !expSweepMeasurement.InProgress)
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
        if (LiveCaptureOwnsSaveLoad)
        {
            await LoadLiveCaptureAsync();
            return;
        }

        if (!expSweepMeasurement.InProgress)
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
            commandController.SetSaveAvailable(
                expSweepMeasurement.HasImpulseResponse);
            FinalizeMeasurementCommandState();
        }
    }

    // Takes the shared revision and checks it between read and install: a disabled Load button does not stop a VDSP Open in analyzers landing first.
    private async Task LoadImpulseResponseFileAsync(string path)
    {
        long revision = ++measurementActivationRevision;
        ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
        if (revision != measurementActivationRevision)
        {
            return;
        }

        ApplyImpulseResponseFile(file, path);
    }

    private void SelectFrequencyResponseCalibration(string? calibrationId)
    {
        frequencyResponseOptions.CalibrationId = calibrationId;
        IReadOnlyList<MicrophoneCalibrationEntry> entries = CalibrationEntries();
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.SelectCalibration(calibrationId, entries));
    }

    // Split from the read so callers can check their guard in between.
    private void ApplyImpulseResponseFile(ImpulseResponseFile file, string path)
    {
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
            file.TimingReference,
            file.MeasuredLowFrequencyHz,
            file.MeasuredHighFrequencyHz,
            file.MeasuredAtUtc > DateTimeOffset.UnixEpoch
                ? file.MeasuredAtUtc
                : file.SavedAtUtc);
        expSweepMeasurement.RestoreLevelSnapshot(file.GetMeterSnapshot());
        AdoptRestoredResult(
            file.SplCalibration,
            file.MicrophoneCalibration,
            file.ArrayMicrophones?.ToCurves() ?? [],
            file.ProtectiveHighPass is { } entry
                ? new ProtectiveHighPassConfiguration(
                    entry.Kind, entry.FrequencyHz, entry.SlopeDbPerOctave)
                : null);
        ApplyLoadedImpulseResponseState(path);
        sessionTracker.MarkLoadedFile(path, file);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
    }

    // VDSP Open in analyzers: history-backed sources use full entry activation (the tab switch queues after its mode restore);
    // file-backed sources switch first, then load like the Load button.
    private async Task OpenVirtualDspSourceInAnalyzersAsync(
        Guid? historyEntryId, string? filePath)
    {
        if (expSweepMeasurement.InProgress)
        {
            return;
        }

        long revision = ++measurementActivationRevision;

        // The entry is tried, not trusted: its file may be gone while VDSP handed a relocated path; fall through on Unavailable.
        if (historyEntryId is { } entryId &&
            measurementHistoryService.FindById(entryId) != null)
        {
            switch (await ActivateHistoryEntryAsync(entryId, revision))
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
            // Read, check, install: a later jump may have landed meanwhile.
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(filePath);
            if (revision != measurementActivationRevision)
            {
                return;
            }

            ApplyImpulseResponseFile(file, filePath);
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

    // REW's text export is the only REW route stating sample 0's time, so a loopback-referenced sweep lands on our absolute base.
    // Undecidable facts are reported, not assumed. No coherence, level snapshot or SPL anchor; the IR is uncalibrated.
    private async Task ImportRewImpulseResponseAsync(string path)
    {
        RewImpulseResponseTextFile file;
        RewImportTimingPlan plan;
        // Claimed before the read so a sweep cannot start meanwhile; released before the redraw (busy draws nothing) and the modal notice.
        using (expSweepMeasurement.Claim())
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

            // Asked while the claim is held, so no sweep starts during the dialog.
            if (!TryPlanRewImportTiming(file, out plan))
            {
                return;
            }

            double[] samples = file.Samples;
            double[] referenced = await Task.Run(
                () => file.ToLoopbackReferencedImpulseResponse(plan.OffsetSeconds));
            // Missing band is tolerated; the fallback is reported in the notes.
            double lowHz = file.LowFrequencyHz ?? DefaultImportedLowFrequencyHz;
            double highHz = file.HighFrequencyHz ?? (file.SampleRate / 2.0);
            // REW keeps the IR shorter than the sweep, and harmonic geometry is keyed to the sweep length.
            double sweepSeconds =
                (file.SweepLengthSamples ?? samples.Length) / (double)file.SampleRate;
            expSweepMeasurement.RestoreImpulseResponse(
                lowHz,
                highHz,
                file.SampleRate,
                ImportedBitDepth,
                sweepSeconds,
                PlaybackChannel.Mono,
                ToComplex(samples),
                PeakIndexOf(samples),
                SweepMeasurementMode.LoopbackTransfer,
                ToComplex(referenced),
                PeakIndexOf(referenced),
                transferCoherence: null,
                averageRunCount: file.SweepCount ?? 1,
                acceptedAverageRunCount: file.SweepCount ?? 1,
                achievedLowFrequencyHz: lowHz,
                achievedHighFrequencyHz: highHz,
                timingReference: plan.Reference);
        }

        // Enters as a measurement, not a file that could be saved back over its source.
        ApplyLoadedImpulseResponseState(path);
        sessionTracker.MarkMeasurementCompleted(expSweepMeasurement);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
        NotifyRewImportDecisions(file, plan);
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

    // An imported measurement looks like a measured one; these notes say how it differs.
    private void NotifyRewImportDecisions(
        RewImpulseResponseTextFile file,
        RewImportTimingPlan plan)
    {
        if (closingInProgress)
        {
            return;
        }

        var notes = new List<string>
        {
            FormattableString.Invariant(
                $"Imported {file.Samples.Length} samples at {file.SampleRate} Hz. The loopback reference sits at sample {file.TimeZeroIndex:0.###} of REW's buffer and is now sample 0 of the transfer response; the fractional part was shifted, not rounded."),
            "REW's sweep exports carry no coherence, no level meters and no SPL calibration, " +
                "and REW applies a microphone calibration to its own curves rather than to the " +
                "impulse response — so this measurement is uncalibrated here, whatever REW showed.",
            FormattableString.Invariant(
                $"The export states no bit depth and no playback channel: {ImportedBitDepth}-bit and Mono were assumed. Neither changes the samples — they describe the sweep this result is filed under."),
            DescribeImportedTiming(file, plan)
        };
        if (file.LowFrequencyHz == null || file.HighFrequencyHz == null)
        {
            notes.Add(FormattableString.Invariant(
                $"The header did not state the swept band, so {DefaultImportedLowFrequencyHz:0.#} Hz to Nyquist was assumed. The harmonic geometry of this measurement follows that band, so set it right if the sweep was narrower."));
        }

        if (file.SweepLengthSamples == null)
        {
            notes.Add(
                "The header did not state the sweep's length, so the impulse response's own " +
                "length stands in for it.");
        }

        MessageBox.Show(
            this,
            string.Join("\r\n\r\n", notes),
            "REW impulse response imported",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string DescribeImportedTiming(
        RewImpulseResponseTextFile file,
        RewImportTimingPlan plan)
    {
        double arrivalMs = plan.ArrivalSamples / file.SampleRate * 1000.0;
        if (plan.Reference == TimingReference.RecordedSweep)
        {
            return FormattableString.Invariant(
                $"The timing offset was left unstated, so this is filed as a recorded sweep: its shape is real and its position is not. Delays within it still mean what they say — a reflection 8 ms after the direct sound is 8 ms — but its arrival cannot be compared with another measurement's. Re-import it with the offset REW was running to place it on this session's time base.");
        }

        string statedAs = plan.OffsetSeconds == 0
            ? "You stated no timing offset"
            : FormattableString.Invariant(
                $"You stated a {plan.OffsetSeconds * 1000.0:0.####} ms timing offset, which was taken back out");
        return FormattableString.Invariant(
            $"{statedAs}, so this measurement is on the session's time base with an arrival of {arrivalMs:0.###} ms. The export itself cannot confirm that: REW folds the offset into the start time and records it nowhere else, so the arrival is true on your word rather than on the file's.");
    }

    // REW's export states no bit depth; this only describes the sweep configuration.
    private const int ImportedBitDepth = 24;

    private const double DefaultImportedLowFrequencyHz = 20.0;

    private static Complex[] ToComplex(double[] samples)
    {
        var values = new Complex[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            values[i] = new Complex(samples[i], 0.0);
        }

        return values;
    }

    private static int PeakIndexOf(double[] samples)
    {
        int peak = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) > Math.Abs(samples[peak]))
            {
                peak = i;
            }
        }

        return peak;
    }

    // Analyzed against the sweep the current settings describe; enters history like a finished sweep.
    private async Task ImportRecordedSweepAsync(string path)
    {
        AudioFileContent recording;
        // Claimed before a decode that can take seconds; released before the redraw.
        using (expSweepMeasurement.Claim())
        {
            recording = await Task.Run(() => RecordedSweepFile.Load(path));
            // Handed over rather than applied first, so a rejected recording leaves the screen alone.
            SweepMeasurementConfiguration configuration =
                measurementSettings.Measurement.BuildConfiguration();
            // Sweep matching picks the channel only when one channel holds it: a reference track copy of the excitation would win
            // and pass every check, so with multiple channels the user is asked.
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
        // An import has no SPL anchor; re-evaluate availability downward.
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
        NotifyImportDecisions(recording);
    }

    // Silent when nothing was decided (mono, no stretch).
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
