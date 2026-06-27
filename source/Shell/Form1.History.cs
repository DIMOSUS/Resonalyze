using Resonalyze.History;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
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
                    currentHistoryEntryId,
                    currentHistoryEntryId);
            },
            async _ => await Task.CompletedTask);
    }

    private void HandleHistoryChanged()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke((MethodInvoker)delegate
        {
            dockedHistoryHost.InvokeIfOpen<MeasurementHistoryWindow>(dialog =>
            {
                Guid? selectedEntryId = dialog.SelectedEntryId;
                dialog.SetEntries(
                    measurementHistoryService.Entries,
                    selectedEntryId ?? currentHistoryEntryId,
                    currentHistoryEntryId);
            });
        });
    }

    private async void HandleHistoryEntryActivated(Guid entryId)
    {
        try
        {
            MeasurementHistorySnapshot? snapshot =
                await measurementHistoryService.GetSnapshotAsync(entryId);
            if (snapshot == null)
            {
                return;
            }

            // Before leaving the current entry, write the live working state back
            // into it so that returning later restores the latest mode/settings/
            // overlays rather than the state captured at save time.
            if (currentHistoryEntryId != entryId)
            {
                PersistCurrentSessionState();
            }

            await RestoreHistorySnapshotAsync(snapshot);
            currentHistoryEntryId = entryId;
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
            if (currentHistoryEntryId == entryId)
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

        if (currentHistoryEntryId == entryId)
        {
            currentHistoryEntryId = null;
        }

        dockedHistoryHost.InvokeIfOpen<MeasurementHistoryWindow>(dialog =>
        {
            dialog.RemoveEntry(entryId);
            dialog.SetEntries(
                measurementHistoryService.Entries,
                dialog.SelectedEntryId,
                currentHistoryEntryId);
        });
    }

    private async Task RestoreHistorySnapshotAsync(MeasurementHistorySnapshot snapshot)
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
            snapshot.TransferPeakIndex);
        expSweepMeasurement.RestoreLevelSnapshot(snapshot.MeterSnapshot);

        if (snapshot.Session != null)
        {
            ApplySessionSnapshot(snapshot.Session, snapshot.SampleRate);
        }

        ApplyMeasurementConfigurationToControllers();
        SetImpulseResponseSourceFile(null);
        hasCurrentImpulseResponse = true;
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
        PersistCurrentSessionState();

        if (liveSpectrumController.InProgress)
        {
            await liveSpectrumController.AbortAsync();
        }
        liveSpectrumController.ForgetLastCurve();

        currentHistoryEntryId = null;
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

    // Writes the live working state (mode + per-mode settings + active overlays)
    // back into the currently-selected history entry. Safe to call when nothing is
    // selected or no measurement is loaded.
    private void PersistCurrentSessionState()
    {
        if (!currentHistoryEntryId.HasValue || !hasCurrentImpulseResponse)
        {
            return;
        }

        measurementHistoryService.UpdateSession(
            currentHistoryEntryId.Value,
            CaptureCurrentSessionSnapshot());
    }

    private MeasurementSessionSnapshot CaptureCurrentSessionSnapshot()
    {
        return new MeasurementSessionSnapshot
        {
            ActiveMode = modeController.ActiveTab,
            FrequencyResponse =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    frequencyResponseOptions),
            PhaseResponse =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    phaseResponseOptions),
            GroupDelay =
                MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                    groupDelayOptions),
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
        session.FrequencyResponse.ApplyTo(frequencyResponseOptions);
        session.PhaseResponse.ApplyTo(phaseResponseOptions);
        session.GroupDelay.ApplyTo(groupDelayOptions);
        session.ImpulseResponse.ApplyTo(impulseResponseOptions);
        session.Waterfall.ApplyTo(waterfallGenOptions, WaterfallMode.Fourier);
        session.BurstDecay.ApplyTo(burstDecayGenOptions, WaterfallMode.BurstDecay);
        session.LiveSpectrum.ApplyTo(liveSpectrumOptions);
        session.TimeAlignment.ApplyTo(timeAlignmentOptions, sampleRate);
    }

    private static ModeTab NormalizeSessionMode(ModeTab mode) =>
        Enum.IsDefined(mode) ? mode : ModeTab.Frequency;
}
