using System.Numerics;
using Resonalyze.Dsp;

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
                    "Measurements (*.json;*.wav;*.txt)|*.json;*.wav;*.txt|" +
                    "Resonalyze impulse response (*.json)|*.json|" +
                    "Recorded sweep (*.wav)|*.wav|" +
                    "REW impulse response export (*.txt)|*.txt|" +
                    "All files (*.*)|*.*",
                InitialDirectory = GetImpulseResponseDialogDirectory(),
                Multiselect = false,
                RestoreDirectory = true,
                Title = "Load impulse response, recorded sweep or REW export"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string extension = Path.GetExtension(dialog.FileName);
            bool importRecording = string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase);
            bool importRewExport = string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
            commandController.SetSaveAvailable(false);
            commandController.SetLoadAvailable(false);
            try
            {
                if (importRecording)
                {
                    await ImportRecordedSweepAsync(dialog.FileName);
                }
                else if (importRewExport)
                {
                    await ImportRewImpulseResponseAsync(dialog.FileName);
                }
                else
                {
                    await LoadImpulseResponseFileAsync(dialog.FileName);
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

    // A measurement made in REW, brought over on the time base it was measured on.
    //
    // REW's text export is the only one of its routes that states the time of sample 0,
    // so a sweep it measured against a loopback can be placed on the same absolute base
    // a measurement taken here sits on — which is the whole point: two programs' results
    // are comparable only if their zeros mean the same thing. The reader does the
    // reading and the re-referencing; what is decided here is what the format cannot
    // say, and every one of those decisions is reported rather than assumed silently.
    //
    // Absent by nature, not by omission: a REW sweep export carries no coherence, no
    // level snapshot and no SPL anchor, and the microphone calibration REW applies to
    // its own curves is not in the impulse response — an imported IR is uncalibrated.
    private async Task ImportRewImpulseResponseAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        RewImpulseResponseTextFile file = await Task.Run(
            () => RewImpulseResponseTextFile.Parse(text));
        if (!file.IsLoopbackReferenced)
        {
            // The shape is real; the position is its own. Placing it here would give it
            // an arrival time that means nothing and would be summed with real ones.
            throw new InvalidOperationException(
                "This export was not measured against a loopback timing reference" +
                (string.IsNullOrWhiteSpace(file.Excitation)
                    ? string.Empty
                    : $" (REW says: \u201c{file.Excitation}\u201d)") +
                ". Its shape is real, but nothing ties its zero to anything outside its " +
                "own measurement, so it cannot be placed on this session's time base. " +
                "Measure it in REW with a loopback as the timing reference to import it.");
        }

        double[] samples = file.Samples;
        double[] referenced = await Task.Run(file.ToLoopbackReferencedImpulseResponse);
        // The band REW swept. A header without it is not worth refusing the file over,
        // but the fallback is a guess and says so in the notes below.
        double lowHz = file.LowFrequencyHz ?? DefaultImportedLowFrequencyHz;
        double highHz = file.HighFrequencyHz ?? (file.SampleRate / 2.0);
        // The sweep, not the impulse response: REW keeps the IR shorter than the sweep
        // that produced it, and the harmonic geometry is keyed to the sweep's length.
        double sweepSeconds =
            (file.SweepLengthSamples ?? samples.Length) / (double)file.SampleRate;
        expSweepMeasurement.RestoreImpulseResponse(
            lowHz,
            highHz,
            file.SampleRate,
            ImportedBitDepth,
            sweepSeconds,
            // REW does not say which output it played through, and guessing a side
            // would put a channel name on a measurement that never carried one.
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
            timingReference: TimingReference.SynchronizedLoopback);
        // Like a recorded sweep and unlike a loaded file: nothing on disk holds this
        // impulse response in this program's terms, so it enters the session as a
        // measurement rather than as a file that could be saved back over its source.
        ApplyLoadedImpulseResponseState(path);
        sessionTracker.MarkMeasurementCompleted(expSweepMeasurement);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
        NotifyRewImportDecisions(file);
    }

    // What REW's export could not say, and what was assumed in its place. Always worth
    // showing: an imported measurement looks exactly like a measured one on screen, and
    // these are the ways in which it is not.
    private void NotifyRewImportDecisions(RewImpulseResponseTextFile file)
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
                $"The export states no bit depth and no playback channel: {ImportedBitDepth}-bit and Mono were assumed. Neither changes the samples — they describe the sweep this result is filed under.")
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

    // REW's export states no bit depth: it is a text file of fractions of full scale,
    // and by the time it is written the capture depth has left no trace. The value is
    // carried only as the configuration's description of the sweep.
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
