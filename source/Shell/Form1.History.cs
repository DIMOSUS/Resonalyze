using Resonalyze.History;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
    // One token for every request that makes a measurement current (history activation, VDSP Open in analyzers):
    // "newest wins" only holds if all bump and check the same counter.
    private long measurementActivationRevision;

    // Makes each multi-await restore atomic; the revision token then decides which one runs last.
    private readonly SemaphoreSlim historyRestoreGate = new(1, 1);

    private void buttonHistory_Click(object sender, EventArgs e)
    {
        if (dockedHistoryHost.IsOpen)
        {
            dockedHistoryHost.Close();
            return;
        }

        dockedModeSettingsHost.Close();
        dockedMeasurementSettingsHost.Close();
        dockedHistoryHost.Toggle(
            "measurement-history",
            () => new MeasurementHistoryWindow(),
            dialog =>
            {
                dialog.EntryActivated += HandleHistoryEntryActivated;
                dialog.SaveRequested += HandleHistorySaveRequested;
                dialog.DeleteRequested += HandleHistoryDeleteRequested;
                dialog.NewSessionRequested += HandleNewSessionRequested;
                dialog.SetEntries(
                    measurementHistoryService.Entries,
                    sessionTracker.CurrentEntryId,
                    sessionTracker.CurrentEntryId);
            },
            async _ => await Task.CompletedTask);
    }

    private void HandleHistoryChanged()
    {
        TryBeginInvokeOnUiThread(() =>
        {
            dockedHistoryHost.InvokeIfOpen<MeasurementHistoryWindow>(dialog =>
            {
                Guid? selectedEntryId = dialog.SelectedEntryId;
                dialog.SetEntries(
                    measurementHistoryService.Entries,
                    selectedEntryId ?? sessionTracker.CurrentEntryId,
                    sessionTracker.CurrentEntryId);
            });
        });
    }

    private async void HandleHistoryEntryActivated(Guid entryId) =>
        await ActivateHistoryEntryAsync(entryId, ++measurementActivationRevision);

    private enum HistoryActivation
    {
        Landed,

        /// <summary>Nothing landed (file gone, sweep running, load threw); a caller may fall back.</summary>
        Unavailable,

        /// <summary>A newer activation is replacing this one; the caller must not fall back.</summary>
        Superseded
    }

    /// <param name="revision">Taken by the caller: one user action owns one revision, so a VDSP fallback after this still passes its check.</param>
    private async Task<HistoryActivation> ActivateHistoryEntryAsync(
        Guid entryId, long revision)
    {
        // Restoring during a sweep would Init an active measurement.
        if (expSweepMeasurement.InProgress)
        {
            return HistoryActivation.Unavailable;
        }

        // Stale loads are dropped at every await boundary, so a slow entry cannot overwrite a newer one.
        try
        {
            MeasurementHistorySnapshot? snapshot =
                await measurementHistoryService.GetSnapshotAsync(entryId);
            if (revision != measurementActivationRevision)
            {
                return HistoryActivation.Superseded;
            }
            if (snapshot == null)
            {
                return HistoryActivation.Unavailable;
            }

            // Write working state back into the entry being left, so returning restores the latest state.
            if (sessionTracker.CurrentEntryId != entryId)
            {
                sessionTracker.PersistCurrentSessionState();
            }

            string? sourceFilePath = measurementHistoryService.FindById(entryId)
                ?.SourceFilePath;
            await historyRestoreGate.WaitAsync();
            try
            {
                if (revision != measurementActivationRevision)
                {
                    return HistoryActivation.Superseded;
                }

                await RestoreHistorySnapshotAsync(snapshot, sourceFilePath);
                if (revision != measurementActivationRevision)
                {
                    return HistoryActivation.Superseded;
                }

                sessionTracker.MarkRestored(entryId);
            }
            finally
            {
                historyRestoreGate.Release();
            }
            dockedHistoryHost.InvokeIfOpen<MeasurementHistoryWindow>(dialog =>
            {
                dialog.SetEntries(
                    measurementHistoryService.Entries,
                    dialog.SelectedEntryId ?? entryId,
                    entryId);
            });
            return HistoryActivation.Landed;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to load history entry.\r\n\r\n{exception.Message}",
                "History",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return HistoryActivation.Unavailable;
        }
    }

    private async void HandleHistorySaveRequested(Guid entryId)
    {
        MeasurementHistoryEntry? entry = measurementHistoryService.FindById(entryId);
        if (entry == null || !entry.CanSave)
        {
            return;
        }

        MeasurementHistorySnapshot? snapshot =
            await measurementHistoryService.GetSnapshotAsync(entryId);
        if (snapshot == null)
        {
            return;
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

        try
        {
            ImpulseResponseFile file = snapshot.ToImpulseResponseFile();
            await file.SaveAsync(dialog.FileName);
            measurementHistoryService.MarkSaved(
                entryId,
                dialog.FileName,
                file,
                snapshot.Session);
            if (sessionTracker.CurrentEntryId == entryId)
            {
                SetImpulseResponseSourceFile(dialog.FileName);
                UpdateLastImpulseResponseDirectory(dialog.FileName);
                RefreshCurrentModePlot();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to save history entry.\r\n\r\n{exception.Message}",
                "History",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void HandleHistoryDeleteRequested(Guid entryId)
    {
        if (!measurementHistoryService.Delete(entryId))
        {
            return;
        }

        sessionTracker.ForgetEntry(entryId);

        dockedHistoryHost.InvokeIfOpen<MeasurementHistoryWindow>(dialog =>
        {
            dialog.RemoveEntry(entryId);
            dialog.SetEntries(
                measurementHistoryService.Entries,
                dialog.SelectedEntryId,
                sessionTracker.CurrentEntryId);
        });
    }

    private async Task RestoreHistorySnapshotAsync(
        MeasurementHistorySnapshot snapshot,
        string? sourceFilePath)
    {
        (double restoredLowHz, double restoredHighHz) = snapshot.ResolveSweepBand();
        (double achievedLowHz, double achievedHighHz) = snapshot.ResolveAchievedSweepBand();
        expSweepMeasurement.RestoreImpulseResponse(
            restoredLowHz,
            restoredHighHz,
            snapshot.SampleRate,
            snapshot.Bits,
            snapshot.SweepDurationSeconds,
            snapshot.PlayChannel,
            snapshot.SweepDeconvolutionImpulseResponse,
            snapshot.SweepDeconvolutionPeakIndex,
            snapshot.MeasurementMode,
            snapshot.TransferImpulseResponse,
            snapshot.TransferPeakIndex,
            snapshot.TransferCoherence,
            snapshot.AverageRunCount,
            snapshot.AcceptedAverageRunCount,
            achievedLowHz,
            achievedHighHz,
            snapshot.TimingReference,
            snapshot.MeasuredLowFrequencyHz,
            snapshot.MeasuredHighFrequencyHz,
            snapshot.MeasuredAtUtc);
        expSweepMeasurement.RestoreLevelSnapshot(snapshot.MeterSnapshot);
        // Both halves of K travel with the entry, as when opening the file.
        AdoptRestoredResult(
            snapshot.SplCalibration,
            snapshot.MicrophoneCalibration,
            snapshot.ArrayMicrophones,
            snapshot.ProtectiveHighPass);

        if (snapshot.Session != null)
        {
            ApplySessionSnapshot(snapshot.Session, snapshot.SampleRate);
        }

        ApplyMeasurementConfigurationToControllers();
        SetImpulseResponseSourceFile(sourceFilePath);
        sessionTracker.SetImpulseResponseAvailable(true);
        UpdatePeakInfo();
        dockedModeSettingsHost.InvokeIfOpen<Options.FROptions>(
            panel => panel.RefreshSplAvailability());

        if (snapshot.Session != null)
        {
            // Mode switch re-prepares overlays hidden, so only the active slots are re-shown. Audio settings untouched.
            await SelectModeAsync(NormalizeSessionMode(snapshot.Session.ActiveMode));
            overlayCollection.RestoreActiveSlots(
                CurrentMode,
                snapshot.Session.ActiveOverlaySlots);
            SaveMeasurementSettings();
        }
        else
        {
            RefreshCurrentModePlot();
        }
    }

    private async void HandleNewSessionRequested()
    {
        try
        {
            await StartNewSessionAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to start a new session.\r\n\r\n{exception.Message}",
                "New session",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // Resets mode settings, measurement and overlays; keeps audio settings, history and overlay files. Saves the active entry first.
    private async Task StartNewSessionAsync()
    {
        // Emptying the session supersedes any in-flight load or activation.
        measurementActivationRevision++;
        sessionTracker.PersistCurrentSessionState();

        if (liveSpectrumController.InProgress)
        {
            await liveSpectrumController.AbortAsync();
        }
        liveSpectrumController.ForgetLastCurve();

        sessionTracker.Reset();
        SetImpulseResponseAvailability(false);
        SetImpulseResponseSourceFile(null);

        ApplySessionSnapshot(
            new MeasurementSessionSnapshot(),
            expSweepMeasurement.SampleRate);
        ApplyMeasurementConfigurationToControllers();
        SaveMeasurementSettings();

        await SelectModeAsync(ModeTab.Frequency);

        dockedHistoryHost.InvokeIfOpen<MeasurementHistoryWindow>(dialog =>
            dialog.SetEntries(measurementHistoryService.Entries, null, null));
    }

    private MeasurementSessionSnapshot CaptureCurrentSessionSnapshot()
    {
        return new MeasurementSessionSnapshot
        {
            ActiveMode = modeController.ActiveTab,
            FrequencyResponse =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    frequencyResponseOptions, frequencyResponseVisibility),
            PhaseResponse =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    phaseResponseOptions, phaseResponseVisibility),
            GroupDelay =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    groupDelayOptions, groupDelayVisibility),
            ImpulseResponse =
                MeasurementSettingsFile.ImpulseResponseSettings.Capture(
                    impulseResponseOptions),
            Waterfall =
                MeasurementSettingsFile.WaterfallSettings.Capture(
                    waterfallGenOptions),
            BurstDecay =
                MeasurementSettingsFile.WaterfallSettings.Capture(
                    burstDecayGenOptions),
            LiveSpectrum =
                MeasurementSettingsFile.LiveSpectrumSettings.Capture(
                    liveSpectrumOptions),
            TimeAlignment =
                MeasurementSettingsFile.TimeAlignmentSettings.Capture(
                    timeAlignmentOptions),
            ActiveOverlaySlots = overlayCollection.CaptureActiveSlots(CurrentMode)
        };
    }

    private void ApplySessionSnapshot(
        MeasurementSessionSnapshot session,
        int sampleRate)
    {
        session.FrequencyResponse.ApplyTo(frequencyResponseOptions, frequencyResponseVisibility);
        session.PhaseResponse.ApplyTo(phaseResponseOptions, phaseResponseVisibility);
        session.GroupDelay.ApplyTo(groupDelayOptions, groupDelayVisibility);
        session.ImpulseResponse.ApplyTo(impulseResponseOptions);
        session.Waterfall.ApplyTo(waterfallGenOptions, WaterfallMode.Fourier);
        session.BurstDecay.ApplyTo(burstDecayGenOptions, WaterfallMode.BurstDecay);
        session.LiveSpectrum.ApplyTo(liveSpectrumOptions);
        session.TimeAlignment.ApplyTo(timeAlignmentOptions, sampleRate);
    }

    private static ModeTab NormalizeSessionMode(ModeTab mode) =>
        Enum.IsDefined(mode) ? mode : ModeTab.Frequency;
}
