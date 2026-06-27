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
            await SelectModeAsync(NormalizeSessionMode(snapshot.Session.ActiveMode));
            overlayCollection.RestoreSessionState(
                CurrentMode,
                snapshot.Session.ActiveOverlays);
            SaveMeasurementSettings();
        }
        else
        {
            RefreshCurrentModePlot();
        }
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
            ActiveOverlays = overlayCollection.CaptureSessionState(CurrentMode)
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
