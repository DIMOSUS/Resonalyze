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
        // In MMM the buttons belong to that mode's own measurement, not to the
        // impulse response (see Form1.LiveCapture).
        if (LiveCaptureOwnsSaveLoad)
        {
            await SaveLiveCaptureAsync();
            return;
        }

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
        if (LiveCaptureOwnsSaveLoad)
        {
            await LoadLiveCaptureAsync();
            return;
        }

        if (!expSweepMeasurement.InProgress)
        {
            if (liveSpectrumController.InProgress || liveSpectrumController.TimerEnabled)
            {
                await liveSpectrumController.AbortAsync();
            }

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

            // A stored capture knows which mode it belongs to, so opening one here
            // takes the application there rather than refusing it for not being an
            // impulse response. A file that CLAIMS to be a capture and then fails to
            // parse is reported as the broken capture it is, not handed on to the
            // impulse-response loader to be misdiagnosed as a bad format.
            try
            {
                if (await TryOpenLiveCaptureAsync(dialog.FileName))
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

            await LoadImpulseResponseLikeAsync(dialog.FileName);
        }
    }

    /// <summary>
    /// Loads whatever is NOT a capture: a Resonalyze impulse response, a recorded
    /// sweep or a REW export, dispatched by extension.
    /// </summary>
    /// <remarks>
    /// Shared by both Load buttons. The file decides which measurement it is, so
    /// both have to be able to open both kinds; without this the moving-mic button
    /// refused an impulse response and the main one refused a capture, each telling
    /// the user the file was the wrong format when it was only the wrong button.
    /// </remarks>
    private async Task LoadImpulseResponseLikeAsync(string path)
    {
        // An impulse response has nowhere to be shown in a live capture mode, so go
        // where it belongs first — the mirror of a capture taking the application to
        // Live Spectrum.
        if (CurrentMode == Mode.LiveSpectrum)
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

    // A plain Load is a request to make a measurement current like any other, so it
    // takes the shared revision and checks it between reading and installing. The
    // Load button being disabled meanwhile is not the same guard: it stops a second
    // Load, not a mode switch to Virtual DSP and an Open in analyzers, which would
    // otherwise land first and then be overwritten by this file arriving late.
    //
    // (The WAV import needs none of this: it holds an ExpSweepMeasurement claim for
    // its whole decode, and every other path refuses to start while the measurement
    // is busy — mutual exclusion rather than a race resolved afterwards.)
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

    /// <summary>
    /// Reads the loaded measurement through the calibration it was measured with.
    /// </summary>
    /// <remarks>
    /// The selection is left alone when a local calibration already holds the same
    /// curve — your own file on your own machine — so the ordinary case shows no
    /// change at all. It moves only when the file's curve is one this machine does
    /// not have, which is exactly when leaving it alone would draw someone else's
    /// measurement through your microphone's correction and say nothing.
    /// </remarks>
    private void AdoptFileCalibration(VirtualCrossoverCalibrationSettings? calibration)
    {
        string? chosen = FileCalibrationSelection.Choose(
            calibration,
            frequencyResponseOptions.CalibrationId,
            microphoneCalibration.GetEntries(),
            microphoneCalibration.Get);
        if (chosen == null)
        {
            return;
        }

        frequencyResponseOptions.CalibrationId = chosen;
        IReadOnlyList<MicrophoneCalibrationEntry> entries = CalibrationEntries();
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.SelectCalibration(chosen, entries));
    }

    // The install half, split from the read so a caller that must not land a stale
    // result can check its own guard between the two — reading a large file takes
    // long enough for a newer request to overtake it.
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
            file.TimingReference);
        expSweepMeasurement.RestoreLevelSnapshot(file.GetMeterSnapshot());
        // The loaded file's own calibration is this result's snapshot (what
        // it was measured under), so it can be shown in dB SPL. The configured
        // calibration for the next new run is left untouched. Its capture
        // identity stands in for the result's input, so re-saving the file
        // validates the anchor against the input it was measured on — not the
        // app's current device — and keeps it.
        expSweepMeasurement.MeasurementSplCalibration = file.SplCalibration;
        // The same reasoning for the microphone calibration and the array: they
        // describe the result that was loaded, not the next run. The impulse
        // response is raw, so without the calibration a recipient would draw a
        // different curve from the author's and nothing would say why.
        expSweepMeasurement.MeasurementMicrophoneCalibration = file.MicrophoneCalibration;
        expSweepMeasurement.ArrayMicrophones =
            file.ArrayMicrophones?.ToCurves() ?? [];
        AdoptFileCalibration(file.MicrophoneCalibration);
        // Whatever the file knows about the protective high-pass travels with it,
        // including "nothing": the app's own setting describes the next run, not the
        // response just loaded.
        expSweepMeasurement.MeasurementProtectiveHighPass =
            file.ProtectiveHighPass is { } entry
                ? new ProtectiveHighPassConfiguration(
                    entry.Kind, entry.FrequencyHz, entry.SlopeDbPerOctave)
                : null;
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

        long revision = ++measurementActivationRevision;

        // The entry is TRIED, not trusted: it can still be listed while the file
        // behind it is gone, in which case the restore lands nothing — and Virtual
        // DSP may well have relocated that measurement and handed a working path
        // alongside. Falling through on Unavailable is what keeps the jump from
        // leaving the previous measurement on screen and calling it done.
        if (historyEntryId is { } entryId &&
            measurementHistoryService.FindById(entryId) != null)
        {
            switch (await ActivateHistoryEntryAsync(entryId, revision))
            {
                case HistoryActivation.Landed:
                    await SelectModeAsync(ModeTab.Frequency);
                    return;

                // A newer activation — another channel's jump, or the History
                // window's own — is already landing. Falling back to this
                // channel's file would race it and could overwrite the newer
                // measurement with this older one; the newest request wins,
                // exactly as it does inside the activation itself.
                case HistoryActivation.Superseded:
                    return;
            }
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
            // Read, then check, then install: a jump started later may already have
            // landed its measurement while this file was still being read, and
            // installing this one now would put the older channel back on screen.
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
        RewImpulseResponseTextFile file;
        RewImportTimingPlan plan;
        // Claimed BEFORE the read: parsing a few hundred thousand samples and
        // running the fractional shift takes long enough for the record button to
        // start a sweep in between, and this import would then arrive on top of it.
        // Released once the result is published and before the redraw below — a busy
        // measurement draws no curves, and the notice that follows is modal, so a
        // claim held to the end of the method would outlast the dialog on screen.
        using (expSweepMeasurement.Claim())
        {
            string text = await File.ReadAllTextAsync(path);
            file = await Task.Run(
                () => RewImpulseResponseTextFile.Parse(text));
            // The sweep this result would be filed under is generated at the configured
            // rate; a file at another rate is not describing the same signal, and the
            // state applied afterwards would report a rate the measurement does not have.
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

            // The one fact the format cannot carry, asked for rather than guessed at.
            // The question is put while the claim is still held: nothing has been
            // published yet, so there is nothing on screen waiting to be redrawn, and
            // the claim is exactly what stops a sweep starting while the dialog is up.
            if (!TryPlanRewImportTiming(file, out plan))
            {
                return;
            }

            double[] samples = file.Samples;
            double[] referenced = await Task.Run(
                () => file.ToLoopbackReferencedImpulseResponse(plan.OffsetSeconds));
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
                timingReference: plan.Reference);
        }

        // Like a recorded sweep and unlike a loaded file: nothing on disk holds this
        // impulse response in this program's terms, so it enters the session as a
        // measurement rather than as a file that could be saved back over its source.
        ApplyLoadedImpulseResponseState(path);
        sessionTracker.MarkMeasurementCompleted(expSweepMeasurement);
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());
        NotifyRewImportDecisions(file, plan);
    }

    // Puts the timing-offset question and turns the answer into a plan, or explains why
    // the answer cannot be true of this file. False means the user cancelled — which is
    // not an error and gets no notice.
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

    // What REW's export could not say, and what was assumed in its place. Always worth
    // showing: an imported measurement looks exactly like a measured one on screen, and
    // these are the ways in which it is not.
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

    // What the import was told about time, and what that makes the arrival worth. Said
    // in the notice because an imported measurement looks like a measured one on screen
    // and this is the difference between a delay and a number that resembles one.
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
