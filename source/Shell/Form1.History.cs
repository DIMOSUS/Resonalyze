using Resonalyze.History;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
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

    private async void HandleHistoryEntryActivated(Guid entryId)
    {
        // A run or an import is producing the next result.
        if (analyzerDocument.TryBegin() is { } request)
        {
            await ActivateHistoryEntryAsync(entryId, request);
        }
    }

    private enum HistoryActivation
    {
        Landed,

        /// <summary>Nothing landed (file gone, load threw); a caller may fall back.</summary>
        Unavailable,

        /// <summary>A newer request is replacing this one; the caller must not fall back.</summary>
        Superseded
    }

    /// <param name="request">Taken by the caller: one user action owns one request, so a VDSP fallback after this can still land.</param>
    private async Task<HistoryActivation> ActivateHistoryEntryAsync(
        Guid entryId, AnalyzerDocument.Request request)
    {
        // Stale loads are dropped at every await boundary, so a slow entry cannot overwrite a newer one.
        try
        {
            MeasurementResult? result = await measurementHistoryService.GetResultAsync(entryId);
            if (!request.IsCurrent)
            {
                return HistoryActivation.Superseded;
            }
            if (result == null)
            {
                return HistoryActivation.Unavailable;
            }

            // Write working state back into the entry being left, so returning restores the latest state.
            if (sessionTracker.CurrentEntryId != entryId)
            {
                sessionTracker.PersistCurrentSessionState();
            }

            MeasurementHistoryEntry? entry = measurementHistoryService.FindById(entryId);
            await historyRestoreGate.WaitAsync();
            try
            {
                if (!await RestoreHistoryResultAsync(request, result, entry?.Session, entry?.SourceFilePath) ||
                    !request.IsCurrent)
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

        MeasurementResult? result = await measurementHistoryService.GetResultAsync(entryId);
        if (result == null)
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
            ImpulseResponseFile file = ImpulseResponseFile.From(result);
            await file.SaveAsync(dialog.FileName);
            measurementHistoryService.MarkSaved(entryId, dialog.FileName, file, result);
            if (sessionTracker.CurrentEntryId == entryId)
            {
                analyzerDocument.Rename(dialog.FileName);
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

    /// <returns>False when a newer request superseded <paramref name="request"/>: nothing was restored.</returns>
    private async Task<bool> RestoreHistoryResultAsync(
        AnalyzerDocument.Request request,
        MeasurementResult result,
        MeasurementSessionSnapshot? session,
        string? sourceFilePath)
    {
        // The live analyzer is configured with the entry's live options, which it refuses while it runs; opening another
        // measurement stops it, as a sweep does. First, so nothing of the entry lands before this await: a load or a run
        // that lands during the stop owns the window.
        await StopLiveCaptureAsync();
        if (!request.IsCurrent)
        {
            return false;
        }

        // Both halves of K travel with the entry, as when opening the file.
        if (!InstallMeasurement(request, result, sourceFilePath))
        {
            return false;
        }

        LiveSpectrumRestartSnapshot liveBefore = LiveSpectrumRestartSnapshot.Capture(viewSettings.LiveSpectrum);
        if (session != null)
        {
            ApplySessionView(session, result.SampleRate);
        }

        ApplyMeasurementConfigurationToControllers();
        // A held live curve must not be redrawn under the entry's acquisition settings (a pink capture re-tilted as
        // white), as the settings panel and New session already ensure.
        if (LiveSpectrumRestartSnapshot.Capture(viewSettings.LiveSpectrum) != liveBefore)
        {
            liveSpectrumController.DiscardCapturedData();
        }

        if (session != null)
        {
            // Audio settings untouched.
            await SelectModeAsync(NormalizeSessionMode(session.ActiveMode));
            // A newer load or run that landed during the switch keeps its own slots and settings.
            if (!request.IsCurrent)
            {
                return false;
            }

            analyzerPlot.ReplaceOverlaySlots(session.ActiveOverlaySlots);
            SaveMeasurementSettings();
        }

        return true;
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
        sessionTracker.PersistCurrentSessionState();
        // Before the first await: no load, import or run already started lands after this.
        sessionTracker.Reset();
        analyzerDocument.Clear();

        if (liveSpectrumSession.InProgress)
        {
            await liveSpectrumController.AbortAsync();
        }
        // The accumulation outlives a stop and a loaded capture is state: both would come back on the next visit.
        liveSpectrumController.DiscardCapturedData();

        // Its result would be dropped; stop the sweep rather than play it out.
        if (expSweepMeasurement.InProgress)
        {
            await expSweepMeasurement.AbortAsync();
        }

        RefreshMeasurementCommands();

        ApplySessionView(new MeasurementSessionSnapshot(), expSweepMeasurement.SampleRate);
        ApplyMeasurementConfigurationToControllers();
        SaveMeasurementSettings();

        await SelectModeAsync(ModeTab.Frequency);
        // Overlay files stay; none is shown until picked again.
        analyzerPlot.ReplaceOverlaySlots([]);

        dockedHistoryHost.InvokeIfOpen<MeasurementHistoryWindow>(dialog =>
            dialog.SetEntries(measurementHistoryService.Entries, null, null));
    }

    private void ApplySessionView(MeasurementSessionSnapshot session, int sampleRate)
    {
        foreach (Mode rescaled in viewSettings.ApplySession(session, sampleRate))
        {
            analyzerPlot.Viewports.Forget(rescaled);
        }
    }

    private MeasurementSessionSnapshot CaptureCurrentSessionSnapshot()
    {
        // On a tool tab the entry keeps the analysis tab shown before it and that tab's overlays.
        (ModeTab tab, List<int> overlaySlots) = analyzerPlot.SessionView();
        return viewSettings.CaptureSession(tab, overlaySlots);
    }

    // A tab without the analysis plot (a tool) opens Frequency Response: switching to it would close the History window
    // the entry was opened from.
    private static ModeTab NormalizeSessionMode(ModeTab mode) =>
        Enum.IsDefined(mode) && ModeCatalog.For(mode).HasPlotView ? mode : ModeTab.Frequency;
}
