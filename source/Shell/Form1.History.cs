using Resonalyze.History;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
    // Monotonic token for history restores: the newest activation wins and
    // stale async loads are dropped instead of overwriting a newer selection.
    private long historyRestoreRevision;

    // Serializes the restore itself: the restore mutates the current IR, the
    // controllers and the mode across several awaits, so two interleaved
    // restores could half-apply each other even with the revision token —
    // the gate makes each restore atomic and the token then guarantees the
    // newest one runs (or re-runs) last.
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

    private async void HandleHistoryEntryActivated(Guid entryId)
    {
        // Restoring a snapshot while a sweep is running would call Init on an
        // active measurement and fail; ignore the activation instead.
        if (expSweepMeasurement.InProgress)
        {
            return;
        }

        // Two rapid activations race: a slow file-backed entry can finish
        // loading AFTER a fast cached one and silently overwrite it, leaving
        // the UI on the earlier selection. The newest activation wins; stale
        // loads are dropped at every await boundary (the same revision guard
        // the async plot rebuild uses).
        long revision = ++historyRestoreRevision;
        try
        {
            MeasurementHistorySnapshot? snapshot =
                await measurementHistoryService.GetSnapshotAsync(entryId);
            if (snapshot == null || revision != historyRestoreRevision)
            {
                return;
            }

            // Before leaving the current entry, write the live working state back
            // into it so that returning later restores the latest mode/settings/
            // overlays rather than the state captured at save time.
            if (sessionTracker.CurrentEntryId != entryId)
            {
                sessionTracker.PersistCurrentSessionState();
            }

            // File-backed entries keep their file name in the plot titles (the same
            // way a freshly saved or loaded IR does); in-memory entries have none.
            string? sourceFilePath = measurementHistoryService.FindById(entryId)
                ?.SourceFilePath;
            await historyRestoreGate.WaitAsync();
            try
            {
                if (revision != historyRestoreRevision)
                {
                    return;
                }

                await RestoreHistorySnapshotAsync(snapshot, sourceFilePath);
                if (revision != historyRestoreRevision)
                {
                    return;
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
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to load history entry.\r\n\r\n{exception.Message}",
                "History",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
        expSweepMeasurement.RestoreImpulseResponse(
            snapshot.Octaves,
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
            snapshot.AcceptedAverageRunCount);
        expSweepMeasurement.RestoreLevelSnapshot(snapshot.MeterSnapshot);

        if (snapshot.Session != null)
        {
            ApplySessionSnapshot(snapshot.Session, snapshot.SampleRate);
        }

        ApplyMeasurementConfigurationToControllers();
        SetImpulseResponseSourceFile(sourceFilePath);
        sessionTracker.SetImpulseResponseAvailable(true);
        UpdatePeakInfo();

        if (snapshot.Session != null)
        {
            // Switching mode re-prepares overlays from their own on-disk files and
            // leaves them hidden, so restoring just re-shows the previously-active
            // slots. Audio device and routing settings are intentionally untouched.
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

    // Starts a fresh session: mode settings return to defaults and the current
    // measurement and overlays are cleared. Audio device and routing settings are
    // preserved. The active history entry is saved first; the history list and the
    // overlays' own on-disk files are left intact.
    private async Task StartNewSessionAsync()
    {
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

        // Re-preparing the default mode reloads overlays (left hidden) and draws an
        // empty plot because there is no current measurement.
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
